using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Integration tests for QEP Polar endpoints.
/// Tests connect redirect, PolarLink CRUD via POST/GET/DELETE /qep/polar/link, and API key auth.
/// Run with: dotnet test --filter "FullyQualifiedName~QepPolarEndpointsTests" --verbosity normal
/// </summary>
[TestFixture]
[Explicit]
public class QepPolarEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _studentClient = null!;
    private HttpClient _adminClient = null!;
    private const string TestApiKeyStudent = "test-api-key-student";
    private const string TestApiKeyFaculty = "test-api-key-qepfaculty";
    private const string TestApiKeyAdmin = "test-api-key-qepadministrator";
    private const string TestRedirectUrl = "http://localhost/Qep/User/UserDetails";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["QepApiKeys:student"] = TestApiKeyStudent,
                        ["QepApiKeys:QepFaculty"] = TestApiKeyFaculty,
                        ["QepApiKeys:QepAdministrator"] = TestApiKeyAdmin,
                        ["QepWebAppRedirectUrl"] = TestRedirectUrl,
                        // Provide minimal Polar config so options validation passes
                        ["Polar:ClientId"] = "test-client-id",
                        ["Polar:ClientSecret"] = "test-client-secret",
                        ["Polar:RedirectUri"] = "http://localhost/qep/polar/callback"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove ALL DbContext-related registrations (options, DbContext itself, etc.)
                    var dbDescriptors = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<PawDbContext>)
                                 || d.ServiceType == typeof(PawDbContext)
                                 || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                        .ToList();
                    foreach (var d in dbDescriptors) services.Remove(d);

                    // Use EF Core InMemory database so no real SQL Server is needed
                    services.AddDbContext<PawDbContext>(options =>
                        options.UseInMemoryDatabase("QepEndpointTests"));

                    // Replace IPolarClient with a fake implementation
                    var polarDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IPolarClient));
                    if (polarDescriptor != null) services.Remove(polarDescriptor);
                    services.AddSingleton<IPolarClient, FakePolarClientForEndpoints>();
                });
            });

        // Disable auto-redirect so we can assert on 302 status codes
        var clientOptions = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        };

        _studentClient = _factory.CreateClient(clientOptions);
        _studentClient.DefaultRequestHeaders.Add("X-QEP-API-Key", TestApiKeyStudent);

        _adminClient = _factory.CreateClient(clientOptions);
        _adminClient.DefaultRequestHeaders.Add("X-QEP-API-Key", TestApiKeyAdmin);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _studentClient?.Dispose();
        _adminClient?.Dispose();
        _factory?.Dispose();
    }

    // ?? Helper ??

    /// <summary>
    /// Creates a fresh DbContext from a new DI scope.
    /// Caller is responsible for disposing the returned context.
    /// Since InMemory DB is shared by name, each scope gets the same data store.
    /// </summary>
    private PawDbContext CreateDbContext()
    {
        return _factory.Services.CreateScope()
            .ServiceProvider.GetRequiredService<PawDbContext>();
    }

    private async Task CleanupTestDataAsync(string personId, string username, int polarId)
    {
        var db = CreateDbContext();
        var existing = await db.PolarLinks
            .Where(p => p.PersonID == personId || p.Username == username || p.PolarID == polarId)
            .ToListAsync();

        if (existing.Any())
        {
            db.PolarLinks.RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }

    // ?? GET /qep/polar/connect ??

    [Test]
    public async Task Connect_WithValidEmailAndPersonId_RedirectsToPolarOAuth()
    {
        // Arrange
        var email = "testuser@southern.edu";
        var personId = "0123456";

        // Act
        var response = await _studentClient.GetAsync($"/qep/polar/connect?email={email}&personId={personId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().Contain("https://flow.polar.com/oauth2/authorization");
        location.Should().Contain("client_id=");
        location.Should().Contain("redirect_uri=");
        location.Should().Contain("state=");
    }

    [Test]
    public async Task Connect_WithMissingEmail_ReturnsBadRequest()
    {
        var response = await _studentClient.GetAsync("/qep/polar/connect?personId=0123456");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Connect_WithMissingPersonId_ReturnsBadRequest()
    {
        var response = await _studentClient.GetAsync("/qep/polar/connect?email=test@southern.edu");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ?? POST /qep/polar/link – create new ??

    [Test]
    public async Task CreateLink_WithValidData_ReturnsCreated()
    {
        var polarId = 12345678;
        var personId = "0999999";
        var username = "newuser";
        var email = "newuser@southern.edu";

        await CleanupTestDataAsync(personId, username, polarId);

        try
        {
            var newLink = new PolarLink
            {
                PolarID = polarId,
                PersonID = personId,
                Username = username,
                Email = email,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = "test_access_token_abc"
            };

            // Act
            var response = await _adminClient.PostAsJsonAsync("/qep/polar/link", newLink);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            // Verify the record is persisted
            using var db = CreateDbContext();
            var created = await db.PolarLinks.FirstOrDefaultAsync(p => p.PolarID == polarId);

            created.Should().NotBeNull();
            created!.PersonID.Should().Be(personId);
            created.Username.Should().Be(username);
            created.Email.Should().Be(email);
            created.AccessToken.Should().Be("test_access_token_abc");
            created.DeviceType.Should().Be("A300");
            created.TargetZone.Should().Be("Fitness");
        }
        finally
        {
            await CleanupTestDataAsync(personId, username, polarId);
        }
    }

    [Test]
    public async Task CreateLink_MissingPersonId_ReturnsBadRequest()
    {
        var link = new PolarLink
        {
            PolarID = 99000001,
            PersonID = "",      // missing
            Username = "",      // missing
            Email = "bad@southern.edu",
            AccessToken = "tok"
        };

        var response = await _adminClient.PostAsJsonAsync("/qep/polar/link", link);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ?? POST /qep/polar/link – update existing ??

    [Test]
    public async Task CreateLink_WhenPolarIdExists_UpdatesExistingRecord()
    {
        var polarId = 11223344;
        var personId = "0777777";
        var username = "connected";
        var email = "connected@southern.edu";

        await CleanupTestDataAsync(personId, username, polarId);

        try
        {
            // Seed an existing record
            using (var db = CreateDbContext())
            {
                db.PolarLinks.Add(new PolarLink
                {
                    PolarID = polarId,
                    Username = username,
                    PersonID = personId,
                    Email = email,
                    DeviceType = "A300",
                    TargetZone = "Fitness",
                    AccessToken = "old_token_12345"
                });
                await db.SaveChangesAsync();
            }

            // POST the same PolarID with a new token
            var updatedLink = new PolarLink
            {
                PolarID = polarId,
                Username = username,
                PersonID = personId,
                Email = email,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = "new_token_xyz"
            };

            var response = await _adminClient.PostAsJsonAsync("/qep/polar/link", updatedLink);

            // Assert – should return 200 OK (updated, not created)
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verify the token was updated
            using var verifyDb = CreateDbContext();
            var link = await verifyDb.PolarLinks.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == polarId);

            link.Should().NotBeNull();
            link!.AccessToken.Should().Be("new_token_xyz");
        }
        finally
        {
            await CleanupTestDataAsync(personId, username, polarId);
        }
    }

    // ?? GET /qep/polar/link/{polarId} ??

    [Test]
    public async Task GetLink_ExistingPolarId_ReturnsOk()
    {
        var polarId = 33445566;
        var personId = "0555555";
        var username = "gettest";

        await CleanupTestDataAsync(personId, username, polarId);

        try
        {
            using (var db = CreateDbContext())
            {
                db.PolarLinks.Add(new PolarLink
                {
                    PolarID = polarId,
                    Username = username,
                    PersonID = personId,
                    Email = "gettest@southern.edu",
                    DeviceType = "A300",
                    TargetZone = "Fitness",
                    AccessToken = "get_token"
                });
                await db.SaveChangesAsync();
            }

            var response = await _studentClient.GetAsync($"/qep/polar/link/{polarId}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<PolarLink>();
            body.Should().NotBeNull();
            body!.PolarID.Should().Be(polarId);
            body.PersonID.Should().Be(personId);
        }
        finally
        {
            await CleanupTestDataAsync(personId, username, polarId);
        }
    }

    [Test]
    public async Task GetLink_NonExistentPolarId_ReturnsNotFound()
    {
        var response = await _studentClient.GetAsync("/qep/polar/link/99999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?? DELETE /qep/polar/link/{polarId} ??

    [Test]
    public async Task DeleteLink_RemovesExistingPolarLink()
    {
        var polarId = 55667788;
        var personId = "0666666";
        var username = "deletetest";

        await CleanupTestDataAsync(personId, username, polarId);

        try
        {
            using (var db = CreateDbContext())
            {
                db.PolarLinks.Add(new PolarLink
                {
                    PolarID = polarId,
                    Username = username,
                    PersonID = personId,
                    Email = "deletetest@southern.edu",
                    DeviceType = "A300",
                    TargetZone = "Fitness",
                    AccessToken = "token_to_delete"
                });
                await db.SaveChangesAsync();
            }

            var response = await _adminClient.DeleteAsync($"/qep/polar/link/{polarId}");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Verify deleted
            using var verifyDb = CreateDbContext();
            var deleted = await verifyDb.PolarLinks.FirstOrDefaultAsync(p => p.PolarID == polarId);
            deleted.Should().BeNull();
        }
        finally
        {
            await CleanupTestDataAsync(personId, username, polarId);
        }
    }

    [Test]
    public async Task DeleteLink_ReturnsNotFound_WhenLinkDoesNotExist()
    {
        var response = await _adminClient.DeleteAsync("/qep/polar/link/99999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?? Fake Polar client ??

    private class FakePolarClientForEndpoints : IPolarClient
    {
        public Task<PolarTokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PolarTokenResponse
            {
                AccessToken = $"fake_access_token_{Guid.NewGuid():N}",
                TokenType = "Bearer",
                ExpiresIn = 3600,
                Scope = "accesslink.read_all",
                XUserId = 12345678L
            });
        }

        public Task<PolarUserRegistrationResponse?> RegisterUserAsync(string accessToken, string memberId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolarUserRegistrationResponse?>(new PolarUserRegistrationResponse
            {
                PolarUserId = 12345678L,
                MemberId = memberId,
                RegistrationDate = DateTime.UtcNow
            });
        }

        #region Unused interface members

        public Task<PolarTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PolarTrainingSession>> GetTrainingsAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PolarExerciseDto>> GetExercisesAsync(DeviceAccount account, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(DeviceAccount account, string exerciseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(PolarExerciseDto? Exercise, string RawJson)?> GetExerciseByIdAsync(string accessToken, string exerciseId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarWebhookResponse?> CreateWebhookAsync(string webhookUrl, List<string> events, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<PolarWebhookInfo?> GetWebhookAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ActivateWebhookAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)> ListExercisesAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<PolarExerciseDto> Exercises, string RawJson)>((Array.Empty<PolarExerciseDto>(), "[]"));

        #endregion
    }
}
