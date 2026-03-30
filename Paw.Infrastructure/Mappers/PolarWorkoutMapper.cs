using System.Globalization;
using Paw.Core.Workouts;
using Paw.Polar;

namespace Paw.Infrastructure.Mappers;

public class PolarWorkoutMapper
{
    public static WorkoutSessionDto ToWorkoutSessionDto(PolarExerciseDto dto)
    {
        // Parse start time + UTC offset
        var localstart = DateTime.Parse(
            dto.StartTime,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal);
        
        var startDateTime = localstart;

        // Parse duration from ISO 8601
        var duration = System.Xml.XmlConvert.ToTimeSpan(dto.DurationIso8601);
        
        // Decide on display name for workout
        var name = !string.IsNullOrEmpty(dto.DetailedSportInfo)
            ? dto.DetailedSportInfo!
            : dto.Sport;
        
        // Parse heart rate zones from the heart_rate_zones array
        // Polar API returns zones with index 0-4, which we map to conventional zones 1-5
        var (zone1, zone2, zone3, zone4, zone5) = ParseHeartRateZones(dto.HeartRateZones);
        
        // Construct and return DTO
        return new WorkoutSessionDto
        {
            Id = dto.Id,
            StartDateTime = startDateTime,
            Duration = duration,
            Name = name,
            HrZone1Seconds = zone1,
            HrZone2Seconds = zone2,
            HrZone3Seconds = zone3,
            HrZone4Seconds = zone4,
            HrZone5Seconds = zone5,
            AverageHeartRate = dto.HeartRate?.Average,
            MaxHeartRate = dto.HeartRate?.Maximum,
            DistanceMeters = dto.Distance ?? 0
        };
    }
    
    private static (int zone1, int zone2, int zone3, int zone4, int zone5) ParseHeartRateZones(
        List<PolarHeartRateZoneDto>? zones)
    {
        var zone1 = 0;
        var zone2 = 0;
        var zone3 = 0;
        var zone4 = 0;
        var zone5 = 0;
        
        if (zones == null || zones.Count == 0)
        {
            return (0, 0, 0, 0, 0);
        }
        
        foreach (var zone in zones)
        {
            // Parse ISO 8601 duration (e.g., "PT6M7S" = 6 minutes 7 seconds)
            var seconds = 0;
            
            if (!string.IsNullOrWhiteSpace(zone.InZone))
            {
                try
                {
                    var timeSpan = System.Xml.XmlConvert.ToTimeSpan(zone.InZone);
                    seconds = (int)timeSpan.TotalSeconds;
                }
                catch
                {
                    // If parsing fails, keep seconds as 0
                }
            }
            
            // Map Polar index (0-4) to conventional heart rate zones (1-5)
            switch (zone.Index)
            {
                case 0:
                    zone1 = seconds;  // Very Light
                    break;
                case 1:
                    zone2 = seconds;  // Light
                    break;
                case 2:
                    zone3 = seconds;  // Moderate
                    break;
                case 3:
                    zone4 = seconds;  // Hard
                    break;
                case 4:
                    zone5 = seconds;  // Maximum
                    break;
            }
        }
        
        return (zone1, zone2, zone3, zone4, zone5);
    }
}

