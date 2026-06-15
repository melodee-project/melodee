using System.Collections.Concurrent;
using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Models;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Rebus.Bus;
using Serilog;
using dbModels = Melodee.Common.Data.Models;
using SearchOption = System.IO.SearchOption;

namespace Melodee.Common.Jobs;

/// <summary>
///     Reads melodee.json metadata files from storage libraries and inserts artists, albums, and songs into the database.
/// </summary>
/// <remarks>
///     <para>
///         This is the final stage of the Melodee media import pipeline. After media has been processed and
///         validated in staging, it is moved to a storage library. This job reads the melodee.json metadata
///         files and creates the corresponding database records, making songs available for API clients to stream.
///     </para>
///     <para>
///         This job is part of the media ingestion chain:
///         <code>
///         LibraryInboundProcessJob → StagingAutoMoveJob → LibraryInsertJob
///         </code>
///         This is the terminal job in the chain - it does not trigger any subsequent jobs.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Scans all storage libraries (LibraryType.Storage) for melodee.json files</item>
///             <item>Filters to files modified since the last scan (unless force mode is enabled)</item>
///             <item>Loads and validates album metadata from each melodee.json file</item>
///             <item>Creates or finds existing Artist records (matching by name, MusicBrainz ID, or Spotify ID)</item>
///             <item>Creates Album records with all metadata (genres, release date, duration, etc.)</item>
///             <item>Creates Song records with media file details (bitrate, duration, file hash, etc.)</item>
///             <item>Creates Contributor records for performers, producers, and publishers</item>
///             <item>Updates library aggregates (total albums, songs, duration)</item>
///             <item>Records scan history for monitoring and debugging</item>
///         </list>
///     </para>
///     <para>
///         This job is marked with [DisallowConcurrentExecution] to prevent database conflicts from
///         simultaneous inserts. It processes files in batches to manage memory usage.
///     </para>
///     <para>
///         Special handling:
///         <list type="bullet">
///             <item>Duplicate albums are detected and prefixed with "__duplicate_" for manual review</item>
///             <item>Invalid melodee.json files trigger a reprocess event and are moved to staging</item>
///             <item>Missing media files (referenced in JSON but not on disk) trigger reprocessing</item>
///             <item>Locked libraries are skipped</item>
///         </list>
///     </para>
///     <para>
///         Configuration settings used:
///         <list type="bullet">
///             <item>ProcessingMaximumProcessingCount: Maximum songs to process per run (0 = unlimited)</item>
///             <item>ProcessingDuplicateAlbumPrefix: Prefix added to duplicate album directories</item>
///             <item>ProcessingIgnoredPerformers/Publishers/Production: Names to exclude from contributors</item>
///         </list>
///     </para>
///     <para>
///         Default schedule: Daily at midnight (configurable via jobs.libraryInsert.cronExpression setting).
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
public class LibraryInsertJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ISerializer serializer,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ArtistService artistService,
    AlbumService albumService,
    AlbumDiscoveryService albumDiscoveryService,
    DirectoryProcessorToStagingService directoryProcessorToStagingService,
    IBus bus) : JobBase(logger, configurationFactory)
{
    private IAlbumValidator _albumValidator = null!;
    private int _batchSize;
    private IMelodeeConfiguration _configuration = null!;
    private JobDataMap _dataMap = null!;
    private string _duplicateAlbumPrefix = string.Empty;
    private string[] _ignorePerformers = [];
    private string[] _ignoreProduction = [];
    private string[] _ignorePublishers = [];
    private int _maxSongsToProcess;
    private Instant _now;
    private int _totalAlbumsInserted;
    private int _totalArtistsInserted;
    private int _totalSongsInserted;
    private int _melodeeFilesDiscovered;
    private int _melodeeFilesFiltered;
    private int _invalidMelodeeFiles;
    private int _albumsAlreadyInDatabase;


    /// <summary>
    ///     This is raised when a Log event happens to return activity to caller.
    /// </summary>
    public event EventHandler<ProcessingEvent>? OnProcessingEvent;

    public override async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var startTicks = Stopwatch.GetTimestamp();
            _configuration = await ConfigurationFactory
                .GetConfigurationAsync(context.CancellationToken)
                .ConfigureAwait(false);
            _albumValidator = new AlbumValidator(_configuration);
            var libraries = await libraryService
                .ListAsync(new PagedRequest(), context.CancellationToken)
                .ConfigureAwait(false);
            if (!libraries.IsSuccess)
            {
                Logger.Warning("[{JobName}] Unable to get libraries, skipping processing.", nameof(LibraryInsertJob));
                return;
            }

            var forceMode = SafeParser.ToBoolean(context.Get(MelodeeJobExecutionContext.ForceMode));
            var scanJustDirectory = context.Get(MelodeeJobExecutionContext.ScanJustDirectory)?.ToString();


            _totalAlbumsInserted = 0;
            _totalArtistsInserted = 0;
            _totalSongsInserted = 0;
            _melodeeFilesDiscovered = 0;
            _melodeeFilesFiltered = 0;
            _invalidMelodeeFiles = 0;
            _albumsAlreadyInDatabase = 0;
            _maxSongsToProcess = _configuration.GetValue<int?>(SettingRegistry.ProcessingMaximumProcessingCount) ?? 0;
            _batchSize = _configuration.BatchProcessingSize();
            var messagesForJobRun = new List<string>();
            var exceptionsForJobRun = new List<Exception>();

            await albumDiscoveryService
                .InitializeAsync(_configuration, context.CancellationToken)
                .ConfigureAwait(false);
            await directoryProcessorToStagingService
                .InitializeAsync(_configuration, context.CancellationToken)
                .ConfigureAwait(false);

            _ignorePerformers = MelodeeConfiguration.FromSerializedJsonArrayNormalized(_configuration.Configuration[SettingRegistry.ProcessingIgnoredPerformers], serializer);
            _ignorePublishers = MelodeeConfiguration.FromSerializedJsonArrayNormalized(_configuration.Configuration[SettingRegistry.ProcessingIgnoredPublishers], serializer);
            _ignoreProduction = MelodeeConfiguration.FromSerializedJsonArrayNormalized(_configuration.Configuration[SettingRegistry.ProcessingIgnoredProduction], serializer);

            _now = Instant.FromDateTimeUtc(DateTime.UtcNow);

            _duplicateAlbumPrefix = _configuration.GetValue<string>(SettingRegistry.ProcessingDuplicateAlbumPrefix) ??
                                    "__duplicate_ ";

            _dataMap = context.JobDetail.JobDataMap;
            var defaultNeverScannedDate = Instant.FromDateTimeUtc(DateTime.MinValue.ToUniversalTime());
            var stagingLibrary = await libraryService.GetStagingLibraryAsync(context.CancellationToken).ConfigureAwait(false);
            if (!stagingLibrary.IsSuccess)
            {
                messagesForJobRun.AddRange(stagingLibrary.Messages ?? []);
                exceptionsForJobRun.AddRange(stagingLibrary.Errors ?? []);
                Logger.Warning("[{JobName}] Unable to get staging library, skipping processing.",
                    nameof(LibraryInsertJob));
                return;
            }

            var librariesToProcess = libraries.Data.Where(x => x.TypeValue == LibraryType.Storage).ToArray();
            _dataMap[JobMapNameRegistry.ScanStatus] = nameof(ScanStatus.InProcess);

            var totalMelodeeFilesProcessed = 0;
            var totalMelodeeFilesToProcess = 0;

            // Process each library with its own scope to avoid long-lived contexts
            foreach (var libraryIndex in librariesToProcess.Select((library, index) => new { library, index }))
            {
                if (libraryIndex.library.IsLocked)
                {
                    Logger.Warning("[{JobName}] Skipped processing locked library [{LibraryName}]",
                        nameof(LibraryInsertJob), libraryIndex.library.Name);
                    continue;
                }

                if (_totalSongsInserted > _maxSongsToProcess && _maxSongsToProcess > 0)
                {
                    Logger.Warning("[{JobName}] Maximum Processing Count reached. Stopping processing.",
                        nameof(LibraryInsertJob));
                    break;
                }

                var libraryProcessStartTicks = Stopwatch.GetTimestamp();
                var lastScanAt = forceMode
                    ? defaultNeverScannedDate
                    : libraryIndex.library.LastScanAt ?? defaultNeverScannedDate;

                OnProcessingEvent?.Invoke(
                    this,
                    new ProcessingEvent(ProcessingEventType.Processing,
                        nameof(LibraryInsertJob),
                        0,
                        0,
                        $"Discovering albums in [{libraryIndex.library.Name}]..."));

                Logger.Information("[{JobName}] Starting to find melodee files for library [{LibraryName}] at path [{Path}]",
                    nameof(LibraryInsertJob), libraryIndex.library.Name, libraryIndex.library.Path);
                var melodeeFilesToProcess = GetMelodeeFilesToProcess(
                    libraryIndex.library,
                    scanJustDirectory,
                    lastScanAt.ToDateTimeUtc());
                Logger.Information("[{JobName}] Found [{Count}] melodee files to process for library [{LibraryName}]",
                    nameof(LibraryInsertJob), melodeeFilesToProcess.Count, libraryIndex.library.Name);

                if (melodeeFilesToProcess.Count == 0)
                {
                    Logger.Information("[{JobName}] found no melodee files to process for directory [{PathName}].",
                        nameof(LibraryInsertJob),
                        scanJustDirectory.Nullify() ?? libraryIndex.library.Path);
                    OnProcessingEvent?.Invoke(
                        this,
                        new ProcessingEvent(ProcessingEventType.Processing,
                            nameof(LibraryInsertJob),
                            0,
                            0,
                            $"No albums found in [{libraryIndex.library.Name}]"));
                    continue;
                }

                totalMelodeeFilesToProcess += melodeeFilesToProcess.Count;

                OnProcessingEvent?.Invoke(
                    this,
                    new ProcessingEvent(ProcessingEventType.Start,
                        nameof(LibraryInsertJob),
                        melodeeFilesToProcess.Count,
                        0,
                        $"Found [{melodeeFilesToProcess.Count}] albums to process"));

                var batches = (melodeeFilesToProcess.Count + _batchSize - 1) / _batchSize;
                Logger.Debug("[{JobName}] Found [{DirName}] melodee files to scan in [{Batches}] batches.",
                    nameof(LibraryInsertJob),
                    melodeeFilesToProcess.Count,
                    batches);

                var albumsProcessedInLibrary = 0;

                // Process batches with optimized database operations
                for (var batch = 0; batch < batches; batch++)
                {
                    var batchFiles = melodeeFilesToProcess.Skip(_batchSize * batch).Take(_batchSize).ToList();
                    var batchStartIndex = albumsProcessedInLibrary;

                    OnProcessingEvent?.Invoke(
                        this,
                        new ProcessingEvent(ProcessingEventType.Processing,
                            nameof(LibraryInsertJob),
                            melodeeFilesToProcess.Count,
                            albumsProcessedInLibrary,
                            $"[{albumsProcessedInLibrary}/{melodeeFilesToProcess.Count}] Loading batch {batch + 1}/{batches}..."));

                    // Load albums in parallel for better I/O performance
                    var melodeeAlbumsForBatch = await LoadAlbumsInParallelAsync(
                        batchFiles,
                        stagingLibrary.Data.Path,
                        context.CancellationToken);

                    if (melodeeAlbumsForBatch.Count == 0)
                    {
                        albumsProcessedInLibrary += batchFiles.Count;
                        OnProcessingEvent?.Invoke(
                            this,
                            new ProcessingEvent(ProcessingEventType.Processing,
                                nameof(LibraryInsertJob),
                                melodeeFilesToProcess.Count,
                                albumsProcessedInLibrary,
                                $"[{albumsProcessedInLibrary}/{melodeeFilesToProcess.Count}] Batch {batch + 1} skipped (no valid albums)"));
                        continue;
                    }

                    // Update progress to show we're partway through the batch
                    var midBatchProgress = batchStartIndex + (batchFiles.Count / 3);
                    OnProcessingEvent?.Invoke(
                        this,
                        new ProcessingEvent(ProcessingEventType.Processing,
                            nameof(LibraryInsertJob),
                            melodeeFilesToProcess.Count,
                            midBatchProgress,
                            $"[{midBatchProgress}/{melodeeFilesToProcess.Count}] Processing {melodeeAlbumsForBatch.Count} artists..."));

                    // Process artists and albums with dedicated contexts for each operation
                    var processedArtistsResult = await ProcessArtistsAsync(
                        libraryIndex.library,
                        melodeeAlbumsForBatch,
                        context.CancellationToken);
                    if (!processedArtistsResult)
                    {
                        albumsProcessedInLibrary += batchFiles.Count;
                        continue;
                    }

                    // Update progress again
                    var twoThirdsBatchProgress = batchStartIndex + (batchFiles.Count * 2 / 3);
                    OnProcessingEvent?.Invoke(
                        this,
                        new ProcessingEvent(ProcessingEventType.Processing,
                            nameof(LibraryInsertJob),
                            melodeeFilesToProcess.Count,
                            twoThirdsBatchProgress,
                            $"[{twoThirdsBatchProgress}/{melodeeFilesToProcess.Count}] Inserting {melodeeAlbumsForBatch.Count} albums..."));

                    var processedAlbumsResult = await ProcessAlbumsAsync(melodeeAlbumsForBatch, context.CancellationToken);
                    if (!processedAlbumsResult)
                    {
                        albumsProcessedInLibrary += batchFiles.Count;
                        continue;
                    }

                    albumsProcessedInLibrary += batchFiles.Count;
                    totalMelodeeFilesProcessed += melodeeAlbumsForBatch.Count;

                    var currentAlbumName = melodeeAlbumsForBatch.LastOrDefault()?.AlbumTitle() ?? "Complete";
                    if (currentAlbumName.Length > 35)
                    {
                        currentAlbumName = currentAlbumName[..32] + "...";
                    }
                    OnProcessingEvent?.Invoke(
                        this,
                        new ProcessingEvent(ProcessingEventType.Processing,
                            nameof(LibraryInsertJob),
                            melodeeFilesToProcess.Count,
                            albumsProcessedInLibrary,
                            $"[{albumsProcessedInLibrary}/{melodeeFilesToProcess.Count}] {currentAlbumName}"));
                }

                // Update library aggregates and scan history with dedicated context
                await using (var libraryContext = await contextFactory.CreateDbContextAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    await libraryService.UpdateAggregatesAsync(libraryIndex.library.Id, context.CancellationToken)
                        .ConfigureAwait(false);

                    var newLibraryScanHistory = new dbModels.LibraryScanHistory
                    {
                        LibraryId = libraryIndex.library.Id,
                        CreatedAt = _now,
                        DurationInMs = Stopwatch.GetElapsedTime(libraryProcessStartTicks).TotalMilliseconds,
                        FoundAlbumsCount = _totalAlbumsInserted,
                        FoundArtistsCount = _totalArtistsInserted,
                        FoundSongsCount = _totalSongsInserted
                    };
                    libraryContext.LibraryScanHistories.Add(newLibraryScanHistory);
                    await libraryContext.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
                }

                OnProcessingEvent?.Invoke(
                    this,
                    new ProcessingEvent(ProcessingEventType.Processing,
                        nameof(LibraryInsertJob),
                        melodeeFilesToProcess.Count,
                        melodeeFilesToProcess.Count,
                        $"Completed library [{libraryIndex.library.Name}]"));
            }

            _dataMap[JobMapNameRegistry.ScanStatus] = nameof(ScanStatus.Idle);
            _dataMap[JobMapNameRegistry.Count] = _totalAlbumsInserted + _totalArtistsInserted + _totalSongsInserted;

            var stopSummary =
                $"Processed [{totalMelodeeFilesProcessed}] albums, inserted [{_totalAlbumsInserted}] albums, [{_totalSongsInserted}] songs";

            OnProcessingEvent?.Invoke(
                this,
                new ProcessingEvent(ProcessingEventType.Stop,
                    nameof(LibraryInsertJob),
                    totalMelodeeFilesToProcess,
                    totalMelodeeFilesProcessed,
                    stopSummary));

            foreach (var message in messagesForJobRun)
            {
                Log.Debug("[{JobName}] Message: [{Message}]", nameof(LibraryInsertJob), message);
            }

            foreach (var exception in exceptionsForJobRun)
            {
                Log.Error(exception, "[{JobName}] Processing Exception", nameof(LibraryInsertJob));
            }

            context.Result = new ScanStepResult(
                ArtistsInserted: _totalArtistsInserted,
                AlbumsInserted: _totalAlbumsInserted,
                SongsInserted: _totalSongsInserted);

            Log.Information("ℹ️ [{JobName}] Completed. {StopSummary}",
                nameof(LibraryInsertJob),
                stopSummary);
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{JobName}] Processing Exception", nameof(LibraryInsertJob));
        }
    }

    /// <summary>
    ///     For all albums with songs, add to db albums
    /// </summary>
    private async Task<bool> ProcessAlbumsAsync(
        List<Album> melodeeAlbumsForDirectory,
        CancellationToken cancellationToken)
    {
        var currentAlbum = melodeeAlbumsForDirectory.FirstOrDefault();
        var currentSong = currentAlbum?.Songs?.FirstOrDefault();
        try
        {
            await using (var scopedContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var dbAlbumsToAdd = new List<dbModels.Album>();
                foreach (var melodeeAlbum in melodeeAlbumsForDirectory)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    currentAlbum = melodeeAlbum;
                    var artistName = melodeeAlbum.Artist.Name.CleanStringAsIs() ??
                                     throw new Exception("Album artist is required.");
                    var artistNormalizedName = artistName.ToNormalizedString() ?? artistName;
                    var dbArtistResult = await artistService.FindArtistAsync(melodeeAlbum.Artist.ArtistDbId,
                        melodeeAlbum.Artist.Id, artistNormalizedName, melodeeAlbum.Artist.MusicBrainzId,
                        melodeeAlbum.Artist.SpotifyId, melodeeAlbum.Artist.ItunesId, cancellationToken).ConfigureAwait(false);
                    var dbArtistId = dbArtistResult.Data?.Id;
                    var dbArtist = dbArtistId == null
                        ? null
                        : await scopedContext.Artists.FirstOrDefaultAsync(x => x.Id == dbArtistId, cancellationToken)
                            .ConfigureAwait(false);
                    if (dbArtist == null)
                    {
                        Logger.Warning(
                            "Unable to find artist by id [{ArtistDbId}] apikey [{ApiKey}] nameNormalized [{NameNormalized}] musicBrainzId [{MbId}] artist for album [{AlbumUniqueId}].",
                            melodeeAlbum.Artist.ArtistDbId,
                            melodeeAlbum.Artist.Id,
                            artistNormalizedName,
                            melodeeAlbum.Artist.MusicBrainzId,
                            melodeeAlbum.Id);
                        continue;
                    }

                    var albumTitle = melodeeAlbum.AlbumTitle()?.CleanStringAsIs() ??
                                     throw new Exception("Album title is required.");
                    var nameNormalized = albumTitle.ToNormalizedString() ?? albumTitle;
                    if (nameNormalized.Nullify() == null)
                    {
                        Logger.Warning("Album [{Album}] has invalid Album title, unable to generate NameNormalized.",
                            melodeeAlbum);
                        continue;
                    }

                    var dbAlbumResult = await albumService.FindAlbumAsync(dbArtist.Id, melodeeAlbum, cancellationToken)
                        .ConfigureAwait(false);
                    var dbAlbum = dbAlbumResult.Data;
                    var albumDirectory = melodeeAlbum.AlbumDirectoryName(_configuration.Configuration);
                    if (dbAlbum != null)
                    {
                        _albumsAlreadyInDatabase++;
                        Trace.WriteLine(
                            $"[{nameof(LibraryInsertJob)}] Artist [{dbArtist.Id}] Album [{dbAlbum.Name}] already exists in db. Skipping.");
                    }
                    else
                    {
                        var newAlbum = new dbModels.Album
                        {
                            AlbumStatus = (short)melodeeAlbum.Status,
                            AlbumType = SafeParser.ToNumber<short>(melodeeAlbum.AlbumType),
                            AmgId = melodeeAlbum.AmgId,
                            ApiKey = melodeeAlbum.Id,
                            Artist = dbArtist,
                            CreatedAt = _now,
                            Directory = albumDirectory,
                            DiscogsId = melodeeAlbum.DiscogsId,
                            Duration = melodeeAlbum.TotalDuration(),
                            Genres = melodeeAlbum.Genre() == null ? null : melodeeAlbum.Genre()!.Split('/'),
                            ImageCount = melodeeAlbum.Images?.Count(),
                            IsCompilation = melodeeAlbum.IsVariousArtistTypeAlbum(),
                            ItunesId = melodeeAlbum.ItunesId,
                            LastFmId = melodeeAlbum.LastFmId,
                            MetaDataStatus = (int)MetaDataModelStatus.ReadyToProcess,
                            MusicBrainzId = SafeParser.ToGuid(melodeeAlbum.MusicBrainzId),
                            Name = albumTitle,
                            NameNormalized = nameNormalized,
                            OriginalReleaseDate = melodeeAlbum.OriginalAlbumYear() == null
                                ? null
                                : SafeParser.ToLocalDate(melodeeAlbum.OriginalAlbumYear()!.Value),
                            ReleaseDate = SafeParser.ToLocalDate(melodeeAlbum.AlbumYear() ??
                                                                 throw new Exception("Album year is required.")),
                            SongCount = SafeParser.ToNumber<short>(melodeeAlbum.Songs?.Count() ?? 0),
                            SortName = _configuration.RemoveUnwantedArticles(albumTitle.CleanString(true)),
                            SpotifyId = melodeeAlbum.SpotifyId,
                            WikiDataId = melodeeAlbum.WikiDataId
                        };
                        if (dbAlbumsToAdd.Any(x => x.Artist.Id == dbArtist.Id && x.NameNormalized == nameNormalized) ||
                            dbAlbumsToAdd.Any(x =>
                                x.MusicBrainzId != null && x.MusicBrainzId == newAlbum.MusicBrainzId) ||
                            dbAlbumsToAdd.Any(x => x.SpotifyId != null && x.SpotifyId == newAlbum.SpotifyId))
                        {
                            Logger.Warning("For artist [{Artist}] found duplicate album [{Album}]", dbArtist, newAlbum);
                            melodeeAlbum.Directory.AppendPrefix(_duplicateAlbumPrefix);
                            continue;
                        }

                        Logger.Debug(
                            "[{JobName}] Creating new album for ArtistId [{ArtistId}] Id [{Id}] NormalizedName [{Name}] Directory [{Directory}]",
                            nameof(LibraryInsertJob),
                            dbArtist.Id,
                            melodeeAlbum.Id,
                            nameNormalized,
                            melodeeAlbum.Directory.FullName());

                        var newAlbumSongs = new List<dbModels.Song>();
                        foreach (var song in melodeeAlbum.Songs ?? [])
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                newAlbumSongs.Clear();
                                break;
                            }

                            currentSong = song;
                            var mediaFile = song.File.ToFileInfo(melodeeAlbum.Directory);
                            if (!mediaFile.Exists)
                            {
                                newAlbumSongs.Clear();
                                Logger.Warning(
                                    "[{JobName}] Unable to find media file [{FileName}], deleting metadata album [{Album}] and triggering reprocess event.",
                                    nameof(LibraryInsertJob),
                                    mediaFile.FullName,
                                    melodeeAlbum.MelodeeDataFileName);
                                await bus.SendLocal(new MelodeeAlbumReprocessEvent(melodeeAlbum.Directory.FullName()))
                                    .ConfigureAwait(false);
                                if (File.Exists(melodeeAlbum.MelodeeDataFileName!))
                                {
                                    File.Delete(melodeeAlbum.MelodeeDataFileName!);
                                }

                                break;
                            }

                            var mediaFileHash = Crc32.Calculate(mediaFile);
                            var songTitle = song.Title()?.CleanStringAsIs() ??
                                            throw new Exception("Song title is required.");
                            var s = new dbModels.Song
                            {
                                AlbumId = newAlbum.Id,
                                ApiKey = song.Id,
                                BitDepth = song.BitDepth(),
                                BitRate = song.BitRate(),
                                BPM = song.MetaTagValue<int>(MetaTagIdentifier.Bpm),
                                ContentType = song.ContentType(),
                                CreatedAt = _now,
                                Duration = song.Duration() ?? throw new Exception("Song duration is required."),
                                FileHash = mediaFileHash,
                                FileName = mediaFile.Name,
                                FileSize = mediaFile.Length,
                                SamplingRate = song.SamplingRate(),
                                Title = songTitle,
                                TitleNormalized = songTitle.ToNormalizedString() ?? songTitle,
                                SongNumber = song.SongNumber(),
                                ChannelCount = song.ChannelCount(),
                                Genres = (song.Genre()?.Nullify() ?? melodeeAlbum.Genre()?.Nullify())?.Split('/'),
                                IsVbr = song.IsVbr(),
                                Lyrics = song.MetaTagValue<string>(MetaTagIdentifier.UnsynchronisedLyrics)
                                             ?.CleanStringAsIs() ??
                                         song.MetaTagValue<string>(MetaTagIdentifier.SynchronisedLyrics)
                                             ?.CleanStringAsIs(),
                                MusicBrainzId = song.MetaTagValue<Guid?>(MetaTagIdentifier.MusicBrainzId),
                                PartTitles = song.MetaTagValue<string>(MetaTagIdentifier.SubTitle)?.CleanStringAsIs(),
                                SortOrder = song.SortOrder,
                                TitleSort = songTitle.CleanString(true)
                            };
                            newAlbumSongs.Add(s);
                            _totalSongsInserted++;
                        }

                        if (newAlbumSongs.Any())
                        {
                            newAlbum.Songs = newAlbumSongs;
                            dbAlbumsToAdd.Add(newAlbum);
                        }
                    }
                }

                if (dbAlbumsToAdd.Count > 0)
                {
                    try
                    {
                        await scopedContext.Albums.AddRangeAsync(dbAlbumsToAdd, cancellationToken)
                            .ConfigureAwait(false);
                        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException ex) when (IsAlbumUniqueConstraint(ex))
                    {
                        Logger.Warning(ex,
                            "[{JobName}] Duplicate album detected during insert, reloading existing albums.",
                            nameof(LibraryInsertJob));
                        scopedContext.ChangeTracker.Clear();

                        var retryList = new List<dbModels.Album>();
                        foreach (var pendingAlbum in dbAlbumsToAdd)
                        {
                            var existing = await scopedContext.Albums.AsNoTracking()
                                .FirstOrDefaultAsync(x =>
                                        x.ArtistId == pendingAlbum.ArtistId &&
                                        x.NameNormalized == pendingAlbum.NameNormalized &&
                                        x.ReleaseDate == pendingAlbum.ReleaseDate,
                                    cancellationToken)
                                .ConfigureAwait(false);

                            if (existing != null)
                            {
                                _albumsAlreadyInDatabase++;
                                continue;
                            }

                            retryList.Add(pendingAlbum);
                        }

                        if (retryList.Count > 0)
                        {
                            await scopedContext.Albums.AddRangeAsync(retryList, cancellationToken).ConfigureAwait(false);
                            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Unable to insert albums into db.");
                    }

                    _totalAlbumsInserted += dbAlbumsToAdd.Count;
                    UpdateDataMap();

                    var dbContributorsToAdd = new List<dbModels.Contributor>();
                    foreach (var dbAlbum in dbAlbumsToAdd)
                    {
                        var melodeeAlbum = melodeeAlbumsForDirectory.First(x => x.Id == dbAlbum.ApiKey);
                        foreach (var song in melodeeAlbum.Songs ?? [])
                        {
                            var dbSong = dbAlbum.Songs.FirstOrDefault(x => x.ApiKey == song.Id);
                            if (dbSong != null)
                            {
                                dbContributorsToAdd.AddRange(await song.GetContributorsForSong(
                                    _now,
                                    artistService,
                                    dbAlbum.ArtistId,
                                    dbAlbum.Id,
                                    dbSong.Id,
                                    _ignorePerformers,
                                    _ignoreProduction,
                                    _ignorePublishers,
                                    cancellationToken));
                            }
                        }

                        if (!dbAlbum.IsCompilation)
                        {
                            // Some Contributor types are one per song and some are one per album.
                            // For the ones that are one per album, ensure there is only one.
                            var uniqueContributors = new HashSet<(string Name, ContributorType Type)>();
                            var contributorsToRemove = new List<dbModels.Contributor>();

                            foreach (var contributor in dbContributorsToAdd.Where(x =>
                                         x.AlbumId == dbAlbum.Id && x.ContributorTypeValue.RestrictToOnePerAlbum()))
                            {
                                var key = (contributor.ContributorName ?? string.Empty, contributor.ContributorTypeValue);

                                if (!uniqueContributors.Add(key))
                                {
                                    // This is a duplicate, so mark it for removal
                                    contributorsToRemove.Add(contributor);
                                }
                            }

                            foreach (var contributor in contributorsToRemove)
                            {
                                dbContributorsToAdd.Remove(contributor);
                            }
                        }

                        // For all contributors that are type RestrictToOnePerAlbum, if every song has the same contributor, then remove all but first and set the first to the album.
                        var songContributorsToRestrictToOnePerAlbum = dbContributorsToAdd
                            .Where(x => x.AlbumId == dbAlbum.Id && x.ContributorTypeValue.RestrictToOnePerAlbum())
                            .GroupBy(x => x.ContributorName);
                        foreach (var songContributorToRestrictToOnePerAlbum in songContributorsToRestrictToOnePerAlbum
                                     .Where(x => x.Count() == dbAlbum.SongCount))
                        {
                            dbContributorsToAdd.RemoveAll(x =>
                                x.AlbumId == dbAlbum.Id && x.ContributorTypeValue.RestrictToOnePerAlbum() &&
                                x.ContributorName == songContributorToRestrictToOnePerAlbum.Key);
                            var firstContributor = songContributorToRestrictToOnePerAlbum.First();
                            dbContributorsToAdd.Add(new dbModels.Contributor
                            {
                                AlbumId = dbAlbum.Id,
                                ArtistId = firstContributor.ArtistId,
                                ContributorName = firstContributor.ContributorName,
                                ContributorType = firstContributor.ContributorType,
                                CreatedAt = _now,
                                Role = firstContributor.Role,
                                SongId = null
                            });
                        }
                    }

                    if (dbContributorsToAdd.Count > 0)
                    {
                        try
                        {
                            await scopedContext.Contributors.AddRangeAsync(dbContributorsToAdd, cancellationToken)
                                .ConfigureAwait(false);
                            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Unable to insert album contributors into db.");
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{JobName}] [{MethodName}] Processing album [{Album}] song [{Song}]",
                nameof(LibraryInsertJob), nameof(ProcessAlbumsAsync), currentAlbum, currentSong);
        }

        return false;
    }

    private void UpdateDataMap()
    {
        _dataMap[JobMapNameRegistry.Count] =
            _totalAlbumsInserted +
            _totalArtistsInserted +
            _totalSongsInserted;
    }

    private static bool IsAlbumUniqueConstraint(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("Albums", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     For given albums, add to the db album and db song artists.
    /// </summary>
    private async Task<bool> ProcessArtistsAsync(
        dbModels.Library library,
        List<Album> melodeeAlbumsForDirectory, CancellationToken cancellationToken)
    {
        Artist? currentArtist = null;
        Artist? lastAddedArtist = null;

        try
        {
            await using (var scopedContext =
                         await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var artists = melodeeAlbumsForDirectory
                    .Select(x => x.Artist)
                    .Where(x => x.IsValid())
                    .DistinctBy(x => x.NameNormalized)
                    .OrderBy(x => x.Name)
                    .ToArray();

                // Bulk lookup existing artists to reduce database round trips
                var artistNormalizedNames = artists.Select(x => x.NameNormalized).ToArray();
                var artistMusicBrainzIds = artists.Where(x => x.MusicBrainzId.HasValue)
                    .Select(x => x.MusicBrainzId!.Value).ToArray();
                var artistSpotifyIds = artists.Where(x => !string.IsNullOrEmpty(x.SpotifyId))
                    .Select(x => x.SpotifyId!).ToArray();
                var artistItunesIds = artists.Where(x => !string.IsNullOrEmpty(x.ItunesId))
                    .Select(x => x.ItunesId!).ToArray();

                var existingArtists = await scopedContext.Artists
                    .Where(x => artistNormalizedNames.Contains(x.NameNormalized) ||
                               (x.MusicBrainzId.HasValue && artistMusicBrainzIds.Contains(x.MusicBrainzId.Value)) ||
                               (x.SpotifyId != null && artistSpotifyIds.Contains(x.SpotifyId)) ||
                               (x.ItunesId != null && artistItunesIds.Contains(x.ItunesId)))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var existingArtistLookup = new Dictionary<string, dbModels.Artist>();
                foreach (var existingArtist in existingArtists)
                {
                    // Create multiple lookup keys for different matching criteria
                    existingArtistLookup.TryAdd($"name:{existingArtist.NameNormalized}", existingArtist);
                    if (existingArtist.MusicBrainzId.HasValue)
                        existingArtistLookup.TryAdd($"mb:{existingArtist.MusicBrainzId.Value}", existingArtist);
                    if (!string.IsNullOrEmpty(existingArtist.SpotifyId))
                        existingArtistLookup.TryAdd($"sp:{existingArtist.SpotifyId}", existingArtist);
                    if (!string.IsNullOrEmpty(existingArtist.ItunesId))
                        existingArtistLookup.TryAdd($"it:{existingArtist.ItunesId}", existingArtist);
                }

                var dbArtistsToAdd = new List<dbModels.Artist>();
                foreach (var artist in artists)
                {
                    currentArtist = artist;

                    // Try to find existing artist using cached lookup
                    if (artist.MusicBrainzId.HasValue &&
                        existingArtistLookup.TryGetValue($"mb:{artist.MusicBrainzId.Value}", out var existingArtist))
                    {
                        // Found by MusicBrainz ID (highest priority)
                    }
                    else if (!string.IsNullOrEmpty(artist.SpotifyId) &&
                             existingArtistLookup.TryGetValue($"sp:{artist.SpotifyId}", out existingArtist))
                    {
                        // Found by Spotify ID
                    }
                    else if (!string.IsNullOrEmpty(artist.ItunesId) &&
                             existingArtistLookup.TryGetValue($"it:{artist.ItunesId}", out existingArtist))
                    {
                        // Found by iTunes ID
                    }
                    else if (existingArtistLookup.TryGetValue($"name:{artist.NameNormalized}", out existingArtist))
                    {
                        // Found by normalized name
                    }

                    if (existingArtist == null)
                    {
                        lastAddedArtist = artist;

                        var newArtistDirectory = artist.ToDirectoryName(
                            _configuration.GetValue<int>(SettingRegistry.ProcessingMaximumArtistDirectoryNameLength));

                        Logger.Debug(
                            "[{JobName}] Creating new artist for NormalizedName [{Name}] MusicBrainzId [{MusicBrainzId}] with directory [{Directory}] for albums [{Album}]",
                            nameof(LibraryInsertJob),
                            artist.NameNormalized,
                            artist.MusicBrainzId?.ToString(),
                            newArtistDirectory,
                            string.Empty.AddTags(melodeeAlbumsForDirectory
                                .Where(x => x.Artist.NameNormalized == artist.NameNormalized)
                                .Select(x => x.MelodeeDataFileName)));

                        var newArtist = new dbModels.Artist
                        {
                            AmgId = artist.AmgId?.CleanStringAsIs(),
                            ApiKey = artist.Id,
                            CreatedAt = _now,
                            Directory = newArtistDirectory,
                            DiscogsId = artist.DiscogsId?.CleanStringAsIs(),
                            ItunesId = artist.ItunesId?.CleanStringAsIs(),
                            LastFmId = artist.LastFmId?.CleanStringAsIs(),
                            LibraryId = library.Id,
                            MetaDataStatus = (int)MetaDataModelStatus.ReadyToProcess,
                            MusicBrainzId = artist.MusicBrainzId,
                            Name = artist.Name.CleanStringAsIs() ?? artist.Name,
                            NameNormalized = artist.NameNormalized,
                            SortName = artist.SortName?.CleanStringAsIs() ?? artist.SortName,
                            SpotifyId = artist.SpotifyId?.CleanStringAsIs(),
                            WikiDataId = artist.WikiDataId?.CleanStringAsIs()
                        };

                        dbArtistsToAdd.Add(newArtist);

                        // Add to lookup cache for subsequent lookups within this batch
                        existingArtistLookup.TryAdd($"name:{artist.NameNormalized}", newArtist);
                        if (artist.MusicBrainzId.HasValue)
                            existingArtistLookup.TryAdd($"mb:{artist.MusicBrainzId.Value}", newArtist);
                        if (!string.IsNullOrEmpty(artist.SpotifyId))
                            existingArtistLookup.TryAdd($"sp:{artist.SpotifyId}", newArtist);
                        if (!string.IsNullOrEmpty(artist.ItunesId))
                            existingArtistLookup.TryAdd($"it:{artist.ItunesId}", newArtist);
                    }
                }

                if (dbArtistsToAdd.Count > 0)
                {
                    await scopedContext.Artists.AddRangeAsync(dbArtistsToAdd, cancellationToken).ConfigureAwait(false);
                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    _totalArtistsInserted += dbArtistsToAdd.Count;
                    UpdateDataMap();
                }
            }

            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{JobName}] [{MethodName}] error processing artist [{Artist}]", nameof(LibraryInsertJob),
                nameof(ProcessArtistsAsync), serializer.Serialize(lastAddedArtist ?? currentArtist));
        }

        return false;
    }

    private List<FileInfo> GetMelodeeFilesToProcess(
        dbModels.Library library,
        string? scanJustDirectory,
        DateTime lastScanAtUtc)
    {
        var melodeeFilesToProcess = new List<FileInfo>();

        Logger.Debug("[{JobName}] Starting to enumerate melodee.json files in library [{LibraryName}] at path [{Path}]",
            nameof(LibraryInsertJob), library.Name, scanJustDirectory.Nullify() ?? library.Path);

        var searchPath = scanJustDirectory.Nullify() != null
            ? scanJustDirectory!
            : library.Path;

        if (scanJustDirectory.Nullify() != null)
        {
            var scanJustDir = scanJustDirectory!.ToFileSystemDirectoryInfo();
            if (!scanJustDir.Exists())
            {
                Logger.Warning("[{JobName}] Scan directory [{ScanDir}] does not exist, skipping",
                    nameof(LibraryInsertJob), scanJustDirectory);
                return melodeeFilesToProcess;
            }
        }

        Logger.Debug("[{JobName}] Scanning path [{Path}] for melodee.json files modified since [{LastScanAt}]",
            nameof(LibraryInsertJob), searchPath, lastScanAtUtc);

        var scannedCount = 0;
        var matchedCount = 0;

        foreach (var melodeeFile in Directory.EnumerateFiles(searchPath, Album.JsonFileName, SearchOption.AllDirectories))
        {
            scannedCount++;
            var f = new FileInfo(melodeeFile);
            if (f is { Directory: not null, Name.Length: > 3 } && f.LastWriteTimeUtc >= lastScanAtUtc)
            {
                melodeeFilesToProcess.Add(f);
                matchedCount++;
            }

            if (scannedCount % 500 == 0)
            {
                OnProcessingEvent?.Invoke(
                    this,
                    new ProcessingEvent(ProcessingEventType.Processing,
                        nameof(LibraryInsertJob),
                        0,
                        matchedCount,
                        $"Scanning [{library.Name}]... {matchedCount:N0} albums to process ({scannedCount:N0} scanned)"));
            }
        }

        _melodeeFilesDiscovered = scannedCount;
        _melodeeFilesFiltered = matchedCount;

        Logger.Information("[{JobName}] Scanned [{TotalCount}] albums, [{FilteredCount}] need processing in library [{LibraryName}]",
            nameof(LibraryInsertJob), scannedCount, matchedCount, library.Name);

        return melodeeFilesToProcess;
    }

    private async Task<List<Album>> LoadAlbumsInParallelAsync(
        List<FileInfo> melodeeFileInfos,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        var melodeeAlbums = new ConcurrentBag<Album>();

        await Task.WhenAll(melodeeFileInfos.Select(melodeeFileInfo => Task.Run(async () =>
        {
            try
            {
                var allDirectoryFiles =
                    melodeeFileInfo.Directory!.GetFiles("*", SearchOption.TopDirectoryOnly);
                var mediaFiles = allDirectoryFiles.Where(x => FileHelper.IsFileMediaType(x.Extension))
                    .ToArray();
                if (mediaFiles.Length == 0)
                {
                    return;
                }

                try
                {
                    var melodeeAlbum = await Album.DeserializeAndInitializeAlbumAsync(serializer,
                        melodeeFileInfo.FullName, cancellationToken).ConfigureAwait(false);
                    if (melodeeAlbum == null)
                    {
                        Interlocked.Increment(ref _invalidMelodeeFiles);
                        Logger.Warning("[{JobName}] Unable to load melodee file [{MelodeeFile}]",
                            nameof(LibraryInsertJob),
                            melodeeAlbum?.ToString() ?? melodeeFileInfo.FullName);
                        return;
                    }

                    var validationResult = _albumValidator.ValidateAlbum(melodeeAlbum);
                    if (!validationResult.Data.IsValid)
                    {
                        Interlocked.Increment(ref _invalidMelodeeFiles);
                        Logger.Warning(
                            "[{JobName}] Invalid Melodee file [{MelodeeFile}] validation result [{ValidationResult}]",
                            nameof(LibraryInsertJob),
                            melodeeAlbum.ToString(),
                            validationResult.Data.ToString());
                        await bus.SendLocal(
                                new MelodeeAlbumReprocessEvent(melodeeAlbum.Directory.FullName()))
                            .ConfigureAwait(false);
                        if (File.Exists(melodeeAlbum.MelodeeDataFileName!))
                        {
                            File.Delete(melodeeAlbum.MelodeeDataFileName!);
                        }

                        return;
                    }

                    melodeeAlbums.Add(melodeeAlbum);
                }
                catch
                {
                    // The melodee data file won't load.
                    var albumDirectoryToMove = melodeeFileInfo.Directory!.Parent;
                    if (albumDirectoryToMove != null)
                    {
                        var moveDirectoryTo = Path.Combine(stagingPath,
                            albumDirectoryToMove.Name);
                        albumDirectoryToMove.MoveTo(moveDirectoryTo);
                        var p = Path.Combine(moveDirectoryTo, Album.JsonFileName);
                        if (File.Exists(p))
                        {
                            File.Delete(p);
                        }

                        Logger.Warning(
                            "[{JobName}] Invalid Melodee File. Deleted and moved directory [{From}] to staging [{To}]",
                            nameof(LibraryInsertJob),
                            albumDirectoryToMove,
                            moveDirectoryTo);
                        await bus.SendLocal(new MelodeeAlbumReprocessEvent(moveDirectoryTo))
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "[{JobName}] Error processing directory [{Dir}]",
                    nameof(LibraryInsertJob), melodeeFileInfo.Directory);
            }
        }, cancellationToken))).ConfigureAwait(false);

        return melodeeAlbums.ToList();
    }
}
