namespace Paw.Core.DTOs;

public class WorkoutWeekStats
{
    public int QualifyingWorkoutDays { get; set; }
    public int TargetDays { get; set; } = 5;
    public DateTime WeekStartDate { get; set; }
    public DateTime WeekEndDate { get; set; }
    public List<WorkoutDaySummary> Days { get; set; } = new();
}

public class WorkoutDaySummary
{
    public DateTime Date { get; set; }
    public bool HasQualifyingWorkout { get; set; }
    public int TotalWorkoutMinutes { get; set; }
    public int QualifyingMinutes { get; set; }
    public List<ActivitySummary> Activities { get; set; } = new();
}

public class ActivitySummary
{
    public long ActivityId { get; set; }
    public string SportType { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public int HeartRateZoneMinutes { get; set; }
    public bool Qualifies { get; set; }
    public HeartRateZoneBreakdown? ZoneBreakdown { get; set; }
}

public class HeartRateZoneBreakdown
{
    public int Zone1Minutes { get; set; }  // Very Light (50-60% max HR)
    public int Zone2Minutes { get; set; }  // Light (60-70% max HR)
    public int Zone3Minutes { get; set; }  // Moderate (70-80% max HR)
    public int Zone4Minutes { get; set; }  // Hard (80-90% max HR)
    public int Zone5Minutes { get; set; }  // Maximum (90-100% max HR)

    public int TotalMinutes => Zone1Minutes + Zone2Minutes + Zone3Minutes + Zone4Minutes + Zone5Minutes;
}

