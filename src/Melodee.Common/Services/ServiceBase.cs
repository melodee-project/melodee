using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models.DTOs;
using Melodee.Common.Enums;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Artist = Melodee.Common.Models.Artist;
using Directory = System.IO.Directory;

namespace Melodee.Common.Services;

public abstract class ServiceBase
{
    public const string CacheName = "melodee";

    protected static TimeSpan DefaultCacheDuration = TimeSpan.FromDays(1);

    /// <summary>
    ///     This is required for Mocking in unit tests.
    /// </summary>
    protected ServiceBase()
    {
    }

    protected ServiceBase(
        ILogger logger,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    protected ILogger Logger { get; } = null!;
    protected ICacheManager CacheManager { get; } = null!;
    protected IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = null!;

    protected async Task ProcessExistingDirectoryMoveMergeAsync(
        IMelodeeConfiguration configuration,
        ISerializer serializer,
        IImageProcessor imageProcessor,
        Album albumToMove,
        string existingAlbumPath,
        CancellationToken cancellationToken = default)
    {
        var albumToMoveDir = albumToMove.Directory;
        var existingDir = new DirectoryInfo(existingAlbumPath);

        Logger.Debug(
            "[{ServiceName}] :\u2552: processing existing directory merge from [{ExistingDirectoryPath}] to [{NewDirectoryPath}]",
            nameof(LibraryService),
            albumToMove.Directory.Name, existingDir.Name);

        if (albumToMove.Images?.Any() == true)
        {
            var existingImages = ImageHelper.ImageFilesInDirectory(existingDir.FullName, SearchOption.TopDirectoryOnly)
                .ToList();
            var existingImagesCrc = existingImages.Select(async x =>
            {
                var crc = Crc32.Calculate(new FileInfo(x));
                return new { Crc = crc, ImageFileName = x };
            }).ToArray();
            var imagesToMoveCrc = albumToMove.Images
                .Select(x => new { Crc = x.CrcHash, ImageFileName = x.FileInfo!.FullName(albumToMoveDir) }).ToList();

            // if none exist take all images from albumToMove
            if (existingImages.Count == 0)
            {
                Trace.WriteLine("No image found in existing directory. Copying all images from Album...");
                foreach (var image in albumToMove.Images)
                {
                    if (image.FileInfo != null)
                    {
                        var imagePath = image.FileInfo.FullName(albumToMoveDir);
                        if (File.Exists(imagePath))
                        {
                            File.Move(imagePath, Path.Combine(existingDir.FullName, image.FileInfo.Name));
                            Logger.Debug("[{ServiceName}] :\u2502: moving new image [{FileName}]",
                                nameof(LibraryService), image.FileInfo.Name);
                        }
                    }
                }
            }
            else
            {
                var existingImageInfos = await Task.WhenAll(existingImagesCrc.Select(x => x)).ConfigureAwait(false);
                foreach (var imageToMove in imagesToMoveCrc.ToArray())
                {
                    if (existingImageInfos.Any(x => x.Crc == imageToMove.Crc))
                    {
                        // Exact duplicate found, dont do anything
                        imagesToMoveCrc.Remove(imageToMove);
                        continue;
                    }

                    var existingWithSameFileNames = existingImageInfos.Where(x =>
                            string.Equals(x.ImageFileName, imageToMove.ImageFileName,
                                StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    foreach (var existingWithSameFileName in existingWithSameFileNames)
                    {
                        imagesToMoveCrc.Remove(imageToMove);

                        // If some exist, not duplicate CRC, same name, keep the higher resolution
                        var existingInfoSizeInfo = await imageProcessor
                            .IdentifyAsync(existingWithSameFileName.ImageFileName, cancellationToken)
                            .ConfigureAwait(false);
                        var toMoveInfoSizeInfo = await imageProcessor.IdentifyAsync(imageToMove.ImageFileName, cancellationToken)
                            .ConfigureAwait(false);
                        if (existingInfoSizeInfo != null && toMoveInfoSizeInfo != null && existingInfoSizeInfo.Width > toMoveInfoSizeInfo.Width)
                        {
                            continue;
                        }

                        var toFile = Path.Combine(existingDir.FullName, Path.GetFileName(imageToMove.ImageFileName));
                        Trace.WriteLine(
                            $"Existing image [{existingWithSameFileName.ImageFileName}] to be overwritten by image [{toFile}]...");
                        File.Delete(existingWithSameFileName.ImageFileName);
                        File.Move(imageToMove.ImageFileName, toFile);
                        Logger.Debug("[{ServiceName}] :\u2502: moving better image [{FileName}]",
                            nameof(LibraryService), Path.GetFileName(imageToMove.ImageFileName));
                    }
                }

                foreach (var imageToMove in imagesToMoveCrc)
                {
                    var toFile = Path.Combine(existingDir.FullName, Path.GetFileName(imageToMove.ImageFileName));
                    if (!File.Exists(toFile))
                    {
                        Trace.WriteLine($"Moving image [{imageToMove.ImageFileName}] to [{toFile}]...");
                        File.Move(imageToMove.ImageFileName, toFile);
                        Logger.Debug("[{ServiceName}] :\u2502: moving image [{FileName}]", nameof(LibraryService),
                            Path.GetFileName(imageToMove.ImageFileName));
                    }
                }
            }
        }

        var songsToMove = albumToMove.Songs?.ToList() ?? [];
        if (songsToMove.Any())
        {
            var imageValidator = new ImageValidator(imageProcessor, configuration);
            var imageConvertor = new ImageConvertor(imageProcessor, configuration);
            var atlMetTag = new AtlMetaTag(new MetaTagsProcessor(configuration, serializer), imageProcessor, imageConvertor,
                imageValidator, configuration);
            var existingSongsFileInfos = albumToMoveDir.AllMediaTypeFileInfos().ToArray();
            var existingSongs = new List<Song>();
            foreach (var song in existingSongsFileInfos)
            {
                var songLoadResult = await atlMetTag
                    .ProcessFileAsync(existingDir.ToDirectorySystemInfo(), song.ToFileSystemInfo(), cancellationToken)
                    .ConfigureAwait(false);
                if (songLoadResult.IsSuccess)
                {
                    existingSongs.Add(songLoadResult.Data);
                }
            }

            foreach (var songToMove in songsToMove.ToArray())
            {
                var existingForSongToMove =
                    existingSongs.FirstOrDefault(x => x.SongNumber() == songToMove.SongNumber());
                if (existingForSongToMove != null)
                {
                    // Recalculate CRC hash from file to ensure accurate comparison
                    // Song.FileHash should always equal CRC32.Calculate over entire file bytes
                    var songToMoveCrcHash = Crc32.Calculate(songToMove.File.ToFileInfo(albumToMoveDir));
                    if (existingForSongToMove.CrcHash == songToMoveCrcHash)
                    {
                        // Duplicate dont do anything
                        songsToMove.Remove(songToMove);
                        continue;
                    }

                    if (existingForSongToMove.File.Size > songToMove.File.Size ||
                        (existingForSongToMove.BitRate() > songToMove.BitRate() &&
                         existingForSongToMove.BitDepth() > songToMove.BitDepth()))
                    {
                        // existing song is better don't do anything
                        songsToMove.Remove(songToMove);
                    }
                }
            }

            // Copy over any song left to move
            foreach (var songToMove in songsToMove)
            {
                var toFile = Path.Combine(existingDir.FullName, songToMove.File.Name);
                if (File.Exists(songToMove.File.FullName(albumToMoveDir)) && !File.Exists(toFile))
                {
                    Logger.Debug("[{ServiceName}] :\u2502: moving song [{FileName}]", nameof(LibraryService),
                        songToMove.File.Name);
                    File.Move(songToMove.File.FullName(albumToMoveDir), toFile);
                }
            }
        }

        // Delete directory to merge as what is wanted has been moved 
        var directoryToDelete = albumToMove.Directory.FullName();
        if (Directory.Exists(directoryToDelete))
        {
            Directory.Delete(directoryToDelete, true);
            Logger.Debug("[{ServiceName}] :\u2558: deleting directory [{FileName}]", nameof(LibraryService), albumToMove.Directory);
        }
        else
        {
            Logger.Debug("[{ServiceName}] :\u2558: directory already deleted or does not exist [{FileName}]", nameof(LibraryService), albumToMove.Directory);
        }
    }


    protected async Task<OperationResult<bool>> UpdateArtistAggregateValuesByIdAsync(
        int artistId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var artist = await scopedContext.Artists
            .Where(a => a.Id == artistId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (artist == null)
        {
            return new OperationResult<bool> { Data = false };
        }

        var albumCount = await scopedContext.Albums
            .Where(a => a.ArtistId == artistId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var songCount = await scopedContext.Songs
            .Where(s => s.Album.ArtistId == artistId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var updated = false;
        if (artist.AlbumCount != albumCount)
        {
            artist.AlbumCount = albumCount;
            artist.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            updated = true;
        }

        if (artist.SongCount != songCount)
        {
            artist.SongCount = songCount;
            artist.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            updated = true;
        }

        if (updated)
        {
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new OperationResult<bool> { Data = updated };
    }


    protected async Task<OperationResult<bool>> UpdateLibraryAggregateStatsByIdAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var library = await scopedContext.Libraries
            .Where(l => l.Id == libraryId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (library == null)
        {
            return new OperationResult<bool> { Data = false };
        }

        var artistCount = await scopedContext.Artists
            .Where(a => a.LibraryId == libraryId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var albumCount = await scopedContext.Albums
            .Where(aa => aa.Artist.LibraryId == libraryId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var songCount = await scopedContext.Songs
            .Where(s => s.Album.Artist.LibraryId == libraryId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        library.ArtistCount = artistCount;
        library.AlbumCount = albumCount;
        library.SongCount = songCount;
        library.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OperationResult<bool> { Data = true };
    }


    protected async Task<AlbumList2[]> AlbumListForArtistApiKey(
        Guid artistApiKey,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Apply Include before any Select/projection operations
        var albums = await scopedContext.Albums
            .Where(a => a.Artist.ApiKey == artistApiKey)
            .Include(a => a.Artist)
            .ThenInclude(aa => aa.Library)
            .Include(a => a.UserAlbums.Where(ua => ua.UserId == userId))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var albumList = new List<AlbumList2>();

        foreach (var album in albums)
        {
            var userAlbum = album.UserAlbums.FirstOrDefault();

            albumList.Add(new AlbumList2
            {
                Id = $"album_{album.ApiKey}",
                Album = album.Name,
                Title = album.Name,
                Name = album.Name,
                CoverArt = $"album_{album.ApiKey}",
                SongCount = album.SongCount ?? 0,
                CreatedRaw = album.CreatedAt,
                Duration = (int)(album.Duration / 1000),
                PlayedCount = album.PlayedCount,
                ArtistId = $"artist_{album.Artist.ApiKey}",
                Artist = album.Artist.Name,
                Year = album.ReleaseDate.Year,
                Genres = album.Genres,
                Parent = $"library_{album.Artist.Library.ApiKey}",
                UserRating = userAlbum?.Rating
            });
        }

        return albumList.ToArray();
    }

    protected async Task<DatabaseDirectoryInfo?> DatabaseArtistInfoForArtistApiKey(
        Guid apiKeyId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Apply Include before any Select/projection operations
        var artist = await scopedContext.Artists
            .Where(a => a.ApiKey == apiKeyId)
            .Include(a => a.Library)
            .Include(a => a.UserArtists.Where(ua => ua.UserId == userId))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (artist?.Library == null)
        {
            return null;
        }

        var userArtist = artist.UserArtists.FirstOrDefault();

        return new DatabaseDirectoryInfo(
            artist.Id,
            artist.ApiKey,
            artist.SortName?.Substring(0, 1) ?? artist.Name.Substring(0, 1),
            artist.Name,
            $"artist_{artist.ApiKey}",
            artist.CalculatedRating,
            artist.AlbumCount,
            artist.PlayedCount,
            artist.CreatedAt,
            artist.LastUpdatedAt,
            artist.LastPlayedAt,
            Path.Combine(artist.Library.Path, artist.Directory),
            userArtist?.StarredAt,
            userArtist?.Rating
        );
    }

    protected async Task<DatabaseDirectoryInfo?> DatabaseAlbumInfoForAlbumApiKey(
        Guid apiKeyId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Apply Include before any Select/projection operations
        var album = await scopedContext.Albums
            .Where(a => a.ApiKey == apiKeyId)
            .Include(a => a.Artist)
            .ThenInclude(aa => aa.Library)
            .Include(a => a.UserAlbums.Where(ua => ua.UserId == userId))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (album?.Artist.Library == null)
        {
            return null;
        }

        var userAlbum = album.UserAlbums.FirstOrDefault();

        return new DatabaseDirectoryInfo(
            album.Id,
            album.ApiKey,
            album.SortName?.Substring(0, 1) ?? album.Name.Substring(0, 1),
            album.Name,
            $"album_{album.ApiKey}",
            album.CalculatedRating,
            0,
            album.PlayedCount,
            album.CreatedAt,
            album.LastUpdatedAt,
            album.LastPlayedAt,
            Path.Combine(album.Artist.Library.Path, album.Directory),
            userAlbum?.StarredAt,
            userAlbum?.Rating
        );
    }

    protected async Task<DatabaseDirectoryInfo[]?> DatabaseSongInfosForAlbumApiKey(Guid apiKeyId, int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Apply Include before any Select/projection operations
        var songs = await scopedContext.Songs
            .Where(s => s.Album.ApiKey == apiKeyId)
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(aa => aa.Library)
            .Include(s => s.UserSongs.Where(us => us.UserId == userId))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songInfos = new List<DatabaseDirectoryInfo>();

        foreach (var song in songs)
        {
            var userSong = song.UserSongs.FirstOrDefault();

            songInfos.Add(new DatabaseDirectoryInfo(
                song.Id,
                song.ApiKey,
                song.TitleSort?.Substring(0, 1) ?? song.Title.Substring(0, 1),
                song.Title,
                $"song_{song.ApiKey}",
                song.CalculatedRating,
                0,
                song.PlayedCount,
                song.CreatedAt,
                song.LastUpdatedAt,
                song.LastPlayedAt,
                Path.Combine(song.Album.Artist.Library.Path, song.Album.Directory),
                userSong?.StarredAt,
                userSong?.Rating
            ));
        }

        return songInfos.ToArray();
    }

    protected async Task<DatabaseSongIdsInfo?> DatabaseSongIdsInfoForSongApiKey(Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var result = await scopedContext.Songs
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Where(s => s.ApiKey == apiKeyId)
            .Select(s => new DatabaseSongIdsInfo(
                s.Id,
                s.ApiKey,
                s.Album.Id,
                s.Album.ApiKey,
                s.Album.Artist.Id,
                s.Album.Artist.ApiKey
            ))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    protected async Task<DatabaseSongScrobbleInfo?> DatabaseSongScrobbleInfoForSongApiKey(Guid apiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var result = await scopedContext.Songs
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Where(s => s.ApiKey == apiKey)
            .Select(s => new DatabaseSongScrobbleInfo(
                s.ApiKey,
                s.Album.Artist.Id,
                s.Album.Id,
                s.Id,
                s.Album.Artist.Name,
                s.Album.Name,
                Instant.FromDateTimeUtc(DateTime.UtcNow),
                s.Title,
                s.Duration,
                s.MusicBrainzId,
                s.SongNumber
            ))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>
    ///     Returns all albums created by reading songs and the total number of songs
    /// </summary>
    public async Task<OperationResult<(IEnumerable<Album>, int)>> AllAlbumsForDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        IAlbumValidator albumValidator,
        ISongPlugin[] songPlugins,
        IMelodeeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var albums = new List<Album>();
        var messages = new List<string>();
        var viaPlugins = new List<string>();
        var songs = new List<Song>();

        var maxAlbumProcessingCount = configuration.GetValue<int>(SettingRegistry.ProcessingMaximumProcessingCount,
            value => value < 1 ? int.MaxValue : value);

        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.Path);
        if (dirInfo.Exists)
        {
            using (Operation.At(LogEventLevel.Debug).Time("AllAlbumsForDirectoryAsync [{directoryInfo}]",
                       fileSystemDirectoryInfo.Name))
            {
                foreach (var fileSystemInfo in dirInfo.EnumerateFileSystemInfos("*.*", SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var fsi = fileSystemInfo.ToFileSystemInfo();
                    foreach (var plugin in songPlugins.OrderBy(x => x.SortOrder))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        if (plugin.DoesHandleFile(fileSystemDirectoryInfo, fsi))
                        {
                            var pluginResult =
                                await plugin.ProcessFileAsync(fileSystemDirectoryInfo, fsi, cancellationToken);
                            if (pluginResult.IsSuccess)
                            {
                                songs.Add(pluginResult.Data);
                                viaPlugins.Add(plugin.DisplayName);
                            }

                            messages.AddRange(pluginResult.Messages ?? []);
                        }

                        if (plugin.StopProcessing)
                        {
                            Logger.Warning("[{PluginName}] set stop processing flag.", plugin.DisplayName);
                            break;
                        }
                    }
                }

                foreach (var songsGroupedByAlbum in songs.GroupBy(x => x.AlbumId))
                {
                    foreach (var song in songsGroupedByAlbum)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        var foundAlbum = albums.FirstOrDefault(x => x.Id == songsGroupedByAlbum.Key);
                        if (foundAlbum != null)
                        {
                            albums.Remove(foundAlbum);
                            albums.Add(foundAlbum.MergeSongs([song]));
                        }
                        else
                        {
                            var songTotal = song.SongTotalNumber();
                            if (songTotal < 1)
                            {
                                songTotal = SafeParser.ToNumber<short>(songsGroupedByAlbum.Count());
                            }

                            var newAlbumTags = new List<MetaTag<object?>>
                            {
                                new()
                                {
                                    Identifier = MetaTagIdentifier.Album, Value = song.AlbumTitle(), SortOrder = 1
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.AlbumArtist, Value = song.AlbumArtist(),
                                    SortOrder = 2
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.DiscNumber, Value = 1, SortOrder = 4
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.DiscTotal, Value = 1, SortOrder = 4
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.OrigAlbumYear, Value = song.AlbumYear(),
                                    SortOrder = 100
                                },
                                new() { Identifier = MetaTagIdentifier.SongTotal, Value = songTotal, SortOrder = 101 }
                            };
                            var genres = songsGroupedByAlbum
                                .SelectMany(x => x.Tags ?? [])
                                .Where(x => x.Identifier == MetaTagIdentifier.Genre);
                            newAlbumTags.AddRange(genres
                                .GroupBy(x => x.Value)
                                .Select((genre, i) => new MetaTag<object?>
                                {
                                    Identifier = MetaTagIdentifier.Genre,
                                    Value = genre.Key,
                                    SortOrder = 5 + i
                                }));
                            var artistName = newAlbumTags.FirstOrDefault(x =>
                                    x.Identifier is MetaTagIdentifier.Artist or MetaTagIdentifier.AlbumArtist)?.Value
                                ?.ToString();
                            var newAlbum = new Album
                            {
                                Artist = Artist.NewArtistFromName(artistName ??
                                                                  throw new Exception("Invalid artist name")),
                                Images = songsGroupedByAlbum.Where(x => x.Images != null)
                                    .SelectMany(x => x.Images!)
                                    .DistinctBy(x => x.CrcHash).ToArray(),
                                Directory = fileSystemDirectoryInfo,
                                OriginalDirectory = fileSystemDirectoryInfo,
                                Tags = newAlbumTags,
                                Songs = songsGroupedByAlbum.OrderBy(x => x.SortOrder).ToArray(),
                                ViaPlugins = viaPlugins.Distinct().ToArray()
                            };
                            var validationResult = albumValidator.ValidateAlbum(newAlbum);
                            newAlbum.ValidationMessages = validationResult.Data.Messages ?? [];
                            newAlbum.Status = validationResult.Data.AlbumStatus;
                            newAlbum.StatusReasons = validationResult.Data.AlbumStatusReasons;
                            albums.Add(newAlbum);
                            if (albums.Count(x => x.IsValid) > maxAlbumProcessingCount)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        return new OperationResult<(IEnumerable<Album>, int)>(messages)
        {
            Data = (albums, songs.Count)
        };
    }

    protected virtual OperationResult<(bool, IEnumerable<ValidationResult>?)> ValidateModel<T>(T? dataToValidate)
    {
        if (dataToValidate != null)
        {
            var ctx = new ValidationContext(dataToValidate);
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(dataToValidate, ctx, validationResults, true))
            {
                return new OperationResult<(bool, IEnumerable<ValidationResult>?)>
                {
                    Data = (false, validationResults),
                    Type = OperationResponseType.ValidationFailure
                };
            }
        }

        return new OperationResult<(bool, IEnumerable<ValidationResult>?)>
        {
            Data = (dataToValidate != null, null)
        };
    }
}
