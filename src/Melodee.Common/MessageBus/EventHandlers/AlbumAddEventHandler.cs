using JetBrains.Annotations;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Metadata;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Handlers;
using Rebus.Pipeline;
using Serilog;
using Serilog.Events;
using SerilogTimings;

namespace Melodee.Common.MessageBus.EventHandlers;

/// <summary>
///     Add a new album from album files.
/// </summary>
[UsedImplicitly]
public sealed class AlbumAddEventHandler(
    ILogger logger,
    ISerializer serializer,
    IMessageContext messageContext,
    MelodeeMetadataMaker melodeeMetadataMaker,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ArtistService artistService,
    LibraryService libraryService,
    RequestAutoCompletionService requestAutoCompletionService
) : IHandleMessages<AlbumAddEvent>
{
    public async Task Handle(AlbumAddEvent message)
    {
        var cancellationToken = messageContext.IncomingStepContext.Load<CancellationToken>();

        using (Operation.At(LogEventLevel.Debug)
                   .Time("[{Name}] Handle [{id}]", nameof(AlbumAddEventHandler), message.ToString()))
        {
            try
            {
                await using (var scopedContext =
                             await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

                    // Validate configuration
                    IMelodeeConfiguration configuration;
                    try
                    {
                        configuration = await configurationFactory.GetConfigurationAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "[{Name}] Failed to load configuration", nameof(AlbumAddEventHandler));
                        return;
                    }

                    var ignorePerformers = MelodeeConfiguration.FromSerializedJsonArrayNormalized(
                        configuration.Configuration[SettingRegistry.ProcessingIgnoredPerformers], serializer);
                    var ignorePublishers = MelodeeConfiguration.FromSerializedJsonArrayNormalized(
                        configuration.Configuration[SettingRegistry.ProcessingIgnoredPublishers], serializer);
                    var ignoreProduction = MelodeeConfiguration.FromSerializedJsonArrayNormalized(
                        configuration.Configuration[SettingRegistry.ProcessingIgnoredProduction], serializer);

                    // Validate artist exists and is accessible
                    Artist? dbArtist;
                    try
                    {
                        dbArtist = await scopedContext
                            .Artists
                            .Include(x => x.Library)
                            .Include(x => x.Contributors)
                            .Include(x => x.Albums)
                            .FirstOrDefaultAsync(x => x.Id == message.ArtistId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "[{Name}] Database error while fetching artist with id [{ArtistId}]",
                            nameof(AlbumAddEventHandler), message.ArtistId);
                        return;
                    }

                    if (dbArtist == null)
                    {
                        logger.Warning("[{Name}] Unable to find artist with id [{ArtistId}] in database.",
                            nameof(AlbumAddEventHandler), message.ArtistId);
                        return;
                    }

                    if (dbArtist.IsLocked || dbArtist.Library.IsLocked)
                    {
                        logger.Warning("[{Name}] Library or artist is locked. Skipping rescan request [{ArtistDir}].",
                            nameof(AlbumAddEventHandler),
                            message.AlbumDirectory);
                        return;
                    }

                    // Validate album directory
                    if (string.IsNullOrWhiteSpace(message.AlbumDirectory))
                    {
                        logger.Warning("[{Name}] Album directory is null or empty", nameof(AlbumAddEventHandler));
                        return;
                    }

                    // Process album metadata with better error handling
                    var processResult = await melodeeMetadataMaker
                        .MakeMetadataFileAsync(message.AlbumDirectory, false, false, cancellationToken).ConfigureAwait(false);
                    if (!processResult.IsSuccess || processResult.Data == null)
                    {
                        logger.Warning("[{Name}] Unable to rebuild media in directory [{DirName}]. Error: [{Error}]",
                            nameof(AlbumAddEventHandler), message.AlbumDirectory, processResult.Messages?.FirstOrDefault() ?? "Unknown error");
                        return;
                    }

                    var melodeeAlbum = processResult.Data!;

                    // Validate required album data
                    var albumTitle = melodeeAlbum.AlbumTitle()?.CleanStringAsIs();
                    if (string.IsNullOrWhiteSpace(albumTitle))
                    {
                        logger.Warning("[{Name}] Album title is required but missing for directory [{DirName}]",
                            nameof(AlbumAddEventHandler), message.AlbumDirectory);
                        return;
                    }

                    var nameNormalized = albumTitle.ToNormalizedString() ?? albumTitle;
                    if (string.IsNullOrWhiteSpace(nameNormalized))
                    {
                        logger.Warning("Album [{Album}] has invalid Album title, unable to generate NameNormalized.",
                            melodeeAlbum);
                        return;
                    }

                    var albumYear = melodeeAlbum.AlbumYear();
                    if (albumYear == null)
                    {
                        logger.Warning("[{Name}] Album year is required but missing for album [{Album}]",
                            nameof(AlbumAddEventHandler), albumTitle);
                        return;
                    }

                    var newAlbum = new Album
                    {
                        AlbumStatus = (short)melodeeAlbum.Status,
                        AlbumType = SafeParser.ToNumber<short>(melodeeAlbum.AlbumType),
                        AmgId = melodeeAlbum.AmgId,
                        ApiKey = melodeeAlbum.Id,
                        Artist = dbArtist,
                        CreatedAt = now,
                        Directory = melodeeAlbum.AlbumDirectoryName(configuration.Configuration),
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
                        ReleaseDate = SafeParser.ToLocalDate(albumYear.Value),
                        SongCount = SafeParser.ToNumber<short>(melodeeAlbum.Songs?.Count() ?? 0),
                        SortName = configuration.RemoveUnwantedArticles(albumTitle.CleanString(true)),
                        SpotifyId = melodeeAlbum.SpotifyId,
                        WikiDataId = melodeeAlbum.WikiDataId
                    };

                    // Check for duplicates
                    if (dbArtist.Albums.Any(x => x.NameNormalized == nameNormalized ||
                                                 (x.MusicBrainzId != null &&
                                                  x.MusicBrainzId == newAlbum.MusicBrainzId) ||
                                                 (x.SpotifyId != null && x.SpotifyId == newAlbum.SpotifyId)))
                    {
                        logger.Warning("For artist [{Artist}] found duplicate album [{Album}]", dbArtist, newAlbum);
                        try
                        {
                            var duplicatePrefix = configuration.GetValue<string>(SettingRegistry.ProcessingDuplicateAlbumPrefix) ?? "__duplicate_";
                            melodeeAlbum.Directory.AppendPrefix(duplicatePrefix);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "[{Name}] Failed to rename duplicate album directory", nameof(AlbumAddEventHandler));
                        }
                        return;
                    }

                    logger.Debug(
                        "[{JobName}] Creating new album for ArtistId [{ArtistId}] Id [{Id}] NormalizedName [{Name}] Directory [{Directory}]",
                        nameof(AlbumAddEventHandler),
                        dbArtist.Id,
                        melodeeAlbum.Id,
                        nameNormalized,
                        melodeeAlbum.Directory.FullName());

                    // Process songs with validation
                    var newAlbumSongs = new List<Song>();
                    if (melodeeAlbum.Songs?.Any() == true)
                    {
                        foreach (var song in melodeeAlbum.Songs)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                logger.Information("[{Name}] Cancellation requested, stopping song processing", nameof(AlbumAddEventHandler));
                                return;
                            }

                            try
                            {
                                var mediaFile = song.File.ToFileInfo(melodeeAlbum.Directory);
                                if (!mediaFile.Exists)
                                {
                                    logger.Warning("[{JobName}] Unable to find media file [{FileName}].",
                                        nameof(AlbumAddEventHandler),
                                        mediaFile.FullName);
                                    continue; // Skip this song but continue processing others
                                }

                                var songTitle = song.Title()?.CleanStringAsIs();
                                if (string.IsNullOrWhiteSpace(songTitle))
                                {
                                    logger.Warning("[{Name}] Song title is required but missing for file [{FileName}]",
                                        nameof(AlbumAddEventHandler), mediaFile.Name);
                                    continue;
                                }

                                var songDuration = song.Duration();
                                if (songDuration == null)
                                {
                                    logger.Warning("[{Name}] Song duration is required but missing for file [{FileName}]",
                                        nameof(AlbumAddEventHandler), mediaFile.Name);
                                    continue;
                                }

                                var mediaFileHash = Crc32.Calculate(mediaFile);
                                var s = new Song
                                {
                                    AlbumId = newAlbum.Id,
                                    ApiKey = song.Id,
                                    BitDepth = song.BitDepth(),
                                    BitRate = song.BitRate(),
                                    BPM = song.MetaTagValue<int>(MetaTagIdentifier.Bpm),
                                    ContentType = song.ContentType(),
                                    CreatedAt = now,
                                    Duration = songDuration.Value,
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
                                        ?.CleanStringAsIs() ?? song.MetaTagValue<string>(MetaTagIdentifier.SynchronisedLyrics)
                                        ?.CleanStringAsIs(),
                                    MusicBrainzId = song.MetaTagValue<Guid?>(MetaTagIdentifier.MusicBrainzId),
                                    PartTitles = song.MetaTagValue<string>(MetaTagIdentifier.SubTitle)?.CleanStringAsIs(),
                                    SortOrder = song.SortOrder,
                                    TitleSort = songTitle.CleanString(true)
                                };
                                newAlbumSongs.Add(s);
                            }
                            catch (Exception e)
                            {
                                logger.Error(e, "[{Name}] Failed to process song [{SongId}] for album [{Album}]",
                                    nameof(AlbumAddEventHandler), song.Id, albumTitle);
                                // Continue processing other songs
                            }
                        }
                    }

                    if (!newAlbumSongs.Any())
                    {
                        logger.Warning("[{Name}] No valid songs found for album [{Album}]",
                            nameof(AlbumAddEventHandler), albumTitle);
                        return;
                    }

                    // Use transaction for database operations
                    using var transaction = await scopedContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        newAlbum.Songs = newAlbumSongs;
                        newAlbum.SongCount = SafeParser.ToNumber<short>(newAlbumSongs.Count);

                        scopedContext.Albums.Add(newAlbum);
                        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                        // Process contributors
                        var dbContributorsToAdd = new List<Contributor>();
                        foreach (var song in melodeeAlbum.Songs ?? [])
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                logger.Information("[{Name}] Cancellation requested, stopping contributor processing", nameof(AlbumAddEventHandler));
                                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            try
                            {
                                var dbSong = newAlbum.Songs.FirstOrDefault(x => x.ApiKey == song.Id);
                                if (dbSong != null)
                                {
                                    var contributorsForSong = await song.GetContributorsForSong(
                                        now,
                                        artistService,
                                        newAlbum.ArtistId,
                                        newAlbum.Id,
                                        dbSong.Id,
                                        ignorePerformers,
                                        ignoreProduction,
                                        ignorePublishers,
                                        cancellationToken);

                                    foreach (var cfs in contributorsForSong)
                                    {
                                        if (!dbContributorsToAdd.Any(x => x.AlbumId == cfs.AlbumId &&
                                                                          (x.ArtistId == cfs.ArtistId ||
                                                                           x.ContributorName == cfs.ContributorName) &&
                                                                          x.MetaTagIdentifier == cfs.MetaTagIdentifier))
                                        {
                                            dbContributorsToAdd.Add(cfs);
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                logger.Error(e, "[{Name}] Failed to process contributors for song [{SongId}]",
                                    nameof(AlbumAddEventHandler), song.Id);
                                // Continue processing other songs
                            }
                        }

                        if (dbContributorsToAdd.Count > 0)
                        {
                            try
                            {
                                await scopedContext.Contributors
                                    .AddRangeAsync(dbContributorsToAdd, cancellationToken).ConfigureAwait(false);
                                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                logger.Error(e, "[{Name}] Unable to insert album contributors into db. Rolling back transaction.", nameof(AlbumAddEventHandler));
                                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                                return;
                            }
                        }

                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        logger.Information("[{Name}] Successfully added album [{Album}] with [{SongCount}] songs and [{ContributorCount}] contributors",
                            nameof(AlbumAddEventHandler), albumTitle, newAlbumSongs.Count, dbContributorsToAdd.Count);

                        // Process request auto-completion
                        try
                        {
                            await requestAutoCompletionService.ProcessAlbumAddedAsync(newAlbum, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "[{Name}] Failed to process request auto-completion for album [{Album}]",
                                nameof(AlbumAddEventHandler), albumTitle);
                        }

                        // Update aggregates after successful commit
                        if (!message.IsFromArtistScan)
                        {
                            try
                            {
                                await libraryService
                                    .UpdateAggregatesAsync(newAlbum.Artist.Library.Id, cancellationToken)
                                    .ConfigureAwait(false);
                                await artistService.ClearCacheAsync(newAlbum.Artist, cancellationToken);
                            }
                            catch (Exception e)
                            {
                                logger.Error(e, "[{Name}] Failed to update aggregates or clear cache for album [{Album}]",
                                    nameof(AlbumAddEventHandler), albumTitle);
                                // Don't fail the entire operation for this
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "[{Name}] Database transaction failed for album [{Album}]. Rolling back.",
                            nameof(AlbumAddEventHandler), albumTitle);
                        try
                        {
                            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception rollbackEx)
                        {
                            logger.Error(rollbackEx, "[{Name}] Failed to rollback transaction", nameof(AlbumAddEventHandler));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.Information("[{Name}] Operation was cancelled", nameof(AlbumAddEventHandler));
            }
            catch (Exception e)
            {
                logger.Error(e, "[{Name}] Unexpected error while processing album add event for directory [{Directory}]",
                    nameof(AlbumAddEventHandler), message.AlbumDirectory);
            }
        }
    }
}
