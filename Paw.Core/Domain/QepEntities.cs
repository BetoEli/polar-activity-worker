namespace Paw.Core.Domain;

// ============================================================================
// QEP Database Entities
// All entities in this file map to existing tables in QEPTest database
// ============================================================================

/// <summary>
/// Represents a Polar device connection in the QEP database format.
/// Maps to existing QEPTest.dbo.PolarLinks table for backward compatibility with QEP Web App.
/// PolarID is set to Polar's X-User-ID from OAuth token exchange.
/// </summary>
public class PolarLink
{
    /// <summary>
    /// Primary key - Polar's X-User-ID from OAuth token response.
    /// Stored as BIGINT because Polar user IDs can exceed INT range (2^31-1).
    /// </summary>
    public long PolarID { get; set; }

    /// <summary>
    /// Username parsed from email (before @ sign).
    /// Example: "jdoe" from "jdoe@southern.edu"
    /// </summary>
    public string Username { get; set; } = "";
    
    /// <summary>
    /// QEP Student PersonID (e.g., "0501476").
    /// This is the QEP internal student identifier.
    /// </summary>
    public string PersonID { get; set; } = "";

    /// <summary>
    /// Student's email address.
    /// </summary>
    public string Email { get; set; } = "";
    
    /// <summary>
    /// Polar device model (e.g., "A300", "FT60", "H10").
    /// Nullable to support existing rows.
    /// </summary>
    public string? DeviceType { get; set; }
    
    /// <summary>
    /// Target heart rate zone (e.g., "Fitness", "Fat Burn", "Cardio").
    /// Nullable to support existing rows.
    /// </summary>
    public string? TargetZone { get; set; }

    /// <summary>
    /// OAuth access token received after Polar OAuth flow.
    /// Nullable to support existing rows that don't have tokens yet.
    /// </summary>
    public string? AccessToken { get; set; }
}

/// <summary>
/// Represents exercise data fetched from Polar, stored for processing by QEP.
/// Maps to existing QEPTest.dbo.PolarTransactions table.
/// This table tracks raw exercise responses and their processing status.
/// </summary>
public class PolarTransaction
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// Database uses INT (32-bit), not BIGINT.
    /// </summary>
    public int PolarTransactionID { get; set; }
    
    /// <summary>
    /// Reference to the Polar user. Stored as BIGINT to match PolarLinks.PolarID.
    /// </summary>
    public long? PolarID { get; set; }
    
    /// <summary>
    /// When this exercise data was first fetched from Polar.
    /// </summary>
    public DateTime? FirstTouched { get; set; }
    
    /// <summary>
    /// When this exercise data was last accessed/updated.
    /// </summary>
    public DateTime? LastTouched { get; set; }
    
    /// <summary>
    /// Exercise URL from webhook (e.g., "https://polaraccesslink.com/v3/exercises/abc123").
    /// </summary>
    public string Location { get; set; } = "";
    
    /// <summary>
    /// Raw JSON response from Polar API.
    /// </summary>
    public string Response { get; set; } = "";
    
    /// <summary>
    /// Whether the data has been committed to QEP's main Activity/HeartRateZone tables.
    /// </summary>
    public bool IsCommitted { get; set; }
    
    /// <summary>
    /// Whether the webhook processing completed successfully.
    /// </summary>
    public bool IsProcessed { get; set; }
    
    /// <summary>
    /// Number of processing attempts.
    /// </summary>
    public int Attempt { get; set; }
}

/// <summary>
/// Represents an activity/workout in the QEP database format.
/// Maps to existing QEPTest.dbo.Activity table for backward compatibility.
/// </summary>
public class QepActivity
{
    /// <summary>
    /// Auto-incrementing primary key (BIGINT in database).
    /// Database generates this automatically.
    /// </summary>
    public long ActivityID { get; set; }
    
    /// <summary>
    /// Unique identifier for this activity from external source (e.g., Polar exercise ID "y6deXzab").
    /// </summary>
    public string? EntityID { get; set; } = string.Empty;

    /// <summary>
    /// Activity type ID.
    /// </summary>
    public int? ActivityTypeID { get; set; }
    
    /// <summary>
    /// QEP Student PersonID (matches PolarLinks.PersonID).
    /// Example: "0471319"
    /// </summary>
    public string UserID { get; set; } = "";

    /// <summary>
    /// Username from PolarLinks.Username.
    /// Example: "mann"
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Measurement value (legacy field).
    /// Often 0 in existing data.
    /// </summary>
    public double? Measurement { get; set; }
    
    /// <summary>
    /// Duration in whole minutes.
    /// Can be 0 in existing data.
    /// </summary>
    public int? Minutes { get; set; }
    
    /// <summary>
    /// Duration in decimal minutes (float).
    /// Example: 31.4517 minutes, 86.1913 minutes
    /// NOTE: Despite legacy comments, actual QEP data shows this stores MINUTES, not seconds!
    /// Verified from production data: Duration values like 31.45, 86.19 match Minutes field when rounded.
    /// </summary>
    public double? Duration { get; set; }
    
    /// <summary>
    /// Distance in meters.
    /// Can be NULL.
    /// </summary>
    public double? Distance { get; set; }
    
    /// <summary>
    /// Aerobic points calculated based on heart rate zones and duration.
    /// Can be 0.
    /// </summary>
    public int? AerobicPoints { get; set; }
    
    /// <summary>
    /// When the activity was performed (workout start time).
    /// Example: 2060-06-11 09:54:58
    /// </summary>
    public DateTime? DateDone { get; set; }
    
    /// <summary>
    /// When the activity was entered/synced into the system.
    /// Example: 2020-01-23 12:22:28
    /// </summary>
    public DateTime? DateEntered { get; set; }
    
    /// <summary>
    /// Device type from PolarLinks.
    /// Example: "A300"
    /// </summary>
    public string? DeviceType { get; set; }
    
    /// <summary>
    /// Target heart rate zone from PolarLinks.
    /// Example: "Fat Burn"
    /// </summary>
    public string? TargetZone { get; set; }
    
    // Navigation property
    public ICollection<HeartRateZones> HeartRateZones { get; set; } = new List<HeartRateZones>();
}

/// <summary>
/// Represents heart rate zone data for an activity in the QEP database format.
/// Maps to existing QEPTest.dbo.HeartRateZones table.
/// </summary>
public class HeartRateZones
{
    /// <summary>
    /// Auto-incrementing primary key (BIGINT in database).
    /// </summary>
    public long HeartRateZoneID { get; set; }
    
    /// <summary>
    /// Foreign key to Activity table (BIGINT in database).
    /// </summary>
    public long ActivityID { get; set; }

    /// <summary>
    /// New column - Unique identifier for the activity from polar api.
    /// Nullable for existing rows without an EntityID.
    /// </summary>
    public string? EntityID { get; set; }

    /// <summary>
    /// Zone number (1-5).
    /// Polar uses index 0-4, we map to conventional zones 1-5.
    /// Nullable to support legacy rows with missing data.
    /// </summary>
    public int? Zone { get; set; }
    
    /// <summary>
    /// Lower heart rate limit in BPM for this zone.
    /// </summary>
    public int? Lower { get; set; }
    
    /// <summary>
    /// Upper heart rate limit in BPM for this zone.
    /// </summary>
    public int? Upper { get; set; }
    
    /// <summary>
    /// Duration in this zone (minutes in legacy schema, stored as float).
    /// Nullable to prevent SqlNullValueException for historical rows.
    /// </summary>
    public double? Duration { get; set; }
    
    // Navigation property
    public QepActivity Activity { get; set; } = null!;
}

/// <summary>
/// Represents an activity type in the QEP database format.
/// Maps to existing QEPTest.dbo.ActivityType table.
/// </summary>
public class QepActivityType
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int ActivityTypeID { get; set; }
    
    /// <summary>
    /// Activity type description (e.g., "RUNNING", "PILATES", "NORDIC_WALKING").
    /// </summary>
    public string Description { get; set; } = "";
    
    /// <summary>
    /// Units of measurement (typically "BPM" for heart rate-based activities).
    /// </summary>
    public string? Units { get; set; }
    
    /// <summary>
    /// Whether this activity type is active/enabled.
    /// </summary>
    public bool Active { get; set; }
    
    /// <summary>
    /// Whether this activity type supports automated entry (from devices).
    /// </summary>
    public bool AutomatedEntry { get; set; }
    
    /// <summary>
    /// Legacy field for information ID.
    /// </summary>
    public int? InformationID { get; set; }
    
    /// <summary>
    /// Legacy field for information URL.
    /// </summary>
    public string? InformationURL { get; set; }
    
    /// <summary>
    /// Legacy field for information IsID flag.
    /// </summary>
    public bool InformationIsID { get; set; }
    
    /// <summary>
    /// Legacy field for information IsURL flag.
    /// </summary>
    public bool InformationIsURL { get; set; }
}
