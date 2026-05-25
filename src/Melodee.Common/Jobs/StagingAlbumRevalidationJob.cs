using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Models;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.SearchEngines;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
///     Periodically re-validates albums in staging that have invalid artists.
/// </summary>
/// <remarks>
///     <para>
///         This job addresses a common scenario where an album is processed before its artist exists
///         in the search engine sources (MusicBrainz, Spotify, etc.). When the artist is later added
///         to these sources, this job re-queries and updates the album, potentially allowing it to
///         pass validation and be automatically moved to storage.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Retrieves the staging library configuration</item>
///             <item>Scans for albums with AlbumNeedsAttentionReasons.HasInvalidArtists or HasUnknownArtist</item>
///             <item>Re-queries the ArtistSearchEngineService for each album's artist</item>
///             <item>If the artist is now found, updates the album with the search result</item>
///             <item>Re-validates the album - if now valid, status becomes AlbumStatus.Ok</item>
///             <item>Saves updated albums so StagingAutoMoveJob can move them to storage</item>
///         </list>
///     </para>
///     <para>
///         This job is NOT part of the media ingestion chain. It runs independently to provide
///         a self-healing mechanism for albums that were processed before their artist data
///         became available in external sources.
///     </para>
///     <para>
///         Skipped conditions:
///         <list type="bullet">
///             <item>Staging library is locked</item>
///             <item>No albums with invalid/unknown artist status in staging</item>
///         </list>
///     </para>
///     <para>
///         Default schedule: Every 6 hours (configurable via jobs.stagingAlbumRevalidation.cronExpression setting).
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
public class StagingAlbumRevalidationJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    AlbumDiscoveryService albumDiscoveryService,
    ArtistSearchEngineService artistSearchEngineService,
    ISerializer serializer,
    IFileSystemService fileSystemService) : JobBase(logger, configurationFactory)
{
    /// <summary>
    ///     This is raised when a Log event happens to return activity to caller.
    /// </summary>
    public event EventHandler<ProcessingEvent>? OnProcessingEvent;

    /// <summary>
    ///     Returns whether an album has enough useful artist information to spend a revalidation lookup on it.
    /// </summary>
    public static bool CanAttemptArtistRevalidation(Album album)
    {
        if (album.Artist.IsValid())
        {
            return true;
        }

        if (album.Artist.Name.Nullify() == null)
        {
            return false;
        }

        if (album.StatusReasons.HasFlag(AlbumNeedsAttentionReasons.ArtistNameHasUnwantedText))
        {
            return false;
        }

        return album.StatusReasons.HasFlag(AlbumNeedsAttentionReasons.HasInvalidArtists) ||
               album.StatusReasons.HasFlag(AlbumNeedsAttentionReasons.HasUnknownArtist);
    }

    public override async Task Execute(IJobExecutionContext context)
    {
        var startTicks = Stopwatch.GetTimestamp();
        var albumsRevalidated = 0;
        var albumsNowValid = 0;
        var albumsSkippedRevalidation = 0;
        var dataMap = context.JobDetail.JobDataMap;
        var sharedRunContext = context.MergedJobDataMap.ContainsKey(MelodeeJobExecutionContext.DirectoryRunContext)
            ? context.MergedJobDataMap[MelodeeJobExecutionContext.DirectoryRunContext] as DirectoryRunContext
            : null;
        using var localRunContext = sharedRunContext is null ? new DirectoryRunContext() : null;
        var activeRunContext = sharedRunContext ?? localRunContext!;

        try
        {
            Logger.Debug("[{JobName}] Starting staging album revalidation", nameof(StagingAlbumRevalidationJob));

            var configuration = await ConfigurationFactory.GetConfigurationAsync(context.CancellationToken).ConfigureAwait(false);
            var albumValidator = new AlbumValidator(configuration);

            var stagingLibraryResult = await libraryService.GetStagingLibraryAsync(context.CancellationToken).ConfigureAwait(false);
            if (!stagingLibraryResult.IsSuccess || stagingLibraryResult.Data == null)
            {
                Logger.Warning("[{JobName}] Unable to get staging library, skipping processing", nameof(StagingAlbumRevalidationJob));
                return;
            }

            var stagingLibrary = stagingLibraryResult.Data;
            if (stagingLibrary.IsLocked)
            {
                Logger.Warning("[{JobName}] Staging library is locked, skipping processing", nameof(StagingAlbumRevalidationJob));
                return;
            }

            await albumDiscoveryService.InitializeAsync(configuration, context.CancellationToken).ConfigureAwait(false);
            await artistSearchEngineService.InitializeAsync(configuration, context.CancellationToken).ConfigureAwait(false);

            var stagingDirectoryInfo = new FileSystemDirectoryInfo
            {
                Path = stagingLibrary.Path,
                Name = stagingLibrary.Name
            };

            var albumsResult = await albumDiscoveryService.AllMelodeeAlbumDataFilesForDirectoryAsync(
                stagingDirectoryInfo,
                context.CancellationToken).ConfigureAwait(false);

            if (!albumsResult.IsSuccess || albumsResult.Data == null)
            {
                Logger.Debug("[{JobName}] No albums found in staging", nameof(StagingAlbumRevalidationJob));
                return;
            }

            var albumsNeedingRevalidation = albumsResult.Data
                .Where(album => album.StatusReasons.HasFlag(AlbumNeedsAttentionReasons.HasInvalidArtists) ||
                               album.StatusReasons.HasFlag(AlbumNeedsAttentionReasons.HasUnknownArtist))
                .ToArray();

            if (albumsNeedingRevalidation.Length == 0)
            {
                Logger.Debug("[{JobName}] No albums with invalid artists found in staging", nameof(StagingAlbumRevalidationJob));
                return;
            }

            Logger.Information("[{JobName}] Found [{Count}] albums with invalid artists to revalidate",
                nameof(StagingAlbumRevalidationJob),
                albumsNeedingRevalidation.Length);

            OnProcessingEvent?.Invoke(
                this,
                new ProcessingEvent(ProcessingEventType.Start,
                    nameof(StagingAlbumRevalidationJob),
                    albumsNeedingRevalidation.Length,
                    0,
                    $"Found [{albumsNeedingRevalidation.Length}] albums to revalidate"));

            foreach (var album in albumsNeedingRevalidation)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    if (!CanAttemptArtistRevalidation(album))
                    {
                        albumsSkippedRevalidation++;
                        activeRunContext.RecordAlbumSkippedRevalidation();
                        Logger.Debug(
                            "[{JobName}] Skipping album [{Album}] revalidation because artist data is not searchable: [{Reasons}]",
                            nameof(StagingAlbumRevalidationJob),
                            album.AlbumTitle(),
                            album.StatusReasons);
                        continue;
                    }

                    var shouldRevalidateAlbum = album.Artist.IsValid();

                    if (!shouldRevalidateAlbum)
                    {
                        var searchRequest = album.Artist.ToArtistQuery([
                            new KeyValue((album.AlbumYear() ?? 0).ToString(),
                                album.AlbumTitle().ToNormalizedString() ?? album.AlbumTitle())
                        ]);

                        var artistSearchResult = await artistSearchEngineService.DoSearchAsync(
                            searchRequest,
                            1,
                            activeRunContext,
                            bypassNegativeCache: true,
                            context.CancellationToken).ConfigureAwait(false);

                        if (artistSearchResult.IsSuccess && artistSearchResult.Data.Any())
                        {
                            var artistFromSearch = artistSearchResult.Data.OrderByDescending(x => x.Rank).FirstOrDefault();
                            if (artistFromSearch != null)
                            {
                                album.Artist = album.Artist with
                                {
                                    AmgId = album.Artist.AmgId ?? artistFromSearch.AmgId,
                                    ArtistDbId = album.Artist.ArtistDbId ?? artistFromSearch.Id,
                                    DiscogsId = album.Artist.DiscogsId ?? artistFromSearch.DiscogsId,
                                    ItunesId = album.Artist.ItunesId ?? artistFromSearch.ItunesId,
                                    LastFmId = album.Artist.LastFmId ?? artistFromSearch.LastFmId,
                                    MusicBrainzId = album.Artist.MusicBrainzId ?? artistFromSearch.MusicBrainzId,
                                    Name = album.Artist.Name.Nullify() ?? artistFromSearch.Name,
                                    NameNormalized = album.Artist.NameNormalized.Nullify() ??
                                                    artistFromSearch.Name.ToNormalizedString() ??
                                                    artistFromSearch.Name,
                                    OriginalName = artistFromSearch.Name != album.Artist.Name ? album.Artist.Name : null,
                                    SearchEngineResultUniqueId = album.Artist.SearchEngineResultUniqueId is null or < 1
                                        ? artistFromSearch.UniqueId
                                        : album.Artist.SearchEngineResultUniqueId,
                                    SortName = album.Artist.SortName.Nullify() ?? artistFromSearch.SortName,
                                    SpotifyId = album.Artist.SpotifyId ?? artistFromSearch.SpotifyId,
                                    WikiDataId = album.Artist.WikiDataId ?? artistFromSearch.WikiDataId
                                };
                                shouldRevalidateAlbum = true;
                            }
                        }
                    }

                    if (shouldRevalidateAlbum)
                    {
                        var validationResult = albumValidator.ValidateAlbum(album);
                        album.ValidationMessages = validationResult.Data.Messages ?? [];
                        album.Status = validationResult.Data.AlbumStatus;
                        album.StatusReasons = validationResult.Data.AlbumStatusReasons;
                        album.Modified = DateTimeOffset.UtcNow;

                        var jsonPath = fileSystemService.CombinePath(album.Directory.FullName(), Album.JsonFileName);
                        var serialized = serializer.Serialize(album);
                        await fileSystemService.WriteAllBytesAsync(
                            jsonPath,
                            System.Text.Encoding.UTF8.GetBytes(serialized ?? string.Empty),
                            context.CancellationToken).ConfigureAwait(false);

                        albumsRevalidated++;

                        if (album.Status == AlbumStatus.Ok)
                        {
                            albumsNowValid++;
                            Logger.Information(
                                "[{JobName}] Album [{Album}] is now valid after artist revalidation",
                                nameof(StagingAlbumRevalidationJob),
                                album.AlbumTitle());
                        }
                        else
                        {
                            Logger.Debug(
                                "[{JobName}] Album [{Album}] revalidated but still invalid: [{Reasons}]",
                                nameof(StagingAlbumRevalidationJob),
                                album.AlbumTitle(),
                                album.StatusReasons);
                        }
                    }

                    OnProcessingEvent?.Invoke(
                        this,
                        new ProcessingEvent(ProcessingEventType.Processing,
                            nameof(StagingAlbumRevalidationJob),
                            albumsNeedingRevalidation.Length,
                            albumsRevalidated,
                            $"Revalidated [{albumsRevalidated}/{albumsNeedingRevalidation.Length}]"));
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex,
                        "[{JobName}] Error revalidating album [{Album}]",
                        nameof(StagingAlbumRevalidationJob),
                        album.AlbumTitle());
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(startTicks);

            dataMap[JobMapNameRegistry.AlbumsRevalidated] = albumsRevalidated;
            dataMap[JobMapNameRegistry.AlbumsNowValid] = albumsNowValid;

            context.Result = new ScanStepResult(
                AlbumsRevalidated: albumsRevalidated,
                AlbumsNowValid: albumsNowValid,
                AlbumsSkippedRevalidation: albumsSkippedRevalidation);

            OnProcessingEvent?.Invoke(
                this,
                new ProcessingEvent(ProcessingEventType.Stop,
                    nameof(StagingAlbumRevalidationJob),
                    albumsNeedingRevalidation.Length,
                    albumsRevalidated,
                    $"Revalidated [{albumsRevalidated}] albums, [{albumsNowValid}] now valid, skipped [{albumsSkippedRevalidation}]"));

            Logger.Information(
                "[{JobName}] Completed in [{Elapsed}]ms. Revalidated [{Revalidated}] albums, [{NowValid}] now valid and ready to move, skipped [{Skipped}]",
                nameof(StagingAlbumRevalidationJob),
                elapsed.TotalMilliseconds,
                albumsRevalidated,
                albumsNowValid,
                albumsSkippedRevalidation);
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{JobName}] Processing Exception", nameof(StagingAlbumRevalidationJob));
        }
    }
}
