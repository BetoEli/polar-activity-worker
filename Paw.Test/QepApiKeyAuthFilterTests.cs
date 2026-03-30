using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Tests for QepApiKeyAuthFilter — verifies that protected endpoints correctly
/// enforce API key authentication and role-based access control.
/// Run with: dotnet test --filter "FullyQualifiedName~QepApiKeyAuthFilterTests"
/// </summary>
[TestFixture]
public class QepApiKeyAuthFilterTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private const string AdminKey = "test-admin-key";
    private const string StudentKey = "test-student-key";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["QepApiKeys:QepAdministrator"] = AdminKey,
                        ["QepApiKeys:QepFaculty"] = "test-faculty-key",
                        ["QepApiKeys:student"] = StudentKey,
                        ["ConnectionStrings:DefaultConnection"] = "ignored",
                        ["Polar:ClientId"] = "test",
                        ["Polar:ClientSecret"] = "test",
                        ["Polar:RedirectUri"] = "http://localhost/callback",
                        ["Polar:WebhookUrl"] = "https://example.com/webhooks/polar",
                        ["QepWebAppUrl"] = "http://localhost:5002",
                        ["QepWebAppRedirectUrl"] = "http://localhost:5002/Polar/Connected",
                    });
                });
                builder.ConfigureServices(services =>
                {
                    // Remove ALL DbContext-related registrations so we can swap in InMemory
                    var dbDescriptors = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<PawDbContext>)
                                 || d.ServiceType == typeof(PawDbContext)
                                 || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                        .ToList();
                    foreach (var d in dbDescriptors) services.Remove(d);

                    services.AddDbContext<PawDbContext>(o =>
                        o.UseInMemoryDatabase($"QepApiKeyAuthTests_{Guid.NewGuid()}"));

                    var polarDescriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(IPolarClient));
                    if (polarDescriptor != null) services.Remove(polarDescriptor);
                    services.AddSingleton<IPolarClient>(new StubPolarClient());
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task ProtectedEndpoint_NoApiKey_Returns401()
    {
        var response = await _client.GetAsync("/qep/polar/link/12345");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ProtectedEndpoint_WrongApiKey_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/qep/polar/link/12345");
        request.Headers.Add("X-QEP-API-Key", "completely-wrong-key");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ProtectedEndpoint_ValidStudentKey_PassesAuth()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/qep/polar/link/99999");
        request.Headers.Add("X-QEP-API-Key", StudentKey);

        var response = await _client.SendAsync(request);

        // 404 means auth passed (no such PolarLink), 401 would mean auth failed
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AdminOnlyEndpoint_StudentKey_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/polar/webhook/setup");
        request.Headers.Add("X-QEP-API-Key", StudentKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AdminOnlyEndpoint_AdminKey_PassesAuth()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/polar/webhook/setup");
        request.Headers.Add("X-QEP-API-Key", AdminKey);

        var response = await _client.SendAsync(request);

        // Will not be 401 — auth passed. Actual status depends on stub Polar client response.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Response_401_HasProblemDetailsFormat()
    {
        var response = await _client.GetAsync("/qep/polar/link/12345");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().ContainAny("title", "status", "detail");
    }

    // Minimal stub: returns sensible defaults so auth tests can exercise the auth layer
    // without calling the real Polar API.
    private sealed class StubPolarClient : IPolarClient
    {
        public Task<PolarTokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(new PolarTokenResponse { AccessToken = "stub", XUserId = 1L });

        public Task<PolarTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarUserRegistrationResponse?> RegisterUserAsync(string accessToken, string memberId, CancellationToken cancellationToken = default)
            => Task.FromResult<PolarUserRegistrationResponse?>(null);

        [Obsolete]
        public Task<IReadOnlyList<PolarTrainingSession>> GetTrainingsAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PolarTrainingSession>>(Array.Empty<PolarTrainingSession>());

        public Task<IReadOnlyList<PolarExerciseDto>> GetExercisesAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PolarExerciseDto>>(Array.Empty<PolarExerciseDto>());

        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(DeviceAccount account, string exerciseId, CancellationToken cancellationToken = default)
            => Task.FromResult<(PolarExerciseDto?, string)?>(null);

        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(string accessToken, string exerciseId, CancellationToken cancellationToken = default)
            => Task.FromResult<(PolarExerciseDto?, string)?>(null);

        public Task<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)> ListExercisesAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<PolarExerciseDto>, string)>((Array.Empty<PolarExerciseDto>(), "[]"));

        public Task<PolarWebhookResponse?> CreateWebhookAsync(string webhookUrl, List<string> events, CancellationToken cancellationToken = default)
            => Task.FromResult<PolarWebhookResponse?>(null);

        public Task<PolarWebhookInfo?> GetWebhookAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<PolarWebhookInfo?>(new PolarWebhookInfo { Data = new List<PolarWebhookInfoData>() });

        public Task ActivateWebhookAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
