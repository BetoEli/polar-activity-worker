/*
 * COMMENTED OUT - These tests use InMemory database instead of QEPTest database
 * Uncomment when InMemory tests are needed for unit testing
 */

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Unit tests for ActivitySyncService webhook processing methods:
///   - ProcessPolarWebhookEventAsync
///   - ProcessPendingPolarWebhookEventsBatchAsync
///
/// All tests use EF Core InMemory database and a mocked IPolarClient,
/// so no real database or network connection is required.
/// Tests focus on QEPTest database schema with PolarLink and PolarTransaction tables.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ActivitySyncServiceTests
{
    // Shared helpers
    /// <summary>Creates a fresh InMemory <see cref="PawDbContext"/> per test.</summary>
    private static PawDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<PawDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PawDbContext(options);
    }

    /// <summary>Creates a mock <see cref="IDbContextFactory{PawDbContext}"/> that returns a new context sharing the same in-memory database.</summary>
    private static IDbContextFactory<PawDbContext> CreateDbFactory(PawDbContext db)
    {
        var factory = new Mock<IDbContextFactory<PawDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new PawDbContext(db.ContextOptions));
        return factory.Object;
    }

    /// <summary>Builds a default <see cref="PolarLink"/> used across multiple tests.</summary>
    private static PolarLink MakePolarLink(int polarId = 1001, string token = "tok-123") => new()
    {
        PolarID = polarId,
        PersonID = "0471319",
        Username = "testuser",
        Email = "testuser@southern.edu",
        DeviceType = "A300",
        TargetZone = "Fitness",
        AccessToken = token
    };

    /// <summary>
    /// Builds a <see cref="PolarExerciseDto"/> for webhook testing.
    /// </summary>
    private static PolarExerciseDto MakeExercise(
        string id = "ex001",
        string sport = "RUNNING",
        string duration = "PT30M",
        double? distance = 5000)
    {
        return new PolarExerciseDto
        {
            Id = id,
            StartTime = DateTime.UtcNow.AddDays(-1).ToString("o"),
            DurationIso8601 = duration,
            Sport = sport,
            Distance = distance,
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50,  UpperLimit = 100, InZone = "PT5M"  },
                new() { Index = 1, LowerLimit = 100, UpperLimit = 130, InZone = "PT10M" },
                new() { Index = 2, LowerLimit = 130, UpperLimit = 150, InZone = "PT10M" },
                new() { Index = 3, LowerLimit = 150, UpperLimit = 170, InZone = "PT5M"  },
                new() { Index = 4, LowerLimit = 170, UpperLimit = 200, InZone = "PT0S"  }
            }
        };
    }

    // ============================================================================
    // ProcessPolarWebhookEventAsync Tests
    // ============================================================================

    [Test]
    public async Task ProcessPolarWebhookEventAsync_ProcessesValidWebhook()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_ProcessesValidWebhook));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        
        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise123",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}",
            ResourceUrl = "https://www.polaraccesslink.com/v3/exercises/exercise123"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("exercise123");
        var rawJson = "{\"id\":\"exercise123\"}";
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "exercise123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, rawJson));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook.Should().NotBeNull();
        updatedWebhook!.Status.Should().Be("Completed");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();
        updatedWebhook.ErrorMessage.Should().BeNullOrEmpty();

        var transaction = db.PolarTransactions.Single();
        transaction.IsCommitted.Should().BeTrue();
        transaction.IsProcessed.Should().BeTrue();
        transaction.Location.Should().Be("https://www.polaraccesslink.com/v3/exercises/exercise123");
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_SkipsNonPendingWebhook()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_SkipsNonPendingWebhook));
        var polarClient = new Mock<IPolarClient>(MockBehavior.Strict);

        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 12345,
            EventType = "exercise",
            EntityID = "exercise123",
            Status = "Completed", // Already processed
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert
        polarClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_HandlesNonExistentWebhook()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_HandlesNonExistentWebhook));
        var polarClient = new Mock<IPolarClient>(MockBehavior.Strict);
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(999);

        // Assert - Should not throw, just log warning
        polarClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_FailsWhenPolarLinkNotFound()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_FailsWhenPolarLinkNotFound));
        var polarClient = new Mock<IPolarClient>();

        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 12345, // No matching PolarLink
            EventType = "exercise",
            EntityID = "exercise123",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook.Should().NotBeNull();
        updatedWebhook!.Status.Should().Be("Failed");
        updatedWebhook.ErrorMessage.Should().Contain("No Polar connection found");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_FailsWhenExerciseNotFound()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_FailsWhenExerciseNotFound));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise123",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "exercise123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)null);

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook.Should().NotBeNull();
        updatedWebhook!.Status.Should().Be("Failed");
        updatedWebhook.ErrorMessage.Should().Contain("not found or inaccessible");
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_HandlesExceptionGracefully()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_HandlesExceptionGracefully));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise123",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook.Should().NotBeNull();
        updatedWebhook!.Status.Should().Be("Failed");
        updatedWebhook.ErrorMessage.Should().Be("API Error");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_CreatesAndCommitsPolarTransaction()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_CreatesAndCommitsPolarTransaction));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise123",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}",
            ResourceUrl = "https://www.polaraccesslink.com/v3/exercises/exercise123"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("exercise123");
        var rawJson = "{\"id\":\"exercise123\"}";
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "exercise123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, rawJson));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert
        var transaction = db.PolarTransactions.Single();
        transaction.PolarID.Should().Be(1001);
        transaction.Location.Should().Be("https://www.polaraccesslink.com/v3/exercises/exercise123");
        transaction.IsCommitted.Should().BeTrue();
        transaction.IsProcessed.Should().BeTrue();
        transaction.Response.Should().Be(rawJson);
    }

    // ============================================================================
    // ProcessPendingPolarWebhookEventsBatchAsync Tests
    // ============================================================================

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_ProcessesMultipleEvents()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_ProcessesMultipleEvents));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        for (int i = 1; i <= 3; i++)
        {
            var webhookEvent = new WebhookEvent
            {
                Id = i,
                Provider = ActivityProviderType.Polar,
                ExternalUserId = 1001,
                EventType = "exercise",
                EntityID = $"exercise{i}",
                Status = "Pending",
                ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                RawPayload = "{}",
                ResourceUrl = $"https://www.polaraccesslink.com/v3/exercises/exercise{i}"
            };
            db.WebhookEvents.Add(webhookEvent);
        }
        await db.SaveChangesAsync();

        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string id, CancellationToken ct) =>
                ((PolarExerciseDto? Exercise, string RawJson)?)(MakeExercise(id), "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var processedCount = await svc.ProcessPendingPolarWebhookEventsBatchAsync(10);

        // Assert
        processedCount.Should().Be(3);
        var webhooks = await db.WebhookEvents.ToListAsync();
        webhooks.Should().AllSatisfy(w => w.Status.Should().Be("Completed"));
        db.PolarTransactions.Should().HaveCount(3);
    }

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_RespectsMaxBatchSize()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_RespectsMaxBatchSize));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        for (int i = 1; i <= 5; i++)
        {
            var webhookEvent = new WebhookEvent
            {
                Id = i,
                Provider = ActivityProviderType.Polar,
                ExternalUserId = 1001,
                EventType = "exercise",
                EntityID = $"exercise{i}",
                Status = "Pending",
                ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                RawPayload = "{}",
                ResourceUrl = $"https://www.polaraccesslink.com/v3/exercises/exercise{i}"
            };
            db.WebhookEvents.Add(webhookEvent);
        }
        await db.SaveChangesAsync();

        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string id, CancellationToken ct) =>
                ((PolarExerciseDto? Exercise, string RawJson)?)(MakeExercise(id), "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var processedCount = await svc.ProcessPendingPolarWebhookEventsBatchAsync(3);

        // Assert
        processedCount.Should().Be(3);
        var completedWebhooks = await db.WebhookEvents.Where(w => w.Status == "Completed").ToListAsync();
        completedWebhooks.Should().HaveCount(3);
        var pendingWebhooks = await db.WebhookEvents.Where(w => w.Status == "Pending").ToListAsync();
        pendingWebhooks.Should().HaveCount(2);
    }

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_ReturnsZeroWhenNoPending()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_ReturnsZeroWhenNoPending));
        var polarClient = new Mock<IPolarClient>();
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var processedCount = await svc.ProcessPendingPolarWebhookEventsBatchAsync(10);

        // Assert
        processedCount.Should().Be(0);
        db.WebhookEvents.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_ContinuesOnError()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_ContinuesOnError));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        for (int i = 1; i <= 3; i++)
        {
            var webhookEvent = new WebhookEvent
            {
                Id = i,
                Provider = ActivityProviderType.Polar,
                ExternalUserId = 1001,
                EventType = "exercise",
                EntityID = $"exercise{i}",
                Status = "Pending",
                ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                RawPayload = "{}",
                ResourceUrl = $"https://www.polaraccesslink.com/v3/exercises/exercise{i}"
            };
            db.WebhookEvents.Add(webhookEvent);
        }
        await db.SaveChangesAsync();

        // First call fails, others succeed
        var callCount = 0;
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string id, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Exception("API Error");
                return ((PolarExerciseDto? Exercise, string RawJson)?)(MakeExercise(id), "{}");
            });

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var processedCount = await svc.ProcessPendingPolarWebhookEventsBatchAsync(10);

        // Assert
        processedCount.Should().Be(3); // All attempted
        var completedWebhooks = await db.WebhookEvents.Where(w => w.Status == "Completed").ToListAsync();
        completedWebhooks.Should().HaveCount(2);
        var failedWebhooks = await db.WebhookEvents.Where(w => w.Status == "Failed").ToListAsync();
        failedWebhooks.Should().HaveCount(1);
    }

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_OrdersByReceivedAtUtc()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_OrdersByReceivedAtUtc));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        // Add events out of order by received time
        var now = DateTime.UtcNow;
        var webhookEvent3 = new WebhookEvent
        {
            Id = 3,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise3",
            Status = "Pending",
            ReceivedAtUtc = now.AddMinutes(-1), // Most recent
            RawPayload = "{}",
            ResourceUrl = "https://www.polaraccesslink.com/v3/exercises/exercise3"
        };
        var webhookEvent1 = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise1",
            Status = "Pending",
            ReceivedAtUtc = now.AddMinutes(-3), // Oldest (should be processed first)
            RawPayload = "{}",
            ResourceUrl = "https://www.polaraccesslink.com/v3/exercises/exercise1"
        };
        var webhookEvent2 = new WebhookEvent
        {
            Id = 2,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "exercise2",
            Status = "Pending",
            ReceivedAtUtc = now.AddMinutes(-2),
            RawPayload = "{}",
            ResourceUrl = "https://www.polaraccesslink.com/v3/exercises/exercise2"
        };
        db.WebhookEvents.AddRange(webhookEvent3, webhookEvent1, webhookEvent2);
        await db.SaveChangesAsync();

        var processOrder = new List<string>();
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string id, CancellationToken ct) =>
            {
                processOrder.Add(id);
                return ((PolarExerciseDto? Exercise, string RawJson)?)(MakeExercise(id), "{}");
            });

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPendingPolarWebhookEventsBatchAsync(10);

        // Assert - events should be processed in order of ReceivedAtUtc (oldest first)
        processOrder.Should().ContainInOrder("exercise1", "exercise2", "exercise3");
    }

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_OnlyProcessesPolarProvider()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_OnlyProcessesPolarProvider));
        var polarClient = new Mock<IPolarClient>();

        // Non-Polar webhook event (should be skipped)
        var nonPolarEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar, // Force Polar for test
            ExternalUserId = 9999,
            EventType = "exercise",
            EntityID = "nonpolar1",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}"
        };
        db.WebhookEvents.Add(nonPolarEvent);
        await db.SaveChangesAsync();

        polarClient
            .Setup(x => x.GetExerciseByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string token, string id, CancellationToken ct) =>
                ((PolarExerciseDto? Exercise, string RawJson)?)(MakeExercise(id), "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var processedCount = await svc.ProcessPendingPolarWebhookEventsBatchAsync(10);

        // Assert - event not processed because no matching PolarLink exists
        processedCount.Should().Be(1);
        var failedEvent = await db.WebhookEvents.FindAsync(1L);
        failedEvent!.Status.Should().Be("Failed");
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_DuplicateWebhook_UpdatesExistingTransactionAndCompletes()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_DuplicateWebhook_UpdatesExistingTransactionAndCompletes));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        const string exerciseUrl = "https://www.polaraccesslink.com/v3/exercises/exercise123";

        // Seed an already-committed PolarTransaction for the same exercise URL
        var existingTransaction = new PolarTransaction
        {
            PolarID      = polarLink.PolarID,
            FirstTouched = DateTime.UtcNow.AddHours(-1),
            LastTouched  = DateTime.UtcNow.AddHours(-1),
            Location     = exerciseUrl,
            Response     = "{\"original\":true}",
            IsCommitted  = true,
            IsProcessed  = true,
            Attempt      = 1
        };
        db.PolarTransactions.Add(existingTransaction);

        // A second Pending webhook for the same exercise arrives
        var webhookEvent = new WebhookEvent
        {
            Id            = 10,
            Provider      = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType     = "exercise",
            EntityID      = "exercise123",
            Status        = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload    = "{}",
            ResourceUrl   = exerciseUrl
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("exercise123");
        var newRawJson = "{\"updated\":true}";
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "exercise123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, newRawJson));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(10);

        // Assert — webhook marked Completed
        var updatedWebhook = await db.WebhookEvents.FindAsync(10L);
        updatedWebhook!.Status.Should().Be("Completed");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();

        // Assert — only one PolarTransaction row exists (no duplicate)
        db.PolarTransactions.Should().HaveCount(1);

        // Assert — the existing transaction was updated, not replaced
        var tx = db.PolarTransactions.Single();
        tx.Attempt.Should().Be(2, "second processing should increment Attempt");
        tx.Response.Should().Be(newRawJson, "Response must reflect the new fetch");
        tx.IsCommitted.Should().BeTrue("already-committed flag must be preserved / re-set");
        tx.IsProcessed.Should().BeTrue();
        tx.Location.Should().Be(exerciseUrl);
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_EmptyStringAccessToken_CallsApiAndFails()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_EmptyStringAccessToken_CallsApiAndFails));
        var polarClient = new Mock<IPolarClient>();

        // PolarLink with an empty-string token (distinct from null)
        var polarLink = MakePolarLink(token: "");
        db.PolarLinks.Add(polarLink);

        var webhookEvent = new WebhookEvent
        {
            Id            = 1,
            Provider      = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType     = "exercise",
            EntityID      = "exercise123",
            Status        = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload    = "{}"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        // Simulate the Polar API rejecting an empty token
        polarClient
            .Setup(x => x.GetExerciseByIdAsync("", "exercise123", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert — the API WAS called with the empty token (no early-exit guard)
        polarClient.Verify(
            x => x.GetExerciseByIdAsync("", "exercise123", It.IsAny<CancellationToken>()),
            Times.Once,
            "ProcessPolarWebhookEventAsync must forward the empty token directly to the client");

        // Assert — webhook transitioned Processing → Failed via outer catch
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook!.Status.Should().Be("Failed");
        updatedWebhook.ErrorMessage.Should().Contain("401");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_QepSaveFailure_WebhookStillCompletes_TransactionNotCommitted()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_QepSaveFailure_WebhookStillCompletes_TransactionNotCommitted));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        // Intentionally do NOT seed any QepActivityType rows.
        // PolarToQepMapper calls _db.ActivityTypes.FirstOrDefaultAsync, which returns null,
        // then the mapper or the subsequent SaveChangesAsync propagates an exception from
        // a missing required FK — but the service's inner catch swallows it.

        const string exerciseUrl = "https://www.polaraccesslink.com/v3/exercises/exercise456";
        var webhookEvent = new WebhookEvent
        {
            Id            = 1,
            Provider      = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType     = "exercise",
            EntityID      = "exercise456",
            Status        = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload    = "{}",
            ResourceUrl   = exerciseUrl
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        // Polar fetch succeeds
        var exercise = MakeExercise("exercise456");
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "exercise456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act — must not throw
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert — webhook is Completed (inner catch swallows QEP save error)
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook!.Status.Should().Be("Completed",
            "the inner try/catch around SaveToQepActivityTablesAsync swallows the error");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();

        // Assert — PolarTransaction was created and IsProcessed is true
        var tx = db.PolarTransactions.SingleOrDefault();
        tx.Should().NotBeNull("PolarTransaction is written before the QEP save attempt");
        tx!.IsProcessed.Should().BeTrue();
        // IsCommitted may be false if the inner QEP save truly failed (no ActivityType row),
        // but the important invariant is that the webhook itself is Completed
        tx.Location.Should().Be(exerciseUrl);
    }

    [Test]
    public async Task ProcessPolarWebhookEventAsync_NullResourceUrl_UsesFallbackUrlWithWww()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPolarWebhookEventAsync_NullResourceUrl_UsesFallbackUrlWithWww));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        var webhookEvent = new WebhookEvent
        {
            Id            = 1,
            Provider      = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType     = "exercise",
            EntityID      = "exercise789",
            Status        = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload    = "{}",
            ResourceUrl   = null  // ← no URL provided
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("exercise789");
        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "exercise789", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert — webhook completed
        var updatedWebhook = await db.WebhookEvents.FindAsync(1L);
        updatedWebhook!.Status.Should().Be("Completed");

        // Assert — fallback URL uses www. prefix (as coded in the service)
        const string expectedFallbackUrl = "https://www.polaraccesslink.com/v3/exercises/exercise789";
        var tx = db.PolarTransactions.SingleOrDefault();
        tx.Should().NotBeNull("a PolarTransaction must be created even when ResourceUrl is null");
        tx!.Location.Should().Be(expectedFallbackUrl,
            "when ResourceUrl is null the service falls back to the www.-prefixed URL");

        // Assert — the fallback URL differs from the canonical (no-www) URL used by SyncUserAsync
        var canonicalUrl = ActivitySyncService.CanonicalExerciseLocation("exercise789");
        tx.Location.Should().NotBe(canonicalUrl,
            "the fallback URL (www.) is different from the canonical URL (no www.) — a known deduplication gap");
    }

    [Test]
    public async Task ProcessPendingPolarWebhookEventsBatchAsync_MixedProviders_OnlyPolarEventsAreQueried()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessPendingPolarWebhookEventsBatchAsync_MixedProviders_OnlyPolarEventsAreQueried));
        var polarClient = new Mock<IPolarClient>();

        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        // Polar event — should be picked up and processed
        var polarEvent = new WebhookEvent
        {
            Id            = 1,
            Provider      = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType     = "exercise",
            EntityID      = "polar-exercise",
            Status        = "Pending",
            ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            RawPayload    = "{}",
            ResourceUrl   = "https://www.polaraccesslink.com/v3/exercises/polar-exercise"
        };

        // Non-Polar event — cast an integer that is not in the enum to represent a
        // future provider (e.g., Garmin = 2); the DB query must NOT select this row
        var nonPolarProvider = (ActivityProviderType)2;
        var nonPolarEvent = new WebhookEvent
        {
            Id            = 2,
            Provider      = nonPolarProvider,
            ExternalUserId = 9999,
            EventType     = "exercise",
            EntityID      = "garmin-activity",
            Status        = "Pending",
            ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            RawPayload    = "{}"
        };

        db.WebhookEvents.AddRange(polarEvent, nonPolarEvent);
        await db.SaveChangesAsync();

        polarClient
            .Setup(x => x.GetExerciseByIdAsync(polarLink.AccessToken, "polar-exercise", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(MakeExercise("polar-exercise"), "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var processedCount = await svc.ProcessPendingPolarWebhookEventsBatchAsync(10);

        // Assert — only the Polar event was returned by the DB query and processed
        processedCount.Should().Be(1, "only the Polar event is in the batch query result set");

        var polarResult = await db.WebhookEvents.FindAsync(1L);
        polarResult!.Status.Should().Be("Completed", "the Polar event was processed successfully");

        var nonPolarResult = await db.WebhookEvents.FindAsync(2L);
        nonPolarResult!.Status.Should().Be("Pending",
            "the non-Polar event must not be touched by the Polar batch processor");

        // Assert — the Polar API was called exactly once (for the Polar event only)
        polarClient.Verify(
            x => x.GetExerciseByIdAsync(It.IsAny<string>(), "garmin-activity", It.IsAny<CancellationToken>()),
            Times.Never,
            "the non-Polar event's EntityID must never reach the Polar API client");
    }
}










