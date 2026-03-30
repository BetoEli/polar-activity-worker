using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Paw.Core.Domain;
using Paw.Core.Services;
using Paw.Infrastructure.Mappers;
using Paw.Polar;

namespace Paw.Infrastructure;

public class ActivitySyncService : IActivitySyncService
{
    private readonly PawDbContext _db;
    private readonly IDbContextFactory<PawDbContext> _dbFactory;
    private readonly IPolarClient _polarClient;
    private readonly ILogger<ActivitySyncService> _logger;

    public ActivitySyncService(PawDbContext db, IDbContextFactory<PawDbContext> dbFactory, IPolarClient polarClient, ILogger<ActivitySyncService> logger)
    {
        _db = db;
        _dbFactory = dbFactory;
        _polarClient = polarClient;
        _logger = logger;
    }

    public async Task SyncActivitiesForUserAsync(Guid userId, ActivityProviderType provider, DateTime? sinceUtc = null)
    {
        throw new NotSupportedException("SyncActivitiesForUserAsync is not supported with QEPTest database structure. Use webhook-based sync instead.");
        
        // Note: This method previously relied on DeviceAccounts and Activities tables
        // which don't exist in QEPTest. Webhook processing handles activity sync.
    }

    public async Task ProcessPolarWebhookEventAsync(long webhookEventId, CancellationToken cancellationToken = default)
    {
        var webhookEvent = await _db.WebhookEvents
            .FirstOrDefaultAsync(w => w.Id == webhookEventId, cancellationToken);

        if (webhookEvent == null)
        {
            _logger.LogWarning("Webhook event {Id} not found", webhookEventId);
            return;
        }

        if (webhookEvent.Provider != ActivityProviderType.Polar)
        {
            _logger.LogWarning("Webhook event {Id} is not from Polar provider", webhookEventId);
            return;
        }

        if (!string.Equals(webhookEvent.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Webhook event {Id} already processed with status {Status}", webhookEventId, webhookEvent.Status);
            return;
        }

        _logger.LogInformation("Processing Polar webhook event {Id} for external user {ExternalUserId}", webhookEventId, webhookEvent.ExternalUserId);

        webhookEvent.Status = "Processing";
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            // Find PolarLink for this Polar user (using PolarID which is Polar's X-User-ID)
            var polarLink = await _db.PolarLinks
                .FirstOrDefaultAsync(p => p.PolarID == webhookEvent.ExternalUserId, cancellationToken);

            if (polarLink == null)
            {
                webhookEvent.Status = "Failed";
                webhookEvent.ErrorMessage = $"No Polar connection found for Polar user ID {webhookEvent.ExternalUserId}";
                webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("No Polar connection found for Polar user ID {ExternalUserId}", webhookEvent.ExternalUserId);
                return;
            }

            _logger.LogInformation("Found Polar connection for {Email} (PolarID: {PolarID})", polarLink.Email, polarLink.PolarID);

            // Fetch the specific exercise using the AccessToken from PolarLink
            var result = await _polarClient.GetExerciseByIdAsync(polarLink.AccessToken, webhookEvent.EntityID, cancellationToken);

            if (result == null || result.Value.Exercise == null)
            {
                webhookEvent.Status = "Failed";
                webhookEvent.ErrorMessage = $"Exercise {webhookEvent.EntityID} not found or inaccessible";
                webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Exercise {EntityID} not found for webhook event {Id}", webhookEvent.EntityID, webhookEventId);
                return;
            }

            var (exercise, rawJson) = result.Value;
            _logger.LogInformation("Retrieved exercise {EntityID} for webhook event {Id}. Student: {Email}", 
                webhookEvent.EntityID, webhookEventId, polarLink.Email);

            // Store exercise data in PolarTransactions table
            var now = DateTime.UtcNow;
            
            // Use ResourceUrl from webhook as Location (full URL to the exercise)
            var exerciseUrl = webhookEvent.ResourceUrl ?? $"https://www.polaraccesslink.com/v3/exercises/{webhookEvent.EntityID}";
            
            var polarIdInt = polarLink.PolarID;
            
            // Check if this exercise already exists in PolarTransactions
            var existingTransaction = await _db.PolarTransactions
                .FirstOrDefaultAsync(pt => 
                    pt.PolarID == polarIdInt && 
                    pt.Location == exerciseUrl, 
                    cancellationToken);

            if (existingTransaction != null)
            {
                // Update existing transaction
                existingTransaction.LastTouched = now;
                existingTransaction.Response = rawJson;
                existingTransaction.IsProcessed = true;
                existingTransaction.Attempt += 1;
                
                _logger.LogInformation("Updated existing PolarTransaction {TransactionId} for exercise {EntityID}, attempt #{Attempt}",
                    existingTransaction.PolarTransactionID, webhookEvent.EntityID, existingTransaction.Attempt);
            }
            else
            {
                // Create new transaction with explicit type casting
                var transaction = new PolarTransaction
                {
                    PolarID = polarIdInt,
                    FirstTouched = now,
                    LastTouched = now,
                    Location = exerciseUrl,
                    Response = rawJson,
                    IsCommitted = false,
                    IsProcessed = true,
                    Attempt = 1
                };

                _db.PolarTransactions.Add(transaction);
                
                _logger.LogInformation("Created new PolarTransaction for exercise {EntityID}, PolarID: {PolarID}, URL: {Url}",
                    webhookEvent.EntityID, polarIdInt, exerciseUrl);
            }

            // Save PolarTransactions changes first
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("PolarTransaction saved successfully for exercise {EntityID}", webhookEvent.EntityID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save PolarTransaction for exercise {EntityID}. Error: {Error}", 
                    webhookEvent.EntityID, ex.Message);
                throw;
            }

            // Also save to Activity and HeartRateZone tables for QEP
            try
            {
                await SaveToQepActivityTablesAsync(exercise, polarLink, _db, cancellationToken);
                _logger.LogInformation("Activity and heart rate zones saved successfully for exercise {EntityID}", webhookEvent.EntityID);
                
                // Mark transaction as committed
                if (existingTransaction != null)
                {
                    existingTransaction.IsCommitted = true;
                }
                else
                {
                    var transaction = await _db.PolarTransactions
                        .FirstOrDefaultAsync(pt => pt.PolarID == polarIdInt && pt.Location == exerciseUrl, cancellationToken);
                    if (transaction != null)
                    {
                        transaction.IsCommitted = true;
                    }
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save Activity/HeartRateZone for exercise {EntityID}. Error: {Error}", 
                    webhookEvent.EntityID, ex.Message);
                // Don't fail the webhook processing if Activity save fails
                // The data is still in PolarTransactions and can be reprocessed
            }

            webhookEvent.Status = "Completed";
            webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
            webhookEvent.ErrorMessage = null;
            
            // Save webhook status update separately
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully processed webhook event {Id} for student {Email}, exercise data stored in all tables", 
                webhookEventId, polarLink.Email);
        }
        catch (Exception ex)
        {
            webhookEvent.Status = "Failed";
            webhookEvent.ErrorMessage = ex.Message;
            webhookEvent.ProcessedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            
            _logger.LogError(ex, "Failed to process webhook event {Id}", webhookEventId);
        }
    }

    public async Task<int> ProcessPendingPolarWebhookEventsBatchAsync(int maxBatchSize, CancellationToken cancellationToken = default)
    {
        var pending = await _db.WebhookEvents
            .Where(w => w.Provider == ActivityProviderType.Polar && w.Status == "Pending")
            .OrderBy(w => w.ReceivedAtUtc)
            .Take(maxBatchSize)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Processing batch of {Count} pending Polar webhook events", pending.Count);

        var processedCount = 0;

        foreach (var id in pending)
        {
            try
            {
                await ProcessPolarWebhookEventAsync(id, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook event {Id} in batch", id);
                // Continue with next event
            }
        }

        return processedCount;
    }

    // Note: This method is deprecated for QEPTest database
    // Activities are not stored in QEPTest - only PolarLinks and WebhookEvents
    public async Task UpsertActivityFromExerciseAsync(Guid userId, PolarExerciseDto exercise, string rawExerciseJson, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("UpsertActivityFromExerciseAsync is not supported with QEPTest database structure. Activities table does not exist in QEPTest.");
    }

    // ============================================================================
    // 30-Day Sweep Reconciliation
    // ============================================================================

    /// <summary>
    /// Normalizes an exercise ID to a canonical PolarTransactions.Location value.
    /// Always stored as <c>https://polaraccesslink.com/v3/exercises/{id}</c>.
    /// </summary>
    internal static string CanonicalExerciseLocation(string exerciseId)
        => $"https://polaraccesslink.com/v3/exercises/{exerciseId}";

    /// <inheritdoc/>
    public Task<int> SyncUserAsync(PolarLink polarLink, DateTime? since = null, CancellationToken cancellationToken = default)
        => SyncUserCoreAsync(polarLink, _db, since, cancellationToken);

    private async Task<int> SyncUserCoreAsync(PolarLink polarLink, PawDbContext db, DateTime? since, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(polarLink.AccessToken))
        {
            _logger.LogWarning("Skipping sync for {Email} - no access token", polarLink.Email);
            return 0;
        }

        var sinceUtc = since ?? DateTime.UtcNow.AddDays(-30);

        _logger.LogInformation("Syncing exercises for {Email} (PolarID: {PolarID}) since {Since:u}",
            polarLink.Email, polarLink.PolarID, sinceUtc);

        IReadOnlyList<PolarExerciseDto> exercises;
        string rawListJson;
        try
        {
            (exercises, rawListJson) = await _polarClient.ListExercisesAsync(polarLink.AccessToken, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list exercises for {Email}", polarLink.Email);
            return 0;
        }

        if (exercises.Count == 0)
        {
            _logger.LogInformation("No exercises returned for {Email}", polarLink.Email);
            return 0;
        }

        // Filter by upload_time >= since
        var filtered = exercises.Where(e =>
        {
            if (DateTime.TryParse(e.UploadTime, out var uploadTime))
                return uploadTime.ToUniversalTime() >= sinceUtc;
            return true; // include if upload_time is missing/unparseable
        }).ToList();

        _logger.LogInformation("ListExercises returned {Total} exercise(s) for {Email}, {Filtered} after since-filter",
            exercises.Count, polarLink.Email, filtered.Count);

        var polarIdInt = polarLink.PolarID;
        int committed = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var exercise in filtered)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var canonicalUrl = CanonicalExerciseLocation(exercise.Id);

            try
            {
                // Dedupe: skip if already committed
                var alreadyCommitted = await db.PolarTransactions
                    .AnyAsync(pt => pt.PolarID == polarIdInt
                                 && pt.Location == canonicalUrl
                                 && pt.IsCommitted,
                              cancellationToken);

                if (alreadyCommitted)
                {
                    skipped++;
                    continue;
                }

                var rawExerciseJson = System.Text.Json.JsonSerializer.Serialize(exercise);
                var now = DateTime.UtcNow;

                // Upsert PolarTransaction (stage raw data; IsCommitted stays false until QEP tables succeed)
                var existingTx = await db.PolarTransactions
                    .FirstOrDefaultAsync(pt => pt.PolarID == polarIdInt && pt.Location == canonicalUrl, cancellationToken);

                PolarTransaction dbTx;
                if (existingTx != null)
                {
                    existingTx.LastTouched = now;
                    existingTx.Response    = rawExerciseJson;
                    existingTx.IsProcessed = true;
                    existingTx.IsCommitted = false;
                    existingTx.Attempt    += 1;
                    dbTx = existingTx;
                }
                else
                {
                    dbTx = new PolarTransaction
                    {
                        PolarID      = polarIdInt,
                        FirstTouched = now,
                        LastTouched  = now,
                        Location     = canonicalUrl,
                        Response     = rawExerciseJson,
                        IsProcessed  = true,
                        IsCommitted  = false,
                        Attempt      = 1
                    };
                    db.PolarTransactions.Add(dbTx);
                }

                await db.SaveChangesAsync(cancellationToken);

                await SaveToQepActivityTablesAsync(exercise, polarLink, db, cancellationToken);

                dbTx.IsCommitted = true;
                await db.SaveChangesAsync(cancellationToken);

                committed++;
                _logger.LogInformation("Synced exercise {Id} for {Email} (attempt #{Attempt})",
                    exercise.Id, polarLink.Email, dbTx.Attempt);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "Failed to sync exercise {Id} for {Email} - left uncommitted for retry",
                    exercise.Id, polarLink.Email);
            }
        }

        _logger.LogInformation(
            "Sync complete for {Email}: {Committed} committed, {Skipped} skipped (already committed), {Failed} failed out of {Total} filtered exercises",
            polarLink.Email, committed, skipped, failed, filtered.Count);

        return committed;
    }

    /// <inheritdoc/>
    public async Task<int> SyncAllUsersAsync(DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var activeLinks = await _db.PolarLinks
            .Where(p => p.AccessToken != null && p.AccessToken != "")
            .ToListAsync(cancellationToken);

        if (activeLinks.Count == 0)
        {
            _logger.LogInformation("SyncAllUsers: no active PolarLinks found");
            return 0;
        }

        _logger.LogInformation("SyncAllUsers: syncing {Count} user(s) with max 5 parallel", activeLinks.Count);

        var totalCommitted = 0;

        await Parallel.ForEachAsync(
            activeLinks,
            new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = cancellationToken },
            async (link, ct) =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                try
                {
                    var count = await SyncUserCoreAsync(link, db, since, ct);
                    Interlocked.Add(ref totalCommitted, count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SyncAllUsers: error syncing {Email} - continuing", link.Email);
                }
            });

        _logger.LogInformation("SyncAllUsers complete: {Total} exercises committed", totalCommitted);
        return totalCommitted;
    }

    private async Task SaveToQepActivityTablesAsync(
        PolarExerciseDto exercise,
        PolarLink polarLink,
        PawDbContext db,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SaveToQepActivityTablesAsync starting for PersonID: {PersonID}, Username: {Username}, EntityID: {EntityID}",
            polarLink.PersonID, polarLink.Username, exercise.Id);

        // Wrap Activity + HeartRateZones in a single transaction so a partial failure
        // (e.g. Activity saved, HeartRateZones fail) doesn't leave orphaned rows.
        // In-memory EF (used in tests) does not support transactions, so only start one
        // when the provider is relational.
        IDbContextTransaction? tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {

        var existingActivity = await db.Activities
            .Include(a => a.HeartRateZones)
            .FirstOrDefaultAsync(a => a.EntityID == exercise.Id, cancellationToken);

        if (existingActivity != null)
        {
            _logger.LogInformation("Found existing activity by EntityID {EntityID} (ActivityID: {ActivityID}), will update",
                exercise.Id, existingActivity.ActivityID);

            var updatedActivity = await PolarToQepMapper.ToQepActivityAsync(exercise, polarLink, db, cancellationToken);

            existingActivity.Minutes = updatedActivity.Minutes;
            existingActivity.Duration = updatedActivity.Duration;
            existingActivity.Distance = updatedActivity.Distance;
            existingActivity.AerobicPoints = updatedActivity.AerobicPoints;
            existingActivity.DateEntered = DateTime.Now;
            existingActivity.Measurement = updatedActivity.Measurement;

            _logger.LogInformation("Updated activity fields: Minutes={Minutes}, AerobicPoints={Points}",
                existingActivity.Minutes, existingActivity.AerobicPoints);

            db.HeartRateZones.RemoveRange(existingActivity.HeartRateZones);
            _logger.LogInformation("Removed {Count} old heart rate zones", existingActivity.HeartRateZones.Count);

            var newZones = PolarToQepMapper.ToQepHeartRateZones(exercise);
            _logger.LogInformation("Mapper created {Count} new heart rate zones", newZones.Count);

            foreach (var zone in newZones)
            {
                zone.ActivityID = existingActivity.ActivityID;
                existingActivity.HeartRateZones.Add(zone);
            }

            _logger.LogInformation("Updated existing Activity {ActivityID} for user {UserID} (PersonID: {PersonID})",
                existingActivity.ActivityID, polarLink.Email, polarLink.PersonID);
        }
        else
        {
            _logger.LogInformation("No existing activity found, creating new activity");

            var newActivity = await PolarToQepMapper.ToQepActivityAsync(exercise, polarLink, db, cancellationToken);
            _logger.LogInformation("Mapper created activity: UserID={UserID}, Minutes={Minutes}, Points={Points}",
                newActivity.UserID, newActivity.Minutes, newActivity.AerobicPoints);

            db.Activities.Add(newActivity);

            _logger.LogInformation("Saving activity to get ActivityID...");
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Activity saved with ActivityID: {ActivityID}", newActivity.ActivityID);

            var zones = PolarToQepMapper.ToQepHeartRateZones(exercise);
            _logger.LogInformation("Mapper created {Count} heart rate zones", zones.Count);

            foreach (var zone in zones)
            {
                zone.ActivityID = newActivity.ActivityID;
                newActivity.HeartRateZones.Add(zone);
                _logger.LogDebug("Added zone {Zone}: {Lower}-{Upper} BPM, {Duration} minutes",
                    zone.Zone, zone.Lower, zone.Upper, zone.Duration);
            }

            _logger.LogInformation("Created new Activity {ActivityID} for user {UserID} (PersonID: {PersonID}), {ZoneCount} heart rate zones",
                newActivity.ActivityID, polarLink.Email, polarLink.PersonID, zones.Count);
        }

        _logger.LogInformation("Saving final changes...");
        await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);
        _logger.LogInformation("SaveToQepActivityTablesAsync completed successfully");

        } // end try
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }
}
