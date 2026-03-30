using System.Security.Cryptography;
using Microsoft.Extensions.Primitives;

namespace Paw.Api.Authentication;

/// <summary>
/// Endpoint filter that validates API key for QEP integration endpoints.
/// The API key must be provided in the X-QEP-API-Key header.
/// </summary>
public class QepApiKeyAuthFilter : IEndpointFilter
{
    private const string ApiKeyHeaderName = "X-QEP-API-Key";
    private readonly ILogger<QepApiKeyAuthFilter> _logger;
    private readonly IReadOnlyCollection<string> _allowedRoles;

    public QepApiKeyAuthFilter(IEnumerable<string> allowedRoles, ILogger<QepApiKeyAuthFilter> logger)
    {
        _allowedRoles = allowedRoles?.ToArray() ?? Array.Empty<string>();
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();

        // Get configured API keys
        var legacyApiKey = config["QepApiKey"]; // Legacy fallback
        var apiKeys = new Dictionary<string, string?>
        {
            ["student"] = config["QepApiKeys:student"],
            ["QepFaculty"] = config["QepApiKeys:QepFaculty"],
            ["QepAdministrator"] = config["QepApiKeys:QepAdministrator"],
            ["legacy"] = legacyApiKey
        };

        if (apiKeys.Values.All(string.IsNullOrWhiteSpace))
        {
            _logger.LogCritical("No QepApiKeys configured in appsettings. QEP endpoints will reject all requests.");
            return Results.Problem(
                title: "Configuration Error",
                detail: "API authentication is not properly configured",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Check if the API key header is present
        if (!httpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out StringValues providedApiKey))
        {
            _logger.LogWarning("QEP API request rejected: Missing {HeaderName} header. IP: {IP}, Path: {Path}",
                ApiKeyHeaderName,
                httpContext.Connection.RemoteIpAddress,
                httpContext.Request.Path);

            return Results.Problem(
                title: "Authentication Required",
                detail: $"The {ApiKeyHeaderName} header is required",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var providedKey = providedApiKey.ToString();

        // Determine if this key is allowed for the endpoint role set
        var allowedRoleKeys = _allowedRoles.Count == 0
            ? apiKeys
            : apiKeys.Where(kvp => _allowedRoles.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                     .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        if (allowedRoleKeys.Values.All(string.IsNullOrWhiteSpace))
        {
            _logger.LogCritical("QEP API request rejected: Allowed roles not configured. Roles: {Roles}",
                string.Join(", ", _allowedRoles));
            return Results.Problem(
                title: "Configuration Error",
                detail: "API authentication is not properly configured for this endpoint",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var isAuthorized = allowedRoleKeys.Values
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Any(key => SecureStringEquals(providedKey, key!));

        if (!isAuthorized)
        {
            _logger.LogWarning("QEP API request rejected: Invalid API key. IP: {IP}, Path: {Path}, Provided: {Provided}",
                httpContext.Connection.RemoteIpAddress,
                httpContext.Request.Path,
                MaskApiKey(providedKey));

            return Results.Problem(
                title: "Authentication Failed",
                detail: "The provided API key is invalid or not authorized for this operation",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        _logger.LogInformation("QEP API request authenticated successfully. IP: {IP}, Path: {Path}",
            httpContext.Connection.RemoteIpAddress,
            httpContext.Request.Path);

        return await next(context);
    }

    /// <summary>
    /// Performs constant-time string comparison to prevent timing attacks.
    /// Uses CryptographicOperations.FixedTimeEquals which does not leak length.
    /// </summary>
    private static bool SecureStringEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    /// <summary>
    /// Masks API key for logging (shows first 4 chars only).
    /// </summary>
    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return "<empty>";
        }

        if (apiKey.Length <= 4)
        {
            return "****";
        }

        return $"{apiKey[..4]}****";
    }
}
