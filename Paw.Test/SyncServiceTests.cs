using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Unit tests for ActivitySyncService sync methods:
///   - SyncUserAsync
///   - SyncAllUsersAsync
///   - CanonicalExerciseLocation (helper)
///
/// All tests use EF Core InMemory database and a mocked IPolarClient,
/// so no real database or network connection is required.
/// </summary>
[TestFixture]
[Category("Unit")]
public class SyncServiceTests
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
        PolarID   = polarId,
        PersonID  = "0471319",
        Username  = "testuser",
        Email     = "testuser@southern.edu",
        DeviceType = "A300",
        TargetZone = "Fitness",
        AccessToken = token
    };

    /// <summary>
    /// Builds a <see cref="PolarExerciseDto"/> whose <c>UploadTime</c> is within the last 30 days
    /// so it passes the since-filter inside <see cref="ActivitySyncService.SyncUserAsync"/>.
    /// </summary>
    private static PolarExerciseDto MakeExercise(
        string id = "ex001",
        string? uploadTime = null,
        string sport = "RUNNING",
        string duration = "PT30M",
        double? distance = 5000)
    {
        return new PolarExerciseDto
        {
            Id              = id,
            UploadTime      = uploadTime ?? DateTime.UtcNow.AddDays(-1).ToString("o"),
            StartTime       = DateTime.UtcNow.AddDays(-1).ToString("o"),
            DurationIso8601 = duration,
            Sport           = sport,
            Distance        = distance,
            HeartRateZones  = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50,  UpperLimit = 100, InZone = "PT5M"  },
                new() { Index = 1, LowerLimit = 100, UpperLimit = 130, InZone = "PT10M" },
                new() { Index = 2, LowerLimit = 130, UpperLimit = 150, InZone = "PT10M" },
                new() { Index = 3, LowerLimit = 150, UpperLimit = 170, InZone = "PT5M"  },
                new() { Index = 4, LowerLimit = 170, UpperLimit = 200, InZone = "PT0S"  }
            }
        };
    }

    // CanonicalExerciseLocation (static internal helper)

    [Test]
    public void CanonicalExerciseLocation_ReturnsExpectedUrl()
    {
        var url = ActivitySyncService.CanonicalExerciseLocation("abc123");
        url.Should().Be("https://polaraccesslink.com/v3/exercises/abc123");
    }

    [Test]
    public void CanonicalExerciseLocation_WithDifferentId_ReturnsExpectedUrl()
    {
        var url = ActivitySyncService.CanonicalExerciseLocation("y6deXzab");
        url.Should().Be("https://polaraccesslink.com/v3/exercises/y6deXzab");
    }

    // SyncUserAsync � no access token

    [Test]
    public async Task SyncUser_NoAccessToken_ReturnsZeroWithoutCallingPolar()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_NoAccessToken_ReturnsZeroWithoutCallingPolar));
        var polarClient = new Mock<IPolarClient>(MockBehavior.Strict);
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        var link = MakePolarLink();
        link.AccessToken = null; // No token

        // Act
        var result = await svc.SyncUserAsync(link);

        // Assert
        result.Should().Be(0);
        // Strict mock: ListExercisesAsync must NOT have been called
        polarClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SyncUser_EmptyAccessToken_ReturnsZero()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_EmptyAccessToken_ReturnsZero));
        var polarClient = new Mock<IPolarClient>(MockBehavior.Strict);
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        var link = MakePolarLink();
        link.AccessToken = "   "; // whitespace only

        // Act
        var result = await svc.SyncUserAsync(link);

        // Assert
        result.Should().Be(0);
        polarClient.VerifyNoOtherCalls();
    }

    // SyncUserAsync � polar API returns empty list

    [Test]
    public async Task SyncUser_PolarReturnsNoExercises_ReturnsZero()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_PolarReturnsNoExercises_ReturnsZero));
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto>().AsReadOnly() as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(0);
        db.PolarTransactions.Should().BeEmpty();
        db.Activities.Should().BeEmpty();
    }

    // SyncUserAsync � happy path: new exercise committed

    [Test]
    public async Task SyncUser_NewExercise_CommitsOneActivityWithZones()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_NewExercise_CommitsOneActivityWithZones));
        var exercise = MakeExercise("ex001");
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(1, "one new exercise should be committed");

        // PolarTransaction created and committed
        var tx = db.PolarTransactions.Single();
        tx.Location.Should().Be(ActivitySyncService.CanonicalExerciseLocation("ex001"));
        tx.IsCommitted.Should().BeTrue();
        tx.IsProcessed.Should().BeTrue();
        tx.Attempt.Should().Be(1);

        // Activity row created
        var activity = db.Activities.Single();
        activity.EntityID.Should().Be("ex001");
        activity.UserID.Should().Be("0471319");
        activity.Username.Should().Be("testuser");
        activity.DeviceType.Should().Be("A300");
        activity.TargetZone.Should().Be("Fitness");
        activity.Distance.Should().Be(5000);

        // HeartRateZones saved
        var zones = db.HeartRateZones.Where(z => z.EntityID == "ex001").ToList();
        zones.Should().HaveCount(5);
        zones.Select(z => z.Zone).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    // SyncUserAsync � deduplication: already-committed exercise is skipped

    [Test]
    public async Task SyncUser_AlreadyCommittedExercise_IsSkipped()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_AlreadyCommittedExercise_IsSkipped));

        // Pre-seed a committed transaction
        db.PolarTransactions.Add(new PolarTransaction
        {
            PolarID      = 1001,
            Location     = ActivitySyncService.CanonicalExerciseLocation("ex001"),
            Response     = "{}",
            IsCommitted  = true,
            IsProcessed  = true,
            Attempt      = 1,
            FirstTouched = DateTime.UtcNow,
            LastTouched  = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var exercise = MakeExercise("ex001");
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert � already committed, so nothing new should be committed
        result.Should().Be(0, "the exercise was already committed");
        db.Activities.Should().BeEmpty("no new activity should have been inserted");
    }

    // SyncUserAsync � since filter: old upload_time excluded

    [Test]
    public async Task SyncUser_ExerciseOlderThanSince_IsFiltered()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_ExerciseOlderThanSince_IsFiltered));

        // Exercise uploaded 60 days ago � outside the default 30-day window
        var oldExercise = MakeExercise("oldex", uploadTime: DateTime.UtcNow.AddDays(-60).ToString("o"));

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { oldExercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act � default since = 30 days ago
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(0, "the exercise is older than the since window");
        db.Activities.Should().BeEmpty();
        db.PolarTransactions.Should().BeEmpty();
    }

    [Test]
    public async Task SyncUser_ExerciseWithNullUploadTime_IsIncluded()
    {
        // Exercises with a missing/unparseable upload_time are included by design
        await using var db = CreateDb(nameof(SyncUser_ExerciseWithNullUploadTime_IsIncluded));

        var exercise = MakeExercise("exNull");
        exercise.UploadTime = null;

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        var result = await svc.SyncUserAsync(MakePolarLink());

        result.Should().Be(1, "exercises with null upload_time should not be filtered out");
        db.Activities.Should().HaveCount(1);
    }

    [Test]
    public async Task SyncUser_CustomSinceDate_FiltersCorrectly()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_CustomSinceDate_FiltersCorrectly));

        // One recent, one old
        var recentExercise = MakeExercise("recent", uploadTime: DateTime.UtcNow.AddDays(-5).ToString("o"));
        var oldExercise    = MakeExercise("old",    uploadTime: DateTime.UtcNow.AddDays(-20).ToString("o"));

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { recentExercise, oldExercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act � custom since = 10 days ago
        var result = await svc.SyncUserAsync(MakePolarLink(), since: DateTime.UtcNow.AddDays(-10));

        // Assert
        result.Should().Be(1, "only the recent exercise is within the custom window");
        db.Activities.Single().EntityID.Should().Be("recent");
    }

    // SyncUserAsync � multiple exercises in one run

    [Test]
    public async Task SyncUser_MultipleNewExercises_CommitsAll()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_MultipleNewExercises_CommitsAll));

        var exercises = new List<PolarExerciseDto>
        {
            MakeExercise("ex001"),
            MakeExercise("ex002"),
            MakeExercise("ex003")
        };

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((exercises as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(3);
        db.Activities.Should().HaveCount(3);
        db.PolarTransactions.Should().HaveCount(3);
        db.PolarTransactions.All(t => t.IsCommitted).Should().BeTrue();
    }

    [Test]
    public async Task SyncUser_MixOfNewAndAlreadyCommitted_CommitsOnlyNew()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_MixOfNewAndAlreadyCommitted_CommitsOnlyNew));

        // Pre-seed one committed transaction
        db.PolarTransactions.Add(new PolarTransaction
        {
            PolarID      = 1001,
            Location     = ActivitySyncService.CanonicalExerciseLocation("ex001"),
            Response     = "{}",
            IsCommitted  = true,
            IsProcessed  = true,
            Attempt      = 1,
            FirstTouched = DateTime.UtcNow,
            LastTouched  = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var exercises = new List<PolarExerciseDto>
        {
            MakeExercise("ex001"), // already committed
            MakeExercise("ex002"), // new
        };

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((exercises as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(1, "only the new exercise should be committed");
        db.Activities.Single().EntityID.Should().Be("ex002");
    }

    // SyncUserAsync � retry logic: uncommitted transaction gets re-attempted

    [Test]
    public async Task SyncUser_ExistingUncommittedTransaction_IncrementsAttempt()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_ExistingUncommittedTransaction_IncrementsAttempt));

        // Pre-seed an uncommitted (previously-failed) transaction
        db.PolarTransactions.Add(new PolarTransaction
        {
            PolarID      = 1001,
            Location     = ActivitySyncService.CanonicalExerciseLocation("ex001"),
            Response     = "{}",
            IsCommitted  = false,
            IsProcessed  = false,
            Attempt      = 2,
            FirstTouched = DateTime.UtcNow.AddDays(-1),
            LastTouched  = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var exercise = MakeExercise("ex001");
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(1, "the exercise should be retried and committed");

        var tx = db.PolarTransactions.Single();
        tx.Attempt.Should().Be(3, "attempt count should increment from 2 to 3");
        tx.IsCommitted.Should().BeTrue();
        tx.IsProcessed.Should().BeTrue();
    }

    // SyncUserAsync � Polar API throws; returns 0, does not crash

    [Test]
    public async Task SyncUser_PolarApiThrows_ReturnsZeroGracefully()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_PolarApiThrows_ReturnsZeroGracefully));

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Polar API unavailable"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(0, "a Polar API failure should be handled gracefully");
        db.Activities.Should().BeEmpty();
        db.PolarTransactions.Should().BeEmpty();
    }

    // SyncUserAsync � PolarTransaction stores correct fields

    [Test]
    public async Task SyncUser_NewExercise_PolarTransactionHasCorrectFields()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_NewExercise_PolarTransactionHasCorrectFields));

        var exercise = MakeExercise("exFieldCheck");
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.SyncUserAsync(MakePolarLink(polarId: 1001));

        // Assert
        var tx = db.PolarTransactions.Single();
        tx.PolarID.Should().Be(1001);
        tx.Location.Should().Be("https://polaraccesslink.com/v3/exercises/exFieldCheck");
        tx.IsProcessed.Should().BeTrue();
        tx.IsCommitted.Should().BeTrue();
        tx.Attempt.Should().Be(1);
        tx.FirstTouched.Should().NotBeNull();
        tx.LastTouched.Should().NotBeNull();
        tx.Response.Should().NotBeNullOrEmpty("the raw exercise JSON should be stored");
    }

    // SyncUserAsync � activity duration mapping

    [Test]
    [TestCase("PT30M",   30, 30.0)]
    [TestCase("PT45M",   45, 45.0)]
    [TestCase("PT1H",    60, 60.0)]
    [TestCase("PT1H30M", 90, 90.0)]
    public async Task SyncUser_DurationMappedToMinutes(string iso, int expectedMinutes, double expectedDuration)
    {
        // Arrange
        var dbName = $"{nameof(SyncUser_DurationMappedToMinutes)}_{iso}";
        await using var db = CreateDb(dbName);

        var exercise = MakeExercise("dur-test", duration: iso);
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.SyncUserAsync(MakePolarLink());

        // Assert
        var activity = db.Activities.Single();
        activity.Minutes.Should().Be(expectedMinutes);
        activity.Duration.Should().BeApproximately(expectedDuration, 0.1);
    }

    // SyncUserAsync � heart rate zones saved with correct zone numbers

    [Test]
    public async Task SyncUser_HeartRateZones_SavedWithCorrectZoneNumbers()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_HeartRateZones_SavedWithCorrectZoneNumbers));

        var exercise = MakeExercise("exZones");
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        await svc.SyncUserAsync(MakePolarLink());

        // Assert � Polar index 0-4 must map to QEP zone 1-5
        var zones = db.HeartRateZones
            .Where(z => z.EntityID == "exZones")
            .OrderBy(z => z.Zone)
            .ToList();

        zones.Should().HaveCount(5);
        zones.Select(z => z.Zone).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });

        // Zone 2: lower=100, upper=130, 10 min
        var zone2 = zones[1];
        zone2.Lower.Should().Be(100);
        zone2.Upper.Should().Be(130);
        zone2.Duration.Should().BeApproximately(10.0, 0.1);
    }

    [Test]
    public async Task SyncUser_ExerciseWithNoHeartRateZones_CommitsActivityWithoutZones()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_ExerciseWithNoHeartRateZones_CommitsActivityWithoutZones));

        var exercise = MakeExercise("exNoZones");
        exercise.HeartRateZones = null;

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(1, "missing heart rate zones should not prevent the activity being committed");
        db.Activities.Should().HaveCount(1);
        db.HeartRateZones.Should().BeEmpty();
    }

    [Test]
    public async Task SyncUser_ExerciseWithEmptyHeartRateZones_CommitsActivityWithoutZones()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_ExerciseWithEmptyHeartRateZones_CommitsActivityWithoutZones));

        var exercise = MakeExercise("exEmptyZones");
        exercise.HeartRateZones = new List<PolarHeartRateZoneDto>();

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(1);
        db.Activities.Should().HaveCount(1);
        db.HeartRateZones.Should().BeEmpty();
    }

    // SyncUserAsync � update existing activity when exercise already present
    // in Activity table but not yet committed in PolarTransactions

    [Test]
    public async Task SyncUser_ExistingActivityUpdated_WhenExerciseReprocessed()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_ExistingActivityUpdated_WhenExerciseReprocessed));

        // Seed an existing activity (uncommitted transaction ? eligible for retry)
        db.PolarTransactions.Add(new PolarTransaction
        {
            PolarID      = 1001,
            Location     = ActivitySyncService.CanonicalExerciseLocation("ex001"),
            Response     = "{}",
            IsCommitted  = false,
            IsProcessed  = false,
            Attempt      = 1,
            FirstTouched = DateTime.UtcNow.AddDays(-1),
            LastTouched  = DateTime.UtcNow.AddDays(-1)
        });
        db.Activities.Add(new QepActivity
        {
            EntityID       = "ex001",
            UserID         = "0471319",
            Username       = "testuser",
            ActivityTypeID = 2,
            Minutes        = 10, // stale
            Duration       = 10,
            Distance       = 0,
            AerobicPoints  = 0,
            DateDone       = DateTime.UtcNow.AddDays(-1),
            DateEntered    = DateTime.UtcNow.AddDays(-1),
            DeviceType     = "A300",
            TargetZone     = "Fitness"
        });
        await db.SaveChangesAsync();

        // New exercise with updated distance and duration
        var updatedExercise = MakeExercise("ex001", duration: "PT45M", distance: 9000);
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { updatedExercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncUserAsync(MakePolarLink());

        // Assert
        result.Should().Be(1);
        var activity = db.Activities.Single(a => a.EntityID == "ex001");
        activity.Minutes.Should().Be(45, "duration should be updated from 10 to 45");
        activity.Distance.Should().Be(9000, "distance should be updated to 9000");
    }

    // SyncUserAsync � cancellation respected

    [Test]
    public async Task SyncUser_CancelledToken_StopsProcessingEarly()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncUser_CancelledToken_StopsProcessingEarly));

        // Three exercises to give the loop something to iterate over
        var exercises = new List<PolarExerciseDto>
        {
            MakeExercise("ex001"),
            MakeExercise("ex002"),
            MakeExercise("ex003")
        };

        using var cts = new CancellationTokenSource();

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((exercises as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Cancel immediately � the in-loop IsCancellationRequested check exits without processing,
        // and SaveChangesAsync will throw OperationCanceledException if it is reached first.
        cts.Cancel();

        // Act � either returns 0 (loop exited via IsCancellationRequested guard)
        // or throws OperationCanceledException (SaveChangesAsync propagates cancellation).
        // Both are valid, observable behaviours for a pre-cancelled token.
        int result = 0;
        try
        {
            result = await svc.SyncUserAsync(MakePolarLink(), cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: EF propagated the cancellation
            result = -1;
        }

        // Assert � fewer than all 3 exercises were committed
        result.Should().BeLessThan(3, "cancellation should prevent all exercises from being processed");
    }

    // SyncAllUsersAsync

    [Test]
    public async Task SyncAllUsers_NoActiveLinks_ReturnsZero()
    {
        // Arrange � db has no PolarLinks with tokens
        await using var db = CreateDb(nameof(SyncAllUsers_NoActiveLinks_ReturnsZero));
        var polarClient = new Mock<IPolarClient>();
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncAllUsersAsync();

        // Assert
        result.Should().Be(0);
        polarClient.Verify(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SyncAllUsers_LinksWithoutTokens_AreSkipped()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncAllUsers_LinksWithoutTokens_AreSkipped));

        // Two links: one with token, one without
        db.PolarLinks.AddRange(
            new PolarLink { PolarID = 1001, PersonID = "P001", Username = "user1", Email = "u1@southern.edu", AccessToken = "tok-001" },
            new PolarLink { PolarID = 1002, PersonID = "P002", Username = "user2", Email = "u2@southern.edu", AccessToken = null }
        );
        await db.SaveChangesAsync();

        var exercise = MakeExercise("ex001");
        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncAllUsersAsync();

        // Assert
        result.Should().Be(1, "only the user with a token contributes an exercise");
        // ListExercisesAsync called exactly once (for the user with the token)
        polarClient.Verify(p => p.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SyncAllUsers_MultipleUsers_SumsTotalCommitted()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncAllUsers_MultipleUsers_SumsTotalCommitted));

        db.PolarLinks.AddRange(
            new PolarLink { PolarID = 2001, PersonID = "P001", Username = "user1", Email = "u1@southern.edu", AccessToken = "tok-2001" },
            new PolarLink { PolarID = 2002, PersonID = "P002", Username = "user2", Email = "u2@southern.edu", AccessToken = "tok-2002" }
        );
        await db.SaveChangesAsync();

        var exercise1 = MakeExercise("ex-u1");
        var exercise2 = MakeExercise("ex-u2");

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-2001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise1 } as IReadOnlyList<PolarExerciseDto>, "[]"));
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-2002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise2 } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncAllUsersAsync();

        // Assert
        result.Should().Be(2, "one exercise committed per user");
        db.Activities.Should().HaveCount(2);
    }

    [Test]
    public async Task SyncAllUsers_OneUserFails_OthersStillProcessed()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncAllUsers_OneUserFails_OthersStillProcessed));

        db.PolarLinks.AddRange(
            new PolarLink { PolarID = 3001, PersonID = "P001", Username = "user1", Email = "u1@southern.edu", AccessToken = "tok-3001" },
            new PolarLink { PolarID = 3002, PersonID = "P002", Username = "user2", Email = "u2@southern.edu", AccessToken = "tok-3002" }
        );
        await db.SaveChangesAsync();

        var exercise2 = MakeExercise("ex-u2");

        var polarClient = new Mock<IPolarClient>();
        // First user's Polar call throws
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-3001", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network error"));
        // Second user succeeds
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-3002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise2 } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncAllUsersAsync();

        // Assert
        result.Should().Be(1, "second user's exercise should still be committed despite first user failing");
        db.Activities.Single().EntityID.Should().Be("ex-u2");
    }

    [Test]
    public async Task SyncAllUsers_OnlyLinksWithNonEmptyTokensQueried()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncAllUsers_OnlyLinksWithNonEmptyTokensQueried));

        db.PolarLinks.AddRange(
            new PolarLink { PolarID = 4001, PersonID = "P001", Username = "user1", Email = "u1@s.edu", AccessToken = "" },
            new PolarLink { PolarID = 4002, PersonID = "P002", Username = "user2", Email = "u2@s.edu", AccessToken = null }
        );
        await db.SaveChangesAsync();

        var polarClient = new Mock<IPolarClient>(MockBehavior.Strict);
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act � neither link has a token so Polar should never be called
        var result = await svc.SyncAllUsersAsync();

        // Assert
        result.Should().Be(0);
        polarClient.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SyncAllUsers_WithCustomSinceDate_PassesSinceDateToEachUser()
    {
        // Arrange
        await using var db = CreateDb(nameof(SyncAllUsers_WithCustomSinceDate_PassesSinceDateToEachUser));

        db.PolarLinks.Add(
            new PolarLink { PolarID = 5001, PersonID = "P001", Username = "user1", Email = "u1@s.edu", AccessToken = "tok-5001" }
        );
        await db.SaveChangesAsync();

        // Exercise uploaded 7 days ago � passes a 10-day custom window
        var exercise = MakeExercise("exCustom", uploadTime: DateTime.UtcNow.AddDays(-7).ToString("o"));

        var polarClient = new Mock<IPolarClient>();
        polarClient
            .Setup(p => p.ListExercisesAsync("tok-5001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto> { exercise } as IReadOnlyList<PolarExerciseDto>, "[]"));

        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act
        var result = await svc.SyncAllUsersAsync(since: DateTime.UtcNow.AddDays(-10));

        // Assert
        result.Should().Be(1, "exercise within the custom window should be committed");
    }

    [Test]
    public void SyncAllUsers_CancelledToken_ThrowsOperationCancelledException()
    {
        // Arrange � create a fresh db synchronously (can't use await in a sync test)
        var options = new DbContextOptionsBuilder<PawDbContext>()
            .UseInMemoryDatabase(nameof(SyncAllUsers_CancelledToken_ThrowsOperationCancelledException))
            .Options;
        using var db = new PawDbContext(options);

        for (int i = 1; i <= 5; i++)
        {
            db.PolarLinks.Add(new PolarLink
            {
                PolarID     = 6000 + i,
                PersonID    = $"P{i:D3}",
                Username    = $"user{i}",
                Email       = $"u{i}@s.edu",
                AccessToken = $"tok-{i}"
            });
        }
        db.SaveChanges();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancelled

        var polarClient = new Mock<IPolarClient>();
        var svc = new ActivitySyncService(db, CreateDbFactory(db), polarClient.Object, NullLogger<ActivitySyncService>.Instance);

        // Act & Assert � EF propagates OperationCanceledException for a pre-cancelled token
        Assert.ThrowsAsync<OperationCanceledException>(
            () => svc.SyncAllUsersAsync(cancellationToken: cts.Token));
    }
}

