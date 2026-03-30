using System.Xml;
using Microsoft.EntityFrameworkCore;
using Paw.Core.Domain;
using Paw.Polar;

namespace Paw.Infrastructure.Mappers;

/// <summary>
/// Maps Polar exercise data to QEP database format (Activity and HeartRateZone tables).
/// </summary>
public static class PolarToQepMapper
{
    // QEP aerobic-points formula: N points per AerobicPointsWindowMinutes minutes in a zone.
    private const int AerobicPointsWindowMinutes = 10;
    private const int LowIntensityMultiplier = 1;    // Polar zones 0-1 (QEP 1-2)
    private const int MedIntensityMultiplier = 2;    // Polar zone 2  (QEP 3)
    private const int HighIntensityMultiplier = 3;   // Polar zones 3-4 (QEP 4-5)

    /// <summary>
    /// Converts a Polar exercise to a QEP Activity entity.
    /// </summary>
    public static async Task<QepActivity> ToQepActivityAsync(
        PolarExerciseDto exercise,
        PolarLink polarLink,
        PawDbContext db,
        CancellationToken cancellationToken = default)
    {
        var duration = ParseIsoDuration(exercise.DurationIso8601);
        var durationMinutes = duration.TotalMinutes;
        var minutes = (int)Math.Round(durationMinutes);

        // Calculate aerobic points based on heart rate zones
        var aerobicPoints = CalculateAerobicPoints(exercise.HeartRateZones);
        var averageHeartRate = exercise.HeartRate?.Average ?? 0;

        // Parse start time
        var startTime = DateTime.TryParse(exercise.StartTime, out var parsedTime)
            ? parsedTime
            : DateTime.UtcNow;

        // Look up ActivityTypeID from database
        // Try detailed_sport_info first, fallback to sport
        var sportName = exercise.DetailedSportInfo ?? exercise.Sport ?? "OTHER";
        var activityTypeID = await GetActivityTypeIdAsync(db, sportName, cancellationToken);

        return new QepActivity
        {
            // EntityID from Polar exercise ID
            EntityID = exercise.Id,

            // UserID = PersonID from PolarLinks (QEP uses PersonID as UserID)
            UserID = polarLink.PersonID,
            Username = polarLink.Username,

            // ActivityTypeID looked up from database
            ActivityTypeID = activityTypeID,
            
            // Duration fields
            Minutes = minutes,
            Duration = durationMinutes,
            
            // Distance in meters
            Distance = exercise.Distance ?? 0,
            
            // Aerobic points
            AerobicPoints = aerobicPoints,
            
            // Dates
            DateDone = startTime,
            DateEntered = DateTime.Now,
            
            // Device info from PolarLinks
            DeviceType = polarLink.DeviceType,
            TargetZone = polarLink.TargetZone,

            // Measurement: average heart rate (BPM)
            Measurement = averageHeartRate
        };
    }

    /// <summary>
    /// Converts a Polar exercise to a QEP Activity entity (sync version for unit testing).
    /// Uses default ActivityTypeID = 2. For production use, prefer ToQepActivityAsync.
    /// </summary>
    public static QepActivity ToQepActivity(
        PolarExerciseDto exercise,
        PolarLink polarLink)
    {
        var duration = ParseIsoDuration(exercise.DurationIso8601);
        var durationMinutes = duration.TotalMinutes;
        var minutes = (int)Math.Round(durationMinutes);

        // Calculate aerobic points based on heart rate zones
        var aerobicPoints = CalculateAerobicPoints(exercise.HeartRateZones);
        var averageHeartRate = exercise.HeartRate?.Average ?? 0;

        // Parse start time
        var startTime = DateTime.TryParse(exercise.StartTime, out var parsedTime)
            ? parsedTime
            : DateTime.UtcNow;

        return new QepActivity
        {
            // EntityID from Polar exercise ID
            EntityID = exercise.Id,

            // UserID = PersonID from PolarLinks (QEP uses PersonID as UserID)
            UserID = polarLink.PersonID,
            Username = polarLink.Username,

            // ActivityTypeID: default for testing (use ToQepActivityAsync with DB lookup in production)
            ActivityTypeID = 2,
            
            // Duration fields
            Minutes = minutes,
            Duration = durationMinutes,
            
            // Distance in meters
            Distance = exercise.Distance ?? 0,
            
            // Aerobic points
            AerobicPoints = aerobicPoints,
            
            // Dates
            DateDone = startTime,
            DateEntered = DateTime.Now,
            
            // Device info from PolarLinks
            DeviceType = polarLink.DeviceType,
            TargetZone = polarLink.TargetZone,

            // Measurement: average heart rate (BPM)
            Measurement = averageHeartRate
        };
    }

    /// <summary>
    /// Looks up the ActivityTypeID from the database based on Polar sport name.
    /// Returns a default ID if no match is found.
    /// </summary>
    private static async Task<int> GetActivityTypeIdAsync(
        PawDbContext db, 
        string sportName, 
        CancellationToken cancellationToken)
    {
        // Normalize the sport name to uppercase for case-insensitive matching
        var normalizedSport = sportName.ToUpperInvariant();

        // Try exact match first
        var activityType = await db.ActivityTypes
            .AsNoTracking()
            .Where(at => at.Active && at.AutomatedEntry)
            .FirstOrDefaultAsync(at => at.Description.ToUpper() == normalizedSport, cancellationToken);

        if (activityType != null)
        {
            return activityType.ActivityTypeID;
        }

        // Try partial match (e.g., "WATERSPORTS_CANOEING" contains "CANOEING")
        activityType = await db.ActivityTypes
            .AsNoTracking()
            .Where(at => at.Active && at.AutomatedEntry)
            .FirstOrDefaultAsync(at => at.Description.ToUpper().Contains(normalizedSport), cancellationToken);

        if (activityType != null)
        {
            return activityType.ActivityTypeID;
        }

        // Default fallback: return ID for "OTHER" or a generic activity type
        var defaultType = await db.ActivityTypes
            .AsNoTracking()
            .Where(at => at.Active && at.AutomatedEntry)
            .FirstOrDefaultAsync(at => at.Description.ToUpper() == "OTHER", cancellationToken);

        return defaultType?.ActivityTypeID ?? 2; // Fallback to ID 2 if "OTHER" doesn't exist
    }

    /// <summary>
    /// Converts Polar heart rate zones to QEP HeartRateZone entities.
    /// </summary>
    public static List<HeartRateZones> ToQepHeartRateZones(
        PolarExerciseDto exercise)
    {
        if (exercise.HeartRateZones == null || !exercise.HeartRateZones.Any())
            return new List<HeartRateZones>();

        var zones = new List<HeartRateZones>();

        foreach (var zone in exercise.HeartRateZones)
        {
            var zoneDuration = ParseIsoDuration(zone.InZone);
            
            zones.Add(new HeartRateZones
            {
                EntityID = $"{exercise.Id}",
                
                // Polar uses 0-4, QEP uses 1-5
                Zone = zone.Index + 1,
                Lower = zone.LowerLimit,
                Upper = zone.UpperLimit,
                // Duration stored in minutes to match QEP schema
                Duration = zoneDuration.TotalMinutes
            });
        }

        return zones;
    }

    /// <summary>
    /// Parses ISO 8601 duration string (e.g., "PT30M") to TimeSpan.
    /// </summary>
    private static TimeSpan ParseIsoDuration(string? isoDuration)
    {
        if (string.IsNullOrEmpty(isoDuration))
            return TimeSpan.Zero;

        try
        {
            return XmlConvert.ToTimeSpan(isoDuration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Calculates aerobic points based on heart rate zones and duration.
    /// QEP formula:
    /// - Zones 1-2: 1 point per 10 minutes
    /// - Zone 3: 2 points per 10 minutes
    /// - Zones 4-5: 3 points per 10 minutes
    /// </summary>
    private static int CalculateAerobicPoints(List<PolarHeartRateZoneDto>? zones)
    {
        if (zones == null || !zones.Any())
            return 0;

        int points = 0;
        
        foreach (var zone in zones)
        {
            var duration = ParseIsoDuration(zone.InZone);
            var minutes = (int)duration.TotalMinutes;
            
            // QEP aerobic points formula:
            // Zone 0-1 (QEP 1-2): 1 point per 10 minutes
            // Zone 2 (QEP 3): 2 points per 10 minutes
            // Zone 3-4 (QEP 4-5): 3 points per 10 minutes
            
            int multiplier = zone.Index switch
            {
                0 or 1 => LowIntensityMultiplier,
                2 => MedIntensityMultiplier,
                3 or 4 => HighIntensityMultiplier,
                _ => LowIntensityMultiplier
            };

            points += (minutes / AerobicPointsWindowMinutes) * multiplier;
        }

        return points;
    }
}
