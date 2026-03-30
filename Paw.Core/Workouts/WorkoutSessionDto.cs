namespace Paw.Core.Workouts;

public class WorkoutSessionDto
{
    public string Id { get; set; } = "";
    
    public DateTime StartDateTime { get; set; }
    
    public TimeSpan Duration { get; set; }
    
    public string Name { get; set; } = "";
    
    // Heart rate zone durations in seconds (Zones 1-5)
    public int HrZone1Seconds { get; set; }
    public int HrZone2Seconds { get; set; }
    public int HrZone3Seconds { get; set; }
    public int HrZone4Seconds { get; set; }
    public int HrZone5Seconds { get; set; }
    
    public int? AverageHeartRate { get; set; }
    public int? MaxHeartRate { get; set; }
    
    public double DistanceMeters { get; set; }
}