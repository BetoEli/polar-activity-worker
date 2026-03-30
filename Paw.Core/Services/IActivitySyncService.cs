namespace Paw.Core.Services;

using Paw.Core.Domain;

public interface IActivitySyncService
{
    /// <summary>
    /// Sync activities for a user from the specified provider since the given date (or all available).
    /// </summary>
    Task SyncActivitiesForUserAsync(Guid userId, ActivityProviderType provider, DateTime? sinceUtc = null);

    /// <summary>
    /// Process a single Polar webhook event by its ID (from WebhookEvents table).
    /// </summary>
    Task ProcessPolarWebhookEventAsync(long webhookEventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a batch of pending Polar webhook events, returns count of processed events.
    /// </summary>
    Task<int> ProcessPendingPolarWebhookEventsBatchAsync(int maxBatchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull exercises from Polar for a single user using GET /v3/exercises (30-day sweep)
    /// and upsert them into the QEP tables. Creates a PolarTransaction for each exercise;
    /// commits ONLY when Activity + HeartRateZones are saved successfully.
    /// If any exercise fails the transaction is left uncommitted.
    /// </summary>
    /// <param name="polarLink">The PolarLink record for the student to sync.</param>
    /// <param name="since">Only process exercises with upload_time >= this UTC time (defaults to 30 days ago).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of exercises successfully synced.</returns>
    Task<int> SyncUserAsync(PolarLink polarLink, DateTime? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run <see cref="SyncUserAsync"/> for every active PolarLink in the database.
    /// Processes users in parallel (up to 5 concurrent users) using isolated DB contexts per task.
    /// </summary>
    /// <param name="since">Only process exercises with upload_time >= this UTC time (defaults to 30 days ago).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of exercises successfully synced across all users.</returns>
    Task<int> SyncAllUsersAsync(DateTime? since = null, CancellationToken cancellationToken = default);
}



