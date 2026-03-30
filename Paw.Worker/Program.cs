using System.Net;
using Microsoft.EntityFrameworkCore;
using Polly;
using Paw.Core.Services;
using Paw.Infrastructure;
using Paw.Polar;

namespace Paw.Worker;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure Polar options
        builder.Services.AddOptions<PolarOptions>()
            .Bind(builder.Configuration.GetSection("Polar"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register Polar HTTP client with exponential back-off retry on transient errors
        builder.Services.AddHttpClient<IPolarClient, PolarClient>()
            .AddPolicyHandler((services, _) =>
            {
                var logger = services.GetRequiredService<ILogger<Worker>>();
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

        // Register database context
        builder.Services.AddDbContext<PawDbContext>(options =>
        {
            var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
            // Use SQL Server for QEPTest database
            options.UseSqlServer(connStr);
        });

        // Health checks (used by container orchestrators / uptime monitors)
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<PawDbContext>();

        // Register activity sync service
        builder.Services.AddScoped<IActivitySyncService, ActivitySyncService>();

        // Bind WorkerOptions to the 'Worker' configuration section
        builder.Services.AddOptions<WorkerOptions>()
            .Bind(builder.Configuration.GetSection("Worker"))
            .ValidateDataAnnotations();

        // Register background worker
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        host.Run();
    }
}

