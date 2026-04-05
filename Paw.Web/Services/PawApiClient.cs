using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Paw.Core.DTOs;

namespace Paw.Web.Services;

public class PawApiClient(HttpClient http, IHttpClientFactory factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<ActivityListItem>> GetActivitiesAsync(
        string personId, int limit = 50, CancellationToken ct = default)
    {
        var response = await http.GetAsync(
            $"/qep/polar/activities/{Uri.EscapeDataString(personId)}?limit={limit}", ct);

        if (!response.IsSuccessStatusCode)
            return new List<ActivityListItem>();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<ActivityListItem>>(json, JsonOpts) ?? new();
    }

    public async Task<WorkoutWeekStats?> GetWeekStatsAsync(
        string personId, DateTime? weekOf = null, CancellationToken ct = default)
    {
        var url = $"/qep/polar/stats/{Uri.EscapeDataString(personId)}";
        if (weekOf.HasValue)
            url += $"?weekOf={Uri.EscapeDataString(weekOf.Value.ToString("O"))}";

        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<WorkoutWeekStats>(json, JsonOpts);
    }

    public async Task<bool> HasPolarLinkAsync(string personId, CancellationToken ct = default)
    {
        var response = await http.GetAsync(
            $"/qep/polar/link/by-person/{Uri.EscapeDataString(personId)}", ct);
        return response.StatusCode == HttpStatusCode.OK;
    }

    // Proxies the connect request server-side so the API key is never exposed in the browser.
    // Returns the Polar OAuth URL from the Location header, or null on failure.
    public async Task<string?> GetPolarConnectUrlAsync(
        string personId, string email, CancellationToken ct = default)
    {
        var noRedirectClient = factory.CreateClient("PawApiNoRedirect");
        var url = $"/qep/polar/connect?email={Uri.EscapeDataString(email)}&personId={Uri.EscapeDataString(personId)}";
        var response = await noRedirectClient.GetAsync(url, ct);

        if (response.StatusCode != HttpStatusCode.Redirect &&
            response.StatusCode != HttpStatusCode.MovedPermanently &&
            (int)response.StatusCode is not (>= 300 and < 400))
            return null;

        return response.Headers.Location?.AbsoluteUri;
    }
}
