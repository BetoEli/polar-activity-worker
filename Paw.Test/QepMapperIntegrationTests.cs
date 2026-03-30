using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Integration tests for the Polar webhook workflow against the real QEPTest database.
/// These tests only cover the portion of the pipeline that currently works in production:
/// 1. Webhook payload stored in dbo.WebhookEvents
/// 2. Worker calls Polar API and stores the raw payload in dbo.PolarTransactions
/// 3. Run with dotnet test --filter "FullyQualifiedName~QepMapperIntegrationTests"
/// </summary>
[TestFixture]
[Explicit]
public class QepMapperIntegrationTests
{
    private PawDbContext _dbContext = null!;
    private IConfiguration _configuration = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Load configuration from appsettings.Test.json
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();
    }

    [SetUp]
    public void SetUp()
    {
        var connectionString = _configuration.GetConnectionString("QEPTestConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            Assert.Fail("QEPTestConnection not found in appsettings.Test.json");
        }

        var options = new DbContextOptionsBuilder<PawDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        _dbContext = new PawDbContext(options);
        
        // Verify connection
        try
        {
            _dbContext.Database.CanConnect();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Cannot connect to QEPTest database: {ex.Message}");
        }
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext?.Dispose();
    }

    [Test]
    public async Task Integration_ProcessWebhookEvent_StoresPolarTransaction()
    {
        // Arrange: locate an existing PolarLink so we can simulate the webhook flow
        var polarLink = GetExistingPolarLink();
        var entityId = $"test-{Guid.NewGuid():N}";
        var exerciseUrl = $"https://polaraccesslink.com/v3/exercises/{entityId}";
        await CleanupExistingArtifactsAsync(polarLink.PolarID, exerciseUrl, entityId);

        try
        {
            var webhook = new WebhookEvent
            {
                Provider = ActivityProviderType.Polar,
                EventType = "EXERCISE",
                ExternalUserId = polarLink.PolarID,
                EntityID = entityId,
                EventTimestamp = DateTime.UtcNow,
                ResourceUrl = exerciseUrl,
                Status = "Pending",
                ReceivedAtUtc = DateTime.UtcNow,
                RawPayload = $"{{\"event\":\"EXERCISE\",\"entity_id\":\"{entityId}\"}}"
            };
            _dbContext.WebhookEvents.Add(webhook);
            await _dbContext.SaveChangesAsync();

            var exercise = BuildSampleExercise(entityId);
            var rawJson = System.Text.Json.JsonSerializer.Serialize(new { exercise = entityId, exercise.Sport, exercise.DurationIso8601 });
            var fakePolarClient = new FakePolarClient();
            fakePolarClient.AddExercise(entityId, exercise, rawJson);

            var dbFactory = new Mock<IDbContextFactory<PawDbContext>>();
            dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(() => new PawDbContext(_dbContext.ContextOptions));
            var syncService = new ActivitySyncService(_dbContext, dbFactory.Object, fakePolarClient, NullLogger<ActivitySyncService>.Instance);

            // Act: process the webhook
            await syncService.ProcessPolarWebhookEventAsync(webhook.Id);

            // Assert: webhook marked completed and raw payload stored in PolarTransactions
            var storedWebhook = await _dbContext.WebhookEvents.AsNoTracking().FirstAsync(w => w.Id == webhook.Id);
            storedWebhook.Status.Should().Be("Completed");
            storedWebhook.ProcessedAtUtc.Should().NotBeNull();
            storedWebhook.ErrorMessage.Should().BeNull();

            var transaction = await _dbContext.PolarTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.PolarID == polarLink.PolarID && t.Location == exerciseUrl);

            transaction.Should().NotBeNull();
            transaction!.Response.Should().Be(rawJson);
            transaction.IsProcessed.Should().BeTrue();
            transaction.IsCommitted.Should().BeTrue();
            transaction.Attempt.Should().Be(1);
        }
        finally
        {
            await CleanupExistingArtifactsAsync(polarLink.PolarID, exerciseUrl, entityId);
        }
    }

    /// <summary>
    /// Retrieves a PolarLink from the database so we have a valid PolarID + AccessToken for tests.
    /// </summary>
    private PolarLink GetExistingPolarLink()
    {
        var preferredPolarId = 59002246;
        
        // Filter out records with NULL/empty AccessToken and Email to avoid SQL null value exceptions
        var link = _dbContext.PolarLinks
            .Where(p => p.AccessToken != null && p.AccessToken != "" 
                     && p.Email != null && p.Email != "")
            .FirstOrDefault(p => p.PolarID == preferredPolarId)
                   ?? _dbContext.PolarLinks
                      .Where(p => p.AccessToken != null && p.AccessToken != ""
                               && p.Email != null && p.Email != "")
                      .FirstOrDefault();

        if (link == null)
        {
            Assert.Inconclusive("No PolarLinks found in QEPTest database with valid AccessToken and Email. Connect a Polar account first.");
        }

        if (string.IsNullOrWhiteSpace(link.AccessToken))
        {
            Assert.Inconclusive($"PolarLink {link.PolarID} does not have an AccessToken configured. Update the record before running tests.");
        }

        if (string.IsNullOrWhiteSpace(link.Email))
        {
            Assert.Inconclusive($"PolarLink {link.PolarID} does not have an Email configured. Update the record before running tests.");
        }

        return link;
    }

    private static PolarExerciseDto BuildSampleExercise(string entityId) => new()
    {
        Id = entityId,
        StartTime = DateTime.UtcNow.AddDays(-1).ToString("O"),
        DurationIso8601 = "PT30M",
        Sport = "RUNNING",
        Distance = 5000,
        HeartRateZones = new List<PolarHeartRateZoneDto>
        {
            new() { Index = 0, LowerLimit = 60, UpperLimit = 100, InZone = "PT5M" },
            new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "PT10M" },
            new() { Index = 2, LowerLimit = 120, UpperLimit = 140, InZone = "PT8M" },
            new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT5M" }
        }
    };

    private async Task CleanupExistingArtifactsAsync(long polarId, string exerciseUrl, string entityId)
    {
        var existingTransactions = await _dbContext.PolarTransactions
            .Where(t => t.PolarID == polarId && t.Location == exerciseUrl)
            .ToListAsync();
        if (existingTransactions.Any())
        {
            _dbContext.PolarTransactions.RemoveRange(existingTransactions);
        }

        var existingWebhooks = await _dbContext.WebhookEvents
            .Where(w => w.EntityID == entityId)
            .ToListAsync();
        if (existingWebhooks.Any())
        {
            _dbContext.WebhookEvents.RemoveRange(existingWebhooks);
        }

        var heartRateZones = await _dbContext.HeartRateZones
            .Where(z => z.EntityID == entityId)
            .ToListAsync();
        if (heartRateZones.Any())
        {
            _dbContext.HeartRateZones.RemoveRange(heartRateZones);
        }

        var activities = await _dbContext.Activities
            .Where(a => a.EntityID == entityId)
            .ToListAsync();
        if (activities.Any())
        {
            _dbContext.Activities.RemoveRange(activities);
        }

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Minimal fake implementation of IPolarClient – only GetExerciseByIdAsync is needed for these tests.
    /// </summary>
    private sealed class FakePolarClient : IPolarClient
    {
        private readonly Dictionary<string, (PolarExerciseDto Exercise, string RawJson)> _responses = new();

        public void AddExercise(string exerciseId, PolarExerciseDto exercise, string rawJson)
            => _responses[exerciseId] = (exercise, rawJson);

        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(string accessToken, string exerciseId, CancellationToken cancellationToken = default)
        {
            if (_responses.TryGetValue(exerciseId, out var value))
            {
                return Task.FromResult<(PolarExerciseDto? Exercise, string RawJson)?>(value);
            }

            return Task.FromResult<(PolarExerciseDto? Exercise, string RawJson)?>(null);
        }

        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(DeviceAccount account, string exerciseId, CancellationToken cancellationToken = default)
            => GetExerciseByIdAsync(account.AccessToken, exerciseId, cancellationToken);

        #region Unused interface members

        public Task<PolarTokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarUserRegistrationResponse?> RegisterUserAsync(string accessToken, string memberId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PolarTrainingSession>> GetTrainingsAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PolarExerciseDto>> GetExercisesAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarWebhookResponse?> CreateWebhookAsync(string webhookUrl, List<string> events, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarWebhookInfo?> GetWebhookAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ActivateWebhookAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)> ListExercisesAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)>((Array.Empty<PolarExerciseDto>(), "[]"));

        #endregion
    }
}
