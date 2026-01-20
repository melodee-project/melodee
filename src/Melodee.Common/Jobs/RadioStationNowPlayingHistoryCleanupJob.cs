using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
/// Cleans up old now-playing history entries, keeping max 200 per station
/// </summary>
public class RadioStationNowPlayingHistoryCleanupJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : JobBase(logger, configurationFactory)
{
    private const int MaxHistoryEntriesPerStation = 200;

    public override async Task Execute(IJobExecutionContext context)
    {
        Logger.Debug("[{JobName}] Starting now-playing history cleanup",
            nameof(RadioStationNowPlayingHistoryCleanupJob));

        await using var dbContext = await contextFactory.CreateDbContextAsync(context.CancellationToken);

        var stationIds = await dbContext.RadioStations
            .Select(s => s.Id)
            .ToArrayAsync(context.CancellationToken);

        if (stationIds.Length == 0)
        {
            Logger.Debug("[{JobName}] No radio stations found for cleanup",
                nameof(RadioStationNowPlayingHistoryCleanupJob));
            return;
        }

        var totalDeleted = 0;

        foreach (var stationId in stationIds)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                Logger.Information("[{JobName}] Cancellation requested, stopping cleanup",
                    nameof(RadioStationNowPlayingHistoryCleanupJob));
                break;
            }

            // Get count of history entries for this station
            var count = await dbContext.RadioStationNowPlayingHistories
                .Where(h => h.RadioStationId == stationId)
                .CountAsync(context.CancellationToken);

            if (count > MaxHistoryEntriesPerStation)
            {
                // Get IDs of entries to delete (oldest ones over the limit)
                var entriesToDelete = count - MaxHistoryEntriesPerStation;

                var idsToDelete = await dbContext.RadioStationNowPlayingHistories
                    .Where(h => h.RadioStationId == stationId)
                    .OrderBy(h => h.CapturedAt)
                    .Take(entriesToDelete)
                    .Select(h => h.Id)
                    .ToArrayAsync(context.CancellationToken);

                if (idsToDelete.Length > 0)
                {
                    await dbContext.RadioStationNowPlayingHistories
                        .Where(h => idsToDelete.Contains(h.Id))
                        .ExecuteDeleteAsync(context.CancellationToken);

                    totalDeleted += idsToDelete.Length;

                    Logger.Debug("[{JobName}] Deleted {Count} old history entries for station {StationId}",
                        nameof(RadioStationNowPlayingHistoryCleanupJob),
                        idsToDelete.Length,
                        stationId);
                }
            }
        }

        Logger.Information(
            "[{JobName}] Completed history cleanup: deleted {TotalDeleted} entries across {StationCount} stations",
            nameof(RadioStationNowPlayingHistoryCleanupJob),
            totalDeleted,
            stationIds.Length);
    }
}
