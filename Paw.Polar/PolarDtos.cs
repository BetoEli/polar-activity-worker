using System.Text.Json.Serialization;

namespace Paw.Polar;

public sealed class PolarTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("x_user_id")]
    public long? XUserId { get; set; }
}

public sealed class PolarUserRegistrationRequest
{
    [JsonPropertyName("member-id")]
    public string MemberId { get; set; } = "";
}

public sealed class PolarErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

public sealed class PolarUserRegistrationResponse
{
    [JsonPropertyName("polar-user-id")]
    public long PolarUserId { get; set; }

    [JsonPropertyName("member-id")]
    public string MemberId { get; set; } = "";

    [JsonPropertyName("registration-date")]
    public DateTime RegistrationDate { get; set; }

    [JsonPropertyName("first-name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last-name")]
    public string? LastName { get; set; }

    [JsonPropertyName("birthdate")]
    public string? Birthdate { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("weight")]
    public int? Weight { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }
}

public class PolarTrainingSession
{
    public string Id { get; set; } = "";
    public DateTime StartTimeUtc { get; set; }
    public int DurationSec { get; set; }
    public double DistanceMeters { get; set; }
    public string SportType { get; set; } = "";

    public int SecondsAbove100Bpm { get; set; }
}

// Webhook DTOs
public sealed class PolarWebhookRequest
{
    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new();

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public sealed class PolarWebhookResponse
{
    [JsonPropertyName("data")]
    public PolarWebhookData Data { get; set; } = new();
}

public sealed class PolarWebhookData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new();

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("signature_secret_key")]
    public string? SignatureSecretKey { get; set; }
}

public sealed class PolarWebhookInfo
{
    [JsonPropertyName("data")]
    public List<PolarWebhookInfoData> Data { get; set; } = new();
}

public sealed class PolarWebhookInfoData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("events")]
    public List<string> Events { get; set; } = new();

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public sealed class PolarWebhookPayload
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("entity_id")]
    public string EntityId { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

// Exercise detail DTOs
public sealed class PolarExerciseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("upload_time")]
    public string? UploadTime { get; set; }

    [JsonPropertyName("polar_user")]
    public string? PolarUser { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = "";

    [JsonPropertyName("start_time_utc_offset")]
    public int? StartTimeUtcOffset { get; set; }

    [JsonPropertyName("duration")]
    public string DurationIso8601 { get; set; } = "";

    [JsonPropertyName("calories")]
    public int? Calories { get; set; }

    [JsonPropertyName("distance")]
    public double? Distance { get; set; }

    [JsonPropertyName("heart_rate")]
    public PolarHeartRateDto? HeartRate { get; set; }

    [JsonPropertyName("sport")]
    public string Sport { get; set; } = "";

    [JsonPropertyName("detailed_sport_info")]
    public string? DetailedSportInfo { get; set; }

    [JsonPropertyName("samples")]
    public List<PolarSampleDto>? Samples { get; set; }
    
    [JsonPropertyName("heart_rate_zones")]
    public List<PolarHeartRateZoneDto>? HeartRateZones { get; set; }
}

public sealed class PolarHeartRateDto
{
    [JsonPropertyName("average")]
    public int? Average { get; set; }

    [JsonPropertyName("maximum")]
    public int? Maximum { get; set; }
}

public sealed class PolarSampleDto
{
    [JsonPropertyName("recording_rate")]
    public int RecordingRateSeconds { get; set; }

    [JsonPropertyName("sample_type")]
    public int SampleType { get; set; } // 0 = heart rate, 1 = speed, 2 = cadence, etc.

    [JsonPropertyName("data")]
    public string Data { get; set; } = "";
}

public sealed class PolarZoneDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("target")]
    public PolarZoneTargetDto? Target { get; set; }

    [JsonPropertyName("in_zone")]
    public string? InZone { get; set; }
}

public sealed class PolarZoneTargetDto
{
    [JsonPropertyName("lower_limit")]
    public int? LowerLimit { get; set; }

    [JsonPropertyName("upper_limit")]
    public int? UpperLimit { get; set; }
}

public sealed class PolarHeartRateZoneDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("lower_limit")]
    public int LowerLimit { get; set; }

    [JsonPropertyName("upper_limit")]
    public int UpperLimit { get; set; }

    [JsonPropertyName("in_zone")]
    public string InZone { get; set; } = ""; // ISO 8601 duration, e.g., "PT6M7S"
}

