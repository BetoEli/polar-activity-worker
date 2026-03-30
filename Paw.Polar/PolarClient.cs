using System.Net;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Paw.Core.Domain;

namespace Paw.Polar;

public interface IPolarClient
{
    Task<PolarTokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default);
    Task<PolarTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<PolarUserRegistrationResponse?> RegisterUserAsync(string accessToken, string memberId, CancellationToken cancellationToken = default);
    [Obsolete("Not implemented — returns empty. Use ListExercisesAsync or GetExerciseByIdAsync.")]
    Task<IReadOnlyList<PolarTrainingSession>> GetTrainingsAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PolarExerciseDto>> GetExercisesAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default);
    Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(DeviceAccount account, string exerciseId, CancellationToken cancellationToken = default);
    Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(string accessToken, string exerciseId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists exercises from the last 30 days using GET /v3/exercises.
    /// Returns deserialized exercises and the raw JSON response.
    /// </summary>
    Task<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)> ListExercisesAsync(
        string accessToken, CancellationToken cancellationToken = default);

    // Webhook management
    Task<PolarWebhookResponse?> CreateWebhookAsync(string webhookUrl, List<string> events, CancellationToken cancellationToken = default);
    Task<PolarWebhookInfo?> GetWebhookAsync(CancellationToken cancellationToken = default);
    Task ActivateWebhookAsync(CancellationToken cancellationToken = default);
}

public class PolarClient : IPolarClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly PolarOptions _options;
    private readonly ILogger<PolarClient> _logger;

    public PolarClient(HttpClient http, IOptions<PolarOptions> options, ILogger<PolarClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<PolarTokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri
            })
        };
        ApplyBasicAuth(request);

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Polar token exchange failed with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("Token exchange failed", response.StatusCode, payload);
        }

        return Deserialize<PolarTokenResponse>(payload);
    }

    public async Task<PolarTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            })
        };
        ApplyBasicAuth(request);

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Polar refresh token failed with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("Refresh token failed", response.StatusCode, payload);
        }

        return Deserialize<PolarTokenResponse>(payload);
    }

    public async Task<PolarUserRegistrationResponse?> RegisterUserAsync(string accessToken, string memberId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.UserRegistrationEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new PolarUserRegistrationRequest
            {
                MemberId = memberId
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Polar user already registered for {MemberId}", memberId);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Polar user registration failed with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("User registration failed", response.StatusCode, payload);
        }

        var registrationResponse = Deserialize<PolarUserRegistrationResponse>(payload);
        _logger.LogInformation("User registered successfully. Polar User ID: {PolarUserId}, Name: {FirstName} {LastName}", 
            registrationResponse.PolarUserId, registrationResponse.FirstName, registrationResponse.LastName);
        
        return registrationResponse;
    }

    [Obsolete("GetTrainingsAsync returns hardcoded stub data and is not implemented. Use ListExercisesAsync or GetExerciseByIdAsync instead.")]
    public async Task<IReadOnlyList<PolarTrainingSession>> GetTrainingsAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return Array.Empty<PolarTrainingSession>();
    }

    public async Task<IReadOnlyList<PolarExerciseDto>> GetExercisesAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        // For now, return empty list since we don't have a "list exercises" endpoint implemented yet
        // Individual exercises will be fetched via GetExerciseByIdAsync when processing webhooks
        _logger.LogInformation("GetExercisesAsync called for user {UserId} - returning empty list (use GetExerciseByIdAsync for webhook processing)", account.UserId);
        await Task.CompletedTask;
        return new List<PolarExerciseDto>();
    }
    
    public async Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(DeviceAccount account, string exerciseId, CancellationToken cancellationToken = default)
    {
        return await GetExerciseByIdAsync(account.AccessToken, exerciseId, cancellationToken);
    }

    public async Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(string accessToken, string exerciseId, CancellationToken cancellationToken = default)
    {
        // Construct the exercise detail URL with query parameters for samples, zones, and route
        var exerciseUrl = $"/v3/exercises/{exerciseId}?samples=true&zones=true&route=true";
        
        var request = new HttpRequestMessage(HttpMethod.Get, exerciseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        _logger.LogInformation("Fetching exercise {ExerciseId}", exerciseId);

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Exercise {ExerciseId} not found", exerciseId);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch exercise {ExerciseId} with status {Status}: {Payload}", 
                exerciseId, response.StatusCode, payload);
            throw new PolarApiException($"Failed to fetch exercise {exerciseId}", response.StatusCode, payload);
        }

        var exercise = Deserialize<PolarExerciseDto>(payload);
        _logger.LogInformation("Successfully fetched exercise {ExerciseId}, sport: {Sport}, duration: {Duration}", 
            exerciseId, exercise.Sport, exercise.DurationIso8601);
        
        return (exercise, payload);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)> ListExercisesAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        var url = "/v3/exercises?samples=true&zones=true&route=true";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        _logger.LogInformation("Listing exercises via GET /v3/exercises");

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("ListExercises failed with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("Failed to list exercises", response.StatusCode, payload);
        }

        var exercises = Deserialize<List<PolarExerciseDto>>(payload);
        _logger.LogInformation("ListExercises returned {Count} exercise(s)", exercises.Count);

        return (exercises, payload);
    }

    public async Task<PolarWebhookResponse?> CreateWebhookAsync(string webhookUrl, List<string> events, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.WebhooksEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new PolarWebhookRequest
            {
                Events = events,
                Url = webhookUrl
            }), Encoding.UTF8, "application/json")
        };
        ApplyBasicAuth(request);

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Webhook already exists for this client");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Webhook creation failed with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("Webhook creation failed", response.StatusCode, payload);
        }

        var webhookResponse = Deserialize<PolarWebhookResponse>(payload);
        _logger.LogInformation("Webhook created successfully. ID: {WebhookId}, Events: {Events}", 
            webhookResponse.Data.Id, string.Join(", ", webhookResponse.Data.Events));
        
        return webhookResponse;
    }

    public async Task<PolarWebhookInfo?> GetWebhookAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _options.WebhooksEndpoint);
        ApplyBasicAuth(request);

        var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to retrieve webhook info with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("Failed to retrieve webhook info", response.StatusCode, payload);
        }

        var webhookInfo = Deserialize<PolarWebhookInfo>(payload);
        _logger.LogInformation("Retrieved webhook info. Count: {Count}", webhookInfo.Data.Count);
        
        return webhookInfo;
    }

    public async Task ActivateWebhookAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.WebhooksActivateEndpoint);
        ApplyBasicAuth(request);

        var response = await _http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            _logger.LogWarning("Webhook does not exist and cannot be activated");
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var payload = await ReadPayloadAsync(response, cancellationToken);
            _logger.LogError("Webhook activation failed with status {Status}: {Payload}", response.StatusCode, payload);
            throw new PolarApiException("Webhook activation failed", response.StatusCode, payload);
        }

        _logger.LogInformation("Webhook activated successfully");
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    private static async Task<string> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, _jsonOptions)!;
    }
}
