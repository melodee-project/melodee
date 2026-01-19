using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
/// Probes radio station health every 15 minutes to check availability and capture diagnostics
/// </summary>
public class RadioStationHealthProbeJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    RadioStationProbeService probeService)
    : JobBase(logger, configurationFactory)
{
    public override async Task Execute(IJobExecutionContext context)
    {
        Logger.Debug("[{JobName}] Starting radio station health probe", nameof(RadioStationHealthProbeJob));

        await using var dbContext = await contextFactory.CreateDbContextAsync(context.CancellationToken);
        
        var stations = await dbContext.RadioStations
            .ToArrayAsync(context.CancellationToken);

        if (stations.Length == 0)
        {
            Logger.Debug("[{JobName}] No radio stations found to probe", nameof(RadioStationHealthProbeJob));
            return;
        }

        var probeResults = new { SuccessCount = 0, FailCount = 0 };
        
        foreach (var station in stations)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                Logger.Information("[{JobName}] Cancellation requested, stopping health probe", 
                    nameof(RadioStationHealthProbeJob));
                break;
            }

            var probeResult = await probeService.ProbeStationAsync(
                station.StreamUrl,
                context.CancellationToken);

            station.LastHealthCheckAt = SystemClock.Instance.GetCurrentInstant();

            if (probeResult.IsSuccess && probeResult.Data != null)
            {
                if (probeResult.Data.IsHealthy)
                {
                    station.LastHealthStatus = RadioStationHealthStatus.Ok;
                    station.LastHealthOkAt = SystemClock.Instance.GetCurrentInstant();
                    station.LastHealthError = null;
                    station.LastResolvedStreamUrl = probeResult.Data.ResolvedStreamUrl;
                    station.LastContentType = probeResult.Data.ContentType;
                    station.LastBitrateKbps = probeResult.Data.BitrateKbps;
                    probeResults = new { SuccessCount = probeResults.SuccessCount + 1, FailCount = probeResults.FailCount };
                }
                else
                {
                    station.LastHealthStatus = RadioStationHealthStatus.Fail;
                    station.LastHealthError = probeResult.Data.ErrorMessage;
                    probeResults = new { SuccessCount = probeResults.SuccessCount, FailCount = probeResults.FailCount + 1 };
                    
                    Logger.Debug("[{JobName}] Station {StationId} ({StationName}) failed health check: {Error}",
                        nameof(RadioStationHealthProbeJob),
                        station.Id,
                        station.Name,
                        probeResult.Data.ErrorMessage);
                }
            }
            else
            {
                station.LastHealthStatus = RadioStationHealthStatus.Fail;
                station.LastHealthError = "Probe failed";
                probeResults = new { SuccessCount = probeResults.SuccessCount, FailCount = probeResults.FailCount + 1 };
            }
        }

        await dbContext.SaveChangesAsync(context.CancellationToken);

        Logger.Information(
            "[{JobName}] Completed health probe for {TotalStations} stations: {SuccessCount} healthy, {FailCount} failed",
            nameof(RadioStationHealthProbeJob),
            stations.Length,
            probeResults.SuccessCount,
            probeResults.FailCount);
    }
}
