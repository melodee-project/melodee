using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using Serilog.Events;
using SerilogTimings;

namespace Melodee.Common.Services;

public sealed class StatisticsService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    PlaylistService playlistService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private static DateTimeZone ResolveZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return DateTimeZone.Utc;
        }

        return DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId) ?? DateTimeZone.Utc;
    }

    private static TimeSeriesPoint[] ZeroFillDailySeries(
        IEnumerable<(LocalDate Day, double Value)> points,
        LocalDate startDay,
        LocalDate endDay)
    {
        var map = points
            .GroupBy(x => x.Day)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        var results = new List<TimeSeriesPoint>();
        for (var day = startDay; day <= endDay; day = day.PlusDays(1))
        {
            results.Add(new TimeSeriesPoint(day, map.TryGetValue(day, out var v) ? v : 0));
        }

        return results.ToArray();
    }

    private static async Task<int?> ResolveUserIdAsync(MelodeeDbContext context, Guid userApiKey,
        CancellationToken cancellationToken)
    {
        return await context.Users
            .AsNoTracking()
            .Where(x => x.ApiKey == userApiKey)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<Statistic?>> GetAlbumCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess)
        {
            return new OperationResult<Statistic?>
            {
                Data = null
            };
        }
        return new OperationResult<Statistic?>
        {
            Data = stats.Data.FirstOrDefault(x => x is { Type: StatisticType.Count, Category: StatisticCategory.CountAlbum })
        };
    }

    public async Task<OperationResult<Statistic?>> GetArtistCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess)
        {
            return new OperationResult<Statistic?>
            {
                Data = null
            };
        }
        return new OperationResult<Statistic?>
        {
            Data = stats.Data.FirstOrDefault(x => x is { Type: StatisticType.Count, Category: StatisticCategory.CountArtist })
        };
    }

    public async Task<OperationResult<Statistic?>> GetSongCountAsync(CancellationToken cancellationToken = default)
    {
        var stats = await GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
        if (!stats.IsSuccess)
        {
            return new OperationResult<Statistic?>
            {
                Data = null
            };
        }
        return new OperationResult<Statistic?>
        {
            Data = stats.Data.FirstOrDefault(x => x is { Type: StatisticType.Count, Category: StatisticCategory.CountSong })
        };
    }

    public async Task<OperationResult<Statistic[]>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Statistic>();

        // Helper to run a query in its own context
        async Task<T> RunInOwnContextAsync<T>(Func<MelodeeDbContext, Task<T>> query)
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await query(context).ConfigureAwait(false);
        }

        var albumsCountTask = RunInOwnContextAsync(ctx => ctx.Albums.AsNoTracking().CountAsync(cancellationToken));
        var artistsCountTask = RunInOwnContextAsync(ctx => ctx.Artists.AsNoTracking().CountAsync(cancellationToken));
        var contributorsCountTask = RunInOwnContextAsync(ctx => ctx.Contributors.AsNoTracking().CountAsync(cancellationToken));
        var librariesCountTask = RunInOwnContextAsync(ctx => ctx.Libraries.AsNoTracking().CountAsync(cancellationToken));
        var playlistsCountTask = playlistService.GetTotalPlaylistCountAsync(cancellationToken);
        var radioStationsCountTask = RunInOwnContextAsync(ctx => ctx.RadioStations.AsNoTracking().CountAsync(cancellationToken));
        var sharesCountTask = RunInOwnContextAsync(ctx => ctx.Shares.AsNoTracking().CountAsync(cancellationToken));
        var songsCountTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().CountAsync(cancellationToken));
        var songsPlayedCountTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().SumAsync(x => x.PlayedCount, cancellationToken));
        var usersCountTask = RunInOwnContextAsync(ctx => ctx.Users.AsNoTracking().CountAsync(cancellationToken));
        var userArtistsFavoritedTask = RunInOwnContextAsync(ctx => ctx.UserArtists.AsNoTracking().CountAsync(x => x.StarredAt != null, cancellationToken));
        var userAlbumsFavoritedTask = RunInOwnContextAsync(ctx => ctx.UserAlbums.AsNoTracking().CountAsync(x => x.StarredAt != null, cancellationToken));
        var userSongsFavoritedTask = RunInOwnContextAsync(ctx => ctx.UserSongs.AsNoTracking().CountAsync(x => x.StarredAt != null, cancellationToken));
        var userSongsRatedTask = RunInOwnContextAsync(ctx => ctx.UserSongs.AsNoTracking().CountAsync(x => x.Rating > 0, cancellationToken));
        var songsFileSizeTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().SumAsync(x => x.FileSize, cancellationToken));
        var songsDurationTask = RunInOwnContextAsync(ctx => ctx.Songs.AsNoTracking().SumAsync(x => x.Duration, cancellationToken));
        var genresCountTask = RunInOwnContextAsync(ctx => GetUniqueGenresCountAsync(ctx, cancellationToken));

        // Wait for all tasks to complete
        await Task.WhenAll(
            albumsCountTask,
            artistsCountTask,
            contributorsCountTask,
            librariesCountTask,
            playlistsCountTask,
            radioStationsCountTask,
            sharesCountTask,
            songsCountTask,
            songsPlayedCountTask,
            usersCountTask,
            userArtistsFavoritedTask,
            userAlbumsFavoritedTask,
            userSongsFavoritedTask,
            userSongsRatedTask,
            songsFileSizeTask,
            songsDurationTask,
            genresCountTask
        ).ConfigureAwait(false);

        var albumsCount = await albumsCountTask.ConfigureAwait(false);
        var artistsCount = await artistsCountTask.ConfigureAwait(false);
        var contributorsCount = await contributorsCountTask.ConfigureAwait(false);
        var librariesCount = await librariesCountTask.ConfigureAwait(false);
        var playlistsCount = await playlistsCountTask.ConfigureAwait(false);
        var radioStationsCount = await radioStationsCountTask.ConfigureAwait(false);
        var sharesCount = await sharesCountTask.ConfigureAwait(false);
        var songsCount = await songsCountTask.ConfigureAwait(false);
        var songsPlayedCount = await songsPlayedCountTask.ConfigureAwait(false);
        var usersCount = await usersCountTask.ConfigureAwait(false);
        var userArtistsFavorited = await userArtistsFavoritedTask.ConfigureAwait(false);
        var userAlbumsFavorited = await userAlbumsFavoritedTask.ConfigureAwait(false);
        var userSongsFavorited = await userSongsFavoritedTask.ConfigureAwait(false);
        var userSongsRated = await userSongsRatedTask.ConfigureAwait(false);
        var songsFileSize = await songsFileSizeTask.ConfigureAwait(false);
        var songsDuration = await songsDurationTask.ConfigureAwait(false);
        var genresCount = await genresCountTask.ConfigureAwait(false);

        // Build results efficiently
        results.AddRange([
            new Statistic(StatisticType.Count, "Albums", albumsCount, null, null, 1, "album", true, StatisticCategory.CountAlbum),
            new Statistic(StatisticType.Count, "Artists", artistsCount, null, null, 2, "artist", true, StatisticCategory.CountArtist),
            new Statistic(StatisticType.Count, "Contributors", contributorsCount, null, null, 3, "contacts_product"),
            new Statistic(StatisticType.Count, "Genres", genresCount, null, null, 4, "genres"),
            new Statistic(StatisticType.Count, "Libraries", librariesCount, null, null, 5, "library_music"),
            new Statistic(StatisticType.Count, "Playlists", playlistsCount, null, null, 6, "playlist_play", true),
            new Statistic(StatisticType.Count, "Radio Stations", radioStationsCount, null, null, 7, "radio"),
            new Statistic(StatisticType.Count, "Shares", sharesCount, null, null, 8, "share"),
            new Statistic(StatisticType.Count, "Songs", songsCount, null, null, 9, "music_note", true, StatisticCategory.CountSong),
            new Statistic(StatisticType.Count, "Songs: Played count", songsPlayedCount, null, null, 10, "analytics"),
            new Statistic(StatisticType.Count, "Users", usersCount, null, null, 11, "group", false, StatisticCategory.CountUsers),
            new Statistic(StatisticType.Count, "Users: Favorited artists", userArtistsFavorited, null, null, 12, "analytics"),
            new Statistic(StatisticType.Count, "Users: Favorited albums", userAlbumsFavorited, null, null, 13, "analytics"),
            new Statistic(StatisticType.Count, "Users: Favorited songs", userSongsFavorited, null, null, 14, "analytics"),
            new Statistic(StatisticType.Count, "Users: Rated songs", userSongsRated, null, null, 15, "analytics"),
            new Statistic(StatisticType.Information, "Total: Song Mb", songsFileSize.FormatFileSize(), null, null, 16, "bar_chart"),
            new Statistic(StatisticType.Information, "Total: Song Duration", songsDuration.ToTimeSpan().ToYearDaysMinutesHours(), null, "Total song duration in Year:Day:Hour:Minute format.", 17, "bar_chart")
        ]);

        return new OperationResult<Statistic[]>
        {
            Data = results.ToArray()
        };
    }

    /// <summary>
    /// Gets the count of unique genres from Albums and Songs using EF Core.
    /// </summary>
    private static async Task<int> GetUniqueGenresCountAsync(MelodeeDbContext context, CancellationToken cancellationToken)
    {
        // Get all non-null genre arrays from Albums and Songs
        var albumGenres = await context.Albums
            .AsNoTracking()
            .Where(a => a.Genres != null && a.Genres.Length > 0)
            .Select(a => a.Genres)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songGenres = await context.Songs
            .AsNoTracking()
            .Where(s => s.Genres != null && s.Genres.Length > 0)
            .Select(s => s.Genres)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Flatten arrays and get unique genres
        var uniqueGenres = albumGenres
            .Concat(songGenres)
            .Where(genreArray => genreArray != null)
            .SelectMany(genreArray => genreArray!)
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet();

        return uniqueGenres.Count;
    }

    public async Task<OperationResult<Statistic[]>> GetUserSongStatisticsAsync(Guid userApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserSongs
            .AsNoTracking()
            .Where(x => x.User.ApiKey == userApiKey);

        var favCount = await baseQuery
            .CountAsync(x => x.StarredAt != null, cancellationToken).ConfigureAwait(false);

        var ratedCount = await baseQuery
            .CountAsync(x => x.Rating > 0, cancellationToken).ConfigureAwait(false);

        var results = new Statistic[]
        {
            new(StatisticType.Count, "Your Favorite songs", favCount, null, null, 1, "analytics"),
            new(StatisticType.Count, "Your Rated songs", ratedCount, null, null, 2, "analytics")
        };

        return new OperationResult<Statistic[]>
        {
            Data = results
        };
    }

    public async Task<OperationResult<Statistic[]>> GetUserAlbumStatisticsAsync(Guid userApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Use AsNoTracking for performance
        var favoriteAlbumsCount = await scopedContext.UserAlbums
            .AsNoTracking()
            .Where(x => x.User.ApiKey == userApiKey)
            .CountAsync(x => x.StarredAt != null, cancellationToken)
            .ConfigureAwait(false);

        var results = new Statistic[]
        {
            new(StatisticType.Count, "Your Favorite albums", favoriteAlbumsCount, null, null, 1, "analytics")
        };

        return new OperationResult<Statistic[]>
        {
            Data = results
        };
    }

    public async Task<OperationResult<Statistic[]>> GetUserArtistStatisticsAsync(Guid userApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Use AsNoTracking for performance
        var favoriteArtistsCount = await scopedContext.UserArtists
            .AsNoTracking()
            .Where(x => x.User.ApiKey == userApiKey)
            .CountAsync(x => x.StarredAt != null, cancellationToken)
            .ConfigureAwait(false);

        var results = new Statistic[]
        {
            new(StatisticType.Count, "Your Favorite artists", favoriteArtistsCount, null, null, 1, "analytics")
        };

        return new OperationResult<Statistic[]>
        {
            Data = results
        };
    }

    public async Task<OperationResult<TimeSeriesPoint[]>> GetSongsAddedPerDayAsync(
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);

        // Query window based on local day boundaries.
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var createdAts = await context.Songs
            .AsNoTracking()
            .Where(x => x.CreatedAt >= startInstant && x.CreatedAt < endExclusiveInstant)
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var points = createdAts
            .Select(i => (Day: i.InZone(zone).Date, Value: 1d));

        return new OperationResult<TimeSeriesPoint[]>
        {
            Data = ZeroFillDailySeries(points, startDay, endDay)
        };
    }

    public async Task<OperationResult<TimeSeriesPoint[]>> GetUserSongPlaysPerDayAsync(
        Guid userApiKey,
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var userId = await ResolveUserIdAsync(context, userApiKey, cancellationToken).ConfigureAwait(false);
        if (userId == null)
        {
            return new OperationResult<TimeSeriesPoint[]>
            {
                Data = ZeroFillDailySeries([], startDay, endDay)
            };
        }

        var played = await context.UserSongPlayHistories
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value && x.PlayedAt >= startInstant && x.PlayedAt < endExclusiveInstant)
            .Select(x => x.PlayedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var points = played.Select(i => (Day: i.InZone(zone).Date, Value: 1d));

        return new OperationResult<TimeSeriesPoint[]>
        {
            Data = ZeroFillDailySeries(points, startDay, endDay)
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetUserTopPlayedSongsAsync(
        Guid userApiKey,
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var userId = await ResolveUserIdAsync(context, userApiKey, cancellationToken).ConfigureAwait(false);
        if (userId == null)
        {
            return new OperationResult<TopItemStat[]> { Data = [] };
        }

        var query = await context.UserSongPlayHistories
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value && x.PlayedAt >= startInstant && x.PlayedAt < endExclusiveInstant)
            .GroupBy(x => x.SongId)
            .Select(g => new { SongId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topN)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songIds = query.Select(x => x.SongId).ToArray();
        var songs = await context.Songs
            .AsNoTracking()
            .Include(x => x.Album)
            .Where(x => songIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.ApiKey, AlbumApiKey = x.Album.ApiKey })
            .ToDictionaryAsync(x => x.Id, cancellationToken)
            .ConfigureAwait(false);

        var result = query
            .Select(x =>
            {
                songs.TryGetValue(x.SongId, out var song);
                return new TopItemStat(song?.Title ?? $"Song {x.SongId}", x.Count, song?.ApiKey, x.SongId, song?.AlbumApiKey.ToString());
            })
            .ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = result
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetUserRecentlyPlayedSongsAsync(
        Guid userApiKey,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var userId = await ResolveUserIdAsync(context, userApiKey, cancellationToken).ConfigureAwait(false);
        if (userId == null)
        {
            return new OperationResult<TopItemStat[]> { Data = [] };
        }

        // Only include completed plays (IsNowPlaying = false), not songs that were just started
        var histories = await context.UserSongPlayHistories
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value && !x.IsNowPlaying)
            .OrderByDescending(x => x.PlayedAt)
            .Take(topN)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songIds = histories.Select(x => x.SongId).Distinct().ToArray();
        var songs = await context.Songs
            .AsNoTracking()
            .Include(x => x.Album)
                .ThenInclude(a => a.Artist)
            .Where(x => songIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.ApiKey,
                x.SongNumber,
                x.Duration,
                AlbumApiKey = x.Album.ApiKey,
                AlbumName = x.Album.Name,
                ArtistApiKey = x.Album.Artist.ApiKey,
                ArtistName = x.Album.Artist.Name
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken)
            .ConfigureAwait(false);

        var result = histories.Select(x =>
        {
            songs.TryGetValue(x.SongId, out var song);
            var extra = song != null ? $"{song.AlbumApiKey}|{song.ArtistApiKey}|{song.AlbumName}|{song.ArtistName}|{song.SongNumber}|{song.Duration}" : null;
            return new TopItemStat(song?.Title ?? $"Song {x.SongId}", x.PlayedAt.ToUnixTimeTicks(), song?.ApiKey, x.SongId, extra);
        }).ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = result
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetUserTopGenresByPlaysAsync(
        Guid userApiKey,
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var userId = await ResolveUserIdAsync(context, userApiKey, cancellationToken).ConfigureAwait(false);
        if (userId == null)
        {
            return new OperationResult<TopItemStat[]> { Data = [] };
        }

        var query = await context.UserSongPlayHistories
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value && x.PlayedAt >= startInstant && x.PlayedAt < endExclusiveInstant)
            .Select(x => x.SongId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songGenres = await context.Songs
            .AsNoTracking()
            .Where(x => query.Contains(x.Id) && x.Genres != null && x.Genres.Length > 0)
            .Select(x => x.Genres)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var flattened = songGenres
            .SelectMany(g => g ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopItemStat(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .Take(topN)
            .ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = flattened
        };
    }

    public async Task<OperationResult<Statistic[]>> GetUserKpisAsync(
        Guid userApiKey,
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {

        using (Operation.At(LogEventLevel.Debug).Time("[{PluginName}] GetUserKpisAsync", nameof(StatisticsService)))
        {
            var zone = ResolveZone(timeZoneId);
            var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
            var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

            await using var context =
                await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var userId = await ResolveUserIdAsync(context, userApiKey, cancellationToken).ConfigureAwait(false);
            if (userId == null)
            {
                return new OperationResult<Statistic[]> { Data = [] };
            }

            var totalPlays = await context.UserSongPlayHistories
                .AsNoTracking()
                .CountAsync(
                    x => x.UserId == userId.Value && x.PlayedAt >= startInstant && x.PlayedAt < endExclusiveInstant,
                    cancellationToken)
                .ConfigureAwait(false);

            var favoritesSongs = await context.UserSongs
                .AsNoTracking()
                .CountAsync(x => x.UserId == userId.Value && x.StarredAt != null, cancellationToken)
                .ConfigureAwait(false);

            var favoritesAlbums = await context.UserAlbums
                .AsNoTracking()
                .CountAsync(x => x.UserId == userId.Value && x.StarredAt != null, cancellationToken)
                .ConfigureAwait(false);

            var favoritesArtists = await context.UserArtists
                .AsNoTracking()
                .CountAsync(x => x.UserId == userId.Value && x.StarredAt != null, cancellationToken)
                .ConfigureAwait(false);

            var ratedSongs = await context.UserSongs
                .AsNoTracking()
                .CountAsync(x => x.UserId == userId.Value && x.Rating > 0, cancellationToken)
                .ConfigureAwait(false);

            var stats = new Statistic[]
            {
                new(StatisticType.Count, "Total plays", totalPlays, null, null, 1, "bar_chart"),
                new(StatisticType.Count, "Favorites: Songs", favoritesSongs, null, null, 2, "favorite"),
                new(StatisticType.Count, "Favorites: Albums", favoritesAlbums, null, null, 3, "album"),
                new(StatisticType.Count, "Favorites: Artists", favoritesArtists, null, null, 4, "artist"),
                new(StatisticType.Count, "Rated Songs", ratedSongs, null, null, 5, "star")
            };

            return new OperationResult<Statistic[]> { Data = stats };
        }
    }

    public async Task<OperationResult<Statistic[]>> GetMissingImagesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var missingArtists = await context.Artists.AsNoTracking()
            .CountAsync(x => x.ImageCount == null || x.ImageCount == 0, cancellationToken).ConfigureAwait(false);
        var missingAlbums = await context.Albums.AsNoTracking()
            .CountAsync(x => x.ImageCount == null || x.ImageCount == 0, cancellationToken).ConfigureAwait(false);
        var missingSongs = await context.Songs.AsNoTracking()
            .CountAsync(x => x.ImageCount == null || x.ImageCount == 0, cancellationToken).ConfigureAwait(false);

        var stats = new Statistic[]
        {
            new(StatisticType.Count, "Artists missing images", missingArtists, null, null, 1, "image_not_supported"),
            new(StatisticType.Count, "Albums missing images", missingAlbums, null, null, 2, "image_not_supported"),
            new(StatisticType.Count, "Songs missing images", missingSongs, null, null, 3, "image_not_supported")
        };

        return new OperationResult<Statistic[]>
        {
            Data = stats
        };
    }

    public async Task<OperationResult<TimeSeriesPoint[]>> GetSearchesPerDayAsync(
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var createdAts = await context.SearchHistories
            .AsNoTracking()
            .Where(x => x.CreatedAt >= startInstant && x.CreatedAt < endExclusiveInstant)
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var points = createdAts.Select(i => (Day: i.InZone(zone).Date, Value: 1d));

        return new OperationResult<TimeSeriesPoint[]>
        {
            Data = ZeroFillDailySeries(points, startDay, endDay)
        };
    }

    public async Task<OperationResult<TimeSeriesPoint[]>> GetUserSearchesPerDayAsync(
        Guid userApiKey,
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var userId = await context.Users
            .AsNoTracking()
            .Where(x => x.ApiKey == userApiKey)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var createdAts = await context.SearchHistories
            .AsNoTracking()
            .Where(x => x.ByUserId == userId)
            .Where(x => x.CreatedAt >= startInstant && x.CreatedAt < endExclusiveInstant)
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var points = createdAts.Select(i => (Day: i.InZone(zone).Date, Value: 1d));

        return new OperationResult<TimeSeriesPoint[]>
        {
            Data = ZeroFillDailySeries(points, startDay, endDay)
        };
    }

    public async Task<OperationResult<TimeSeriesPoint[]>> GetShareViewsPerDayAsync(
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var createdAts = await context.ShareActivities
            .AsNoTracking()
            .Where(x => x.CreatedAt >= startInstant && x.CreatedAt < endExclusiveInstant)
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var points = createdAts.Select(i => (Day: i.InZone(zone).Date, Value: 1d));

        return new OperationResult<TimeSeriesPoint[]>
        {
            Data = ZeroFillDailySeries(points, startDay, endDay)
        };
    }

    public async Task<OperationResult<TimeSeriesPoint[]>> GetLibraryScansPerDayAsync(
        LocalDate startDay,
        LocalDate endDay,
        string? timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var zone = ResolveZone(timeZoneId);
        var startInstant = startDay.AtStartOfDayInZone(zone).ToInstant();
        var endExclusiveInstant = endDay.PlusDays(1).AtStartOfDayInZone(zone).ToInstant();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var createdAts = await context.LibraryScanHistories
            .AsNoTracking()
            .Where(x => x.CreatedAt >= startInstant && x.CreatedAt < endExclusiveInstant)
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var points = createdAts.Select(i => (Day: i.InZone(zone).Date, Value: 1d));

        return new OperationResult<TimeSeriesPoint[]>
        {
            Data = ZeroFillDailySeries(points, startDay, endDay)
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetAlbumsByYearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var grouped = await context.Albums
            .AsNoTracking()
            .GroupBy(x => x.ReleaseDate.Year)
            .Select(g => new { Year = g.Key, Count = g.Count() })
            .OrderBy(x => x.Year)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = grouped
            .Select(x => new TopItemStat(x.Year.ToString(), x.Count))
            .ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = result
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetAlbumsByGenreAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var albums = await context.Albums
            .AsNoTracking()
            .Select(a => a.Genres != null && a.Genres.Length > 0 ? a.Genres[0] : "Unknown")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var grouped = albums
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopItemStat(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = grouped
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetTopArtistsByAlbumCountAsync(
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var artistAlbumCounts = await context.Artists
            .AsNoTracking()
            .Select(a => new { a.Name, a.AlbumCount })
            .OrderByDescending(a => a.AlbumCount)
            .ThenBy(a => a.Name)
            .Take(topN)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = artistAlbumCounts
            .Select(a => new TopItemStat(a.Name, a.AlbumCount))
            .ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = result
        };
    }

    public async Task<OperationResult<TopItemStat[]>> GetTopArtistsByPlaysAsync(
        int topN = 10,
        CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var artistNames = await context.UserSongPlayHistories
            .AsNoTracking()
            .Select(x => x.Song.Album.Artist.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var artistPlayCounts = artistNames
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TopItemStat(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .Take(topN)
            .ToArray();

        return new OperationResult<TopItemStat[]>
        {
            Data = artistPlayCounts
        };
    }
}
