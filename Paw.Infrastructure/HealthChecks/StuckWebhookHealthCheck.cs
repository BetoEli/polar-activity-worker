using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Paw.Infrastructure.HealthChecks;

public class StuckWebhookHealthCheck(PawDbContext db) : IHealthCheck
{
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(30);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - StuckThreshold;

        var stuckCount = await db.WebhookEvents
            .CountAsync(e => e.Status == "Pending" && e.ReceivedAtUtc < cutoff, cancellationToken);

        if (stuckCount > 0)
            return HealthCheckResult.Degraded($"{stuckCount} webhook event(s) stuck in Pending for over 30 minutes.");

        return HealthCheckResult.Healthy();
    }
}
