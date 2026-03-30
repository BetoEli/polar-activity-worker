using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Paw.Core.Domain;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Test;

/// <summary>
/// Integration tests for QEP Polar sync endpoints.
/// Tests POST /qep/polar/sync/{polarId} and POST /qep/polar/sync-all endpoints.
/// Tests API key authentication (student vs faculty/admin roles).
/// Run with: dotnet test --filter "FullyQualifiedName~SyncEndpointsTests" --verbosity normal
/// </summary>
[TestFixture]
[Explicit]
public class SyncEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _studentClient = null!;
    private HttpClient _facultyClient = null!;
    private HttpClient _adminClient = null!;
    private HttpClient _noAuthClient = null!;
    
    private const string TestApiKeyStudent = "test-api-key-student";
    private const string TestApiKeyFaculty = "test-api-key-qepfaculty";
    private const string TestApiKeyAdmin = "test-api-key-qepadministrator";

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
                        ["QepWebAppRedirectUrl"] = "http://localhost/Qep/User/UserDetails",
                        ["Polar:ClientId"] = "test-client-id",
                        ["Polar:ClientSecret"] = "test-client-secret",
                        ["Polar:RedirectUri"] = "http://localhost/qep/polar/callback"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove DbContext registrations
                    var dbDescriptors = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<PawDbContext>)
                                 || d.ServiceType == typeof(PawDbContext)
                                 || d.ServiceType.FullName?.Contains("DbContextOptions") == true)
                        .ToList();
                    foreach (var d in dbDescriptors) services.Remove(d);

                    // Use EF Core InMemory database
                    services.AddDbContext<PawDbContext>(options =>
                        options.UseInMemoryDatabase("SyncEndpointTests"));

                    // Replace IPolarClient with a mock
                    var polarDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IPolarClient));
                    if (polarDescriptor != null) services.Remove(polarDescriptor);
                    
                    services.AddSingleton<IPolarClient>(GetMockPolarClient());
                });
            });

        var clientOptions = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        };

        _studentClient = _factory.CreateClient(clientOptions);
        _studentClient.DefaultRequestHeaders.Add("X-QEP-API-Key", TestApiKeyStudent);

        _facultyClient = _factory.CreateClient(clientOptions);
        _facultyClient.DefaultRequestHeaders.Add("X-QEP-API-Key", TestApiKeyFaculty);

        _adminClient = _factory.CreateClient(clientOptions);
        _adminClient.DefaultRequestHeaders.Add("X-QEP-API-Key", TestApiKeyAdmin);

        _noAuthClient = _factory.CreateClient(clientOptions);
        // No API key header
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _studentClient?.Dispose();
        _facultyClient?.Dispose();
        _adminClient?.Dispose();
        _noAuthClient?.Dispose();
        _factory?.Dispose();
    }

    // ?? Helpers ??

    private static IPolarClient GetMockPolarClient()
    {
        var mockClient = new Mock<IPolarClient>();
        
        // Mock ListExercisesAsync to return sample exercises
        mockClient
            .Setup(c => c.ListExercisesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<PolarExerciseDto>
            {
                new()
                {
                    Id = "ex-001",
                    StartTime = DateTime.UtcNow.AddDays(-5).ToString("o"),
                    UploadTime = DateTime.UtcNow.AddDays(-5).ToString("o"),
                    DurationIso8601 = "PT30M",
                    Sport = "RUNNING",
                    Distance = 5000,
                    HeartRateZones = new List<PolarHeartRateZoneDto>
                    {
                        new() { Index = 0, LowerLimit = 50, UpperLimit = 100, InZone = "PT5M" },
                        new() { Index = 1, LowerLimit = 100, UpperLimit = 130, InZone = "PT10M" },
                        new() { Index = 2, LowerLimit = 130, UpperLimit = 150, InZone = "PT10M" },
                        new() { Index = 3, LowerLimit = 150, UpperLimit = 170, InZone = "PT5M" },
                        new() { Index = 4, LowerLimit = 170, UpperLimit = 200, InZone = "PT0S" }
                    }
                }
            }.AsReadOnly() as IReadOnlyList<PolarExerciseDto>, "[]"));

        return mockClient.Object;
    }

    private PawDbContext CreateDbContext()
    {
        return _factory.Services.CreateScope()
            .ServiceProvider.GetRequiredService<PawDbContext>();
    }

    private async Task CleanupPolarLinkAsync(int polarId)
    {
        using var db = CreateDbContext();
        var existing = await db.PolarLinks
            .Where(p => p.PolarID == polarId)
            .ToListAsync();

        if (existing.Any())
        {
            db.PolarLinks.RemoveRange(existing);
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedPolarLinkAsync(int polarId, string personId, string username, string email, string? token = null)
    {
        using var db = CreateDbContext();
        db.PolarLinks.Add(new PolarLink
        {
            PolarID = polarId,
            PersonID = personId,
            Username = username,
            Email = email,
            DeviceType = "A300",
            TargetZone = "Fitness",
            AccessToken = token ?? "test_access_token_xyz"
        });
        await db.SaveChangesAsync();
    }

    // ?? POST /qep/polar/sync/{polarId} Tests ??

    [Test]
    public async Task SyncUser_WithValidPolarId_Returns200Ok()
    {
        // Arrange
        const int polarId = 7001;
        const string personId = "0001001";
        const string username = "synctest1";
        const string email = "synctest1@southern.edu";

        await CleanupPolarLinkAsync(polarId);
        await SeedPolarLinkAsync(polarId, personId, username, email);

        try
        {
            // Act
            var response = await _studentClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotBeNullOrEmpty();
            body.Should().Contain($"\"polarId\":{polarId}");
            body.Should().Contain($"\"email\":\"{email}\"");
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    [Test]
    public async Task SyncUser_WithNonExistentPolarId_Returns404NotFound()
    {
        // Arrange
        const int nonExistentPolarId = 99999999;

        // Act
        var response = await _studentClient.PostAsync($"/qep/polar/sync/{nonExistentPolarId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SyncUser_WithoutApiKey_Returns401Unauthorized()
    {
        // Arrange
        const int polarId = 7002;
        const string personId = "0001002";
        
        await CleanupPolarLinkAsync(polarId);
        await SeedPolarLinkAsync(polarId, personId, "synctest2", "synctest2@southern.edu");

        try
        {
            // Act
            var response = await _noAuthClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    [Test]
    public async Task SyncUser_WithStudentKey_Returns200Ok()
    {
        // Arrange
        const int polarId = 7003;
        const string personId = "0001003";
        const string username = "synctest3";
        const string email = "synctest3@southern.edu";

        await CleanupPolarLinkAsync(polarId);
        await SeedPolarLinkAsync(polarId, personId, username, email);

        try
        {
            // Act - Student key should be accepted for sync
            var response = await _studentClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    [Test]
    public async Task SyncUser_WithFacultyKey_Returns200Ok()
    {
        // Arrange
        const int polarId = 7004;
        const string personId = "0001004";
        const string username = "synctest4";
        const string email = "synctest4@southern.edu";

        await CleanupPolarLinkAsync(polarId);
        await SeedPolarLinkAsync(polarId, personId, username, email);

        try
        {
            // Act - Faculty key should be accepted
            var response = await _facultyClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    [Test]
    public async Task SyncUser_WithAdminKey_Returns200Ok()
    {
        // Arrange
        const int polarId = 7005;
        const string personId = "0001005";
        const string username = "synctest5";
        const string email = "synctest5@southern.edu";

        await CleanupPolarLinkAsync(polarId);
        await SeedPolarLinkAsync(polarId, personId, username, email);

        try
        {
            // Act - Admin key should be accepted
            var response = await _adminClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    [Test]
    public async Task SyncUser_WithoutAccessToken_Returns400BadRequest()
    {
        // Arrange
        const int polarId = 7006;
        const string personId = "0001006";
        const string username = "synctest6";
        const string email = "synctest6@southern.edu";

        await CleanupPolarLinkAsync(polarId);
        
        using (var db = CreateDbContext())
        {
            db.PolarLinks.Add(new PolarLink
            {
                PolarID = polarId,
                PersonID = personId,
                Username = username,
                Email = email,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = null  // No token
            });
            await db.SaveChangesAsync();
        }

        try
        {
            // Act
            var response = await _studentClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    [Test]
    public async Task SyncUser_ReturnsExerciseCount()
    {
        // Arrange
        const int polarId = 7007;
        const string personId = "0001007";
        const string username = "synctest7";
        const string email = "synctest7@southern.edu";

        await CleanupPolarLinkAsync(polarId);
        await SeedPolarLinkAsync(polarId, personId, username, email);

        try
        {
            // Act
            var response = await _studentClient.PostAsync($"/qep/polar/sync/{polarId}", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<SyncResponseDto>();
            body.Should().NotBeNull();
            if (body != null)
            {
                body.exercisesCommitted.Should().BeGreaterThanOrEqualTo(0);
                body.polarId.Should().Be(polarId);
            }
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId);
        }
    }

    // ?? POST /qep/polar/sync-all Tests ??

    [Test]
    public async Task SyncAll_WithFacultyKey_Returns200Ok()
    {
        // Arrange - Seed some test data
        const int polarId1 = 8001;
        const int polarId2 = 8002;

        await CleanupPolarLinkAsync(polarId1);
        await CleanupPolarLinkAsync(polarId2);

        await SeedPolarLinkAsync(polarId1, "0002001", "syncalltest1", "syncalltest1@southern.edu");
        await SeedPolarLinkAsync(polarId2, "0002002", "syncalltest2", "syncalltest2@southern.edu");

        try
        {
            // Act
            var response = await _facultyClient.PostAsync("/qep/polar/sync-all", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<SyncAllResponseDto>();
            body.Should().NotBeNull();
            body?.usersProcessed.Should().BeGreaterThanOrEqualTo(0);
            body?.totalExercisesCommitted.Should().BeGreaterThanOrEqualTo(0);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId1);
            await CleanupPolarLinkAsync(polarId2);
        }
    }

    [Test]
    public async Task SyncAll_WithAdminKey_Returns200Ok()
    {
        // Arrange - Seed some test data
        const int polarId1 = 8003;
        const int polarId2 = 8004;

        await CleanupPolarLinkAsync(polarId1);
        await CleanupPolarLinkAsync(polarId2);

        await SeedPolarLinkAsync(polarId1, "0002003", "syncalltest3", "syncalltest3@southern.edu");
        await SeedPolarLinkAsync(polarId2, "0002004", "syncalltest4", "syncalltest4@southern.edu");

        try
        {
            // Act
            var response = await _adminClient.PostAsync("/qep/polar/sync-all", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<SyncAllResponseDto>();
            body.Should().NotBeNull();
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId1);
            await CleanupPolarLinkAsync(polarId2);
        }
    }

    [Test]
    public async Task SyncAll_WithStudentKey_Returns401Unauthorized()
    {
        // Act - Student key should NOT be accepted for sync-all
        var response = await _studentClient.PostAsync("/qep/polar/sync-all", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SyncAll_WithoutApiKey_Returns401Unauthorized()
    {
        // Act
        var response = await _noAuthClient.PostAsync("/qep/polar/sync-all", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SyncAll_WithNoActiveLinks_Returns200Ok()
    {
        // Arrange - No PolarLinks with tokens
        // Act
        var response = await _facultyClient.PostAsync("/qep/polar/sync-all", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SyncAllResponseDto>();
        body.Should().NotBeNull();
        body!.usersProcessed.Should().Be(0);
        body.message.Should().Contain("No active PolarLinks");
    }

    [Test]
    public async Task SyncAll_WithMultipleActiveUsers_ProcessesAllUsers()
    {
        // Arrange - Seed multiple users
        const int polarId1 = 8005;
        const int polarId2 = 8006;
        const int polarId3 = 8007;

        await CleanupPolarLinkAsync(polarId1);
        await CleanupPolarLinkAsync(polarId2);
        await CleanupPolarLinkAsync(polarId3);

        await SeedPolarLinkAsync(polarId1, "0002005", "syncalltest5", "syncalltest5@southern.edu");
        await SeedPolarLinkAsync(polarId2, "0002006", "syncalltest6", "syncalltest6@southern.edu");
        await SeedPolarLinkAsync(polarId3, "0002007", "syncalltest7", "syncalltest7@southern.edu");

        try
        {
            // Act
            var response = await _facultyClient.PostAsync("/qep/polar/sync-all", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<SyncAllResponseDto>();
            body.Should().NotBeNull();
            body!.usersProcessed.Should().Be(3);
            body.totalActiveUsers.Should().Be(3);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId1);
            await CleanupPolarLinkAsync(polarId2);
            await CleanupPolarLinkAsync(polarId3);
        }
    }

    [Test]
    public async Task SyncAll_SkipsUsersWithoutAccessToken()
    {
        // Arrange - Mix of users with and without tokens
        const int polarId1 = 8008;
        const int polarId2 = 8009;

        await CleanupPolarLinkAsync(polarId1);
        await CleanupPolarLinkAsync(polarId2);

        // User with token
        await SeedPolarLinkAsync(polarId1, "0002008", "syncalltest8", "syncalltest8@southern.edu", "valid_token");
        
        // User without token
        using (var db = CreateDbContext())
        {
            db.PolarLinks.Add(new PolarLink
            {
                PolarID = polarId2,
                PersonID = "0002009",
                Username = "syncalltest9",
                Email = "syncalltest9@southern.edu",
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = null  // No token - should be skipped
            });
            await db.SaveChangesAsync();
        }

        try
        {
            // Act
            var response = await _facultyClient.PostAsync("/qep/polar/sync-all", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<SyncAllResponseDto>();
            body.Should().NotBeNull();
            // Only one user should be processed (the one with token)
            body!.usersProcessed.Should().Be(1);
            body.totalActiveUsers.Should().Be(1);  // Only active users count
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId1);
            await CleanupPolarLinkAsync(polarId2);
        }
    }

    [Test]
    public async Task SyncAll_ReturnsAggregateStatistics()
    {
        // Arrange - Seed multiple users
        const int polarId1 = 8010;
        const int polarId2 = 8011;

        await CleanupPolarLinkAsync(polarId1);
        await CleanupPolarLinkAsync(polarId2);

        await SeedPolarLinkAsync(polarId1, "0002010", "syncalltest10", "syncalltest10@southern.edu");
        await SeedPolarLinkAsync(polarId2, "0002011", "syncalltest11", "syncalltest11@southern.edu");

        try
        {
            // Act
            var response = await _facultyClient.PostAsync("/qep/polar/sync-all", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<SyncAllResponseDto>();
            body.Should().NotBeNull();
            body!.usersProcessed.Should().BeGreaterThanOrEqualTo(0);
            body.usersFailed.Should().BeGreaterThanOrEqualTo(0);
            body.totalExercisesCommitted.Should().BeGreaterThanOrEqualTo(0);
            body.totalActiveUsers.Should().Be(2);
        }
        finally
        {
            await CleanupPolarLinkAsync(polarId1);
            await CleanupPolarLinkAsync(polarId2);
        }
    }

    // ?? Response DTOs ??

    private class SyncResponseDto
    {
        public int polarId { get; set; }
        public string email { get; set; } = "";
        public int exercisesCommitted { get; set; }
    }

    private class SyncAllResponseDto
    {
        public int usersProcessed { get; set; }
        public int usersFailed { get; set; }
        public int totalExercisesCommitted { get; set; }
        public int totalActiveUsers { get; set; }
        public string? message { get; set; }
    }
}
