using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paw.Api.Authentication;
using Paw.Core.Domain;
using Paw.Core.DTOs;
using Paw.Core.Services;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Api;

public static class QepPolarEndpoints
{
    public static void MapQepPolarEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/qep/polar")
            .WithTags("QEP Polar Registration")
            .WithOpenApi();
        // Note: API key authentication is applied per-endpoint, not to the entire group

        // GET /qep/polar/connect - Start OAuth flow with email and PersonID
        group.MapGet("/connect", (
            [FromQuery] string email,
            [FromQuery] string personId,
            Microsoft.Extensions.Options.IOptions<PolarOptions> options,
            ILogger<Program> logger) =>
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(personId))
                {
                    logger.LogWarning("Missing required parameters: email={Email}, personId={PersonId}", email, personId);
                    return Results.BadRequest(new { message = "Email and PersonID are required" });
                }

                logger.LogInformation("QEP student connecting Polar: {Email}, {PersonId}", email, personId);

                var polar = options.Value;
                
                // Encode email and PersonID in state parameter
                var stateData = new QepPolarState
                {
                    Email = email,
                    PersonId = personId
                };
                var stateJson = System.Text.Json.JsonSerializer.Serialize(stateData);
                var stateBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(stateJson));
                
                // Build Polar OAuth URL with encoded state
                var authUrl = $"{polar.AuthorizationEndpoint}?" +
                    $"response_type=code&" +
                    $"client_id={polar.ClientId}&" +
                    $"redirect_uri={Uri.EscapeDataString(polar.RedirectUri)}&" +
                    $"state={Uri.EscapeDataString(stateBase64)}";
                
                logger.LogInformation("Redirecting to Polar OAuth for student {Email}", email);
                return Results.Redirect(authUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initiating Polar connection for QEP student");
                return Results.Problem("An error occurred while connecting to Polar");
            }
        })
        .WithName("QepPolarConnect")
        .WithSummary("Start Polar OAuth flow using email and PersonID")
        .WithDescription("Requires X-QEP-API-Key header for authentication. Called by QEP Web App.")
        .RequireQepApiKey("student", "QepFaculty", "QepAdministrator")
        .Produces(302)
        .Produces(400)
        .Produces(401);

        // GET /qep/polar/callback - OAuth callback with QEP student data
        group.MapGet("/callback", async (
            [FromQuery] string code,
            [FromQuery] string state,
            IPolarClient polarClient,
            PawDbContext db,
            IConfiguration config,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            // Resolve and validate redirect URL once — shared by all code paths including catch blocks.
            var qepWebAppUrl = config["QepWebAppUrl"] ?? "http://localhost:5000";
            var qepRedirectUrl = config["QepWebAppRedirectUrl"] ?? $"{qepWebAppUrl}/Polar/Connected";

            // Guard against open redirect: redirect target must be rooted on the configured web app.
            if (!RedirectUrlValidator.IsAllowed(qepRedirectUrl, qepWebAppUrl))
            {
                logger.LogError("Redirect URL {RedirectUrl} is not within configured web app base {BaseUrl} — aborting OAuth callback",
                    qepRedirectUrl, qepWebAppUrl);
                return Results.Problem("Invalid redirect configuration", statusCode: 500);
            }

            try
            {
                // Decode QEP student data from state
                var stateJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));
                var stateData = System.Text.Json.JsonSerializer.Deserialize<QepPolarState>(stateJson);

                if (stateData == null)
                {
                    logger.LogError("Failed to decode state parameter");
                    return Results.Redirect($"{qepRedirectUrl}?status=error&message={Uri.EscapeDataString("Invalid state parameter")}");
                }

                logger.LogInformation("Processing Polar callback for QEP student {Email}", stateData.Email);
                var username = stateData.Email.Split('@')[0];
 
                // Exchange code for access token (we need X-User-ID for PolarID)
                var token = await polarClient.ExchangeCodeForTokenAsync(code, ct);
                logger.LogInformation("Token exchange successful. X-User-ID: {XUserId}", token.XUserId);

                if (!token.XUserId.HasValue)
                {
                    logger.LogError("Token exchange did not return X-User-ID");
                    return Results.Redirect($"{qepRedirectUrl}?status=error&message={Uri.EscapeDataString("Failed to get Polar user ID")}");
                }

                var polarUserId = token.XUserId.Value;

                // STEP 1: Try to update placeholder record (PolarID = 0 or NULL)
                // This must happen FIRST to avoid any EF Core queries on records with null/zero primary keys
                var placeholderUpdated = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.PolarLinks
                    SET 
                        PolarID = {polarUserId},
                        AccessToken = {token.AccessToken},
                        Email = CASE WHEN Email IS NULL OR Email = '' THEN {stateData.Email} ELSE Email END
                    WHERE PersonID = {stateData.PersonId}
                      AND Username = {username}
                      AND (PolarID IS NULL OR PolarID = 0)
                ", ct);

                if (placeholderUpdated > 0)
                {
                    logger.LogInformation("Successfully upgraded placeholder PolarLink for {Email} (PersonID: {PersonId}, PolarID: {PolarId})", 
                        stateData.Email, stateData.PersonId, polarUserId);
                    return Results.Redirect($"{qepRedirectUrl}?status=success&email={Uri.EscapeDataString(stateData.Email)}");
                }

                // STEP 2: Check if this PolarID already exists (student already registered)
                var connection = db.Database.GetDbConnection();
                await connection.OpenAsync(ct);
                
                using (var checkCommand = connection.CreateCommand())
                {
                    checkCommand.CommandText = @"
                        SELECT Email, AccessToken
                        FROM dbo.PolarLinks
                        WHERE PolarID = @PolarId";
                    
                    var polarIdParam = checkCommand.CreateParameter();
                    polarIdParam.ParameterName = "@PolarId";
                    polarIdParam.Value = polarUserId;
                    checkCommand.Parameters.Add(polarIdParam);
                    
                    using var reader = await checkCommand.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        var existingEmail = reader.IsDBNull(0) ? null : reader.GetString(0);
                        var oldToken = reader.IsDBNull(1) ? null : reader.GetString(1);
                        
                        reader.Close();
                        
                        // SAFETY NOTE: ExecuteSqlInterpolatedAsync auto-parameterizes interpolated values.
                        // Do NOT change to ExecuteSqlRawAsync - that would create a SQL injection vulnerability.
                        var tokenUpdateCount = await db.Database.ExecuteSqlInterpolatedAsync($@"
                            UPDATE dbo.PolarLinks
                            SET 
                                AccessToken = {token.AccessToken},
                                Email = CASE WHEN Email IS NULL OR Email = '' THEN {stateData.Email} ELSE Email END
                            WHERE PolarID = {polarUserId}
                        ", ct);
                        
                        var finalEmail = existingEmail ?? stateData.Email;
                        
                        logger.LogInformation("Updated AccessToken and Email for existing Polar User ID {PolarUserId} ({Email}). Old token length: {OldLength}, New token length: {NewLength}", 
                            polarUserId, finalEmail, oldToken?.Length ?? 0, token.AccessToken?.Length ?? 0);
                        
                        logger.LogWarning("Polar User ID {PolarUserId} is already connected to {Email}", 
                            polarUserId, finalEmail);
                        
                        return Results.Redirect($"{qepRedirectUrl}?status=already_connected&email={Uri.EscapeDataString(finalEmail)}");
                    }
                }

                // STEP 3: Check if a complete record already exists for this PersonID + Username
                using (var completeCheckCommand = connection.CreateCommand())
                {
                    completeCheckCommand.CommandText = @"
                        SELECT COUNT(*)
                        FROM dbo.PolarLinks
                        WHERE PersonID = @PersonId
                          AND Username = @Username
                          AND PolarID IS NOT NULL
                          AND PolarID <> 0
                          AND AccessToken IS NOT NULL
                          AND AccessToken <> ''";
                    
                    var personIdParam = completeCheckCommand.CreateParameter();
                    personIdParam.ParameterName = "@PersonId";
                    personIdParam.Value = stateData.PersonId;
                    completeCheckCommand.Parameters.Add(personIdParam);
                    
                    var usernameParam = completeCheckCommand.CreateParameter();
                    usernameParam.ParameterName = "@Username";
                    usernameParam.Value = username;
                    completeCheckCommand.Parameters.Add(usernameParam);
                    
                    var existingCompleteCount = (int?)await completeCheckCommand.ExecuteScalarAsync(ct) ?? 0;

                    if (existingCompleteCount > 0)
                    {
                        logger.LogWarning("PolarLink for PersonID {PersonId} and Username {Username} already has complete data.", 
                            stateData.PersonId, username);
                        return Results.Redirect($"{qepRedirectUrl}?status=already_connected&email={Uri.EscapeDataString(stateData.Email)}");
                    }
                }
 
                // STEP 4: Register user with Polar AccessLink
                var memberId = $"{stateData.Email}:{stateData.PersonId}";
                var registrationResponse = await polarClient.RegisterUserAsync(token.AccessToken, memberId, ct);
                
                if (registrationResponse != null)
                {
                    logger.LogInformation("Student {Email} registered with Polar. Polar User ID: {PolarUserId}", 
                        stateData.Email, registrationResponse.PolarUserId);
                }

                // SAFETY NOTE: ExecuteSqlInterpolatedAsync auto-parameterizes interpolated values.
                // Do NOT change to ExecuteSqlRawAsync - that would create a SQL injection vulnerability.
                var insertCount = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO dbo.PolarLinks (PolarID, Username, PersonID, Email, DeviceType, TargetZone, AccessToken)
                    VALUES ({polarUserId}, {username}, {stateData.PersonId}, {stateData.Email}, 'A300', 'Fitness', {token.AccessToken})
                ", ct);

                if (insertCount > 0)
                {
                    logger.LogInformation("Student {Email} (PersonID: {PersonId}) connected Polar device. PolarID: {PolarId}", 
                        stateData.Email, stateData.PersonId, polarUserId);
                    return Results.Redirect($"{qepRedirectUrl}?status=success&email={Uri.EscapeDataString(stateData.Email)}");
                }
                else
                {
                    logger.LogError("Failed to insert PolarLink for {Email} (PersonID: {PersonId})", 
                        stateData.Email, stateData.PersonId);
                    return Results.Redirect($"{qepRedirectUrl}?status=error&message={Uri.EscapeDataString("Failed to create record")}");
                }
            }
            catch (PolarApiException ex)
            {
                logger.LogError(ex, "Polar API error during callback");
                return Results.Redirect($"{qepRedirectUrl}?status=error&message={Uri.EscapeDataString(ex.Message)}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Polar callback for QEP student");
                return Results.Redirect($"{qepRedirectUrl}?status=error&message={Uri.EscapeDataString("An error occurred")}");
            }
        })
        .WithName("QepPolarCallback")
        .WithSummary("Handle Polar OAuth callback and register user")
        .WithDescription("Called by Polar Flow after user authorization. Does NOT require API key authentication.")
        .Produces(302);
        // Note: No API key authentication on callback - this is called by Polar, not QEP

        // POST /qep/polar/link - Create or update a PolarLink record directly, Ideally we should use a PATCH method instead of POST but this is for full CRUD on
        // PolarLinks and we want to allow both creation and updates in one endpoint for simplicity of the QEP Web App integration. Called by QEP Web App with X-QEP-API-Key header.
        group.MapPost("/link", async (
            [FromBody] PolarLink polarLink,
            PawDbContext db,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(polarLink.PersonID) || string.IsNullOrWhiteSpace(polarLink.Username))
            {
                return Results.BadRequest(new { message = "PersonID and Username are required" });
            }

            // Check if a PolarLink with this PolarID already exists
            var existing = await db.PolarLinks.FirstOrDefaultAsync(p => p.PolarID == polarLink.PolarID, ct);

            if (existing != null)
            {
                // Update existing record
                existing.AccessToken = polarLink.AccessToken ?? existing.AccessToken;
                existing.Email = !string.IsNullOrWhiteSpace(polarLink.Email) ? polarLink.Email : existing.Email;
                existing.DeviceType = polarLink.DeviceType ?? existing.DeviceType;
                existing.TargetZone = polarLink.TargetZone ?? existing.TargetZone;
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Updated PolarLink for {Email} (PolarID: {PolarId})", existing.Email, existing.PolarID);
                return Results.Ok(existing);
            }

            // Create new record
            db.PolarLinks.Add(polarLink);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Created PolarLink for {Email} (PolarID: {PolarId})", polarLink.Email, polarLink.PolarID);
            return Results.Created($"/qep/polar/link/{polarLink.PolarID}", polarLink);
        })
        .WithName("CreateOrUpdatePolarLink")
        .WithSummary("Create or update a PolarLink record")
        .WithDescription("Creates a new PolarLink or updates an existing one if PolarID already exists. Requires X-QEP-API-Key header.")
        .RequireQepApiKey("QepFaculty", "QepAdministrator")
        .Produces<PolarLink>(201)
        .Produces<PolarLink>(200)
        .Produces(400)
        .Produces(401);

        // GET /qep/polar/link/by-person/{personId} - Get a PolarLink by QEP PersonID
        // Must be registered before /link/{polarId:long} to avoid route ambiguity
        group.MapGet("/link/by-person/{personId}", async (
            [FromRoute] string personId,
            PawDbContext db,
            CancellationToken ct) =>
        {
            var link = await db.PolarLinks
                .FirstOrDefaultAsync(p => p.PersonID == personId, ct);
            return link is null ? Results.NotFound() : Results.Ok(link);
        })
        .WithName("GetPolarLinkByPersonId")
        .WithSummary("Get a Polar link by QEP PersonID")
        .RequireQepApiKey("student", "QepFaculty", "QepAdministrator")
        .Produces<PolarLink>(200)
        .Produces(404)
        .Produces(401);

        // GET /qep/polar/link/{polarId} - Get a PolarLink by PolarID
        group.MapGet("/link/{polarId:long}", async (
            [FromRoute] long polarId,
            PawDbContext db,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var polarLink = await db.PolarLinks.FirstOrDefaultAsync(p => p.PolarID == polarId, ct);

            if (polarLink == null)
            {
                return Results.NotFound(new { message = $"PolarLink with PolarID {polarId} not found" });
            }

            return Results.Ok(polarLink);
        })
        .WithName("GetPolarLink")
        .WithSummary("Get a Polar link by Polar user ID")
        .WithDescription("Requires X-QEP-API-Key header for authentication.")
        .RequireQepApiKey("student", "QepFaculty", "QepAdministrator")
        .Produces<PolarLink>(200)
        .Produces(404)
        .Produces(401);

        group.MapDelete("/link/{polarId:long}", async (
            [FromRoute] long polarId,
            PawDbContext db,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var polarLink = await db.PolarLinks.FirstOrDefaultAsync(p => p.PolarID == polarId, ct);

            if (polarLink == null)
            {
                logger.LogWarning("Attempted to remove PolarLink {PolarId} but it was not found", polarId);
                return Results.NotFound(new { message = $"PolarLink with PolarID {polarId} not found" });
            }

            db.PolarLinks.Remove(polarLink);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Removed PolarLink for {Email} (PolarID: {PolarId})", polarLink.Email, polarId);
            return Results.NoContent();
        })
        .WithName("DeletePolarLink")
        .WithSummary("Remove an existing Polar link by Polar user ID")
        .WithDescription("Requires X-QEP-API-Key header for authentication. Called by QEP Web App.")
        .RequireQepApiKey("QepFaculty", "QepAdministrator")
        .Produces(204)
        .Produces(404)
        .Produces(401);

        // POST /qep/polar/sync/{polarId} - Sync exercises for a single user
        group.MapPost("/sync/{polarId:long}", async (
            [FromRoute] long polarId,
            PawDbContext db,
            IActivitySyncService syncService,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var polarLink = await db.PolarLinks.FirstOrDefaultAsync(p => p.PolarID == polarId, ct);

            if (polarLink == null)
            {
                return Results.NotFound(new { message = $"PolarLink with PolarID {polarId} not found" });
            }

            if (string.IsNullOrWhiteSpace(polarLink.AccessToken))
            {
                return Results.BadRequest(new { message = $"PolarLink {polarId} does not have an AccessToken" });
            }

            logger.LogInformation("Sync requested for {Email} (PolarID: {PolarId})", polarLink.Email, polarId);

            var committed = await syncService.SyncUserAsync(polarLink, cancellationToken: ct);

            return Results.Ok(new { polarId, email = polarLink.Email, exercisesCommitted = committed });
        })
        .WithName("SyncUser")
        .WithSummary("Run past 30-day exercise sync for a single Polar user")
        .WithDescription("Fetches exercises from Polar via GET /v3/exercises and upserts any missing ones into Activity/HeartRateZones. Requires X-QEP-API-Key header.")
        .RequireQepApiKey("QepFaculty", "QepAdministrator", "student")
        .RequireRateLimiting("sync-user")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .Produces(401)
        .Produces(429);

        // POST /qep/polar/sync-all - Sync exercises for all active users
        group.MapPost("/sync-all", async (
            IServiceProvider services,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation("Sync-all requested - starting sequential sweep");

            using var scope = services.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IActivitySyncService>();

            var totalCommitted = await syncService.SyncAllUsersAsync(cancellationToken: ct);

            logger.LogInformation("SyncAll complete: {Committed} exercises synced", totalCommitted);

            return Results.Ok(new
            {
                totalExercisesCommitted = totalCommitted
            });
        })
        .WithName("SyncAllUsers")
        .WithSummary("Run past 30-day exercise sync for all active Polar users")
        .WithDescription("Processes up to ~1000 users with throttled concurrency (max 10 parallel). Requires X-QEP-API-Key header.")
        .RequireQepApiKey("QepFaculty", "QepAdministrator")
        .RequireRateLimiting("sync-all")
        .Produces(200)
        .Produces(401)
        .Produces(429);
        // GET /qep/polar/activities/{personId} - List recent activities for a student
        group.MapGet("/activities/{personId}", async (
            [FromRoute] string personId,
            [FromQuery] int limit,
            PawDbContext db,
            CancellationToken ct) =>
        {
            if (limit <= 0) limit = 50;

            var activities = await db.Activities
                .Include(a => a.HeartRateZones)
                .Where(a => a.UserID == personId)
                .OrderByDescending(a => a.DateDone)
                .Take(limit)
                .ToListAsync(ct);

            var result = activities.Select(a => new ActivityListItem
            {
                ActivityId = a.ActivityID,
                EntityId = a.EntityID,
                DateDone = a.DateDone,
                Minutes = a.Minutes,
                Distance = a.Distance,
                AerobicPoints = a.AerobicPoints,
                DeviceType = a.DeviceType,
                TargetZone = a.TargetZone,
                HeartRateZones = a.HeartRateZones.Select(z => new HeartRateZoneSummary
                {
                    Zone = z.Zone,
                    DurationMinutes = z.Duration,
                    Lower = z.Lower,
                    Upper = z.Upper
                }).ToList()
            });

            return Results.Ok(result);
        })
        .WithName("GetActivities")
        .WithSummary("List recent activities for a student by PersonID")
        .RequireQepApiKey("student", "QepFaculty", "QepAdministrator")
        .Produces<IEnumerable<ActivityListItem>>(200)
        .Produces(401);

        // GET /qep/polar/stats/{personId} - Weekly workout stats for a student
        group.MapGet("/stats/{personId}", async (
            [FromRoute] string personId,
            [FromQuery] string? weekOf,
            PawDbContext db,
            CancellationToken ct) =>
        {
            var pivot = weekOf != null
                ? DateTime.Parse(weekOf, null, System.Globalization.DateTimeStyles.RoundtripKind)
                : DateTime.Today;
            var weekStart = pivot.AddDays(-(int)pivot.DayOfWeek); // Sunday
            var weekEnd = weekStart.AddDays(7);

            var activities = await db.Activities
                .Include(a => a.HeartRateZones)
                .Where(a => a.UserID == personId && a.DateDone >= weekStart && a.DateDone < weekEnd)
                .ToListAsync(ct);

            var days = activities
                .GroupBy(a => a.DateDone?.Date ?? DateTime.MinValue)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var daySummaries = g.Select(a => new ActivitySummary
                    {
                        ActivityId = a.ActivityID,
                        SportType = a.DeviceType ?? "Unknown",
                        StartTime = a.DateDone ?? DateTime.MinValue,
                        DurationMinutes = a.Minutes ?? 0,
                        Qualifies = (a.Minutes ?? 0) >= 20,
                        HeartRateZoneMinutes = a.HeartRateZones?.Sum(z => (int)(z.Duration ?? 0)) ?? 0,
                        ZoneBreakdown = a.HeartRateZones?.Count > 0 ? new HeartRateZoneBreakdown
                        {
                            Zone1Minutes = (int)(a.HeartRateZones.FirstOrDefault(z => z.Zone == 1)?.Duration ?? 0),
                            Zone2Minutes = (int)(a.HeartRateZones.FirstOrDefault(z => z.Zone == 2)?.Duration ?? 0),
                            Zone3Minutes = (int)(a.HeartRateZones.FirstOrDefault(z => z.Zone == 3)?.Duration ?? 0),
                            Zone4Minutes = (int)(a.HeartRateZones.FirstOrDefault(z => z.Zone == 4)?.Duration ?? 0),
                            Zone5Minutes = (int)(a.HeartRateZones.FirstOrDefault(z => z.Zone == 5)?.Duration ?? 0),
                        } : null
                    }).ToList();

                    return new WorkoutDaySummary
                    {
                        Date = g.Key,
                        Activities = daySummaries,
                        TotalWorkoutMinutes = daySummaries.Sum(s => s.DurationMinutes),
                        QualifyingMinutes = daySummaries.Where(s => s.Qualifies).Sum(s => s.DurationMinutes),
                        HasQualifyingWorkout = daySummaries.Any(s => s.Qualifies)
                    };
                }).ToList();

            var stats = new WorkoutWeekStats
            {
                WeekStartDate = weekStart,
                WeekEndDate = weekEnd.AddDays(-1),
                QualifyingWorkoutDays = days.Count(d => d.HasQualifyingWorkout),
                Days = days
            };

            return Results.Ok(stats);
        })
        .WithName("GetWeekStats")
        .WithSummary("Weekly workout stats for a student by PersonID")
        .RequireQepApiKey("student", "QepFaculty", "QepAdministrator")
        .Produces<WorkoutWeekStats>(200)
        .Produces(401);

        // GET /qep/polar/sync-history/{personId} - Recent webhook processing history for a user
        group.MapGet("/sync-history/{personId}", async (
            [FromRoute] string personId,
            [FromQuery] int? limit,
            PawDbContext db,
            CancellationToken ct) =>
        {
            var take = (limit is null or <= 0) ? 20 : limit.Value;

            var link = await db.PolarLinks
                .FirstOrDefaultAsync(p => p.PersonID == personId, ct);

            if (link is null)
                return Results.NotFound(new { message = $"No Polar link found for personId '{personId}'" });

            var events = await db.WebhookEvents
                .Where(e => e.ExternalUserId == link.PolarID)
                .OrderByDescending(e => e.ReceivedAtUtc)
                .Take(take)
                .Select(e => new WebhookEventSummary
                {
                    Id = e.Id,
                    EventType = e.EventType,
                    EntityId = e.EntityID,
                    Status = e.Status,
                    ReceivedAtUtc = e.ReceivedAtUtc,
                    ProcessedAtUtc = e.ProcessedAtUtc,
                    RetryCount = e.RetryCount,
                    ErrorMessage = e.ErrorMessage
                })
                .ToListAsync(ct);

            return Results.Ok(events);
        })
        .WithName("GetSyncHistory")
        .WithSummary("Recent webhook processing history for a user by PersonID")
        .RequireQepApiKey("student", "QepFaculty", "QepAdministrator")
        .Produces<IEnumerable<WebhookEventSummary>>(200)
        .Produces(404)
        .Produces(401);
     }

    // Note: GetOrCreateUserGuidAsync is no longer needed for QEP flow
    // QEP uses PolarLinks table which doesn't require User records
}

// DTO for QEP Polar OAuth state
public class QepPolarState
{
    public string Email { get; set; } = "";
    public string PersonId { get; set; } = "";
}

file static class RedirectUrlValidator
{
    /// <summary>
    /// Ensures the redirect URL is rooted at the configured web app base URL.
    /// Prevents open redirect if the config value is ever accidentally set to an external host.
    /// </summary>
    internal static bool IsAllowed(string redirectUrl, string webAppBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(redirectUrl) || string.IsNullOrWhiteSpace(webAppBaseUrl))
            return false;

        return redirectUrl.StartsWith(webAppBaseUrl, StringComparison.OrdinalIgnoreCase);
    }
}



