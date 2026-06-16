using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Jobs;
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
using Album = Melodee.Common.Models.Album;
using Song = Melodee.Common.Data.Models.Song;

namespace Melodee.Common.MessageBus.EventHandlers;

/// <summary>
///     Rescan an existing album, syncing database records to album files.
/// </summary>
public sealed class AlbumRescanEventHandler(
    ILogger logger,
    ISerializer serializer,
    IMessageContext messageContext,
    MelodeeMetadataMaker melodeeMetadataMaker,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ArtistService artistService,
    AlbumService albumService,
    LibraryService libraryService
) : IHandleMessages<AlbumRescanEvent>
{
    public async Task Handle(AlbumRescanEvent message)
    {
        var cancellationToken = messageContext.IncomingStepContext.Load<CancellationToken>();

        using (Operation.At(LogEventLevel.Debug)
                   .Time("[{Name}] Handle [{id}]", nameof(AlbumRescanEventHandler), message.ToString()))
        {
            await using (var scopedContext =
                         await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

                var dbAlbum = await scopedContext
                    .Albums
                    .Include(x => x.Contributors)
                    .Include(x => x.Artist).ThenInclude(x => x.Library)
                    .Include(x => x.Songs)
                    .FirstOrDefaultAsync(x => x.Id == message.AlbumId, cancellationToken)
                    .ConfigureAwait(false);

                if (dbAlbum == null)
                {
                    logger.Warning("[{Name}] Unable to find album with id [{AlbumId}] in database.",
                        nameof(AlbumRescanEventHandler), message.AlbumId);
                }
                else
                {
                    if (dbAlbum.IsLocked || dbAlbum.Artist.IsLocked)
                    {
                        logger.Warning("[{Name}] Artist or album is locked. Skipping rescan request [{AlbumDir}].",
                            nameof(AlbumRescanEventHandler),
                            message.AlbumDirectory);
                        return;
                    }

                    var albumDirectory = message.AlbumDirectory.ToFileSystemDirectoryInfo();

                    // Ensure directory for album exists
                    if (!albumDirectory.Exists())
                    {
                        scopedContext.Albums.Remove(dbAlbum);
                        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        await libraryService.UpdateAggregatesAsync(dbAlbum.Artist.Library.Id, cancellationToken)
                            .ConfigureAwait(false);
                        logger.Warning("[{Name}] Album directory [{AlbumDir}] does not exist. Deleted album.",
                            nameof(AlbumRescanEventHandler), message.AlbumDirectory);
                        return;
                    }

                    var processResult = await melodeeMetadataMaker
                        .MakeMetadataFileAsync(message.AlbumDirectory, false, false, cancellationToken).ConfigureAwait(false);
                    if (!processResult.IsSuccess)
                    {
                        logger.Warning("[{Name}] Unable to rebuild media in directory [{DirName}].",
                            nameof(AlbumRescanEventHandler), message.AlbumDirectory);
                    }


                    // Ensure all songs on dbAlbum exist
                    foreach (var dbSong in dbAlbum.Songs.ToArray())
                    {
                        var songPath = Path.Combine(message.AlbumDirectory, dbSong.FileName);
                        if (!File.Exists(songPath))
                        {
                            logger.Warning("[{Name}] Removing song [{SongName}] from album [{AlbumName}].",
                                nameof(AlbumRescanEventHandler),
                                dbSong.FileName,
                                dbAlbum.Name);
                            dbAlbum.Songs.Remove(dbSong);
                            dbAlbum.LastUpdatedAt = now;
                        }
                    }

                    var melodeeFileInfo = albumDirectory.AllFileInfos(Album.JsonFileName).FirstOrDefault();
                    if (melodeeFileInfo == null)
                    {
                        logger.Warning("[{Name}] Unable to find album metadata file in directory [{AlbumDir}].",
                            nameof(AlbumRescanEventHandler),
                            message.AlbumDirectory);
                        return;
                    }

                    var melodeeAlbum = await Album
                        .DeserializeAndInitializeAlbumAsync(serializer, melodeeFileInfo.FullName, cancellationToken)
                        .ConfigureAwait(false);
                    if (melodeeAlbum == null)
                    {
                        logger.Warning("[{JobName}] Unable to load melodee file [{MelodeeFile}]",
                            nameof(LibraryInsertJob),
                            melodeeAlbum?.ToString() ?? melodeeFileInfo.FullName);
                        return;
                    }

                    var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var ignorePerformers = MelodeeConfiguration.FromSerializedJsonArrayNormalized(
                        configuration.Configuration[SettingRegistry.ProcessingIgnoredPerformers], serializer);
                    var ignorePublishers = MelodeeConfiguration.FromSerializedJsonArrayNormalized(
                        configuration.Configuration[SettingRegistry.ProcessingIgnoredPublishers], serializer);
                    var ignoreProduction = MelodeeConfiguration.FromSerializedJsonArrayNormalized(
                        configuration.Configuration[SettingRegistry.ProcessingIgnoredProduction], serializer);

                    // Get all songs in directory for album, add any missing, remove any on album not in the directory
                    foreach (var mediaFile in albumDirectory.AllMediaTypeFileInfos())
                    {
                        var mediaFileHash = Crc32.Calculate(mediaFile);
                        var melodeeSong = melodeeAlbum.Songs?.FirstOrDefault(x => x.File.Name == mediaFile.Name);
                        if (melodeeSong == null)
                        {
                            logger.Warning("[{Name}] Unable to find melodee song with name [{Name}] in album metadata.",
                                nameof(AlbumRescanEventHandler),
                                mediaFile.Name);
                            return;
                        }

                        var songTitle = melodeeSong.Title()?.CleanStringAsIs() ??
                                        throw new Exception("Song title is required.");

                        var albumDbSong = dbAlbum.Songs.FirstOrDefault(x => x.FileName == mediaFile.Name);
                        if (albumDbSong == null)
                        {
                            var dbSong = new Song
                            {
                                AlbumId = dbAlbum.Id,
                                ApiKey = melodeeSong.Id,
                                BitDepth = melodeeSong.BitDepth(),
                                BitRate = melodeeSong.BitRate(),
                                BPM = melodeeSong.MetaTagValue<int>(MetaTagIdentifier.Bpm),
                                ContentType = melodeeSong.ContentType(),
                                CreatedAt = now,
                                Duration = melodeeSong.Duration() ?? throw new Exception("Song duration is required."),
                                FileHash = mediaFileHash,
                                FileName = mediaFile.Name,
                                FileSize = mediaFile.Length,
                                SamplingRate = melodeeSong.SamplingRate(),
                                Title = songTitle,
                                TitleNormalized = songTitle.ToNormalizedString() ?? songTitle,
                                SongNumber = melodeeSong.SongNumber(),
                                ChannelCount = melodeeSong.ChannelCount(),
                                Genres =
                                    (melodeeSong.Genre()?.Nullify() ?? melodeeAlbum.Genre()?.Nullify())?.Split('/'),
                                IsVbr = melodeeSong.IsVbr(),
                                Lyrics =
                                    melodeeSong.MetaTagValue<string>(MetaTagIdentifier.UnsynchronisedLyrics)
                                        ?.CleanStringAsIs() ?? melodeeSong
                                        .MetaTagValue<string>(MetaTagIdentifier.SynchronisedLyrics)?.CleanStringAsIs(),
                                MusicBrainzId = melodeeSong.MetaTagValue<Guid?>(MetaTagIdentifier.MusicBrainzId),
                                PartTitles = melodeeSong.MetaTagValue<string>(MetaTagIdentifier.SubTitle)
                                    ?.CleanStringAsIs(),
                                SortOrder = melodeeSong.SortOrder,
                                TitleSort = songTitle.CleanString(true)
                            };
                            if (dbAlbum.Songs.Any(x => x.SongNumber == dbSong.SongNumber))
                            {
                                logger.Warning(
                                    "[{Name}] Duplicate song number [{SongNumber}] found in album [{AlbumName}].",
                                    nameof(AlbumRescanEventHandler),
                                    dbSong.SongNumber,
                                    dbAlbum.Name);
                                return;
                            }

                            dbAlbum.Songs.Add(dbSong);
                            logger.Information("[{Name}] Adding song [{SongName}] to album [{AlbumName}].",
                                nameof(AlbumRescanEventHandler),
                                mediaFile.Name,
                                dbAlbum.Name);
                        }
                        else
                        {
                            // Update song details with potentially updated media information
                            albumDbSong.BPM = melodeeSong.MetaTagValue<int>(MetaTagIdentifier.Bpm);
                            albumDbSong.BitDepth = melodeeSong.BitDepth();
                            albumDbSong.BitRate = melodeeSong.BitRate();
                            albumDbSong.ContentType = melodeeSong.ContentType();
                            albumDbSong.Duration = melodeeSong.Duration() ??
                                                   throw new Exception("Song duration is required.");
                            albumDbSong.FileHash = mediaFileHash;
                            albumDbSong.FileName = mediaFile.Name;
                            albumDbSong.FileSize = mediaFile.Length;
                            albumDbSong.LastUpdatedAt = now;
                            albumDbSong.SamplingRate = melodeeSong.SamplingRate();
                            albumDbSong.SongNumber = melodeeSong.SongNumber();
                            albumDbSong.Title = songTitle;
                            albumDbSong.TitleNormalized = songTitle.ToNormalizedString() ?? songTitle;
                        }
                    }

                    var imageCount = albumDirectory.AllFileImageTypeFileInfos().Count();
                    if (imageCount != dbAlbum.ImageCount)
                    {
                        dbAlbum.ImageCount = imageCount;
                    }

                    dbAlbum.SongCount = SafeParser.ToNumber<short>(dbAlbum.Songs.Count);
                    dbAlbum.LastUpdatedAt = now;
                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    var dbContributorsToAdd = new List<Contributor>();
                    foreach (var dbSong in dbAlbum.Songs)
                    {
                        var melodeeSong = melodeeAlbum.Songs?.FirstOrDefault(x => x.File.Name == dbSong.FileName);
                        if (melodeeSong != null)
                        {
                            var contributorsForSong = await melodeeSong.GetContributorsForSong(
                                now,
                                artistService,
                                dbAlbum.ArtistId,
                                dbAlbum.Id,
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

                            dbSong.Contributors.Clear();
                        }
                        else
                        {
                            logger.Warning(
                                "[{Name}] Unable to find Melodee Song for DbSong song number [{SongNumber}] filename [{FileName}].",
                                nameof(AlbumRescanEventHandler),
                                dbSong.SongNumber,
                                dbSong.FileName);
                        }
                    }

                    if (dbContributorsToAdd.Count != 0)
                    {
                        scopedContext.Contributors.AddRange(dbContributorsToAdd);
                        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!message.IsFromArtistScan)
                    {
                        await libraryService.UpdateAggregatesAsync(dbAlbum.Artist.Library.Id, cancellationToken)
                            .ConfigureAwait(false);
                        await artistService.ClearCacheAsync(dbAlbum.Artist, cancellationToken);
                    }

                    albumService.ClearCache(dbAlbum);
                }
            }
        }
    }
}
