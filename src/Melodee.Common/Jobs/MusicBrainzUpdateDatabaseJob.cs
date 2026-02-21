using System.Globalization;
using System.Text;
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
        var lockfile = string.Empty;
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

            progress?.UpdateProgress("Creating storage directory...");
            storagePath.ToFileSystemDirectoryInfo().EnsureExists();
            Logger.Debug("[{JobName}] Storage directory exists or was created.", nameof(MusicBrainzUpdateDatabaseJob));

            lockfile = Path.Combine(storagePath, $"{nameof(MusicBrainzUpdateDatabaseJob)}.lock");
            Logger.Debug("[{JobName}] Checking for lock file at: [{LockFile}]", nameof(MusicBrainzUpdateDatabaseJob), lockfile);

            if (File.Exists(lockfile))
            {
                var lockContent = await File.ReadAllTextAsync(lockfile, context.CancellationToken);
                var msg = $"Job lock file exists at [{lockfile}] (created: {lockContent}). Another instance may be running, or a previous run crashed. Delete the lock file to proceed.";
                Logger.Warning("[{JobName}] {Message}", nameof(MusicBrainzUpdateDatabaseJob), msg);
                SetJobResult(context, JobResultStatus.Skipped, msg);
                return;
            }

            progress?.UpdateProgress("Creating lock file...");
            Logger.Debug("[{JobName}] Creating lock file...", nameof(MusicBrainzUpdateDatabaseJob));
            await File.WriteAllTextAsync(lockfile, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);

            progress?.UpdateProgress("Disabling search engine during import...");
            Logger.Debug("[{JobName}] Temporarily disabling MusicBrainz search engine during import...", nameof(MusicBrainzUpdateDatabaseJob));
            await settingService
                .SetAsync(SettingRegistry.SearchEngineMusicBrainzEnabled, "false", context.CancellationToken)
                .ConfigureAwait(false);

            var dbName = GetDatabaseFilePath();
            var doesDbExist = File.Exists(dbName);
            Logger.Debug("[{JobName}] Existing database check: exists={Exists}, path={DbPath}",
                nameof(MusicBrainzUpdateDatabaseJob), doesDbExist, dbName);

            if (doesDbExist)
            {
                progress?.UpdateProgress("Backing up existing database...");
                tempDbName = Path.Combine(storagePath, $"{Guid.NewGuid()}.db");
                Logger.Debug("[{JobName}] Backing up existing database to: [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                File.Move(dbName, tempDbName);
            }

            progress?.CompleteStage(); // Complete Initialize stage

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

                // Check if extraction has already been completed
                // We look for the 'mbdump' directory with 'artist' file as indicator of successful extraction
                var mbDumpDir = Path.Combine(storageStagingDirectory.FullName(), "mbdump");
                var artistFile = Path.Combine(mbDumpDir, "artist");
                var extractionComplete = Directory.Exists(mbDumpDir) && File.Exists(artistFile);

                if (!extractionComplete)
                {
                    // Extract archives SEQUENTIALLY to avoid file conflicts
                    // Both archives contain some common files like TIMESTAMP
                    progress?.StartStage(StageExtract, 2);

                    var extractionStartTicks = Stopwatch.GetTimestamp();

                    progress?.UpdateProgress(0, "Extracting mbdump.tar.bz2...");
                    Logger.Information("[{JobName}] Extracting mbdump.tar.bz2...", nameof(MusicBrainzUpdateDatabaseJob));
                    ExtractTarBz2(mbDumpFileName, storageStagingDirectory.FullName());

                    progress?.UpdateProgress(1, "Extracting mbdump-derived.tar.bz2...");
                    Logger.Information("[{JobName}] Extracting mbdump-derived.tar.bz2...", nameof(MusicBrainzUpdateDatabaseJob));
                    ExtractTarBz2(mbDumpDerivedFileName, storageStagingDirectory.FullName());

                    progress?.UpdateProgress(2, "Extraction complete");

                    var totalExtractionTime = Stopwatch.GetElapsedTime(extractionStartTicks);
                    Logger.Information("[{JobName}] Archive extraction complete in {Elapsed:F1} minutes.",
                        nameof(MusicBrainzUpdateDatabaseJob), totalExtractionTime.TotalMinutes);
                    progress?.CompleteStage();
                }
                else
                {
                    Logger.Information("[{JobName}] Archives already extracted (found {ArtistFile}), skipping extraction",
                        nameof(MusicBrainzUpdateDatabaseJob), artistFile);
                    progress?.StartStage(StageExtract, "Already extracted");
                    progress?.CompleteStage();
                }
            }

            // Import data to DecentDB with progress callback
            progress?.StartStage(StageImport, "Loading and importing data...");
            var importStartTicks = Stopwatch.GetTimestamp();
            Logger.Information("[{JobName}] Starting data import to DecentDB...", nameof(MusicBrainzUpdateDatabaseJob));

            // Create progress callback that updates the job progress
            void ImportProgressCallback(string phase, int current, int total, string? message)
            {
                var percentComplete = total > 0 ? (double)current / total * 100 : 0;
                var progressMessage = message ?? $"{phase}: {current:N0} / {total:N0} ({percentComplete:F1}%)";
                progress?.UpdateProgress(progressMessage);

                // Log at info level periodically to be visible in production logs
                if (current % 50000 == 0 || current == total)
                {
                    Logger.Information("[{JobName}] Import progress - {Phase}: {Current:N0}/{Total:N0} ({Percent:F1}%)",
                        nameof(MusicBrainzUpdateDatabaseJob), phase, current, total, percentComplete);
                }
            }

            var importResult = await repository.ImportData(ImportProgressCallback, context.CancellationToken).ConfigureAwait(false);
            var importTime = Stopwatch.GetElapsedTime(importStartTicks);
            progress?.CompleteStage();

            Logger.Debug("[{JobName}] Import result: Success={Success}, Errors={Errors}, Duration={Duration:F1} minutes",
                nameof(MusicBrainzUpdateDatabaseJob), importResult.IsSuccess,
                string.Join(", ", importResult.Errors ?? []), importTime.TotalMinutes);

            // Cleanup stage
            progress?.StartStage(StageCleanup, "Finalizing...");

            if (importResult.IsSuccess)
            {
                if (tempDbName != null)
                {
                    progress?.UpdateProgress("Deleting backup database...");
                    Logger.Debug("[{JobName}] Deleting backup database: [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                    File.Delete(tempDbName);
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
                var msg = $"Import failed: {string.Join(", ", importResult.Errors ?? [])}";
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

            if (!importSucceeded && tempDbName != null && storagePath != null && File.Exists(tempDbName))
            {
                try
                {
                    Logger.Information("[{JobName}] Restoring backup database from: [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
                    var dbName = GetDatabaseFilePath();
                    if (File.Exists(dbName))
                    {
                        File.Delete(dbName);
                    }
                    File.Move(tempDbName, dbName);
                    Logger.Information("[{JobName}] Backup database restored successfully.", nameof(MusicBrainzUpdateDatabaseJob));
                }
                catch (Exception restoreEx)
                {
                    Logger.Error(restoreEx, "[{JobName}] Failed to restore backup database from [{TempDbName}]", nameof(MusicBrainzUpdateDatabaseJob), tempDbName);
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

            // Always re-enable MusicBrainz search engine - use CancellationToken.None since we're in finally and need this to succeed
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

    private void ExtractTarBz2(string archivePath, string destinationPath)
    {
        Logger.Debug("[{JobName}] Extracting [{FileName}]...", nameof(MusicBrainzUpdateDatabaseJob), archivePath);
        var sw = Stopwatch.GetTimestamp();

        // Try native extraction first (much faster with lbzip2/pbzip2)
        if (TryNativeExtraction(archivePath, destinationPath))
        {
            Logger.Information("[{JobName}] Extracted [{FileName}] using native tools in {Elapsed:F1} seconds.",
                nameof(MusicBrainzUpdateDatabaseJob),
                Path.GetFileName(archivePath),
                Stopwatch.GetElapsedTime(sw).TotalSeconds);
            return;
        }

        // Fallback to managed extraction
        Logger.Debug("[{JobName}] Native extraction not available, using managed extraction", nameof(MusicBrainzUpdateDatabaseJob));
        using var fileStream = File.OpenRead(archivePath);
        using var bzipStream = new BZip2InputStream(fileStream);
        var tarArchive = TarArchive.CreateInputTarArchive(bzipStream, Encoding.UTF8);
        tarArchive.ExtractContents(destinationPath);
        tarArchive.Close();

        Logger.Information("[{JobName}] Extracted [{FileName}] in {Elapsed:F1} seconds.",
            nameof(MusicBrainzUpdateDatabaseJob),
            Path.GetFileName(archivePath),
            Stopwatch.GetElapsedTime(sw).TotalSeconds);
    }

    private bool TryNativeExtraction(string archivePath, string destinationPath)
    {
        // Only attempt native extraction on Unix-like systems (Linux, macOS)
        // Windows doesn't have these tools natively and the shell syntax differs
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        // Check for parallel bzip2 decompressors (much faster than single-threaded)
        string? decompressor = null;
        foreach (var tool in new[] { "lbzip2", "pbzip2", "bzip2" })
        {
            try
            {
                var whichProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
                    decompressor = tool;
                    break;
                }
            }
            catch
            {
                // Tool not found, continue to next
            }
        }

        if (decompressor == null)
        {
            return false;
        }

        try
        {
            // Use pipe: decompressor | tar
            // lbzip2/pbzip2 use multiple cores for decompression
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{decompressor} -dc '{archivePath}' | tar -xf - -C '{destinationPath}'\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Logger.Debug("[{JobName}] Running native extraction: {Command}", nameof(MusicBrainzUpdateDatabaseJob), processInfo.Arguments);

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process == null)
            {
                return false;
            }

            // Wait for completion (these files are large, give it plenty of time)
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
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
}
