using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paw.Core.Domain;
using Paw.Core.DTOs;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Tests for GET /qep/polar/sync-history/{personId}.
/// Run with: dotnet test --filter "FullyQualifiedName~SyncHistoryEndpointTests" --verbosity normal
/// </summary>
[TestFixture]
public class SyncHistoryEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private const string ApiKey = "hist-test-key";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["QepApiKeys:student"] = ApiKey,
                        ["QepApiKeys:QepFaculty"] = ApiKey,
                        ["QepApiKeys:QepAdministrator"] = ApiKey,
                        ["QepWebAppRedirectUrl"] = "http://localhost/Polar/Connected",
                        ["Polar:ClientId"] = "test-id",
                        ["Polar:ClientSecret"] = "test-secret",
                        ["Polar:RedirectUri"] = "http://localhost/qep/polar/callback"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    var dbDescriptors = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<PawDbContext>)
                                 || d.ServiceType == typeof(PawDbContext)
                                 || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                        .ToList();
                    foreach (var d in dbDescriptors) services.Remove(d);

                    services.AddDbContext<PawDbContext>(o =>
                        o.UseInMemoryDatabase("SyncHistoryTests"));

                    var polarDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPolarClient));
                    if (polarDescriptor != null) services.Remove(polarDescriptor);
                    services.AddSingleton<IPolarClient, StubPolarClient>();
                });
            });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-QEP-API-Key", ApiKey);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    private PawDbContext NewDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<PawDbContext>();

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task SeedLinkAsync(string personId, long polarId)
    {
        var db = NewDbContext();
        if (!await db.PolarLinks.AnyAsync(p => p.PersonID == personId))
        {
            db.PolarLinks.Add(new PolarLink
            {
                PolarID = polarId,
                PersonID = personId,
                Username = personId,
                Email = $"{personId}@test.com"
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedEventAsync(long polarId, string entityId, string status, DateTime receivedAt)
    {
        var db = NewDbContext();
        db.WebhookEvents.Add(new WebhookEvent
        {
            Provider = ActivityProviderType.Polar,
            EventType = "EXERCISE",
            ExternalUserId = polarId,
            EntityID = entityId,
            EventTimestamp = receivedAt,
            Status = status,
            ReceivedAtUtc = receivedAt,
            RawPayload = "{}"
        });
        await db.SaveChangesAsync();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task ReturnsHistory_OrderedByReceivedAtDesc()
    {
        await SeedLinkAsync("hist001", 10001);
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);
        await SeedEventAsync(10001, "entity-A", "Completed", older);
        await SeedEventAsync(10001, "entity-B", "Completed", newer);

        var response = await _client.GetAsync("/qep/polar/sync-history/hist001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<WebhookEventSummary>>();
        body.Should().NotBeNull();
        body!.Should().HaveCountGreaterThanOrEqualTo(2);
        body[0].EntityId.Should().Be("entity-B"); // newer first
        body[1].EntityId.Should().Be("entity-A");
    }

    [Test]
    public async Task ReturnsNotFound_WhenPersonIdHasNoLink()
    {
        var response = await _client.GetAsync("/qep/polar/sync-history/no-such-person");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ReturnsUnauthorized_WithoutApiKey()
    {
        using var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/qep/polar/sync-history/hist001");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RespectsLimit_QueryParameter()
    {
        await SeedLinkAsync("hist002", 10002);
        var now = DateTime.UtcNow;
        await SeedEventAsync(10002, "e1", "Completed", now.AddMinutes(-30));
        await SeedEventAsync(10002, "e2", "Completed", now.AddMinutes(-20));
        await SeedEventAsync(10002, "e3", "Completed", now.AddMinutes(-10));

        var response = await _client.GetAsync("/qep/polar/sync-history/hist002?limit=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<WebhookEventSummary>>();
        body.Should().HaveCount(2);
    }

    [Test]
    public async Task ReturnsEmptyList_WhenNoEventsForLinkedUser()
    {
        await SeedLinkAsync("hist003", 10003);

        var response = await _client.GetAsync("/qep/polar/sync-history/hist003");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<WebhookEventSummary>>();
        body.Should().BeEmpty();
    }

    // ── stub ─────────────────────────────────────────────────────────────────

    private sealed class StubPolarClient : IPolarClient
    {
        public Task<PolarTokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolarTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolarUserRegistrationResponse?> RegisterUserAsync(string accessToken, string memberId, CancellationToken ct = default) => Task.FromResult<PolarUserRegistrationResponse?>(null);
        public Task<IReadOnlyList<PolarTrainingSession>> GetTrainingsAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PolarTrainingSession>>(Array.Empty<PolarTrainingSession>());
        public Task<IReadOnlyList<PolarExerciseDto>> GetExercisesAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PolarExerciseDto>>(Array.Empty<PolarExerciseDto>());
        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(DeviceAccount account, string exerciseId, CancellationToken ct = default) => Task.FromResult<(PolarExerciseDto?, string)?>(null);
        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(string accessToken, string exerciseId, CancellationToken ct = default) => Task.FromResult<(PolarExerciseDto?, string)?>(null);
        public Task<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)> ListExercisesAsync(string accessToken, CancellationToken ct = default) => Task.FromResult<(IReadOnlyList<PolarExerciseDto>, string)>((Array.Empty<PolarExerciseDto>(), "{}"));
        public Task<PolarWebhookResponse?> CreateWebhookAsync(string webhookUrl, List<string> events, CancellationToken ct = default) => Task.FromResult<PolarWebhookResponse?>(null);
        public Task<PolarWebhookInfo?> GetWebhookAsync(CancellationToken ct = default) => Task.FromResult<PolarWebhookInfo?>(null);
        public Task ActivateWebhookAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
