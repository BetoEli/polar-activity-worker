using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Paw.Core.Domain;
using Paw.Infrastructure;

namespace Paw.Test;

/// <summary>
/// Integration and Schema Validation tests for PolarLinks table operations against the real QEPTest database.
/// Tests CRUD operations, unique constraints, and data integrity.
/// Run with: dotnet test --filter "FullyQualifiedName~PolarLinksIntegrationTests"
/// </summary>
[TestFixture]
[Explicit]
public class PolarLinksIntegrationTests
{
    private PawDbContext _dbContext = null!;
    private IConfiguration _configuration = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Load configuration from appsettings.Test.json
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();
    }

    [SetUp]
    public void SetUp()
    {
        var connectionString = _configuration.GetConnectionString("QEPTestConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            Assert.Fail("QEPTestConnection not found in appsettings.Test.json");
        }

        var options = new DbContextOptionsBuilder<PawDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        _dbContext = new PawDbContext(options);
        
        // Verify connection
        try
        {
            _dbContext.Database.CanConnect();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Cannot connect to QEPTest database: {ex.Message}");
        }
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext?.Dispose();
    }

    [Test]
    public async Task CreatePolarLink_WithAllFields_SavesSuccessfully()
    {
        // Arrange
        var testPolarId = 99000001;
        var testPersonId = "0999001";
        var testUsername = "integtest01";
        var testEmail = "integtest01@southern.edu";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = "test_token_123456789"
            };

            // Act
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Assert
            var savedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == testPolarId);

            savedLink.Should().NotBeNull();
            savedLink!.PolarID.Should().Be(testPolarId);
            savedLink.Username.Should().Be(testUsername);
            savedLink.PersonID.Should().Be(testPersonId);
            savedLink.Email.Should().Be(testEmail);
            savedLink.DeviceType.Should().Be("A300");
            savedLink.TargetZone.Should().Be("Fitness");
            savedLink.AccessToken.Should().Be("test_token_123456789");
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task CreatePolarLink_WithNullableFields_SavesSuccessfully()
    {
        // Arrange
        var testPolarId = 99000002;
        var testPersonId = "0999002";
        var testUsername = "integtest02";
        var testEmail = "integtest02@southern.edu";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = null,  // Nullable
                TargetZone = null,  // Nullable
                AccessToken = null  // Nullable
            };

            // Act
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Assert
            var savedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == testPolarId);

            savedLink.Should().NotBeNull();
            savedLink!.PolarID.Should().Be(testPolarId);
            savedLink.DeviceType.Should().BeNull();
            savedLink.TargetZone.Should().BeNull();
            savedLink.AccessToken.Should().BeNull();
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task UpdatePolarLink_AccessToken_UpdatesSuccessfully()
    {
        // Arrange
        var testPolarId = 99000003;
        var testPersonId = "0999003";
        var testUsername = "integtest03";
        var testEmail = "integtest03@southern.edu";
        var originalToken = "original_token_123";
        var updatedToken = "updated_token_456";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            // Create initial link
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = originalToken
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act - Update access token
            var linkToUpdate = await _dbContext.PolarLinks.FirstAsync(p => p.PolarID == testPolarId);
            linkToUpdate.AccessToken = updatedToken;
            await _dbContext.SaveChangesAsync();

            // Assert
            var updatedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstAsync(p => p.PolarID == testPolarId);

            updatedLink.AccessToken.Should().Be(updatedToken);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task UpdatePolarLink_RawSql_FillsInNullFields()
    {
        // Arrange
        var testPolarId = 99000004;
        var testPersonId = "0999004";
        var testUsername = "integtest04";
        var testEmail = "integtest04@southern.edu";
        var newToken = "filled_token_789";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            // Create link with NULL PolarID and AccessToken using raw SQL
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                VALUES (0, {testUsername}, {testPersonId}, {testEmail}, 'A300', 'Fitness', NULL)
            ");

            // Act - Update using raw SQL to fill in missing fields
            var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.PolarLinks
                SET 
                    PolarID = CASE WHEN PolarID IS NULL OR PolarID = 0 THEN {testPolarId} ELSE PolarID END,
                    AccessToken = CASE WHEN AccessToken IS NULL OR AccessToken = '' THEN {newToken} ELSE AccessToken END,
                    Email = CASE WHEN Email IS NULL OR Email = '' THEN {testEmail} ELSE Email END
                WHERE PersonID = {testPersonId}
                  AND Username = {testUsername}
                  AND ((PolarID IS NULL OR PolarID = 0) OR (AccessToken IS NULL OR AccessToken = '') OR (Email IS NULL OR Email = ''))
            ");

            // Assert
            rowsAffected.Should().Be(1);

            var updatedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstAsync(p => p.PolarID == testPolarId);

            updatedLink.PolarID.Should().Be(testPolarId);
            updatedLink.AccessToken.Should().Be(newToken);
            updatedLink.Email.Should().Be(testEmail);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task QueryPolarLink_ByPolarID_ReturnsCorrectRecord()
    {
        // Arrange
        var testPolarId = 99000005;
        var testPersonId = "0999005";
        var testUsername = "integtest05";
        var testEmail = "integtest05@southern.edu";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "FT60",
                TargetZone = "Fat Burn",
                AccessToken = "query_test_token"
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act
            var foundLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == testPolarId);

            // Assert
            foundLink.Should().NotBeNull();
            foundLink!.PersonID.Should().Be(testPersonId);
            foundLink.Username.Should().Be(testUsername);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task QueryPolarLink_ByPersonIdAndUsername_ReturnsCorrectRecord()
    {
        // Arrange
        var testPolarId = 99000006;
        var testPersonId = "0999006";
        var testUsername = "integtest06";
        var testEmail = "integtest06@southern.edu";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "A300",
                TargetZone = "Cardio",
                AccessToken = "query_test_token_2"
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act
            var foundLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PersonID == testPersonId && p.Username == testUsername);

            // Assert
            foundLink.Should().NotBeNull();
            foundLink!.PolarID.Should().Be(testPolarId);
            foundLink.Email.Should().Be(testEmail);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task QueryPolarLink_ByEmail_ReturnsCorrectRecord()
    {
        // Arrange
        var testPolarId = 99000007;
        var testPersonId = "0999007";
        var testUsername = "integtest07";
        var testEmail = "integtest07@southern.edu";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "H10",
                TargetZone = "Fitness",
                AccessToken = "email_query_token"
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act
            var foundLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Email == testEmail);

            // Assert
            foundLink.Should().NotBeNull();
            foundLink!.PolarID.Should().Be(testPolarId);
            foundLink.PersonID.Should().Be(testPersonId);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task DeletePolarLink_ByPolarID_RemovesRecord()
    {
        // Arrange
        var testPolarId = 99000008;
        var testPersonId = "0999008";
        var testUsername = "integtest08";
        var testEmail = "integtest08@southern.edu";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = "delete_test_token"
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act
            var linkToDelete = await _dbContext.PolarLinks.FirstAsync(p => p.PolarID == testPolarId);
            _dbContext.PolarLinks.Remove(linkToDelete);
            await _dbContext.SaveChangesAsync();

            // Assert
            var deletedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == testPolarId);

            deletedLink.Should().BeNull();
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task QueryPolarLink_WithNullPolarID_UsingRawSql()
    {
        // Arrange
        var testPersonId = "0999011";
        var testUsername = "integtest11";
        var testEmail = "integtest11@southern.edu";

        await CleanupTestDataAsync(0, testPersonId, testUsername);

        try
        {
            // Create link with NULL/0 PolarID using raw SQL
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                VALUES (0, {testUsername}, {testPersonId}, {testEmail}, 'A300', 'Fitness', NULL)
            ");

            // Act - Query using raw SQL to avoid EF Core materialization issues
            var connection = _dbContext.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM dbo.PolarLinks
                WHERE PersonID = @PersonId
                  AND Username = @Username
                  AND (PolarID IS NULL OR PolarID = 0)";

            var personIdParam = command.CreateParameter();
            personIdParam.ParameterName = "@PersonId";
            personIdParam.Value = testPersonId;
            command.Parameters.Add(personIdParam);

            var usernameParam = command.CreateParameter();
            usernameParam.ParameterName = "@Username";
            usernameParam.Value = testUsername;
            command.Parameters.Add(usernameParam);

            var count = (int?)await command.ExecuteScalarAsync() ?? 0;

            // Assert
            count.Should().Be(1, "Should find one record with NULL/0 PolarID");
        }
        finally
        {
            await CleanupTestDataAsync(0, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task GetAllPolarLinks_ReturnsMultipleRecords()
    {
        // Arrange
        var testPolarId1 = 99000012;
        var testPolarId2 = 99000013;
        var testPersonId1 = "0999012";
        var testPersonId2 = "0999013";
        var testUsername1 = "integtest12";
        var testUsername2 = "integtest13";

        await CleanupTestDataAsync(testPolarId1, testPersonId1, testUsername1);
        await CleanupTestDataAsync(testPolarId2, testPersonId2, testUsername2);

        try
        {
            var polarLink1 = new PolarLink
            {
                PolarID = testPolarId1,
                Username = testUsername1,
                PersonID = testPersonId1,
                Email = "integtest12@southern.edu",
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = "token_12"
            };

            var polarLink2 = new PolarLink
            {
                PolarID = testPolarId2,
                Username = testUsername2,
                PersonID = testPersonId2,
                Email = "integtest13@southern.edu",
                DeviceType = "FT60",
                TargetZone = "Fat Burn",
                AccessToken = "token_13"
            };

            _dbContext.PolarLinks.AddRange(polarLink1, polarLink2);
            await _dbContext.SaveChangesAsync();

            // Act
            var allLinks = await _dbContext.PolarLinks
                .AsNoTracking()
                .Where(p => p.PolarID >= 99000000)  // Only get our test records
                .OrderBy(p => p.PolarID)
                .ToListAsync();

            // Assert
            allLinks.Should().HaveCountGreaterOrEqualTo(2);
            allLinks.Should().Contain(p => p.PolarID == testPolarId1);
            allLinks.Should().Contain(p => p.PolarID == testPolarId2);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId1, testPersonId1, testUsername1);
            await CleanupTestDataAsync(testPolarId2, testPersonId2, testUsername2);
        }
    }

    [Test]
    public async Task UpdatePolarLink_MultipleFields_SavesSuccessfully()
    {
        // Arrange
        var testPolarId = 99000014;
        var testPersonId = "0999014";
        var testUsername = "integtest14";
        var testEmail = "integtest14@southern.edu";
        var updatedEmail = "updated14@southern.edu";
        var updatedDeviceType = "H10";
        var updatedTargetZone = "Cardio";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = "original_token"
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act
            var linkToUpdate = await _dbContext.PolarLinks.FirstAsync(p => p.PolarID == testPolarId);
            linkToUpdate.Email = updatedEmail;
            linkToUpdate.DeviceType = updatedDeviceType;
            linkToUpdate.TargetZone = updatedTargetZone;
            await _dbContext.SaveChangesAsync();

            // Assert
            var updatedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstAsync(p => p.PolarID == testPolarId);

            updatedLink.Email.Should().Be(updatedEmail);
            updatedLink.DeviceType.Should().Be(updatedDeviceType);
            updatedLink.TargetZone.Should().Be(updatedTargetZone);
            updatedLink.Username.Should().Be(testUsername); // Unchanged
            updatedLink.PersonID.Should().Be(testPersonId); // Unchanged
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task RegisterPlaceholderAccount_WithNullEmailAndZeroPolarId_UpdatesSuccessfully()
    {
        // Arrange: Simulate a placeholder record like what exists in production
        var testPersonId = "0999015";
        var testUsername = "integtest15";
        var testEmail = "integtest15@southern.edu";
        var newPolarId = 99000015;
        var newAccessToken = "registered_token_xyz";

        await CleanupTestDataAsync(0, testPersonId, testUsername);
        await CleanupTestDataAsync(newPolarId, testPersonId, testUsername);

        try
        {
            // Create placeholder record with NULL Email, 0 PolarID, NULL AccessToken (typical pre-registration state)
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                VALUES (0, {testUsername}, {testPersonId}, NULL, 'A300', 'Fitness', NULL)
            ");

            // Act - Simulate OAuth callback filling in missing data (using raw SQL to match production pattern)
            var rowsAffected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.PolarLinks
                SET 
                    PolarID = {newPolarId},
                    Email = {testEmail},
                    AccessToken = {newAccessToken}
                WHERE PersonID = {testPersonId}
                  AND Username = {testUsername}
                  AND (PolarID IS NULL OR PolarID = 0)
            ");

            // Assert
            rowsAffected.Should().Be(1, "Should update exactly one placeholder record");

            var updatedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == newPolarId && p.PersonID == testPersonId);

            updatedLink.Should().NotBeNull();
            updatedLink!.PolarID.Should().Be(newPolarId);
            updatedLink.Email.Should().Be(testEmail);
            updatedLink.AccessToken.Should().Be(newAccessToken);
            updatedLink.Username.Should().Be(testUsername);
            updatedLink.PersonID.Should().Be(testPersonId);
            updatedLink.DeviceType.Should().Be("A300");
            updatedLink.TargetZone.Should().Be("Fitness");
        }
        finally
        {
            await CleanupTestDataAsync(0, testPersonId, testUsername);
            await CleanupTestDataAsync(newPolarId, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task FindPlaceholderAccount_ByPersonIdAndUsername_ReturnsRecord()
    {
        // Arrange: Create a placeholder record like production data
        var testPersonId = "0999016";
        var testUsername = "integtest16";

        await CleanupTestDataAsync(0, testPersonId, testUsername);

        try
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                VALUES (0, {testUsername}, {testPersonId}, NULL, 'A300', 'Fat Burn', NULL)
            ");

            // Act - Find placeholder using raw SQL (EF Core has issues with PolarID = 0 as primary key)
            var connection = _dbContext.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken
                FROM dbo.PolarLinks
                WHERE PersonID = @PersonId
                  AND Username = @Username
                  AND (PolarID IS NULL OR PolarID = 0)";

            var personIdParam = command.CreateParameter();
            personIdParam.ParameterName = "@PersonId";
            personIdParam.Value = testPersonId;
            command.Parameters.Add(personIdParam);

            var usernameParam = command.CreateParameter();
            usernameParam.ParameterName = "@Username";
            usernameParam.Value = testUsername;
            command.Parameters.Add(usernameParam);

            using var reader = await command.ExecuteReaderAsync();
            
            // Assert
            reader.HasRows.Should().BeTrue("Should find the placeholder record");
            
            if (await reader.ReadAsync())
            {
                var polarId = reader.GetInt32(0);
                var username = reader.GetString(1);
                var personId = reader.GetString(2);
                var email = reader.IsDBNull(3) ? null : reader.GetString(3);
                var deviceType = reader.IsDBNull(4) ? null : reader.GetString(4);
                var targetZone = reader.IsDBNull(5) ? null : reader.GetString(5);
                var accessToken = reader.IsDBNull(6) ? null : reader.GetString(6);

                polarId.Should().Be(0);
                username.Should().Be(testUsername);
                personId.Should().Be(testPersonId);
                email.Should().BeNull();
                deviceType.Should().Be("A300");
                targetZone.Should().Be("Fat Burn");
                accessToken.Should().BeNull();
            }
        }
        finally
        {
            await CleanupTestDataAsync(0, testPersonId, testUsername);
        }
    }

    [Test]
    public async Task RegisterMultiplePlaceholderAccounts_IndependentlyUpdated()
    {
        // Arrange: Simulate multiple students registering from placeholder state
        var testPersonId1 = "0999017";
        var testUsername1 = "integtest17";
        var testEmail1 = "integtest17@southern.edu";
        var newPolarId1 = 99000017;
        
        var testPersonId2 = "0999018";
        var testUsername2 = "integtest18";
        var testEmail2 = "integtest18@southern.edu";
        var newPolarId2 = 99000018;

        await CleanupTestDataAsync(0, testPersonId1, testUsername1);
        await CleanupTestDataAsync(0, testPersonId2, testUsername2);
        await CleanupTestDataAsync(newPolarId1, testPersonId1, testUsername1);
        await CleanupTestDataAsync(newPolarId2, testPersonId2, testUsername2);

        try
        {
            // Create two placeholder records
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                VALUES (0, {testUsername1}, {testPersonId1}, NULL, 'A300', 'Fitness', NULL)
            ");

            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                VALUES (0, {testUsername2}, {testPersonId2}, NULL, 'A300', 'Fat Burn', NULL)
            ");

            // Act - Register first student
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.PolarLinks
                SET PolarID = {newPolarId1}, Email = {testEmail1}, AccessToken = 'token1'
                WHERE PersonID = {testPersonId1} AND Username = {testUsername1} AND (PolarID IS NULL OR PolarID = 0)
            ");

            // Act - Register second student
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE dbo.PolarLinks
                SET PolarID = {newPolarId2}, Email = {testEmail2}, AccessToken = 'token2'
                WHERE PersonID = {testPersonId2} AND Username = {testUsername2} AND (PolarID IS NULL OR PolarID = 0)
            ");

            // Assert - Both should be registered independently
            var link1 = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == newPolarId1);

            var link2 = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolarID == newPolarId2);

            link1.Should().NotBeNull();
            link1!.PolarID.Should().Be(newPolarId1);
            link1.Email.Should().Be(testEmail1);
            link1.AccessToken.Should().Be("token1");

            link2.Should().NotBeNull();
            link2!.PolarID.Should().Be(newPolarId2);
            link2.Email.Should().Be(testEmail2);
            link2.AccessToken.Should().Be("token2");
        }
        finally
        {
            await CleanupTestDataAsync(0, testPersonId1, testUsername1);
            await CleanupTestDataAsync(0, testPersonId2, testUsername2);
            await CleanupTestDataAsync(newPolarId1, testPersonId1, testUsername1);
            await CleanupTestDataAsync(newPolarId2, testPersonId2, testUsername2);
        }
    }

    [Test]
    public async Task ReRegisterExistingAccount_UpdatesAccessToken()
    {
        // Arrange: Simulate a student who already registered but needs token refresh
        var testPolarId = 99000019;
        var testPersonId = "0999019";
        var testUsername = "integtest19";
        var testEmail = "integtest19@southern.edu";
        var originalToken = "original_token_abc";
        var refreshedToken = "refreshed_token_xyz";

        await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);

        try
        {
            // Create fully registered account
            var polarLink = new PolarLink
            {
                PolarID = testPolarId,
                Username = testUsername,
                PersonID = testPersonId,
                Email = testEmail,
                DeviceType = "A300",
                TargetZone = "Fitness",
                AccessToken = originalToken
            };
            _dbContext.PolarLinks.Add(polarLink);
            await _dbContext.SaveChangesAsync();

            // Act - Simulate re-authentication (student goes through OAuth again)
            var existingLink = await _dbContext.PolarLinks
                .FirstOrDefaultAsync(p => p.PolarID == testPolarId);

            existingLink.Should().NotBeNull();
            existingLink!.AccessToken = refreshedToken;
            await _dbContext.SaveChangesAsync();

            // Assert
            var updatedLink = await _dbContext.PolarLinks
                .AsNoTracking()
                .FirstAsync(p => p.PolarID == testPolarId);

            updatedLink.AccessToken.Should().Be(refreshedToken);
            updatedLink.Email.Should().Be(testEmail);
            updatedLink.PersonID.Should().Be(testPersonId);
        }
        finally
        {
            await CleanupTestDataAsync(testPolarId, testPersonId, testUsername);
        }
    }

    private async Task CleanupTestDataAsync(int polarId, string personId, string username)
    {
        // For polarId = 0, use raw SQL to avoid EF Core materialization issues with NULL primary key
        if (polarId == 0)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM dbo.PolarLinks
                WHERE (PolarID IS NULL OR PolarID = 0)
                  AND (PersonID = {personId} OR Username = {username})
            ");
            return;
        }

        var linksToDelete = await _dbContext.PolarLinks
            .Where(p => p.PolarID == polarId || p.PersonID == personId || p.Username == username)
            .ToListAsync();

        if (linksToDelete.Any())
        {
            _dbContext.PolarLinks.RemoveRange(linksToDelete);
            await _dbContext.SaveChangesAsync();
        }
    }
}
