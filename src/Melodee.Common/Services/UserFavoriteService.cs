using Ardalis.GuardClauses;
using CsvHelper;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Models.Importing;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using MelodeeModels = Melodee.Common.Models;
using Song = Melodee.Common.Data.Models.Song;

namespace Melodee.Common.Services;

public sealed class UserFavoriteService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    ArtistService artistService,
    AlbumService albumService,
    UserProfileService userProfileService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:user:apikey:{0}";
    private const string CacheKeyDetailByEmailAddressKeyTemplate = "urn:user:emailaddress:{0}";
    private const string CacheKeyDetailByUsernameTemplate = "urn:user:username:{0}";
    private const string CacheKeyDetailTemplate = "urn:user:{0}";

    private readonly IMelodeeConfigurationFactory _configurationFactory = configurationFactory;
    private readonly ArtistService _artistService = artistService;
    private readonly AlbumService _albumService = albumService;
    private readonly UserProfileService _userProfileService = userProfileService;

    public async Task<MelodeeModels.OperationResult<int>> ImportUserFavoriteSongs(
        UserFavoriteSongConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(configuration, nameof(configuration));

        var user = await _userProfileService.GetByApiKeyAsync(configuration.UserApiKey, cancellationToken).ConfigureAwait(false);
        if (!user.IsSuccess || user.Data == null)
        {
            return new MelodeeModels.OperationResult<int>("Unknown user")
            {
                Data = 0,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        if (user.Data.IsLocked)
        {
            return new MelodeeModels.OperationResult<int>("User is locked.")
            {
                Data = 0,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        var recordsCreated = 0;
        var recordsUpdated = 0;
        var recordsFound = 0;
        int songsFromCsv;

        var csvFilenfo = new FileInfo(configuration.CsvFileName);
        if (!csvFilenfo.Exists)
        {
            return new MelodeeModels.OperationResult<int>("CSV file does not exist.")
            {
                Data = 0,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userSongs = await scopedContext.UserSongs.Where(x => x.UserId == user.Data.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var newUserSongs = new List<UserSong>();

            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

            using (var reader = new StreamReader(csvFilenfo.OpenRead(), System.Text.Encoding.UTF8))
            {
                using (var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture))
                {
                    using (var dr = new CsvDataReader(csv))
                    {
                        var dt = new System.Data.DataTable();
                        dt.Columns.Add(configuration.ArtistColumn, typeof(string));
                        dt.Columns.Add(configuration.AlbumColumn, typeof(string));
                        dt.Columns.Add(configuration.SongColumn, typeof(string));

                        dt.Load(dr);
                        songsFromCsv = dt.Rows.Count;
                        foreach (System.Data.DataRow row in dt.Rows)
                        {
                            var artistName = row[configuration.ArtistColumn] as string;
                            var albumName = row[configuration.AlbumColumn] as string;
                            var songName = row[configuration.SongColumn] as string;

                            if (artistName.Nullify() != null && albumName.Nullify() != null &&
                                songName.Nullify() != null)
                            {
                                var artist = artistName.ToNormalizedString() ?? artistName!;
                                var album = albumName.ToNormalizedString() ?? albumName!;
                                var song = songName.ToNormalizedString() ?? songName!;
                                var artistResult = await _artistService.GetByNameNormalized(artist, cancellationToken)
                                    .ConfigureAwait(false);
                                if (!artistResult.IsSuccess)
                                {
                                    Log.Warning(
                                        "[UserFavoriteService] ImportUserFavoriteSongs failed : UNKNOWN ARTIST : [{ArtistName}] [{AlbumName}] [{SongName}]",
                                        artist,
                                        album,
                                        song);
                                    continue;
                                }

                                var artistAlbumListResult = await _albumService
                                    .ListForArtistApiKeyAsync(new MelodeeModels.PagedRequest { PageSize = 1000 },
                                        artistResult.Data!.ApiKey, cancellationToken).ConfigureAwait(false);
                                var artistAlbum =
                                    artistAlbumListResult.Data.FirstOrDefault(x => x.NameNormalized == album);
                                if (artistAlbum == null)
                                {
                                    Log.Warning(
                                        "[UserFavoriteService] ImportUserFavoriteSongs failed : UNKNOWN ALBUM : [{ArtistName}] [{AlbumName}] [{SongName}]",
                                        artist,
                                        album,
                                        song);
                                    continue;
                                }

                                var dbSongInfo =
                                    await DatabaseSongInfosForAlbumApiKey(artistAlbum.ApiKey, user.Data.Id,
                                        cancellationToken).ConfigureAwait(false);
                                var albumSong = dbSongInfo.Data?.FirstOrDefault(x => x.TitleNormalized == song);
                                if (albumSong == null)
                                {
                                    var dbSong = await scopedContext.Songs
                                        .Include(x => x.Album)
                                        .FirstOrDefaultAsync(
                                            x => x.TitleNormalized == song && x.Album.ArtistId == artistResult.Data.Id,
                                            cancellationToken)
                                        .ConfigureAwait(false);
                                    if (dbSong != null)
                                    {
                                        albumSong =
                                            (await DatabaseSongInfosForAlbumApiKey(dbSong.Album.ApiKey, user.Data.Id,
                                                cancellationToken).ConfigureAwait(false))
                                            .Data?.FirstOrDefault(x => x.TitleNormalized == song);
                                    }

                                    if (albumSong == null)
                                    {
                                        Log.Warning(
                                            "[UserFavoriteService] ImportUserFavoriteSongs failed : UNKNOWN SONG : [{ArtistName}] [{AlbumName}] [{SongName}]",
                                            artist,
                                            album,
                                            song);
                                        continue;
                                    }
                                }

                                var userSong = userSongs.FirstOrDefault(x => x.SongId == albumSong.Id);
                                if (userSong == null)
                                {
                                    userSong = new UserSong
                                    {
                                        UserId = user.Data.Id,
                                        SongId = albumSong.Id,
                                        CreatedAt = now
                                    };
                                    newUserSongs.Add(userSong);
                                    recordsCreated++;
                                }
                                else
                                {
                                    if (userSong is { IsStarred: true, Rating: > 0 })
                                    {
                                        recordsFound++;
                                        continue;
                                    }

                                    userSong.LastUpdatedAt = now;
                                    recordsUpdated++;
                                }

                                userSong.IsStarred = true;
                                userSong.Rating = userSong.Rating > 0 ? userSong.Rating : 1;
                                userSongs.Add(userSong);
                            }
                        }
                    }
                }
            }

            if (!configuration.IsPretend)
            {
                if (recordsCreated > 0 || recordsUpdated > 0)
                {
                    if (newUserSongs.Count > 0)
                    {
                        await scopedContext.UserSongs.AddRangeAsync(newUserSongs, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        Log.Information(
            "[UserFavoriteService] ImportUserFavoriteSongs {Pretend} [{UserApiKey}] Songs From Csv [{CsvSongCount}] found {RecordsFound} created {RecordsCreated} records, updated {RecordsUpdated} records, missing [{MissingCount}]",
            configuration.IsPretend ? "[Pretend]" : string.Empty,
            songsFromCsv,
            user.Data.ApiKey,
            recordsFound,
            recordsCreated,
            recordsUpdated,
            songsFromCsv - (recordsFound + recordsCreated + recordsUpdated));

        return new MelodeeModels.OperationResult<int>
        {
            Data = recordsCreated + recordsUpdated
        };
    }

    private new async Task<MelodeeModels.PagedResult<Song>> DatabaseSongInfosForAlbumApiKey(
        Guid albumApiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var songCount = await scopedContext.Songs
            .AsNoTracking()
            .Where(s => s.Album.ApiKey == albumApiKey)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        Song[] songs = [];

        if (songCount > 0)
        {
            songs = await scopedContext.Songs
                .Include(s => s.Album)
                .ThenInclude(a => a.Artist)
                .AsNoTracking()
                .Where(s => s.Album.ApiKey == albumApiKey)
                .OrderBy(x => x.SongNumber)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return new Melodee.Common.Models.PagedResult<Melodee.Common.Data.Models.Song>
        {
            TotalCount = songCount,
            TotalPages = 1,
            Data = songs
        };
    }
}
