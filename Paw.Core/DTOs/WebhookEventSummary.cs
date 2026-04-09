namespace Paw.Core.DTOs;

public class WebhookEventSummary
{
    public long Id { get; set; }
    public string EventType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
}
