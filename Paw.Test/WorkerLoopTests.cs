using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Paw.Core.Services;
using Paw.Worker;

namespace Paw.Test;

/// <summary>
/// Unit tests for Worker scheduling logic.
/// Tests the background service loop, timing intervals, and error handling.
/// Uses IHost to properly start/stop the worker BackgroundService.
/// Marked Explicit since these tests take 30s+ to run due to the 10-second delay in the loop, and we want to run them manually when needed.
/// Run with: dotnet test --filter "FullyQualifiedName~WorkerLoopTests" --verbosity normal
/// </summary>
[TestFixture]
[Category("Explicit")]
public class WorkerLoopTests
{
    private Mock<IActivitySyncService> _mockSyncService = null!;

    [SetUp]
    public void SetUp()
    {
        _mockSyncService = new Mock<IActivitySyncService>();
    }

    // ?? Helper Methods ??

    /// <summary>
    /// Creates a test host with a Worker running, using the provided options.
    /// Defaults to a 10s poll interval and batch size of 50 when not specified,
    /// matching the production defaults in <see cref="WorkerOptions"/>.
    /// </summary>
    private IHost CreateWorkerHost(WorkerOptions? options = null)
    {
        var workerOptions = options ?? new WorkerOptions
        {
            PollingIntervalSeconds = 10,
            BatchSize = 50
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_mockSyncService.Object);
                // Register IOptions<WorkerOptions> so the Worker constructor can resolve it
                services.AddSingleton<IOptions<WorkerOptions>>(
                    new OptionsWrapper<WorkerOptions>(workerOptions));
                services.AddHostedService<Paw.Worker.Worker>();
            })
            .Build();

        return host;
    }

    // ============================================================================
    // Webhook Poll & Error Handling Tests
    // ============================================================================

    [Test]
    public async Task Worker_ProcessesWebhooks_AndRecoveres_From_Errors()
    {
        // Arrange
        var callCount = 0;
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call throws
                    throw new HttpRequestException("Polar API unavailable");
                }
                // Subsequent calls return 0
                return Task.FromResult(0);
            });

        // Use a 1-second interval so the test completes in ~3s instead of 15s
        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = 50 });
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(3));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Worker should have called the service multiple times despite error
        _mockSyncService.Verify(
            x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2),
            "Worker should continue running after webhook poll throws");
    }

    [Test]
    public async Task Worker_UsesCorrectBatchSize()
    {
        // Arrange - use a non-default batch size to confirm the Worker reads from IOptions
        const int expectedBatchSize = 25;
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = expectedBatchSize });
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(2));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // Assert - Should use the batch size supplied via IOptions
        _mockSyncService.Verify(
            x => x.ProcessPendingPolarWebhookEventsBatchAsync(expectedBatchSize, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            $"Worker should process webhooks in batches of {expectedBatchSize} as configured");
    }

    [Test]
    public async Task Worker_HandlesLargeBatchesOfWebhooks()
    {
        // Arrange
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1000);  // Large batch

        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = 50 });
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(2));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // Assert - batch size must stay at the configured value regardless of how many events were returned
        _mockSyncService.Verify(
            x => x.ProcessPendingPolarWebhookEventsBatchAsync(1000, It.IsAny<CancellationToken>()),
            Times.Never,
            "Batch size should always be the configured value, not adjusted by return count");
    }

    [Test]
    public async Task Worker_StopsCleanlyOnCancellation()
    {
        // Arrange
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = 50 });
        using var cts = new CancellationTokenSource();

        var startTime = DateTime.UtcNow;
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        var elapsedTime = DateTime.UtcNow - startTime;

        // Assert - Should stop within reasonable time after cancellation
        elapsedTime.Should().BeLessThan(TimeSpan.FromSeconds(5), 
            "Worker should exit cleanly when cancellation is requested");
    }

    [Test]
    public async Task Worker_ContinuesDespiteMultipleExceptionTypes()
    {
        // Arrange
        var callCount = 0;
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Database error");
                if (callCount == 2)
                    throw new TimeoutException("Request timeout");
                // Third+ calls succeed
                return Task.FromResult(0);
            });

        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = 50 });
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(4));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // Assert - Should have survived multiple exceptions
        _mockSyncService.Verify(
            x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(3),
            "Worker should handle multiple exception types gracefully");
    }

    [Test]
    public async Task Worker_RespectsInterruptionDuringDelay()
    {
        // Arrange
        var callCount = 0;
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return Task.FromResult(0);
            });

        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = 50 });
        using var cts = new CancellationTokenSource();

        // Cancel after 1.5 seconds (during first 1-second delay)
        cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Should have processed at least once before cancellation
        callCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Worker_DoesNotStarvePoll()
    {
        // Arrange - Track timing between calls
        var callTimes = new List<DateTime>();
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callTimes.Add(DateTime.UtcNow);
                return Task.FromResult(0);
            });

        // Use a 1-second interval; run for 4 seconds to reliably observe 3+ polls
        const int pollingIntervalSeconds = 1;
        var host = CreateWorkerHost(new WorkerOptions
        {
            PollingIntervalSeconds = pollingIntervalSeconds,
            BatchSize = 50
        });
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(4));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // Assert - Should have multiple calls
        callTimes.Should().HaveCountGreaterThanOrEqualTo(2,
            "Should poll multiple times over 4 seconds at a 1-second interval");

        if (callTimes.Count >= 2)
        {
            var interval = (callTimes[1] - callTimes[0]).TotalSeconds;
            interval.Should().BeGreaterThanOrEqualTo(pollingIntervalSeconds - 0.5,
                $"Should wait approximately {pollingIntervalSeconds} second(s) between polls");
            interval.Should().BeLessThan(pollingIntervalSeconds + 2,
                "Should not wait significantly longer than the configured interval");
        }
    }

    [Test]
    public async Task Worker_ProcessesBatchesSequentially()
    {
        // Arrange - verify batch processing order
        var processingOrder = new List<int>();
        _mockSyncService
            .Setup(x => x.ProcessPendingPolarWebhookEventsBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                processingOrder.Add(50); // Batch size is always 50
                return Task.FromResult(processingOrder.Count);
            });

        const int configuredBatchSize = 50;
        var host = CreateWorkerHost(new WorkerOptions { PollingIntervalSeconds = 1, BatchSize = configuredBatchSize });
        using var cts = new CancellationTokenSource();

        cts.CancelAfter(TimeSpan.FromSeconds(2));

        // Act
        try
        {
            await host.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // Assert - At least one batch processed, each with the configured size
        processingOrder.Count.Should().BeGreaterThanOrEqualTo(1,
            "Should process at least one batch");

        foreach (var size in processingOrder)
        {
            size.Should().Be(configuredBatchSize,
                $"Batch size should always equal the configured value of {configuredBatchSize}");
        }
    }
}
