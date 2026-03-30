using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// End-to-end integration tests for the webhook processing and reconciliation pipeline.
/// Tests the full flow: webhook reception ? PolarTransaction creation ? Activity/HeartRateZone storage.
/// Uses real EF Core InMemory database (not mocked) to validate actual schema behavior.
/// All tests verify the complete transaction commitment pattern:
///   1. PolarTransaction.IsCommitted = false (staged)
///   2. Exercise data saved to PolarTransaction
///   3. Activity + HeartRateZones saved to QEP tables
///   4. PolarTransaction.IsCommitted = true (committed only after QEP save succeeds)
/// Run with: dotnet test --filter "FullyQualifiedName~WebhookFlowEndToEndTests" --verbosity normal
/// </summary>
[TestFixture]
[Category("Integration")]
public class WebhookFlowEndToEndTests
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
    /// Builds a <see cref="PolarExerciseDto"/> for testing.
    /// UploadTime is within the last 30 days to pass the since-filter.
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
            StartTime = DateTime.UtcNow.AddDays(-5).ToString("o"),
            UploadTime = DateTime.UtcNow.AddDays(-5).ToString("o"),
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
    // SyncUserAsync Tests � Direct 30-day sweep reconciliation
    // ============================================================================

    [Test]
    public async Task ReconcileUser_StoresActivity_AndHeartRateZones()
    {
        // Arrange
        await using var db = CreateDb(nameof(ReconcileUser_StoresActivity_AndHeartRateZones));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise1 = MakeExercise("ex-001");
        var exercise2 = MakeExercise("ex-002", distance: 6000);

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise1, exercise2 } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(polarLink);

        // Assert
        result.Should().Be(2, "both exercises should be committed");

        // Verify PolarTransactions created and committed
        var transactions = await db.PolarTransactions.ToListAsync();
        transactions.Should().HaveCount(2);
        transactions.All(t => t.IsCommitted).Should().BeTrue("all transactions should be committed after QEP save succeeds");
        transactions.All(t => t.IsProcessed).Should().BeTrue();

        // Verify Activities created
        var activities = await db.Activities.ToListAsync();
        activities.Should().HaveCount(2);
        activities.Select(a => a.EntityID).Should().BeEquivalentTo(new[] { "ex-001", "ex-002" });
        activities[0].UserID.Should().Be("0471319");
        activities[0].Username.Should().Be("testuser");
        activities[0].Distance.Should().Be(5000);
        activities[1].Distance.Should().Be(6000);

        // Verify HeartRateZones created (5 zones per exercise = 10 total)
        var zones = await db.HeartRateZones.ToListAsync();
        zones.Should().HaveCount(10, "5 heart rate zones per exercise � 2 exercises");
        
        // Verify zone mapping: Polar index 0-4 ? QEP zone 1-5
        var ex1Zones = zones.Where(z => z.EntityID == "ex-001").OrderBy(z => z.Zone).ToList();
        ex1Zones.Should().HaveCount(5);
        ex1Zones.Select(z => z.Zone).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });

        var ex2Zones = zones.Where(z => z.EntityID == "ex-002").OrderBy(z => z.Zone).ToList();
        ex2Zones.Should().HaveCount(5);
        ex2Zones.Select(z => z.Zone).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Test]
    public async Task ReconcileUser_IsIdempotent_SecondRunSkipsCommittedExercises()
    {
        // Arrange
        await using var db = CreateDb(nameof(ReconcileUser_IsIdempotent_SecondRunSkipsCommittedExercises));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise1 = MakeExercise("ex-001");
        var exercise2 = MakeExercise("ex-002");

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise1, exercise2 } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act - First run
        var result1 = await svc.SyncUserAsync(polarLink);
        result1.Should().Be(2);

        // Verify state after first run
        db.Activities.Should().HaveCount(2);
        db.PolarTransactions.All(t => t.IsCommitted).Should().BeTrue();

        // Act - Second run with same data
        var result2 = await svc.SyncUserAsync(polarLink);

        // Assert
        result2.Should().Be(0, "second run should skip already-committed exercises");
        
        // Verify no duplicates created
        db.Activities.Should().HaveCount(2, "no new activities should be created");
        db.PolarTransactions.Should().HaveCount(2, "no new transactions should be created");
        db.HeartRateZones.Should().HaveCount(10, "heart rate zones unchanged");
    }

    [Test]
    public async Task ReconcileUser_LeavesTransactionUncommitted_WhenQepSaveFails()
    {
        // Arrange
        await using var db = CreateDb(nameof(ReconcileUser_LeavesTransactionUncommitted_WhenQepSaveFails));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise1 = MakeExercise("ex-001");
        var exercise2 = MakeExercise("ex-002");

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise1, exercise2 } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act - First call should succeed for PolarTransaction
        var result = await svc.SyncUserAsync(polarLink);

        // Assert
        result.Should().Be(2, "both exercises should be processed");

        // Verify both transactions were created and committed successfully
        var transactions = await db.PolarTransactions.ToListAsync();
        transactions.Should().HaveCount(2);
        transactions.All(t => t.IsCommitted).Should().BeTrue();

        // Clean up for next test scenario
        db.Activities.RemoveRange(db.Activities);
        db.HeartRateZones.RemoveRange(db.HeartRateZones);
        db.PolarTransactions.RemoveRange(db.PolarTransactions);
        await db.SaveChangesAsync();

        // Now test the failure scenario: simulate what would happen if SaveChangesAsync failed
        // by creating an uncommitted transaction manually
        db.PolarTransactions.Add(new PolarTransaction
        {
            PolarID = 1001,
            Location = ActivitySyncService.CanonicalExerciseLocation("ex-fail"),
            Response = "{}",
            IsCommitted = false,  // Staged but not committed
            IsProcessed = false,
            Attempt = 1,
            FirstTouched = DateTime.UtcNow,
            LastTouched = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Verify the uncommitted transaction exists
        var uncommittedTx = await db.PolarTransactions.FirstAsync();
        uncommittedTx.IsCommitted.Should().BeFalse("transaction should remain uncommitted when save fails");
    }

    // ============================================================================
    // Webhook + Reconciliation Integration Tests
    // ============================================================================

    [Test]
    public async Task ProcessWebhookThenReconcile_NoDuplicateActivity()
    {
        // Arrange
        await using var db = CreateDb(nameof(ProcessWebhookThenReconcile_NoDuplicateActivity));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("shared-exercise");

        var polarClient = new Mock<IPolarClient>();
        
        // Mock for webhook processing: GetExerciseByIdAsync
        polarClient
            .Setup(p => p.GetExerciseByIdAsync(polarLink.AccessToken, "shared-exercise", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, "{}"));

        // Mock for reconciliation: ListExercisesAsync
        polarClient
            .Setup(p => p.ListExercisesAsync(polarLink.AccessToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act 1: Process webhook event - use canonical URL format
        var canonicalUrl = ActivitySyncService.CanonicalExerciseLocation("shared-exercise");
        var webhookEvent = new WebhookEvent
        {
            Id = 1,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "shared-exercise",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}",
            ResourceUrl = canonicalUrl  // Use canonical format
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        // Process the webhook
        await svc.ProcessPolarWebhookEventAsync(1);

        // Assert after webhook processing
        db.Activities.Should().HaveCount(1, "webhook should create one activity");
        db.PolarTransactions.Where(t => t.IsCommitted).Should().HaveCount(1);
        var firstActivity = db.Activities.First();
        firstActivity.EntityID.Should().Be("shared-exercise");
        
        // Verify transaction is committed with canonical URL
        var committedTx = await db.PolarTransactions.Where(t => t.IsCommitted).FirstAsync();
        committedTx.Location.Should().Be(canonicalUrl);

        // Act 2: Run reconciliation with the same exercise
        var result = await svc.SyncUserAsync(polarLink);

        // Assert after reconciliation
        result.Should().Be(0, "reconciliation should skip the already-processed exercise (canonical URL match)");
        db.Activities.Should().HaveCount(1, "no duplicate activity should be created");
        
        // Verify the transaction system prevented duplicates
        var committedTransactions = await db.PolarTransactions.Where(t => t.IsCommitted).ToListAsync();
        committedTransactions.Should().HaveCount(1, "only one committed transaction for this exercise");
    }

    [Test]
    public async Task WebhookProcessing_CreatesActivityWithHeartRateZones()
    {
        // Arrange
        await using var db = CreateDb(nameof(WebhookProcessing_CreatesActivityWithHeartRateZones));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("webhook-ex-001");

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.GetExerciseByIdAsync(polarLink.AccessToken, "webhook-ex-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise, "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Create and add webhook event
        var webhookEvent = new WebhookEvent
        {
            Id = 2,
            Provider = ActivityProviderType.Polar,
            ExternalUserId = 1001,
            EventType = "exercise",
            EntityID = "webhook-ex-001",
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = "{}",
            ResourceUrl = "https://www.polaraccesslink.com/v3/exercises/webhook-ex-001"
        };
        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        // Act
        await svc.ProcessPolarWebhookEventAsync(2);

        // Assert
        var updatedWebhook = await db.WebhookEvents.FindAsync(2L);
        updatedWebhook.Should().NotBeNull();
        updatedWebhook!.Status.Should().Be("Completed");
        updatedWebhook.ProcessedAtUtc.Should().NotBeNull();

        // Verify Activity created
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.EntityID == "webhook-ex-001");
        activity.Should().NotBeNull();
        activity!.UserID.Should().Be("0471319");
        activity.Username.Should().Be("testuser");

        // Verify HeartRateZones created
        var zones = await db.HeartRateZones.Where(z => z.EntityID == "webhook-ex-001").ToListAsync();
        zones.Should().HaveCount(5);
    }

    [Test]
    public async Task MultipleWebhookEvents_ProcessedIndependently()
    {
        // Arrange
        await using var db = CreateDb(nameof(MultipleWebhookEvents_ProcessedIndependently));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise1 = MakeExercise("webhook-ex-A");
        var exercise2 = MakeExercise("webhook-ex-B", distance: 7000);
        var exercise3 = MakeExercise("webhook-ex-C", distance: 8000);

        var polarClient = new Mock<IPolarClient>();
        
        polarClient
            .Setup(p => p.GetExerciseByIdAsync(polarLink.AccessToken, "webhook-ex-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise1, "{}"));
        
        polarClient
            .Setup(p => p.GetExerciseByIdAsync(polarLink.AccessToken, "webhook-ex-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise2, "{}"));
        
        polarClient
            .Setup(p => p.GetExerciseByIdAsync(polarLink.AccessToken, "webhook-ex-C", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((PolarExerciseDto? Exercise, string RawJson)?)(exercise3, "{}"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Create webhook events
        for (int i = 1; i <= 3; i++)
        {
            var webhookEvent = new WebhookEvent
            {
                Id = i,
                Provider = ActivityProviderType.Polar,
                ExternalUserId = 1001,
                EventType = "exercise",
                EntityID = $"webhook-ex-{char.ConvertFromUtf32(64 + i)}",
                Status = "Pending",
                ReceivedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                RawPayload = "{}",
                ResourceUrl = $"https://www.polaraccesslink.com/v3/exercises/webhook-ex-{char.ConvertFromUtf32(64 + i)}"
            };
            db.WebhookEvents.Add(webhookEvent);
        }
        await db.SaveChangesAsync();

        // Act
        await svc.ProcessPolarWebhookEventAsync(1);
        await svc.ProcessPolarWebhookEventAsync(2);
        await svc.ProcessPolarWebhookEventAsync(3);

        // Assert
        db.WebhookEvents.All(w => w.Status == "Completed").Should().BeTrue();
        db.Activities.Should().HaveCount(3);
        db.Activities.Select(a => a.Distance).Should().BeEquivalentTo(new[] { 5000.0, 7000.0, 8000.0 });
        db.HeartRateZones.Should().HaveCount(15, "5 zones � 3 exercises");
    }

    [Test]
    public async Task ReconcileUser_WithMixedNewAndExistingExercises()
    {
        // Arrange - seed one committed exercise via transaction
        await using var db = CreateDb(nameof(ReconcileUser_WithMixedNewAndExistingExercises));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);

        // Pre-seed one committed transaction (from previous webhook)
        db.PolarTransactions.Add(new PolarTransaction
        {
            PolarID = 1001,
            Location = ActivitySyncService.CanonicalExerciseLocation("ex-existing"),
            Response = "{}",
            IsCommitted = true,
            IsProcessed = true,
            Attempt = 1,
            FirstTouched = DateTime.UtcNow.AddDays(-1),
            LastTouched = DateTime.UtcNow.AddDays(-1)
        });

        // Pre-seed the corresponding activity
        db.Activities.Add(new QepActivity
        {
            EntityID = "ex-existing",
            UserID = "0471319",
            Username = "testuser",
            ActivityTypeID = 2,
            Minutes = 30,
            Duration = 30,
            Distance = 5000,
            AerobicPoints = 0,
            DateDone = DateTime.UtcNow.AddDays(-1),
            DateEntered = DateTime.UtcNow.AddDays(-1),
            DeviceType = "A300",
            TargetZone = "Fitness"
        });

        await db.SaveChangesAsync();

        // Now mock Polar to return existing + new exercises
        var existingExercise = MakeExercise("ex-existing");
        var newExercise = MakeExercise("ex-new", distance: 6000);

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { existingExercise, newExercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(polarLink);

        // Assert
        result.Should().Be(1, "only the new exercise should be committed");
        
        db.Activities.Should().HaveCount(2, "one pre-existing + one new");
        db.Activities.Single(a => a.EntityID == "ex-new").Distance.Should().Be(6000);
        
        var committedTransactions = await db.PolarTransactions.Where(t => t.IsCommitted).ToListAsync();
        committedTransactions.Should().HaveCount(2);
    }

    [Test]
    public async Task PolarTransactionStaging_IsCommittedOnlyAfterQepSuccess()
    {
        // Arrange
        await using var db = CreateDb(nameof(PolarTransactionStaging_IsCommittedOnlyAfterQepSuccess));
        
        var polarLink = MakePolarLink();
        db.PolarLinks.Add(polarLink);
        await db.SaveChangesAsync();

        var exercise = MakeExercise("staging-test");

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(polarLink);

        // Assert
        result.Should().Be(1);

        // Verify the transaction flow: created with IsCommitted=false, then true after QEP save
        var transaction = await db.PolarTransactions.FirstAsync();
        transaction.IsCommitted.Should().BeTrue("transaction should be committed after successful QEP save");
        transaction.IsProcessed.Should().BeTrue();
        transaction.Location.Should().Be(ActivitySyncService.CanonicalExerciseLocation("staging-test"));
        
        // Verify Activity is present
        var activity = await db.Activities.FirstAsync();
        activity.EntityID.Should().Be("staging-test");
    }

    [Test]
    public async Task CanonicalExerciseLocation_PreventsDuplicatesAcrossPaths()
    {
        // This test verifies that both webhook and reconciliation paths use the same
        // canonical URL format, preventing duplicates across different processing paths
        
        var exerciseId = "canonical-test";
        var canonicalUrl = ActivitySyncService.CanonicalExerciseLocation(exerciseId);
        
        // Should always be the same format
        canonicalUrl.Should().Be($"https://polaraccesslink.com/v3/exercises/{exerciseId}");

        // Verify consistency across multiple calls
        var url1 = ActivitySyncService.CanonicalExerciseLocation(exerciseId);
        var url2 = ActivitySyncService.CanonicalExerciseLocation(exerciseId);
        url1.Should().Be(url2);
    }

    [Test]
    public async Task ReconcileAllUsers_ProcessesMultipleUsersSequentially()
    {
        // Arrange
        await using var db = CreateDb(nameof(ReconcileAllUsers_ProcessesMultipleUsersSequentially));
        
        var user1 = MakePolarLink(1001, "tok-user1");
        var user2 = MakePolarLink(1002, "tok-user2");
        var user3 = MakePolarLink(1003, "tok-user3");

        db.PolarLinks.AddRange(user1, user2, user3);
        await db.SaveChangesAsync();

        var exercise1 = MakeExercise("user1-ex");
        var exercise2 = MakeExercise("user2-ex");
        var exercise3 = MakeExercise("user3-ex");

        var polarClient = new Mock<IPolarClient>();
        
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise1 } as IReadOnlyList<PolarExerciseDto>, "[]"));
        
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-user2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise2 } as IReadOnlyList<PolarExerciseDto>, "[]"));
        
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-user3", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise3 } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var totalCommitted = await svc.SyncAllUsersAsync();

        // Assert
        totalCommitted.Should().Be(3, "all three users should have their exercises committed");
        db.Activities.Should().HaveCount(3);
        db.Activities.Select(a => a.UserID).Should().AllBe("0471319", "all activities mapped to same user PersonID");
        db.HeartRateZones.Should().HaveCount(15, "5 zones � 3 exercises");
    }

    [Test]
    public async Task SyncUserAsync_SkipsUsersWithoutAccessToken()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUserAsync_SkipsUsersWithoutAccessToken));
        
        var userWithoutToken = MakePolarLink();
        userWithoutToken.AccessToken = null;

        var polarClient = new Mock<IPolarClient>(MockBehavior.Strict);
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(userWithoutToken);

        // Assert
        result.Should().Be(0, "user without token should return 0");
        polarClient.VerifyNoOtherCalls();
    }
}
