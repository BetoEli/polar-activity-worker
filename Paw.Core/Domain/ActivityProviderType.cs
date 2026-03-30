namespace Paw.Core.Domain;

/// <summary>
/// Fitness device/platform providers. Extend this enum when adding new integrations.
/// The integer value is stored in the WebhookEvents.Provider column.
/// </summary>
public enum ActivityProviderType
{
   Polar = 1,
   // Future providers:
   // Garmin = 2,
   // Fitbit = 3,
}

/// <summary>
/// Stores incoming webhook events from fitness providers for background processing.
/// Provider-agnostic: the <see cref="Provider"/> column identifies the source.
/// </summary>
public class WebhookEvent
{
   public long Id { get; set; }
   public ActivityProviderType Provider { get; set; }
   public string EventType { get; set; } = ""; 
   public long ExternalUserId { get; set; } // Provider-specific user ID (e.g., Polar X-User-ID)
   public string EntityID { get; set; } = ""; // Exercise/activity ID from provider
   public DateTime EventTimestamp { get; set; }
   public string? ResourceUrl { get; set; }
   
   public string Status { get; set; } = "Pending"; // Pending, Processing, Completed, Failed
   public string? ErrorMessage { get; set; }
   
   public DateTime ReceivedAtUtc { get; set; }
   public DateTime? ProcessedAtUtc { get; set; }
   
   public string RawPayload { get; set; } = ""; // Store original JSON
}

// ============================================================================
// Legacy types - kept for backward compatibility with IPolarClient interface.
// These are NOT mapped to any database table in QEPTest.
// When IPolarClient is refactored to remove DeviceAccount-based overloads,
// these can be deleted.
// ============================================================================

/// <summary>
/// Legacy device account model. Used only by IPolarClient overloads that
/// accept a DeviceAccount parameter. Not mapped to any database table.
/// </summary>
public class DeviceAccount
{
   public int Id { get; set; }
   public Guid UserId { get; set; }
   public ActivityProviderType Provider { get; set; }
   public string ExternalUserId { get; set; } = "";
   public string AccessToken { get; set; } = "";
   public string RefreshToken { get; set; } = "";
   public DateTime ExpiresAtUtc { get; set; }
}






