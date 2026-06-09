using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DecentDB.AdoNet;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Melodee.Common.Jobs;

/// <summary>
///     Downloads and imports the latest MusicBrainz database dump to enable local artist/album lookups.
/// </summary>
/// <remarks>
///     <para>
///         MusicBrainz is an open music encyclopedia that provides metadata for millions of albums and artists.
///         This job downloads the full database export and imports it into a local DecentDB database for fast,
///         offline lookups during media processing.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Checks if MusicBrainz search engine is enabled in settings</item>
///             <item>Creates a lock file to prevent concurrent runs (job can take hours)</item>
///             <item>Temporarily disables the MusicBrainz search engine during import</item>
///             <item>Downloads LATEST version info from data.metabrainz.org</item>
///             <item>Skips if the latest export has already been imported (based on timestamp)</item>
///             <item>Downloads mbdump.tar.bz2 (~6GB compressed) containing core data (skips if already exists)</item>
///             <item>Downloads mbdump-derived.tar.bz2 (~450MB) containing calculated/derived data (skips if already exists)</item>
///             <item>Extracts both archives sequentially to staging directory (skips if already extracted)</item>
///             <item>Imports the extracted data into local DecentDB database</item>
///             <item>On success, deletes the old database; on failure, restores it</item>
///             <item>Re-enables the MusicBrainz search engine</item>
///         </list>
///     </para>
///     <para>
///         This job is marked with [DisallowConcurrentExecution] because it involves large file downloads
///         and database operations that should not run in parallel.
///     </para>
///     <para>
///         Safety and recovery features:
///         <list type="bullet">
///             <item>Lock file prevents duplicate runs across application restarts</item>
///             <item>Existing database is renamed (not deleted) until import succeeds</item>
///             <item>Search engine is disabled during import to prevent queries against incomplete data</item>
///             <item>Lock file is always deleted in finally block</item>
///             <item>Downloads are skipped if files already exist with correct size (recovery from previous failures)</item>
///             <item>Extraction is skipped if marker files exist indicating successful extraction</item>
///             <item>Archives are extracted sequentially to avoid file conflicts</item>
///         </list>
///     </para>
///     <para>
///         Configuration settings used:
///         <list type="bullet">
///             <item>SearchEngineMusicBrainzEnabled: Must be true for job to run</item>
///             <item>SearchEngineMusicBrainzStoragePath: Directory for database and staging files</item>
///             <item>SearchEngineMusicBrainzImportLastImportTimestamp: Tracks last successful import</item>
///         </list>
///     </para>
///     <para>
///         Default schedule: Monthly on the 1st at noon (configurable via jobs.musicBrainzUpdateDatabase.cronExpression).
///         MusicBrainz publishes new dumps weekly, but monthly updates are usually sufficient.
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
public class MusicBrainzUpdateDatabaseJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    SettingService settingService,
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<MusicBrainzDbContext> dbContextFactory,
    IMusicBrainzRepository repository) : JobBase(logger, configurationFactory)
{
    private const string StageInitialize = "Initialize";
    private const string StageDownloadMbDump = "Download mbdump.tar.bz2";
    private const string StageDownloadMbDumpDerived = "Download mbdump-derived.tar.bz2";
    private const string StageExtract = "Extract Archives";
    private const string StageImport = "Import to Database";
    private const string StageCleanup = "Cleanup";
    private const int ImportStageScale = 1000;
    private const string ImportingDatabaseSuffix = ".importing";
    private const string BackupDatabaseSuffix = ".backup";

    private static readonly string[] DatabaseArtifactSuffixes =
    [
        string.Empty,
        ".wal",
        "-wal",
        ".shm",
        "-shm",
        ".coord"
    ];

    private static readonly string[] RequiredArchiveEntries =
    [
        "mbdump/artist",
        "mbdump/artist_alias",
        "mbdump/link",
        "mbdump/l_artist_artist",
        "mbdump/artist_credit",
        "mbdump/artist_credit_name",
        "mbdump/release_country",
        "mbdump/release_group",
        "mbdump/release_group_meta",
        "mbdump/release"
    ];

    private static readonly string[] BaseArchiveEntries =
    [
        "mbdump/artist",
        "mbdump/artist_alias",
        "mbdump/link",
        "mbdump/l_artist_artist",
        "mbdump/artist_credit",
        "mbdump/artist_credit_name",
        "mbdump/release_country",
        "mbdump/release_group",
        "mbdump/release"
    ];

    private static readonly string[] DerivedArchiveEntries =
    [
        "mbdump/release_group_meta"
    ];

    private static readonly string[] ImportPhaseSequence =
    [
        "Loading Artists",
        "Materializing Artists",
        "Materializing Relations",
        "Cleanup",
        "Loading Albums",
        "Materializing Albums",
        "Cleanup"
    ];

    public override async Task Execute(IJobExecutionContext context)
    {
        var jobStartTicks = Stopwatch.GetTimestamp();
        Logger.Information("[{JobName}] Starting job.", nameof(MusicBrainzUpdateDatabaseJob));

        // Initialize progress tracking
        var progress = GetProgress(context);
        progress?.Initialize(
            StageInitialize,
            StageDownloadMbDump,
            StageDownloadMbDumpDerived,
            StageExtract,
            StageImport,
            StageCleanup);

        var configuration = await ConfigurationFactory.GetConfigurationAsync(context.CancellationToken)
            .ConfigureAwait(false);

        progress?.StartStage(StageInitialize, "Checking configuration...");
        Logger.Debug("[{JobName}] Checking if MusicBrainz search engine is enabled...", nameof(MusicBrainzUpdateDatabaseJob));
        if (!configuration.GetValue<bool>(SettingRegistry.SearchEngineMusicBrainzEnabled))
        {
            var msg = $"MusicBrainz search engine is disabled (setting: {SettingRegistry.SearchEngineMusicBrainzEnabled}). Enable it first.";
            Logger.Warning("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
            SetJobResult(context, JobResultStatus.Skipped, msg);
            return;
        }
        Logger.Debug("[{JobName}] MusicBrainz search engine is enabled.", nameof(MusicBrainzUpdateDatabaseJob));

        string? storagePath = null;
        string? tempDbName = null;
        string? importDbName = null;
        var lockfile = string.Empty;
        var dbName = string.Empty;
        var searchEngineDisabled = false;
        try
        {
            storagePath = configuration.GetValue<string>(SettingRegistry.SearchEngineMusicBrainzStoragePath);
            Logger.Debug("[{JobName}] Storage path configured as: [{StoragePath}]", nameof(MusicBrainzUpdateDatabaseJob), storagePath);

            if (storagePath == null)
            {
                var msg = $"MusicBrainz storage path is not configured (setting: {SettingRegistry.SearchEngineMusicBrainzStoragePath}).";
                Logger.Error("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                SetJobResult(context, JobResultStatus.Failed, msg);
                return;
            }

            dbName = GetDatabaseFilePath();
            importDbName = GetImportDatabaseFilePath(dbName);
            progress?.UpdateProgress("Creating storage directory...");
            storagePath.ToFileSystemDirectoryInfo().EnsureExists();
            Logger.Debug("[{JobName}] Storage directory exists or was created.", nameof(MusicBrainzUpdateDatabaseJob));

            lockfile = Path.Combine(storagePath, $"{nameof(MusicBrainzUpdateDatabaseJob)}.lock");
            Logger.Debug("[{JobName}] Checking for lock file at: [{LockFile}]", nameof(MusicBrainzUpdateDatabaseJob), lockfile);

            if (File.Exists(lockfile))
            {
                var lockState = await ReadLockStateAsync(lockfile, context.CancellationToken).ConfigureAwait(false);
                if (lockState?.ProcessId is int processId && IsProcessRunning(processId))
                {
                    var msg = $"Job lock file exists at [{lockfile}] for active process [{processId}] (created: {lockState.CreatedAtUtc}).";
                    Logger.Warning("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                    SetJobResult(context, JobResultStatus.Skipped, msg);
                    return;
                }

                progress?.UpdateProgress("Recovering stale import state...");
                await RecoverInterruptedImportAsync(
                        storagePath,
                        dbName,
                        lockfile,
                        lockState,
                        context.CancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.UpdateProgress("Creating lock file...");
            Logger.Debug("[{JobName}] Creating lock file...", nameof(MusicBrainzUpdateDatabaseJob));
            await WriteLockStateAsync(lockfile, null, importDbName, context.CancellationToken).ConfigureAwait(false);

            var doesDbExist = File.Exists(dbName);
            Logger.Debug("[{JobName}] Existing database check: exists={Exists}, path={DbPath}",
                nameof(MusicBrainzUpdateDatabaseJob), doesDbExist, dbName);

            using (var client = httpClientFactory.CreateClient())
            {
                var storageStagingDirectory = new FileSystemDirectoryInfo
                {
                    Path = Path.Combine(storagePath, "staging"),
                    Name = "staging"
                };

                // Ensure staging directory exists (don't empty it - we want to preserve partial downloads)
                Logger.Debug("[{JobName}] Preparing staging directory: [{StagingPath}]",
                    nameof(MusicBrainzUpdateDatabaseJob), storageStagingDirectory.Path);
                storageStagingDirectory.EnsureExists();

                Logger.Debug("[{JobName}] Fetching LATEST version from MusicBrainz...", nameof(MusicBrainzUpdateDatabaseJob));
                var latest = await client
                    .GetStringAsync("https://data.metabrainz.org/pub/musicbrainz/data/fullexport/LATEST",
                        context.CancellationToken).ConfigureAwait(false);
                if (latest.Nullify() == null)
                {
                    Logger.Error("[{JobName}] Unable to download LATEST information from MusicBrainz",
                        nameof(MusicBrainzUpdateDatabaseJob));
                    SetJobResult(context, JobResultStatus.Failed, "Unable to download LATEST information from MusicBrainz.");
                    return;
                }

                latest = latest.CleanString();
                Logger.Debug("[{JobName}] Latest MusicBrainz export version: [{Latest}]", nameof(MusicBrainzUpdateDatabaseJob), latest);

                // Store version file to track which version we're downloading
                var versionFile = Path.Combine(storageStagingDirectory.FullName(), "VERSION");
                var existingVersion = File.Exists(versionFile) ? await File.ReadAllTextAsync(versionFile, context.CancellationToken) : null;

                // If staging has a different version, clear it and start fresh
                if (existingVersion != null && existingVersion != latest)
                {
                    Logger.Information("[{JobName}] Staging directory has different version ({OldVersion}), clearing for new version ({NewVersion})",
                        nameof(MusicBrainzUpdateDatabaseJob), existingVersion, latest);
                    storageStagingDirectory.Empty();
                }

                // Write current version
                await File.WriteAllTextAsync(versionFile, latest, context.CancellationToken);

                if (doesDbExist && latest != null)
                {
                    var latestTimeStamp =
                        DateTimeOffset.ParseExact(latest, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    var lastJobRunTimestamp =
                        configuration.GetValue<DateTimeOffset?>(SettingRegistry
                            .SearchEngineMusicBrainzImportLastImportTimestamp);

                    Logger.Debug("[{JobName}] Comparing versions - Latest: {Latest}, Last imported: {LastImport}",
                        nameof(MusicBrainzUpdateDatabaseJob), latestTimeStamp, lastJobRunTimestamp);

                    if (latestTimeStamp < lastJobRunTimestamp)
                    {
                        var msg = $"MusicBrainz database is already up to date. Latest export ({latestTimeStamp:yyyy-MM-dd}) was imported on {lastJobRunTimestamp:yyyy-MM-dd}.";
                        Logger.Information("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                        SetJobResult(context, JobResultStatus.Skipped, msg);
                        return;
                    }
                }

                progress?.CompleteStage(); // Complete Initialize stage

                var mbDumpFileName = Path.Combine(storageStagingDirectory.FullName(), "mbdump.tar.bz2");
                var mbDumpDerivedFileName = Path.Combine(storageStagingDirectory.FullName(), "mbdump-derived.tar.bz2");
                var mbDumpUrl = $"https://data.metabrainz.org/pub/musicbrainz/data/fullexport/{latest}/mbdump.tar.bz2";
                var mbDumpDerivedUrl = $"https://data.metabrainz.org/pub/musicbrainz/data/fullexport/{latest}/mbdump-derived.tar.bz2";

                // Check if mbdump.tar.bz2 needs to be downloaded
                var mbDumpFileInfo = new FileInfo(mbDumpFileName);
                var needToDownloadMbDump = !mbDumpFileInfo.Exists || mbDumpFileInfo.Length == 0;

                if (needToDownloadMbDump)
                {
                    // Download mbdump.tar.bz2 with progress reporting
                    long? mbDumpTotalBytes = null;
                    progress?.StartStage(StageDownloadMbDump, "Starting download...");
                    var downloadStartTicks = Stopwatch.GetTimestamp();
                    Logger.Information("[{JobName}] Downloading mbdump.tar.bz2...",
                        nameof(MusicBrainzUpdateDatabaseJob));
                    Logger.Debug("[{JobName}] Download URL: [{Url}]", nameof(MusicBrainzUpdateDatabaseJob), mbDumpUrl);

                    var downloadedMbDumpFile = await client.DownloadFileAsync(
                        mbDumpUrl,
                        mbDumpFileName,
                        null,
                        dp =>
                        {
                            if (dp.TotalBytes.HasValue && mbDumpTotalBytes != dp.TotalBytes)
                            {
                                mbDumpTotalBytes = dp.TotalBytes;
                                Logger.Debug("[{JobName}] mbdump.tar.bz2 actual size: {Size}",
                                    nameof(MusicBrainzUpdateDatabaseJob), dp.TotalBytesFormatted);
                            }

                            var speedInfo = dp.SpeedFormatted != null ? $" @ {dp.SpeedFormatted}" : "";
                            var etaInfo = dp.EstimatedTimeRemainingFormatted != null ? $" ETA: {dp.EstimatedTimeRemainingFormatted}" : "";
                            progress?.UpdateProgress($"{dp.BytesDownloadedFormatted} / {dp.TotalBytesFormatted} ({dp.PercentComplete:F1}%){speedInfo}{etaInfo}");
                        },
                        context.CancellationToken);

                    var mbDumpDownloadTime = Stopwatch.GetElapsedTime(downloadStartTicks);
                    Logger.Information("[{JobName}] mbdump.tar.bz2 download complete: {Result}, size: {Size}, elapsed: {Elapsed:F1}s",
                        nameof(MusicBrainzUpdateDatabaseJob), downloadedMbDumpFile,
                        mbDumpTotalBytes.HasValue ? FormatBytes(mbDumpTotalBytes.Value) : "unknown",
                        mbDumpDownloadTime.TotalSeconds);

                    if (!downloadedMbDumpFile)
                    {
                        var msg = "Failed to download mbdump.tar.bz2";
                        Logger.Warning("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                        SetJobResult(context, JobResultStatus.Failed, msg);
                        return;
                    }
                    progress?.CompleteStage();
                }
                else
                {
                    Logger.Information("[{JobName}] mbdump.tar.bz2 already exists ({Size}), skipping download",
                        nameof(MusicBrainzUpdateDatabaseJob), FormatBytes(mbDumpFileInfo.Length));
                    progress?.StartStage(StageDownloadMbDump, $"Already downloaded ({FormatBytes(mbDumpFileInfo.Length)})");
                    progress?.CompleteStage();
                }

                // Check if mbdump-derived.tar.bz2 needs to be downloaded
                var mbDumpDerivedFileInfo = new FileInfo(mbDumpDerivedFileName);
                var needToDownloadMbDumpDerived = !mbDumpDerivedFileInfo.Exists || mbDumpDerivedFileInfo.Length == 0;

                if (needToDownloadMbDumpDerived)
                {
                    // Download mbdump-derived.tar.bz2 with progress reporting
                    long? mbDumpDerivedTotalBytes = null;
                    progress?.StartStage(StageDownloadMbDumpDerived, "Starting download...");
                    Logger.Information("[{JobName}] Downloading mbdump-derived.tar.bz2...",
                        nameof(MusicBrainzUpdateDatabaseJob));
                    Logger.Debug("[{JobName}] Download URL: [{Url}]", nameof(MusicBrainzUpdateDatabaseJob), mbDumpDerivedUrl);

                    var downloadedMbDerivedFile = await client.DownloadFileAsync(
                        mbDumpDerivedUrl,
                        mbDumpDerivedFileName,
                        null,
                        dp =>
                        {
                            if (dp.TotalBytes.HasValue && mbDumpDerivedTotalBytes != dp.TotalBytes)
                            {
                                mbDumpDerivedTotalBytes = dp.TotalBytes;
                                Logger.Debug("[{JobName}] mbdump-derived.tar.bz2 actual size: {Size}",
                                    nameof(MusicBrainzUpdateDatabaseJob), dp.TotalBytesFormatted);
                            }

                            var speedInfo = dp.SpeedFormatted != null ? $" @ {dp.SpeedFormatted}" : "";
                            var etaInfo = dp.EstimatedTimeRemainingFormatted != null ? $" ETA: {dp.EstimatedTimeRemainingFormatted}" : "";
                            progress?.UpdateProgress($"{dp.BytesDownloadedFormatted} / {dp.TotalBytesFormatted} ({dp.PercentComplete:F1}%){speedInfo}{etaInfo}");
                        },
                        context.CancellationToken);

                    Logger.Information("[{JobName}] mbdump-derived.tar.bz2 download complete: {Result}, size: {Size}",
                        nameof(MusicBrainzUpdateDatabaseJob), downloadedMbDerivedFile,
                        mbDumpDerivedTotalBytes.HasValue ? FormatBytes(mbDumpDerivedTotalBytes.Value) : "unknown");

                    if (!downloadedMbDerivedFile)
                    {
                        var msg = "Failed to download mbdump-derived.tar.bz2";
                        Logger.Warning("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                        SetJobResult(context, JobResultStatus.Failed, msg);
                        return;
                    }
                    progress?.CompleteStage();
                }
                else
                {
                    Logger.Information("[{JobName}] mbdump-derived.tar.bz2 already exists ({Size}), skipping download",
                        nameof(MusicBrainzUpdateDatabaseJob), FormatBytes(mbDumpDerivedFileInfo.Length));
                    progress?.StartStage(StageDownloadMbDumpDerived, $"Already downloaded ({FormatBytes(mbDumpDerivedFileInfo.Length)})");
                    progress?.CompleteStage();
                }

                Logger.Information("[{JobName}] Downloads complete. Starting extraction...",
                    nameof(MusicBrainzUpdateDatabaseJob));

                // Check if extraction has already been completed for every file the importer actually needs.
                var mbDumpDir = Path.Combine(storageStagingDirectory.FullName(), "mbdump");
                var extractionComplete = HasAllRequiredExtractedFiles(mbDumpDir);

                if (!extractionComplete)
                {
                    DeleteExtractedMusicBrainzFiles(mbDumpDir);

                    progress?.StartStage(StageExtract, 2);

                    var extractionStartTicks = Stopwatch.GetTimestamp();

                    progress?.UpdateProgress(0, "Extracting mbdump.tar.bz2...");
                    Logger.Information("[{JobName}] Extracting mbdump.tar.bz2...", nameof(MusicBrainzUpdateDatabaseJob));
                    await ExtractRequiredArchiveEntriesAsync(
                            mbDumpFileName,
                            storageStagingDirectory.FullName(),
                            BaseArchiveEntries,
                            context.CancellationToken)
                        .ConfigureAwait(false);

                    progress?.UpdateProgress(1, "Extracting mbdump-derived.tar.bz2...");
                    Logger.Information("[{JobName}] Extracting mbdump-derived.tar.bz2...", nameof(MusicBrainzUpdateDatabaseJob));
                    await ExtractRequiredArchiveEntriesAsync(
                            mbDumpDerivedFileName,
                            storageStagingDirectory.FullName(),
                            DerivedArchiveEntries,
                            context.CancellationToken)
                        .ConfigureAwait(false);

                    EnsureRequiredExtractedFilesExist(mbDumpDir);

                    progress?.UpdateProgress(2, "Extraction complete");

                    var totalExtractionTime = Stopwatch.GetElapsedTime(extractionStartTicks);
                    Logger.Information("[{JobName}] Archive extraction complete in {Elapsed:F1} minutes.",
                        nameof(MusicBrainzUpdateDatabaseJob), totalExtractionTime.TotalMinutes);
                    progress?.CompleteStage();
                }
                else
                {
                    Logger.Information("[{JobName}] Required MusicBrainz dump files already extracted, skipping extraction",
                        nameof(MusicBrainzUpdateDatabaseJob));
                    progress?.StartStage(StageExtract, "Required files already extracted");
                    progress?.CompleteStage();
                }
            }

            // Import data to DecentDB with progress callback
            progress?.StartStage(StageImport, ImportStageScale);
            progress?.UpdateProgress(0, "Loading and importing data...");
            var importStartTicks = Stopwatch.GetTimestamp();
            Logger.Information("[{JobName}] Starting data import to DecentDB...", nameof(MusicBrainzUpdateDatabaseJob));
            var importPhaseIndex = -1;
            var lastLoggedPhase = string.Empty;
            var lastLoggedCurrent = -1;
            var lastImportLogTicks = Stopwatch.GetTimestamp();

            // Create progress callback that updates the job progress
            void ImportProgressCallback(string phase, int current, int total, string? message)
            {
                var percentComplete = total > 0 ? (double)current / total * 100 : 0;
                var progressMessage = message ?? $"{phase}: {current:N0} / {total:N0} ({percentComplete:F1}%)";
                importPhaseIndex = AdvanceImportPhase(importPhaseIndex, phase);
                var scaledProgress = CalculateImportStageProgress(importPhaseIndex, current, total);
                progress?.UpdateProgress(scaledProgress, progressMessage);

                var elapsedSinceLastLog = Stopwatch.GetElapsedTime(lastImportLogTicks);
                var shouldLog = phase != lastLoggedPhase
                                || current == 0
                                || current == total
                                || total <= 25
                                || current != lastLoggedCurrent && elapsedSinceLastLog >= TimeSpan.FromSeconds(10);
                if (shouldLog)
                {
                    Logger.Information("[{JobName}] Import progress - {Phase}: {Current:N0}/{Total:N0} ({Percent:F1}%)",
                        nameof(MusicBrainzUpdateDatabaseJob), phase, current, total, percentComplete);
                    lastImportLogTicks = Stopwatch.GetTimestamp();
                    lastLoggedPhase = phase;
                    lastLoggedCurrent = current;
                }
            }

            DeleteDatabaseArtifacts(importDbName);
            var importResult = await repository.ImportData(
                    new MusicBrainzImportRequest(storagePath, importDbName),
                    ImportProgressCallback,
                    context.CancellationToken)
                .ConfigureAwait(false);
            var importTime = Stopwatch.GetElapsedTime(importStartTicks);
            progress?.CompleteStage();

            Logger.Debug("[{JobName}] Import result: Success={Success}, Errors={Errors}, Duration={Duration:F1} minutes",
                nameof(MusicBrainzUpdateDatabaseJob), importResult.IsSuccess,
                string.Join(", ", importResult.Errors ?? []), importTime.TotalMinutes);

            // Cleanup stage
            progress?.StartStage(StageCleanup, "Finalizing...");

            if (importResult.IsSuccess)
            {
                progress?.UpdateProgress("Flushing imported DecentDB WAL...");
                await CheckpointImportedDatabaseAsync(importDbName, context.CancellationToken).ConfigureAwait(false);

                progress?.UpdateProgress("Switching imported database into place...");
                Logger.Debug("[{JobName}] Temporarily disabling MusicBrainz search engine for database swap...", nameof(MusicBrainzUpdateDatabaseJob));
                await settingService
                    .SetAsync(SettingRegistry.SearchEngineMusicBrainzEnabled, "false", context.CancellationToken)
                    .ConfigureAwait(false);
                searchEngineDisabled = true;

                if (doesDbExist)
                {
                    tempDbName = GetBackupDatabaseFilePath(dbName);
                    progress?.UpdateProgress("Backing up existing database...");
                    Logger.Debug("[{JobName}] Backing up existing database to: [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                    DeleteDatabaseArtifacts(tempDbName);
                    MoveDatabaseArtifacts(dbName, tempDbName, overwrite: true);
                    await WriteLockStateAsync(lockfile, tempDbName, importDbName, context.CancellationToken).ConfigureAwait(false);
                }

                progress?.UpdateProgress("Promoting imported database...");
                DeleteDatabaseArtifacts(dbName);
                MoveDatabaseArtifacts(importDbName, dbName, overwrite: true);

                if (tempDbName != null)
                {
                    progress?.UpdateProgress("Deleting backup database...");
                    Logger.Debug("[{JobName}] Deleting backup database: [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                    DeleteDatabaseArtifacts(tempDbName);
                }

                await settingService.SetAsync(SettingRegistry.SearchEngineMusicBrainzImportLastImportTimestamp,
                    DateTimeOffset.UtcNow.ToString("O"), context.CancellationToken).ConfigureAwait(false);

                progress?.CompleteStage(); // Cleanup complete

                var totalElapsedMinutes = Stopwatch.GetElapsedTime(jobStartTicks).TotalMinutes;
                var msg = $"Successfully imported MusicBrainz database in {totalElapsedMinutes:F1} minutes.";
                Logger.Information("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                SetJobResult(context, JobResultStatus.Success, msg);
            }
            else
            {
                var msg = $"Import failed: {FormatOperationErrors(importResult.Errors)}";
                Logger.Error("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                SetJobResult(context, JobResultStatus.Failed, msg);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("[{JobName}] Job was cancelled.", nameof(MusicBrainzUpdateDatabaseJob));
            SetJobResult(context, JobResultStatus.Failed, "Job was cancelled.");
            // Don't rethrow - let finally block handle cleanup
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{JobName}] Unhandled exception during job execution", nameof(MusicBrainzUpdateDatabaseJob));
            SetJobResult(context, JobResultStatus.Failed, e.Message);
        }
        finally
        {
            Logger.Debug("[{JobName}] Cleaning up - deleting lock file, restoring backup if needed, and re-enabling search engine...", nameof(MusicBrainzUpdateDatabaseJob));

            // Restore backup database if import didn't complete successfully
            var jobResult = (context as MelodeeJobExecutionContext)?.JobResult;
            var importSucceeded = jobResult?.Status == JobResultStatus.Success;
            if (!importSucceeded)
            {
                progress?.StartStage(StageCleanup, "Recovering interrupted import state...");
            }

            if (!importSucceeded && tempDbName != null && storagePath != null && File.Exists(tempDbName))
            {
                try
                {
                    progress?.UpdateProgress("Restoring backup database...");
                    Logger.Information("[{JobName}] Restoring backup database from: [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                    DeleteDatabaseArtifacts(dbName);
                    MoveDatabaseArtifacts(tempDbName, dbName, overwrite: true);
                    Logger.Information("[{JobName}] Backup database restored successfully.", nameof(MusicBrainzUpdateDatabaseJob));
                }
                catch (Exception restoreEx)
                {
                    Logger.Error(restoreEx, "[{JobName}] Failed to restore backup database from [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                }
            }
            else if (!importSucceeded && searchEngineDisabled && !string.IsNullOrWhiteSpace(dbName))
            {
                try
                {
                    progress?.UpdateProgress("Deleting partial promoted database artifacts...");
                    DeleteDatabaseArtifacts(dbName);
                }
                catch (Exception cleanupEx)
                {
                    Logger.Warning(cleanupEx, "[{JobName}] Failed to delete partial database artifacts for [{DbPath}]",
                        nameof(MusicBrainzUpdateDatabaseJob), dbName);
                }
            }

            if (importDbName != null)
            {
                try
                {
                    DeleteDatabaseArtifacts(importDbName);
                }
                catch (Exception cleanupEx)
                {
                    Logger.Warning(cleanupEx, "[{JobName}] Failed to delete imported scratch database artifacts for [{DbPath}]",
                        nameof(MusicBrainzUpdateDatabaseJob), importDbName);
                }
            }

            // Always delete lock file
            if (File.Exists(lockfile))
            {
                try
                {
                    File.Delete(lockfile);
                }
                catch (Exception lockEx)
                {
                    Logger.Warning(lockEx, "[{JobName}] Failed to delete lock file: [{LockFile}]", nameof(MusicBrainzUpdateDatabaseJob), lockfile);
                }
            }

            // Re-enable MusicBrainz search engine only if this run disabled it.
            if (searchEngineDisabled)
            {
                try
                {
                    await settingService
                        .SetAsync(SettingRegistry.SearchEngineMusicBrainzEnabled, "true", CancellationToken.None)
                        .ConfigureAwait(false);
                    Logger.Information("[{JobName}] MusicBrainz search engine re-enabled.", nameof(MusicBrainzUpdateDatabaseJob));
                }
                catch (Exception enableEx)
                {
                    Logger.Error(enableEx, "[{JobName}] CRITICAL: Failed to re-enable MusicBrainz search engine! Manual intervention required.", nameof(MusicBrainzUpdateDatabaseJob));
                }
            }

            if (!importSucceeded)
            {
                progress?.CompleteStage();
            }

            var totalJobTime = Stopwatch.GetElapsedTime(jobStartTicks);
            Logger.Debug("[{JobName}] Job cleanup complete. Total execution time: {Elapsed:F1} minutes.",
                nameof(MusicBrainzUpdateDatabaseJob), totalJobTime.TotalMinutes);
        }
    }

    private string GetDatabaseFilePath()
    {
        using var context = dbContextFactory.CreateDbContext();
        var connectionString = context.Database.GetConnectionString()
                               ?? throw new InvalidOperationException("MusicBrainzDbContext has no connection string configured.");
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
        var dataSource = builder.ContainsKey("Data Source")
            ? builder["Data Source"]?.ToString()
            : null;
        return dataSource ?? throw new InvalidOperationException(
            $"Cannot extract 'Data Source' from MusicBrainz connection string: {connectionString}");
    }

    private static string GetImportDatabaseFilePath(string liveDatabasePath)
    {
        return AddPathSuffixBeforeExtension(liveDatabasePath, ImportingDatabaseSuffix);
    }

    private static string GetBackupDatabaseFilePath(string liveDatabasePath)
    {
        return AddPathSuffixBeforeExtension(liveDatabasePath, BackupDatabaseSuffix);
    }

    private static string AddPathSuffixBeforeExtension(string path, string suffix)
    {
        var directoryPath = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directoryPath, $"{fileName}{suffix}{extension}");
    }

    private static JobProgress? GetProgress(IJobExecutionContext context)
    {
        return (context as MelodeeJobExecutionContext)?.Progress;
    }

    private static void SetJobResult(IJobExecutionContext context, JobResultStatus status, string message)
    {
        if (context is MelodeeJobExecutionContext mjc)
        {
            mjc.JobResult = new JobResult(status, message);
        }
    }

    private async Task ExtractRequiredArchiveEntriesAsync(
        string archivePath,
        string destinationPath,
        IReadOnlyCollection<string> requiredEntries,
        CancellationToken cancellationToken)
    {
        Logger.Debug("[{JobName}] Extracting [{FileName}]...", nameof(MusicBrainzUpdateDatabaseJob), archivePath);
        var sw = Stopwatch.GetTimestamp();

        if (requiredEntries.Count == 0)
        {
            return;
        }

        if (await TryNativeExtractionAsync(archivePath, destinationPath, requiredEntries, cancellationToken).ConfigureAwait(false))
        {
            Logger.Information("[{JobName}] Extracted [{FileName}] using native tools in {Elapsed:F1} seconds.",
                nameof(MusicBrainzUpdateDatabaseJob),
                Path.GetFileName(archivePath),
                Stopwatch.GetElapsedTime(sw).TotalSeconds);
            return;
        }

        Logger.Debug("[{JobName}] Native extraction not available, using managed selective extraction", nameof(MusicBrainzUpdateDatabaseJob));
        ExtractRequiredArchiveEntriesManaged(archivePath, destinationPath, requiredEntries, cancellationToken);

        Logger.Information("[{JobName}] Extracted [{FileName}] in {Elapsed:F1} seconds.",
            nameof(MusicBrainzUpdateDatabaseJob),
            Path.GetFileName(archivePath),
            Stopwatch.GetElapsedTime(sw).TotalSeconds);
    }

    private async Task<bool> TryNativeExtractionAsync(
        string archivePath,
        string destinationPath,
        IReadOnlyCollection<string> requiredEntries,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        var decompressor = FindNativeDecompressor();
        if (decompressor == null)
        {
            return false;
        }

        try
        {
            var entryArguments = string.Join(" ", requiredEntries.Select(QuoteShellArgument));
            var shellCommand =
                $"{decompressor} -dc {QuoteShellArgument(archivePath)} | tar -xf - -C {QuoteShellArgument(destinationPath)} {entryArguments}";

            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c {QuoteShellArgument(shellCommand)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Logger.Debug("[{JobName}] Running native extraction: {Command}", nameof(MusicBrainzUpdateDatabaseJob), processInfo.Arguments);

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return false;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryTerminateProcess(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                Logger.Warning("[{JobName}] Native extraction failed with exit code {ExitCode}: {Error}",
                    nameof(MusicBrainzUpdateDatabaseJob), process.ExitCode, error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "[{JobName}] Native extraction threw exception, falling back to managed extraction",
                nameof(MusicBrainzUpdateDatabaseJob));
            return false;
        }
    }

    private void ExtractRequiredArchiveEntriesManaged(
        string archivePath,
        string destinationPath,
        IReadOnlyCollection<string> requiredEntries,
        CancellationToken cancellationToken)
    {
        var requiredEntrySet = requiredEntries
            .Select(NormalizeArchiveEntryName)
            .ToHashSet(StringComparer.Ordinal);
        var buffer = new byte[81920];

        using var fileStream = File.OpenRead(archivePath);
        using var bzipStream = new BZip2InputStream(fileStream);
        using var tarStream = new TarInputStream(bzipStream, Encoding.UTF8);

        TarEntry? entry;
        while ((entry = tarStream.GetNextEntry()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedEntryName = NormalizeArchiveEntryName(entry.Name);
            if (entry.IsDirectory || !requiredEntrySet.Contains(normalizedEntryName))
            {
                continue;
            }

            var destinationFilePath = Path.Combine(
                destinationPath,
                normalizedEntryName.Replace('/', Path.DirectorySeparatorChar));
            var destinationDirectory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            using var outputStream = File.Create(destinationFilePath);
            int bytesRead;
            while ((bytesRead = tarStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                outputStream.Write(buffer, 0, bytesRead);
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F1} {suffixes[i]}";
    }

    private static string FormatOperationErrors(IEnumerable<Exception>? errors)
    {
        var errorMessages = errors?
            .Select(error => error.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return errorMessages is { Length: > 0 }
            ? string.Join(" | ", errorMessages)
            : "The importer did not return a specific error message. Check the logs for details.";
    }

    private static int AdvanceImportPhase(int currentPhaseIndex, string phase)
    {
        if (currentPhaseIndex >= 0 && ImportPhaseSequence[currentPhaseIndex] == phase)
        {
            return currentPhaseIndex;
        }

        for (var i = currentPhaseIndex + 1; i < ImportPhaseSequence.Length; i++)
        {
            if (ImportPhaseSequence[i] == phase)
            {
                return i;
            }
        }

        return Math.Max(currentPhaseIndex, 0);
    }

    private static int CalculateImportStageProgress(int phaseIndex, int current, int total)
    {
        var safePhaseIndex = Math.Clamp(phaseIndex, 0, ImportPhaseSequence.Length - 1);
        var phaseStart = safePhaseIndex * ImportStageScale / ImportPhaseSequence.Length;
        var phaseEnd = (safePhaseIndex + 1) * ImportStageScale / ImportPhaseSequence.Length;
        var phasePercent = total > 0 ? Math.Clamp((double)current / total, 0, 1) : 0;
        return phaseStart + (int)Math.Round((phaseEnd - phaseStart) * phasePercent);
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteDatabaseArtifacts(string dbPath)
    {
        foreach (var path in DatabaseArtifactSuffixes.Select(suffix => $"{dbPath}{suffix}"))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void MoveDatabaseArtifacts(string sourceDbPath, string destinationDbPath, bool overwrite)
    {
        foreach (var suffix in DatabaseArtifactSuffixes)
        {
            var sourcePath = $"{sourceDbPath}{suffix}";
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destinationPath = $"{destinationDbPath}{suffix}";
            if (overwrite && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }
    }

    private async Task CheckpointImportedDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Cannot checkpoint imported DecentDB database because the database file does not exist.", databasePath);
        }

        var walStatusBefore = DecentDBMaintenance.GetWalStatus(databasePath);

        Logger.Information("[{JobName}] Running DecentDB checkpoint through ADO.NET binding. WAL before checkpoint: {WalSize}",
            nameof(MusicBrainzUpdateDatabaseJob),
            FormatBytes(walStatusBefore.TotalWalBytes));

        var checkpointResult = await DecentDBMaintenance
            .CheckpointAsync(databasePath, cancellationToken)
            .ConfigureAwait(false);
        Logger.Information(
            "[{JobName}] DecentDB checkpoint complete in {Elapsed:F1}s. WAL after checkpoint: {WalSize}",
            nameof(MusicBrainzUpdateDatabaseJob),
            checkpointResult.Duration.TotalSeconds,
            FormatBytes(checkpointResult.After.TotalWalBytes));
    }

    private static bool HasAllRequiredExtractedFiles(string mbDumpDirectory)
    {
        return RequiredArchiveEntries.All(entry =>
            File.Exists(Path.Combine(mbDumpDirectory, Path.GetFileName(entry))));
    }

    private static void EnsureRequiredExtractedFilesExist(string mbDumpDirectory)
    {
        var missingFiles = RequiredArchiveEntries
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Where(fileName => !File.Exists(Path.Combine(mbDumpDirectory, fileName!)))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new FileNotFoundException(
                $"Required MusicBrainz dump files were not extracted: {string.Join(", ", missingFiles)}");
        }
    }

    private static void DeleteExtractedMusicBrainzFiles(string mbDumpDirectory)
    {
        Directory.CreateDirectory(mbDumpDirectory);
        foreach (var fileName in RequiredArchiveEntries.Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var path = Path.Combine(mbDumpDirectory, fileName!);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string NormalizeArchiveEntryName(string entryName)
    {
        return entryName.Replace('\\', '/').TrimStart('.', '/');
    }

    private static string QuoteShellArgument(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // Best-effort cancellation cleanup for extraction processes.
        }
    }

    private static string? FindNativeDecompressor()
    {
        foreach (var tool in new[] { "lbzip2", "pbzip2", "bzip2" })
        {
            try
            {
                using var whichProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = tool,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                whichProcess?.WaitForExit(1000);
                if (whichProcess?.ExitCode == 0)
                {
                    return tool;
                }
            }
            catch
            {
                // Tool not found, continue to next candidate.
            }
        }

        return null;
    }

    private async Task RecoverInterruptedImportAsync(
        string storagePath,
        string dbPath,
        string lockFilePath,
        ImportLockState? lockState,
        CancellationToken cancellationToken)
    {
        Logger.Warning("[{JobName}] Recovering stale MusicBrainz import state from lock file [{LockFile}]",
            nameof(MusicBrainzUpdateDatabaseJob),
            lockFilePath);

        var importDatabasePath = GetImportDatabasePath(dbPath, lockState);
        if (importDatabasePath != null)
        {
            DeleteDatabaseArtifacts(importDatabasePath);
        }

        var backupPath = GetBackupDatabasePath(storagePath, dbPath, lockState);
        if (backupPath != null)
        {
            Logger.Information("[{JobName}] Restoring backup database from stale import file [{BackupPath}]",
                nameof(MusicBrainzUpdateDatabaseJob),
                backupPath);
            DeleteDatabaseArtifacts(dbPath);
            MoveDatabaseArtifacts(backupPath, dbPath, overwrite: true);
        }

        File.Delete(lockFilePath);
        await settingService
            .SetAsync(SettingRegistry.SearchEngineMusicBrainzEnabled, "true", cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? GetBackupDatabasePath(string storagePath, string dbPath, ImportLockState? lockState)
    {
        if (!string.IsNullOrWhiteSpace(lockState?.BackupDatabasePath) && File.Exists(lockState.BackupDatabasePath))
        {
            return lockState.BackupDatabasePath;
        }

        var expectedBackupPath = GetBackupDatabaseFilePath(dbPath);
        if (File.Exists(expectedBackupPath))
        {
            return expectedBackupPath;
        }

        var dbPathFull = Path.GetFullPath(dbPath);
        var candidates = Directory
            .EnumerateFiles(storagePath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFullPath(path), dbPathFull, StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetFileNameWithoutExtension(path).Contains(BackupDatabaseSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return candidates.FirstOrDefault();
    }

    private static string? GetImportDatabasePath(string dbPath, ImportLockState? lockState)
    {
        if (!string.IsNullOrWhiteSpace(lockState?.ImportDatabasePath))
        {
            return lockState.ImportDatabasePath;
        }

        var expectedImportPath = GetImportDatabaseFilePath(dbPath);
        return DatabaseArtifactSuffixes.Any(suffix => File.Exists($"{expectedImportPath}{suffix}"))
            ? expectedImportPath
            : null;
    }

    private async Task<ImportLockState?> ReadLockStateAsync(string lockFilePath, CancellationToken cancellationToken)
    {
        var lockContent = await File.ReadAllTextAsync(lockFilePath, cancellationToken).ConfigureAwait(false);

        try
        {
            return JsonSerializer.Deserialize<ImportLockState>(lockContent);
        }
        catch (JsonException)
        {
            return new ImportLockState(lockContent, null, null, null);
        }
    }

    private async Task WriteLockStateAsync(
        string lockFilePath,
        string? backupDatabasePath,
        string? importDatabasePath,
        CancellationToken cancellationToken)
    {
        var lockState = new ImportLockState(
            DateTimeOffset.UtcNow.ToString("O"),
            Environment.ProcessId,
            backupDatabasePath,
            importDatabasePath);
        var json = JsonSerializer.Serialize(lockState);
        await File.WriteAllTextAsync(lockFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    private sealed record ImportLockState(
        string CreatedAtUtc,
        int? ProcessId,
        string? BackupDatabasePath,
        string? ImportDatabasePath);
}
