using System.Net;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Polly;
using Serilog;
using Serilog.Formatting.Compact;
using Paw.Core.Domain;
using Paw.Core.Services;
using Paw.Infrastructure;
using Paw.Infrastructure.HealthChecks;
using Paw.Polar;
using Paw.Api;
using Paw.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, config) =>
{
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter());
});

// Add services to the container.
// Swagger/OpenAPI is only registered in Development (see middleware section below)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "PAW API",
            Version = "v1",
            Description = "PAW — Polar Activity Worker. Self-hosted .NET 8 backend that ingests workouts from Polar wearables via OAuth + webhooks.",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact
            {
                Name = "PAW on GitHub",
                Url = new Uri("https://github.com/albertoelizondo/paw")
            }
        });

        options.AddSecurityDefinition("QepApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Name = "X-QEP-API-Key",
            Description = "API Key for QEP integration endpoints. Required for /qep/polar/* endpoints."
        });

        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "QepApiKey"
                    }
                },
                Array.Empty<string>()
            }
        });
    });
}


// Configure CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() 
                    ?? new[] { "http://localhost:5002" };
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Authentication decision: API-key auth via X-QEP-API-Key header is the sole
// authentication mechanism.  JWT was evaluated and removed because all callers
// are server-side (QEP Web App) and authenticate with a shared secret key.
// If browser-facing flows are added later, re-evaluate adding JWT/cookie auth.

// Rate limiting for endpoints
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("webhook", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });

    // Per-IP: prevent a single caller from hammering individual-user sync
    options.AddPolicy("sync-user", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Global: sync-all triggers Polar API calls for every user — keep it tight
    options.AddFixedWindowLimiter("sync-all", opt =>
    {
        opt.PermitLimit = 3;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PawDbContext>()
    .AddCheck<StuckWebhookHealthCheck>("stuck-webhooks");

builder.Services.AddOptions<PolarOptions>()
    .Bind(builder.Configuration.GetSection("Polar"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IPolarClient, PolarClient>()
    .AddPolicyHandler((services, _) =>
    {
        var logger = services.GetRequiredService<ILogger<PolarClient>>();
        return Policy<HttpResponseMessage>
            .HandleResult(r => r.StatusCode is
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 200)),
                onRetry: (outcome, timespan, retryAttempt, _) =>
                    logger.LogWarning(
                        "Polar API transient error (Status={Status}). Retry {Attempt}/3 in {Delay:F1}s",
                        outcome.Result?.StatusCode, retryAttempt, timespan.TotalSeconds));
    });
builder.Services.AddScoped<IActivitySyncService, ActivitySyncService>();
builder.Services.AddScoped<IPolarWebhookVerifier, PolarWebhookVerifier>();
builder.Services.AddScoped<IWorkoutStatsService, WorkoutStatsService>();
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<PawDbContext>(options => options.UseSqlServer(connStr));
builder.Services.AddDbContextFactory<PawDbContext>(options => options.UseSqlServer(connStr), ServiceLifetime.Scoped);

var app = builder.Build();

// Swagger UI (Development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PAW API v1");
        c.RoutePrefix = "swagger"; // Access at /swagger
        c.DocumentTitle = "PAW API Documentation";
    });
}


// Configure middleware
app.UseCors();
app.UseRateLimiter();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// ###############################
// QEP POLAR INTEGRATION ENDPOINTS
// ###############################
app.MapQepPolarEndpoints();

app.MapGet("/auth/polar/connect", (Guid userId, IOptions<PolarOptions> options) =>
{
    var polar = options.Value;
    
    var query = new QueryString()
        .Add("response_type", "code")
        .Add("client_id", polar.ClientId)
        .Add("redirect_uri", polar.RedirectUri)
        // .Add("scope", "accesslink.read_all")
        .Add("state", userId.ToString());
    
    var authUrl = $"https://flow.polar.com/oauth2/authorization{query}";
    return Results.Redirect(authUrl);
});

// Legacy endpoints (/auth/polar/callback, /polar/sync, /admin/polar/fetch-exercise,
// /users/{userId}/workout-stats, /api/users/*) were removed. They relied on tables
// (DeviceAccounts, Activities, Users) that no longer exist in QEPTest.
// The QEP integration pattern (/qep/polar/*) replaces them entirely.

// Setup webhook - call this once to create the webhook
app.MapPost("/admin/polar/webhook/setup", async (
    IPolarClient polarClient,
    IOptions<PolarOptions> options,
    ILogger<Program> logger) =>
{
    try
    {
        var polarOpts = options.Value;
        
        // Check if webhook already exists
        var existingWebhook = await polarClient.GetWebhookAsync();
        if (existingWebhook?.Data.Count > 0)
        {
            var webhook = existingWebhook.Data[0];
            logger.LogInformation("Webhook already exists. ID: {Id}, Active: {Active}, Events: {Events}", 
                webhook.Id, webhook.Active, string.Join(", ", webhook.Events));
            
            if (!webhook.Active)
            {
                await polarClient.ActivateWebhookAsync();
                return Results.Ok(new { message = "Webhook was inactive and has been reactivated.", webhookId = webhook.Id });
            }
            
            return Results.Ok(new { message = "Webhook already exists and is active.", webhookId = webhook.Id });
        }

        // Create new webhook
        var response = await polarClient.CreateWebhookAsync(
            polarOpts.WebhookUrl,
            new List<string> { "EXERCISE" });

        if (response == null)
        {
            return Results.Conflict(new { message = "Webhook already exists (returned from API)." });
        }

        // The secret is returned once in the response body. It is NOT logged —
        // structured logs are often shipped to external sinks (Seq, Datadog, etc.)
        // where a plaintext secret would be persisted indefinitely.
        logger.LogInformation("Webhook created successfully. ID: {WebhookId}. Copy the signatureSecretKey from the response body and store it in appsettings / a secret manager — it will not be shown again.",
            response.Data.Id);

        return Results.Ok(new
        {
            message = "Webhook created. IMPORTANT: copy signatureSecretKey now — it will not be returned again.",
            webhookId = response.Data.Id,
            signatureSecretKey = response.Data.SignatureSecretKey,
            events = response.Data.Events,
            url = response.Data.Url
        });
    }
    catch (PolarApiException ex)
    {
        logger.LogError(ex, "Failed to setup webhook");
        return Results.Problem(ex.Message, statusCode: (int)ex.StatusCode);
    }
})
.WithName("SetupWebhook")
.WithTags("Admin")
.RequireQepApiKey("QepAdministrator");

// Get webhook status
app.MapGet("/admin/polar/webhook/status", async (
    IPolarClient polarClient,
    ILogger<Program> logger) =>
{
    try
    {
        var webhook = await polarClient.GetWebhookAsync();
        
        if (webhook?.Data.Count == 0)
        {
            return Results.NotFound(new { message = "No webhook configured." });
        }

        return Results.Ok(webhook);
    }
    catch (PolarApiException ex)
    {
        logger.LogError(ex, "Failed to get webhook status");
        return Results.Problem(ex.Message, statusCode: (int)ex.StatusCode);
    }
})
.WithName("GetWebhookStatus")
.WithTags("Admin")
.RequireQepApiKey("QepAdministrator");

// Receive webhook events from Polar
app.MapPost("/webhooks/polar", async (
    HttpRequest request,
    IPolarWebhookVerifier verifier,
    PawDbContext db,
    IConfiguration config,
    ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync();

    logger.LogInformation("Received Polar webhook: {Payload}", payload);

    // Check if this is a ping/verification request
    if (string.IsNullOrWhiteSpace(payload) || payload == "{}" || payload.Contains("\"event\":\"PING\""))
    {
        logger.LogInformation("Received webhook ping/verification request - responding with 200 OK");
        return Results.Ok(new { message = "Webhook verified" });
    }

    // Get signature enforcement setting (defaults to true for production safety)
    var enforceSignature = config.GetValue<bool>("Polar:EnforceWebhookSignature", defaultValue: true);

    // Verify signature
    if (!request.Headers.TryGetValue("Polar-Webhook-Signature", out var signatureHeader))
    {
        if (enforceSignature)
        {
            logger.LogWarning("Webhook signature header missing - rejecting request (enforcement enabled)");
            return Results.Unauthorized();
        }
        
        logger.LogWarning("No signature header found in webhook request - allowing in non-enforced mode");
    }
    else
    {
        var signature = signatureHeader.ToString();
        if (!verifier.VerifySignature(payload, signature))
        {
            logger.LogWarning("Webhook signature verification failed");
            return Results.Unauthorized();
        }
    }

    try
    {
        var webhookPayload = System.Text.Json.JsonSerializer.Deserialize<PolarWebhookPayload>(payload);
        
        if (webhookPayload == null)
        {
            logger.LogWarning("Failed to deserialize webhook payload");
            return Results.BadRequest(new { message = "Invalid payload" });
        }

        // Store the webhook event
        var webhookEvent = new WebhookEvent
        {
            Provider = ActivityProviderType.Polar,
            EventType = webhookPayload.Event,
            ExternalUserId = webhookPayload.UserId,
            EntityID = webhookPayload.EntityId,
            EventTimestamp = webhookPayload.Timestamp,
            ResourceUrl = webhookPayload.Url,
            Status = "Pending",
            ReceivedAtUtc = DateTime.UtcNow,
            RawPayload = payload
        };

        db.WebhookEvents.Add(webhookEvent);
        await db.SaveChangesAsync();

        logger.LogInformation("Webhook event stored. Event: {Event}, UserId: {UserId}, EntityId: {EntityId}", 
            webhookPayload.Event, webhookPayload.UserId, webhookPayload.EntityId);

        // Note: Background processing handled by Paw.Worker service
        // Worker polls for pending webhooks every 10 seconds and processes them
        
        return Results.Ok(new { message = "Webhook received and stored" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing webhook");
        return Results.Ok(new { message = "Webhook acknowledged (with processing error)" });
    }
})
.RequireRateLimiting("webhook");
// Note: Webhook endpoint is public - no authentication required, but rate-limited

// Root path redirects to Swagger in Development, returns 404 otherwise
if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/swagger"))
        .ExcludeFromDescription();
}

// Health check endpoint — returns structured JSON compatible with HealthChecks UI
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();

// Make Program accessible to test project
public partial class Program { }
