namespace Paw.Core.DTOs;

public class ActivityListItem
{
    public long ActivityId { get; set; }
    public string? EntityId { get; set; }
    public DateTime? DateDone { get; set; }
    public int? Minutes { get; set; }
    public double? Distance { get; set; }
    public int? AerobicPoints { get; set; }
    public string? DeviceType { get; set; }
    public string? TargetZone { get; set; }
    public List<HeartRateZoneSummary> HeartRateZones { get; set; } = new();
}

public class HeartRateZoneSummary
{
    public int? Zone { get; set; }
    public double? DurationMinutes { get; set; }
    public int? Lower { get; set; }
    public int? Upper { get; set; }
}
