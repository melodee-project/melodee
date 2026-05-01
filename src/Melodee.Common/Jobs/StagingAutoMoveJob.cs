using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Melodee.Common.Services.Models;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
///     Automatically moves albums with "Ok" status from the staging library to a storage library.
/// </summary>
/// <remarks>
///     <para>
///         This job bridges the gap between staging and storage in the media ingestion pipeline.
///         Albums that have been fully validated and marked as "Ok" (either automatically by the
///         validation system or manually by a user) are moved to the configured storage library.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Retrieves the staging library configuration</item>
///             <item>Retrieves the target storage library (first available if multiple exist)</item>
///             <item>Scans staging for albums with AlbumStatus.Ok</item>
///             <item>Moves each qualifying album to the storage library</item>
///             <item>Preserves album directory structure (Artist/Album format)</item>
///         </list>
///     </para>
///     <para>
///         This job is part of the media ingestion chain:
///         <code>
///         LibraryInboundProcessJob → StagingAutoMoveJob → LibraryInsertJob
///         </code>
///         When triggered by the scheduler (not manually), it will automatically trigger
///         LibraryInsertJob upon successful completion to continue the pipeline.
///     </para>
///     <para>
///         Skipped conditions:
///         <list type="bullet">
///             <item>Staging library is locked</item>
///             <item>No storage libraries configured</item>
///             <item>All storage libraries are locked</item>
///             <item>No albums with "Ok" status in staging</item>
///         </list>
///     </para>
///     <para>
///         Default schedule: Every 15 minutes (configurable via jobs.stagingAutoMove.cronExpression setting).
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
public class StagingAutoMoveJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ISchedulerFactory schedulerFactory) : JobBase(logger, configurationFactory)
{
    public override async Task Execute(IJobExecutionContext context)
    {
        Logger.Debug("[{JobName}] Starting staging auto-move process", nameof(StagingAutoMoveJob));

        var stagingLibraryResult = await libraryService.GetStagingLibraryAsync(context.CancellationToken).ConfigureAwait(false);
        if (!stagingLibraryResult.IsSuccess || stagingLibraryResult.Data == null)
        {
            Logger.Warning("[{JobName}] Unable to get staging library, skipping processing", nameof(StagingAutoMoveJob));
            return;
        }

        var stagingLibrary = stagingLibraryResult.Data;
        if (stagingLibrary.IsLocked)
        {
            Logger.Warning("[{JobName}] Staging library is locked, skipping processing", nameof(StagingAutoMoveJob));
            return;
        }

        var storageLibrariesResult = await libraryService.GetStorageLibrariesAsync(context.CancellationToken).ConfigureAwait(false);
        if (!storageLibrariesResult.IsSuccess || storageLibrariesResult.Data.Length == 0)
        {
            Logger.Warning("[{JobName}] No storage libraries configured, skipping processing", nameof(StagingAutoMoveJob));
            return;
        }

        var targetLibrary = storageLibrariesResult.Data.FirstOrDefault(x => !x.IsLocked);
        if (targetLibrary == null)
        {
            Logger.Warning("[{JobName}] All storage libraries are locked, skipping processing", nameof(StagingAutoMoveJob));
            return;
        }

        var albumsMoved = 0;
        void OnProcessingProgress(object? sender, ProcessingEvent e)
        {
            if (e is { Type: ProcessingEventType.Stop, Statistics: not null })
            {
                albumsMoved = e.Statistics.AlbumsMoved;
            }
        }

        libraryService.OnProcessingProgressEvent += OnProcessingProgress;
        try
        {
            var moveResult = await libraryService.MoveAlbumsFromLibraryToLibrary(
                stagingLibrary.Name,
                targetLibrary.Name,
                album => album.Status == AlbumStatus.Ok,
                false,
                context.CancellationToken).ConfigureAwait(false);

            if (moveResult.IsSuccess)
            {
                context.Result = new ScanStepResult(AlbumsMoved: albumsMoved);
                Logger.Information(
                    "[{JobName}] Completed staging auto-move: moved [{MovedCount}] albums from [{StagingLibrary}] to [{StorageLibrary}]",
                    nameof(StagingAutoMoveJob),
                    albumsMoved,
                    stagingLibrary.Name,
                    targetLibrary.Name);

                // Chain to LibraryInsertJob if this was a scheduled run (not manual) and we moved something,
                // or if ChainOnComplete flag is set (for programmatic triggers like after upload)
                if (ShouldChainToNextJob(context) && albumsMoved > 0)
                {
                    await TriggerNextJobAsync(context, JobKeyRegistry.LibraryProcessJobJobKey).ConfigureAwait(false);
                }
            }
            else
            {
                Logger.Warning(
                    "[{JobName}] Failed to move albums: {Messages}",
                    nameof(StagingAutoMoveJob),
                    string.Join(", ", moveResult.Messages ?? []));
            }
        }
        finally
        {
            libraryService.OnProcessingProgressEvent -= OnProcessingProgress;
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
                Logger.Debug("[{JobName}] Triggering next job in chain: {NextJob}", nameof(StagingAutoMoveJob), nextJobKey.Name);

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
                Logger.Debug("[{JobName}] Next job {NextJob} not scheduled, skipping chain trigger", nameof(StagingAutoMoveJob), nextJobKey.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "[{JobName}] Failed to trigger next job {NextJob}", nameof(StagingAutoMoveJob), nextJobKey.Name);
        }
    }
}
