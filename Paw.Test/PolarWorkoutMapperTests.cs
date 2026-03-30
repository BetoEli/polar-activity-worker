/*
 * COMMENTED OUT - These tests are for PolarWorkoutMapper (different from PolarToQepMapper)
 * Uncomment when testing the Workout mapper instead of QEP mapper
 */

/*
using FluentAssertions;
using Paw.Infrastructure.Mappers;
using Paw.Polar;

namespace Paw.Test;

[TestFixture]
public class PolarWorkoutMapperTests
{
    [Test]
    public void ToWorkoutSessionDto_MapsBasicFields_Correctly()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "exercise123",
            StartTime = "2024-12-15T10:30:00.000Z",
            DurationIso8601 = "PT45M30S",
            Distance = 8500,
            Sport = "RUNNING",
            Calories = 450
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("exercise123");
        result.StartDateTime.Year.Should().Be(2024);
        result.StartDateTime.Month.Should().Be(12);
        result.StartDateTime.Day.Should().Be(15);
        result.Duration.Should().Be(TimeSpan.FromSeconds(2730)); // 45m 30s
        result.DistanceMeters.Should().Be(8500);
        result.Name.Should().Be("RUNNING");
    }

    [Test]
    public void ToWorkoutSessionDto_MapsHeartRateZones_Correctly()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRate = new PolarHeartRateDto
            {
                Average = 145,
                Maximum = 175
            },
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },   // Zone 1 = 300 seconds
                new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "PT10M" },  // Zone 2 = 600 seconds
                new() { Index = 2, LowerLimit = 120, UpperLimit = 140, InZone = "PT8M" },   // Zone 3 = 480 seconds
                new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT5M" },   // Zone 4 = 300 seconds
                new() { Index = 4, LowerLimit = 160, UpperLimit = 200, InZone = "PT2M" }    // Zone 5 = 120 seconds
            }
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.HrZone1Seconds.Should().Be(300);
        result.HrZone2Seconds.Should().Be(600);
        result.HrZone3Seconds.Should().Be(480);
        result.HrZone4Seconds.Should().Be(300);
        result.HrZone5Seconds.Should().Be(120);
        result.AverageHeartRate.Should().Be(145);
        result.MaxHeartRate.Should().Be(175);
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesNullHeartRateData()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRate = null,
            HeartRateZones = null
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.HrZone1Seconds.Should().Be(0);
        result.HrZone2Seconds.Should().Be(0);
        result.HrZone3Seconds.Should().Be(0);
        result.HrZone4Seconds.Should().Be(0);
        result.HrZone5Seconds.Should().Be(0);
        result.AverageHeartRate.Should().BeNull();
        result.MaxHeartRate.Should().BeNull();
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesMissingZones()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },
                new() { Index = 3, LowerLimit = 140, UpperLimit = 160, InZone = "PT10M" }  // Missing zones 1, 2, 4
            }
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.HrZone1Seconds.Should().Be(300);
        result.HrZone2Seconds.Should().Be(0);
        result.HrZone3Seconds.Should().Be(0);
        result.HrZone4Seconds.Should().Be(600);
        result.HrZone5Seconds.Should().Be(0);
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesEmptyHeartRateZones()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>()
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.HrZone1Seconds.Should().Be(0);
        result.HrZone2Seconds.Should().Be(0);
        result.HrZone3Seconds.Should().Be(0);
        result.HrZone4Seconds.Should().Be(0);
        result.HrZone5Seconds.Should().Be(0);
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesNullDistance()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            Distance = null
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.DistanceMeters.Should().Be(0);
    }

    [Test]
    public void ToWorkoutSessionDto_UsesDetailedSportInfoWhenAvailable()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            DetailedSportInfo = "Trail Running"
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.Name.Should().Be("Trail Running");
    }

    [Test]
    public void ToWorkoutSessionDto_FallsBackToSportWhenNoDetailedInfo()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            DetailedSportInfo = null
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.Name.Should().Be("RUNNING");
    }

    [Test]
    public void ToWorkoutSessionDto_MapsAllSportTypes()
    {
        // Arrange
        var sportTypes = new[] { "RUNNING", "CYCLING", "SWIMMING", "WALKING", "GYM" };

        foreach (var sport in sportTypes)
        {
            var exercise = new PolarExerciseDto
            {
                Id = "ex1",
                StartTime = "2024-12-15T10:00:00Z",
                DurationIso8601 = "PT30M",
                Sport = sport
            };

            // Act
            var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

            // Assert
            result.Name.Should().Be(sport);
        }
    }

    [Test]
    public void ToWorkoutSessionDto_CalculatesTotalHeartRateZoneSeconds()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
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
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);
        var totalSeconds = result.HrZone1Seconds + result.HrZone2Seconds + 
                          result.HrZone3Seconds + result.HrZone4Seconds + 
                          result.HrZone5Seconds;

        // Assert
        totalSeconds.Should().Be(1800); // 30 minutes
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesInvalidZoneIndex()
    {
        // Arrange - Polar sends invalid zone index (shouldn't happen but be defensive)
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },
                new() { Index = 5, LowerLimit = 200, UpperLimit = 220, InZone = "PT10M" },  // Invalid zone index
                new() { Index = 10, LowerLimit = 220, UpperLimit = 240, InZone = "PT3M" }   // Invalid zone index
            }
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert - should only map valid zones (0-4)
        result.HrZone1Seconds.Should().Be(300);
        result.HrZone2Seconds.Should().Be(0);
        result.HrZone3Seconds.Should().Be(0);
        result.HrZone4Seconds.Should().Be(0);
        result.HrZone5Seconds.Should().Be(0);
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesComplexIsoDuration()
    {
        // Arrange
        var testCases = new[]
        {
            ("PT30M", 1800),
            ("PT1H", 3600),
            ("PT1H30M", 5400),
            ("PT45M30S", 2730),
            ("PT2H15M45S", 8145),
            ("PT5S", 5)
        };

        foreach (var (iso, expectedSeconds) in testCases)
        {
            var exercise = new PolarExerciseDto
            {
                Id = "ex1",
                StartTime = "2024-12-15T10:00:00Z",
                DurationIso8601 = iso,
                Sport = "RUNNING"
            };

            // Act
            var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

            // Assert
            result.Duration.TotalSeconds.Should().Be(expectedSeconds, 
                $"Duration {iso} should be {expectedSeconds} seconds");
        }
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesWhitespaceInZoneDuration()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT30M",
            Sport = "RUNNING",
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "   " },  // Whitespace
                new() { Index = 1, LowerLimit = 100, UpperLimit = 120, InZone = "" }     // Empty string
            }
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.HrZone1Seconds.Should().Be(0);
        result.HrZone2Seconds.Should().Be(0);
    }

    [Test]
    public void ToWorkoutSessionDto_HandlesMixedHeartRateData()
    {
        // Arrange
        var exercise = new PolarExerciseDto
        {
            Id = "ex1",
            StartTime = "2024-12-15T10:00:00Z",
            DurationIso8601 = "PT45M",
            Sport = "CYCLING",
            Distance = 25000,
            Calories = 600,
            HeartRate = new PolarHeartRateDto
            {
                Average = 155,
                Maximum = 185
            },
            HeartRateZones = new List<PolarHeartRateZoneDto>
            {
                new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT2M" },
                new() { Index = 1, LowerLimit = 100, UpperLimit = 130, InZone = "PT10M" },
                new() { Index = 2, LowerLimit = 130, UpperLimit = 150, InZone = "PT20M" },
                new() { Index = 3, LowerLimit = 150, UpperLimit = 170, InZone = "PT10M" },
                new() { Index = 4, LowerLimit = 170, UpperLimit = 200, InZone = "PT3M" }
            },
            DetailedSportInfo = "Road Cycling"
        };

        // Act
        var result = PolarWorkoutMapper.ToWorkoutSessionDto(exercise);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("ex1");
        result.Name.Should().Be("Road Cycling");
        result.Duration.TotalMinutes.Should().Be(45);
        result.DistanceMeters.Should().Be(25000);
        result.AverageHeartRate.Should().Be(155);
        result.MaxHeartRate.Should().Be(185);
        result.HrZone1Seconds.Should().Be(120);
        result.HrZone2Seconds.Should().Be(600);
        result.HrZone3Seconds.Should().Be(1200);
        result.HrZone4Seconds.Should().Be(600);
        result.HrZone5Seconds.Should().Be(180);
        
        var totalZoneTime = result.HrZone1Seconds + result.HrZone2Seconds + 
                           result.HrZone3Seconds + result.HrZone4Seconds + result.HrZone5Seconds;
        totalZoneTime.Should().Be(2700); // 45 minutes
    }
}


*/
