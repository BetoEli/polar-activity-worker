using Microsoft.EntityFrameworkCore;
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

        // Register Polar HTTP client
        builder.Services.AddHttpClient<IPolarClient, PolarClient>();

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

