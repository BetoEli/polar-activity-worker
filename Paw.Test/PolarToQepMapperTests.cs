using FluentAssertions;
using Paw.Core.Domain;
using Paw.Infrastructure.Mappers;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Unit tests for PolarToQepMapper - validates mapping logic without database.
/// These tests verify that Polar API data is correctly transformed to QEP entities.
/// 
/// NOTE: These are pure unit tests testing mapper logic only.
/// To test actual database insertion, use the ActivitySyncService integration tests
/// or run the application against QEPTest database.
/// </summary>
[TestFixture]
[Category("Unit")]
public class PolarToQepMapperTests
{
    [Test]
    public void ToQepActivity_MapsBasicFields_Correctly()
    {
        // Arrange
        var polarLink = new PolarLink
        {
            PolarID = 59002246,
            PersonID = "0471319",
            Username = "testuser",
            Email = "testuser@southern.edu",
            DeviceType = "A300",
            TargetZone = "Fitness"
        };

        var exercise = new PolarExerciseDto
        {
            Id = "y6deXzab",
            StartTime = "2026-01-25T10:30:00.000Z",
            DurationIso8601 = "PT30M",
            Distance = 5000,
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },
                new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "PT10M" },
                new() { Index = 2, LowerLimit = 120, UpperLimit = 140, InZone = "PT8M" },
                new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT5M" },
                new() { Index = 4, LowerLimit = 160, UpperLimit = 200, InZone = "PT2M" }
            }
        };

        // Act
        var result = PolarToQepMapper.ToQepActivity(exercise, polarLink);

        // Assert - Check Activity fields
        Console.WriteLine("=== QEP ACTIVITY MAPPING ===");
        Console.WriteLine($"EntityID: {result.EntityID}");
        Console.WriteLine($"UserID (PersonID): {result.UserID}");
        Console.WriteLine($"Username: {result.Username}");
        Console.WriteLine($"ActivityTypeID: {result.ActivityTypeID}");
        Console.WriteLine($"Minutes: {result.Minutes}");
        Console.WriteLine($"Duration (minutes): {result.Duration}");
        Console.WriteLine($"Distance (meters): {result.Distance}");
        Console.WriteLine($"AerobicPoints: {result.AerobicPoints}");
        Console.WriteLine($"DateDone: {result.DateDone}");
        Console.WriteLine($"DateEntered: {result.DateEntered}");
        Console.WriteLine($"DeviceType: {result.DeviceType}");
        Console.WriteLine($"TargetZone: {result.TargetZone}");
        Console.WriteLine($"Measurement: {result.Measurement}");

        result.EntityID.Should().Be("y6deXzab");
        result.UserID.Should().Be("0471319");
        result.Username.Should().Be("testuser");
        result.ActivityTypeID.Should().Be(2);
        result.Minutes.Should().Be(30);
        result.Duration.Should().Be(30);
        result.Distance.Should().Be(5000);
        result.DeviceType.Should().Be("A300");
        result.TargetZone.Should().Be("Fitness");
        result.Measurement.Should().Be(0);
    }

    [Test]
    public void ToQepHeartRateZones_MapsZones_Correctly()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "y6deXzab",
            StartTime = "2026-01-25T10:30:00.000Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },   // 5 minutes
                new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "PT10M" },  // 10 minutes
                new() { Index = 2, LowerLimit = 120, UpperLimit = 140, InZone = "PT8M" },   // 8 minutes
                new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT5M" },   // 5 minutes
                new() { Index = 4, LowerLimit = 160, UpperLimit = 200, InZone = "PT2M" }    // 2 minutes
            }
        };

        // Act
        var result = PolarToQepMapper.ToQepHeartRateZones(exercise);

        // Assert
        Console.WriteLine("\n=== QEP HEART RATE ZONES MAPPING ===");
        Console.WriteLine($"Total zones created: {result.Count}");
        Console.WriteLine("\nZone Details (will be saved to HeartRateZone table):");
        Console.WriteLine("-------------------------------------------------------");

        result.Should().HaveCount(5);

        for (int i = 0; i < result.Count; i++)
        {
            var zone = result[i];
            Console.WriteLine($"\nZone {i + 1}:");
            Console.WriteLine($"  EntityID: {zone.EntityID}");
            Console.WriteLine($"  Zone Number: {zone.Zone}");
            Console.WriteLine($"  Lower BPM: {zone.Lower}");
            Console.WriteLine($"  Upper BPM: {zone.Upper}");
            Console.WriteLine($"  Duration (minutes): {zone.Duration}");
        }

        // Verify Zone 1 (Polar index 0 → QEP zone 1)
        var zone1 = result[0];
        zone1.EntityID.Should().Be("y6deXzab");
        zone1.Zone.Should().Be(1); // Polar index 0 → QEP zone 1
        zone1.Lower.Should().Be(50);
        zone1.Upper.Should().Be(100);
        zone1.Duration.Should().Be(5); // 5 minutes = 300 seconds

        // Verify Zone 2 (Polar index 1 → QEP zone 2)
        var zone2 = result[1];
        zone2.EntityID.Should().Be("y6deXzab");
        zone2.Zone.Should().Be(2);
        zone2.Lower.Should().Be(100);
        zone2.Upper.Should().Be(120);
        zone2.Duration.Should().Be(10); // 10 minutes = 600 seconds

        // Verify Zone 3 (Polar index 2 → QEP zone 3)
        var zone3 = result[2];
        zone3.EntityID.Should().Be("y6deXzab");
        zone3.Zone.Should().Be(3);
        zone3.Lower.Should().Be(120);
        zone3.Upper.Should().Be(140);
        zone3.Duration.Should().Be(8); // 8 minutes = 480 seconds

        // Verify Zone 4 (Polar index 3 → QEP zone 4)
        var zone4 = result[3];
        zone4.EntityID.Should().Be("y6deXzab");
        zone4.Zone.Should().Be(4);
        zone4.Lower.Should().Be(140);
        zone4.Upper.Should().Be(160);
        zone4.Duration.Should().Be(5); // 5 minutes = 300 seconds

        // Verify Zone 5 (Polar index 4 → QEP zone 5)
        var zone5 = result[4];
        zone5.EntityID.Should().Be("y6deXzab");
        zone5.Zone.Should().Be(5);
        zone5.Lower.Should().Be(160);
        zone5.Upper.Should().Be(200);
        zone5.Duration.Should().Be(2); // 2 minutes = 120 seconds
    }

    [Test]
    public void ToQepHeartRateZones_HandlesEmptyZones()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "abc123",
            StartTime = "2026-01-25T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>()
        };

        // Act
        var result = PolarToQepMapper.ToQepHeartRateZones(exercise);

        // Assert
        Console.WriteLine("\n=== EMPTY HEART RATE ZONES ===");
        Console.WriteLine($"Result count: {result.Count}");
        result.Should().BeEmpty();
    }

    [Test]
    public void ToQepHeartRateZones_HandlesNullZones()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "abc123",
            StartTime = "2026-01-25T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = null
        };

        // Act
        var result = PolarToQepMapper.ToQepHeartRateZones(exercise);

        // Assert
        Console.WriteLine("\n=== NULL HEART RATE ZONES ===");
        Console.WriteLine($"Result count: {result.Count}");
        result.Should().BeEmpty();
    }

    [Test]
    public void AerobicPoints_CalculatesCorrectly()
    {
        // Arrange
        var polarLink = new PolarLink
        {
            PersonID = "0471319",
            Username = "testuser",
            DeviceType = "A300",
            TargetZone = "Fitness"
        };

        var exercise = new PolarExerciseDto
        {
            Id = "test123",
            StartTime = "2026-01-25T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },   // Zone 1: 5 min → 0 points (less than 10 min)
                new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "PT10M" },  // Zone 2: 10 min → 1 point
                new() { Index = 2, LowerLimit = 120, UpperLimit = 140, InZone = "PT10M" },  // Zone 3: 10 min → 2 points
                new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT10M" },  // Zone 4: 10 min → 3 points
                new() { Index = 4, LowerLimit = 160, UpperLimit = 200, InZone = "PT5M" }    // Zone 5: 5 min → 0 points (less than 10 min)
            }
        };

        // Act
        var result = PolarToQepMapper.ToQepActivity(exercise, polarLink);

        // Assert
        Console.WriteLine("\n=== AEROBIC POINTS CALCULATION ===");
        Console.WriteLine("QEP Formula:");
        Console.WriteLine("  Zones 1-2: 1 point per 10 minutes");
        Console.WriteLine("  Zone 3: 2 points per 10 minutes");
        Console.WriteLine("  Zones 4-5: 3 points per 10 minutes");
        Console.WriteLine("\nCalculation:");
        Console.WriteLine("  Zone 1 (5 min): 0 points (< 10 min)");
        Console.WriteLine("  Zone 2 (10 min): 1 point");
        Console.WriteLine("  Zone 3 (10 min): 2 points");
        Console.WriteLine("  Zone 4 (10 min): 3 points");
        Console.WriteLine("  Zone 5 (5 min): 0 points (< 10 min)");
        Console.WriteLine($"  Total: {result.AerobicPoints} points");

        // Expected: 1 + 2 + 3 = 6 points
        result.AerobicPoints.Should().Be(6);
    }

    [Test]
    public void DatabaseMapping_ShowsWhereDataIsSaved()
    {
        // Arrange
        var polarLink = new PolarLink
        {
            PolarID = 59002246,
            PersonID = "0471319",
            Username = "testuser",
            Email = "testuser@southern.edu",
            DeviceType = "A300",
            TargetZone = "Fitness"
        };

        var exercise = new PolarExerciseDto
        {
            Id = "y6deXzab",
            StartTime = "2026-01-25T10:30:00.000Z",
            DurationIso8601 = "PT30M",
            Distance = 5000,
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },
                new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "PT10M" },
                new() { Index = 2, LowerLimit = 120, UpperLimit = 140, InZone = "PT8M" },
                new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT5M" },
                new() { Index = 4, LowerLimit = 160, UpperLimit = 200, InZone = "PT2M" }
            }
        };

        // Act
        var activity = PolarToQepMapper.ToQepActivity(exercise, polarLink);
        var zones = PolarToQepMapper.ToQepHeartRateZones(exercise);

        // Assert - Print database mapping
        Console.WriteLine("=== DATABASE MAPPING FOR QEP ===");

        Console.WriteLine("\n ACTIVITY TABLE (QEPTest.dbo.Activity)");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine($"ActivityID:      {activity.ActivityID} (auto-generated BIGINT by database)");
        Console.WriteLine($"EntityID:        '{activity.EntityID}' ⭐ NEW - Polar exercise ID");
        Console.WriteLine($"UserID:          '{activity.UserID}' (PersonID from PolarLinks)");
        Console.WriteLine($"Username:        '{activity.Username}'");
        Console.WriteLine($"ActivityTypeID:  {activity.ActivityTypeID} (default Polar mapping)");
        Console.WriteLine($"Minutes:         {activity.Minutes} minutes");
        Console.WriteLine($"Duration:        {activity.Duration} minutes");
        Console.WriteLine($"Distance:        {activity.Distance} meters");
        Console.WriteLine($"AerobicPoints:   {activity.AerobicPoints} points");
        Console.WriteLine($"DateDone:        {activity.DateDone:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"DateEntered:     {activity.DateEntered:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"DeviceType:      '{activity.DeviceType}'");
        Console.WriteLine($"TargetZone:      '{activity.TargetZone}'");
        Console.WriteLine($"Measurement:     {activity.Measurement} (legacy field)");

        Console.WriteLine("\n HEARTRATEZONE TABLE (QEPTest.dbo.HeartRateZone)");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine($"Total zones to insert: {zones.Count}");
        Console.WriteLine();

        foreach (var zone in zones)
        {
            Console.WriteLine($"Zone {zone.Zone}:");
            Console.WriteLine($"  HeartRateZoneID:  (auto-generated BIGINT by database)");
            Console.WriteLine($"  EntityID:         '{zone.EntityID}' ⭐ NEW");
            Console.WriteLine($"  ActivityID:       (FK to Activity.ActivityID after Activity is saved)");
            Console.WriteLine($"  Zone:             {zone.Zone} (1-5)");
            Console.WriteLine($"  Lower:            {zone.Lower} BPM");
            Console.WriteLine($"  Upper:            {zone.Upper} BPM");
            Console.WriteLine($"  Duration:         {zone.Duration} minutes");
            Console.WriteLine();
        }

        Console.WriteLine("\n SQL EQUIVALENT (what gets executed):");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine("-- Step 1: Insert Activity");
        Console.WriteLine("INSERT INTO Activity (EntityID, UserID, Username, ActivityTypeID, Minutes, Duration, Distance, AerobicPoints, DateDone, DateEntered, DeviceType, TargetZone, Measurement)");
        Console.WriteLine($"VALUES ('{activity.EntityID}', '{activity.UserID}', '{activity.Username}', {activity.ActivityTypeID}, {activity.Minutes}, {activity.Duration}, {activity.Distance}, {activity.AerobicPoints}, '{activity.DateDone:yyyy-MM-dd HH:mm:ss}', '{activity.DateEntered:yyyy-MM-dd HH:mm:ss}', '{activity.DeviceType}', '{activity.TargetZone}', {activity.Measurement});");
        Console.WriteLine("\n-- Step 2: Get generated ActivityID");
        Console.WriteLine("SELECT SCOPE_IDENTITY() AS ActivityID;  -- Returns auto-generated ID (e.g., 1009383)");
        Console.WriteLine("\n-- Step 3: Insert HeartRateZones (using ActivityID from Step 2)");

        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            Console.WriteLine($"INSERT INTO HeartRateZone (EntityID, ActivityID, Zone, Lower, Upper, Duration)");
            Console.WriteLine($"VALUES ('{zone.EntityID}', <ActivityID>, {zone.Zone}, {zone.Lower}, {zone.Upper}, {zone.Duration});");
        }

        Console.WriteLine("\n VERIFICATION QUERIES:");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine("-- Check Activity was saved");
        Console.WriteLine($"SELECT * FROM Activity WHERE EntityID = '{activity.EntityID}';");
        Console.WriteLine("\n-- Check HeartRateZones were saved");
        Console.WriteLine($"SELECT * FROM HeartRateZone WHERE EntityID = '{activity.EntityID}' ORDER BY Zone;");
        Console.WriteLine("\n-- Check full data with join");
        Console.WriteLine("SELECT a.ActivityID, a.EntityID, a.Minutes, a.AerobicPoints,");
        Console.WriteLine("       h.Zone, h.Lower, h.Upper, h.Duration");
        Console.WriteLine("FROM Activity a");
        Console.WriteLine("LEFT JOIN HeartRateZone h ON a.ActivityID = h.ActivityID");
        Console.WriteLine($"WHERE a.EntityID = '{activity.EntityID}'");
        Console.WriteLine("ORDER BY h.Zone;");

        // Assertions
        activity.Should().NotBeNull();
        zones.Should().HaveCount(5);
        zones.All(z => z.EntityID == "y6deXzab").Should().BeTrue();
        zones.Select(z => z.Zone).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Test]
    public void DurationMapping_StoresMinutes()
    {
        // Arrange
        var testCases = new[]
        {
            ("PT30M", 30, 30.0),
            ("PT45M", 45, 45.0),
            ("PT1H", 60, 60.0),
            ("PT1H30M", 90, 90.0),
            ("PT15M30S", 16, 15.5)
        };

        Console.WriteLine("\n DURATION MAPPING (Duration stored in minutes)");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");

        foreach (var (iso, expectedMinutes, expectedDurationMinutes) in testCases)
        {
            var polarLink = new PolarLink { PersonID = "TEST", Username = "test", DeviceType = "A300", TargetZone = "Fitness" };
            var exercise = new PolarExerciseDto
            {
                Id = "test",
                StartTime = "2026-01-25T10:00:00Z",
                DurationIso8601 = iso,
                Sport = "TEST"
            };

            // Act
            var result = PolarToQepMapper.ToQepActivity(exercise, polarLink);

            // Assert
            Console.WriteLine($"\nISO Duration: {iso}");
            Console.WriteLine($"  Minutes field: {result.Minutes} minutes");
            Console.WriteLine($"  Duration field: {result.Duration} minutes");
            Console.WriteLine($"  Expected: {expectedDurationMinutes} minutes");

            result.Minutes.Should().Be(expectedMinutes);
            result.Duration.Should().BeApproximately(expectedDurationMinutes, 0.1);
        }
    }
}
