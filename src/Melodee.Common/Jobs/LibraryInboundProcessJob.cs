using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Melodee.Common.Services.Scanning;
using NodaTime;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
///     Scans the inbound library directory for new media files and processes them into the staging directory.
/// </summary>
/// <remarks>
///     <para>
///         This is the first stage of the Melodee media import pipeline. The inbound directory is where users
///         drop new music files. This job scans for changes, parses metadata, and moves processed albums to staging.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Checks if the inbound library needs scanning (compares LastScanAt with directory modification time)</item>
///             <item>Scans all subdirectories for supported audio files (MP3, FLAC, etc.)</item>
///             <item>Parses ID3/Vorbis tags and extracts album artwork</item>
///             <item>Validates and normalizes metadata (artist names, album titles, track numbers)</item>
///             <item>Creates melodee.json metadata files for each discovered album</item>
///             <item>Moves processed albums to the staging directory for review or further processing</item>
///             <item>Records scan history with counts of new artists, albums, and songs discovered</item>
///         </list>
///     </para>
///     <para>
///         This job is part of the media ingestion chain:
///         <code>
///         LibraryInboundProcessJob → StagingAutoMoveJob → LibraryInsertJob
///         </code>
///         When triggered by the scheduler (not manually), it will automatically trigger
///         StagingAutoMoveJob upon successful completion to continue the pipeline.
///     </para>
///     <para>
///         This job is marked with [DisallowConcurrentExecution] to prevent multiple scans from running
///         simultaneously, which could cause file conflicts and duplicate processing.
///     </para>
///     <para>
///         Skipped conditions:
///         <list type="bullet">
///             <item>Inbound library path not configured</item>
///             <item>Library is locked (IsLocked=true)</item>
///             <item>No changes detected since last scan</item>
///         </list>
///     </para>
///     <para>
///         Default schedule: Every 10 minutes (configurable via jobs.libraryProcess.cronExpression setting).
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
public sealed class LibraryInboundProcessJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    DirectoryProcessorToStagingService directoryProcessorToStagingService,
    ISchedulerFactory schedulerFactory) : JobBase(logger, configurationFactory)
{
    public override async Task Execute(IJobExecutionContext context)
    {
        var inboundLibrary =
            (await libraryService.GetInboundLibraryAsync(context.CancellationToken).ConfigureAwait(false)).Data;
        var directoryInbound = inboundLibrary.Path;
        if (directoryInbound.Nullify() == null)
        {
            Logger.Warning("[{JobName}] No inbound library configuration found.", nameof(LibraryInboundProcessJob));
            return;
        }

        if (inboundLibrary.IsLocked)
        {
            Logger.Warning("[{JobName}] Skipped processing locked library [{LibraryName}]",
                nameof(LibraryInboundProcessJob), inboundLibrary.Name);
            return;
        }

        if (!inboundLibrary.NeedsScanning())
        {
            Logger.Debug(
                "[{JobName}] Inbound library does not need scanning. Directory last scanned [{LastScanAt}], Directory last write [{LastWriteTime}]",
                nameof(LibraryInboundProcessJob),
                inboundLibrary.LastScanAt,
                inboundLibrary.LastWriteTime());
            return;
        }

        var dataMap = context.JobDetail.JobDataMap;
        var processedCount = 0;
        try
        {
            dataMap[JobMapNameRegistry.ScanStatus] = ScanStatus.InProcess.ToString();
            await directoryProcessorToStagingService.InitializeAsync(null, context.CancellationToken)
                .ConfigureAwait(false);
            var runContext = context.MergedJobDataMap.ContainsKey(MelodeeJobExecutionContext.DirectoryRunContext)
                ? context.MergedJobDataMap[MelodeeJobExecutionContext.DirectoryRunContext] as DirectoryRunContext
                : null;
            var result = await directoryProcessorToStagingService.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = directoryInbound,
                Name = directoryInbound
            }, inboundLibrary.LastScanAt, null, runContext, context.CancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Logger.Warning("[{JobName}] Failed to Scan inbound library.", nameof(LibraryInboundProcessJob));
            }

            processedCount = result.Data.NewAlbumsCount + result.Data.NewArtistsCount + result.Data.NewSongsCount;
            dataMap[JobMapNameRegistry.ScanStatus] = ScanStatus.Idle.ToString();
            dataMap[JobMapNameRegistry.Count] = processedCount;
            dataMap[JobMapNameRegistry.NewArtistsCount] = result.Data.NewArtistsCount;
            dataMap[JobMapNameRegistry.NewAlbumsCount] = result.Data.NewAlbumsCount;
            dataMap[JobMapNameRegistry.NewSongsCount] = result.Data.NewSongsCount;
            await libraryService.CreateLibraryScanHistory(inboundLibrary, new LibraryScanHistory
            {
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                DurationInMs = result.Data.DurationInMs,
                LibraryId = inboundLibrary.Id,
                FoundAlbumsCount = result.Data.NewAlbumsCount,
                FoundArtistsCount = result.Data.NewArtistsCount,
                FoundSongsCount = result.Data.NewSongsCount
            }, context.CancellationToken).ConfigureAwait(false);

            context.Result = new ScanStepResult(
                NewArtistsCount: result.Data.NewArtistsCount,
                NewAlbumsCount: result.Data.NewAlbumsCount,
                NewSongsCount: result.Data.NewSongsCount);

            // Chain to StagingAutoMoveJob if this was a scheduled run (not manual) and we processed something,
            // or if ChainOnComplete flag is set (for programmatic triggers like after upload)
            if (ShouldChainToNextJob(context) && processedCount > 0)
            {
                await TriggerNextJobAsync(context, JobKeyRegistry.StagingAutoMoveJobKey).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{JobName}] Failed to Scan inbound library.", nameof(LibraryInboundProcessJob));
        }
    }

    private static bool IsManualTrigger(IJobExecutionContext context)
    {
        return context.Trigger is not ICronTrigger;
    }

    private static bool ShouldChainToNextJob(IJobExecutionContext context)
    {
        if (!IsManualTrigger(context))
        {
            return true;
        }

        return context.MergedJobDataMap.ContainsKey(MelodeeJobExecutionContext.ChainOnComplete) &&
               context.MergedJobDataMap.GetBoolean(MelodeeJobExecutionContext.ChainOnComplete);
    }

    private async Task TriggerNextJobAsync(IJobExecutionContext context, JobKey nextJobKey)
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler(context.CancellationToken).ConfigureAwait(false);
            var nextJobExists = await scheduler.CheckExists(nextJobKey, context.CancellationToken).ConfigureAwait(false);

            if (nextJobExists)
            {
                Logger.Debug("[{JobName}] Triggering next job in chain: {NextJob}", nameof(LibraryInboundProcessJob), nextJobKey.Name);

                var jobDataMap = new JobDataMap();
                if (context.MergedJobDataMap.ContainsKey(MelodeeJobExecutionContext.ChainOnComplete) &&
                    context.MergedJobDataMap.GetBoolean(MelodeeJobExecutionContext.ChainOnComplete))
                {
                    jobDataMap[MelodeeJobExecutionContext.ChainOnComplete] = true;
                }

                await scheduler.TriggerJob(nextJobKey, jobDataMap, context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                Logger.Debug("[{JobName}] Next job {NextJob} not scheduled, skipping chain trigger", nameof(LibraryInboundProcessJob), nextJobKey.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "[{JobName}] Failed to trigger next job {NextJob}", nameof(LibraryInboundProcessJob), nextJobKey.Name);
        }
    }
}
