using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
/// Captures now-playing metadata from radio stations every 5 minutes
/// </summary>
public class RadioStationNowPlayingCaptureJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IcyMetadataParser metadataParser,
    IHttpClientFactory httpClientFactory)
    : JobBase(logger, configurationFactory)
{
    private const int HealthyThresholdMinutes = 30;

    public override async Task Execute(IJobExecutionContext context)
    {
        Logger.Debug("[{JobName}] Starting now-playing capture", nameof(RadioStationNowPlayingCaptureJob));

        await using var dbContext = await contextFactory.CreateDbContextAsync(context.CancellationToken);

        var cutoffTime = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromMinutes(HealthyThresholdMinutes));

        // Only capture from stations that are healthy and were checked recently
        var eligibleStations = await dbContext.RadioStations
            .Where(s => s.LastHealthStatus == RadioStationHealthStatus.Ok &&
                       s.LastHealthOkAt != null &&
                       s.LastHealthOkAt > cutoffTime)
            .ToArrayAsync(context.CancellationToken);

        if (eligibleStations.Length == 0)
        {
            Logger.Debug("[{JobName}] No eligible healthy stations found for now-playing capture",
                nameof(RadioStationNowPlayingCaptureJob));
            return;
        }

        var captureResults = new { SuccessCount = 0, SkipCount = 0 };

        foreach (var station in eligibleStations)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                Logger.Information("[{JobName}] Cancellation requested, stopping now-playing capture",
                    nameof(RadioStationNowPlayingCaptureJob));
                break;
            }

            var streamUrl = station.LastResolvedStreamUrl ?? station.StreamUrl;
            var result = await metadataParser.ExtractNowPlayingAsync(
                streamUrl,
                httpClientFactory,
                context.CancellationToken);

            if (result.IsSuccess && result.Data != null && !string.IsNullOrWhiteSpace(result.Data.Title))
            {
                var newTitle = result.Data.Title.Trim();

                // Only update if the title has changed
                if (newTitle != station.NowPlayingRaw)
                {
                    station.NowPlayingRaw = newTitle;
                    station.NowPlayingCapturedAt = SystemClock.Instance.GetCurrentInstant();

                    // Add history entry
                    var historyEntry = new RadioStationNowPlayingHistory
                    {
                        RadioStationId = station.Id,
                        CapturedAt = station.NowPlayingCapturedAt.Value,
                        NowPlayingRaw = newTitle,
                        Source = result.Data.Source
                    };
                    dbContext.RadioStationNowPlayingHistories.Add(historyEntry);

                    captureResults = new { SuccessCount = captureResults.SuccessCount + 1, SkipCount = captureResults.SkipCount };

                    Logger.Debug("[{JobName}] Captured now-playing for station {StationId} ({StationName}): {Title}",
                        nameof(RadioStationNowPlayingCaptureJob),
                        station.Id,
                        station.Name,
                        newTitle);
                }
                else
                {
                    captureResults = new { SuccessCount = captureResults.SuccessCount, SkipCount = captureResults.SkipCount + 1 };
                }
            }
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        Logger.Information(
            "[{JobName}] Completed now-playing capture for {EligibleCount} stations: {SuccessCount} captured, {SkipCount} unchanged",
            nameof(RadioStationNowPlayingCaptureJob),
            eligibleStations.Length,
            captureResults.SuccessCount,
            captureResults.SkipCount);
    }
}
