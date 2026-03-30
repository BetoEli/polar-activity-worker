using Microsoft.Extensions.Options;
using Paw.Core.Services;

namespace Paw.Worker;

/// <summary>
/// Configuration for the background worker polling behavior.
/// </summary>
public class WorkerOptions
{
    /// <summary>
    /// How often the worker polls for pending webhook events (default: 10 seconds).
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of webhook events to process per polling cycle (default: 50).
    /// </summary>
    public int BatchSize { get; set; } = 50;
}

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _services;
    private readonly WorkerOptions _workerOptions;

    public Worker(ILogger<Worker> logger, IServiceProvider services, IOptions<WorkerOptions> workerOptions)
    {
        _logger = logger;
        _services = services;
        _workerOptions = workerOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Polar Worker started at: {time}. Polling every {Interval}s, batch size {BatchSize}",
            DateTimeOffset.Now, _workerOptions.PollingIntervalSeconds, _workerOptions.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IActivitySyncService>();

                var processed = await syncService.ProcessPendingPolarWebhookEventsBatchAsync(
                    _workerOptions.BatchSize, stoppingToken);

                if (processed > 0)
                {
                    _logger.LogInformation("Processed {Count} Polar webhook events at: {time}", processed, DateTimeOffset.Now);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing Polar webhook events in worker");
            }

            await Task.Delay(TimeSpan.FromSeconds(_workerOptions.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Polar Worker stopping at: {time}", DateTimeOffset.Now);
    }
}

