using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public sealed class UserRatingService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    UserProfileService userProfileService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:user:apikey:{0}";
    private const string CacheKeyDetailByEmailAddressKeyTemplate = "urn:user:emailaddress:{0}";
    private const string CacheKeyDetailByUsernameTemplate = "urn:user:username:{0}";
    private const string CacheKeyDetailTemplate = "urn:user:{0}";

    private readonly ArtistService _artistService = artistService;
    private readonly AlbumService _albumService = albumService;
    private readonly SongService _songService = songService;
    private readonly UserProfileService _userProfileService = userProfileService;

    public async Task<MelodeeModels.OperationResult<bool>> SetAlbumRatingAsync(int userId, int albumId, int rating,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var album = await _albumService.GetAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (album.Data != null)
            {
                var userAlbum = await scopedContext.UserAlbums
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.AlbumId == albumId, cancellationToken)
                    .ConfigureAwait(false);
                if (userAlbum == null)
                {
                    userAlbum = new UserAlbum
                    {
                        UserId = userId,
                        AlbumId = albumId,
                        CreatedAt = now
                    };
                    scopedContext.UserAlbums.Add(userAlbum);
                }

                userAlbum.Rating = rating;
                userAlbum.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

                var avgRating = await scopedContext.UserAlbums
                    .Where(ua => ua.AlbumId == userAlbum.AlbumId)
                    .AverageAsync(ua => (decimal?)ua.Rating, cancellationToken)
                    .ConfigureAwait(false) ?? 0;

                await scopedContext.Albums
                    .Where(a => a.Id == userAlbum.AlbumId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(a => a.LastUpdatedAt, now)
                        .SetProperty(a => a.CalculatedRating, avgRating), cancellationToken)
                    .ConfigureAwait(false);

                await _albumService.ClearCacheAsync(userAlbum.AlbumId, cancellationToken).ConfigureAwait(false);

                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetSongRatingAsync(int userId, int songId, int rating,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var song = await _songService.GetAsync(songId, cancellationToken).ConfigureAwait(false);
            if (song.Data != null)
            {
                var userSong = await scopedContext.UserSongs
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == songId, cancellationToken)
                    .ConfigureAwait(false);
                if (userSong == null)
                {
                    userSong = new UserSong
                    {
                        UserId = userId,
                        SongId = songId,
                        CreatedAt = now
                    };
                    scopedContext.UserSongs.Add(userSong);
                }

                userSong.Rating = rating;
                userSong.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

                var avgRating = await scopedContext.UserSongs
                    .Where(us => us.SongId == userSong.SongId)
                    .AverageAsync(us => (decimal?)us.Rating, cancellationToken)
                    .ConfigureAwait(false) ?? 0;

                await scopedContext.Songs
                    .Where(s => s.Id == userSong.SongId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.LastUpdatedAt, now)
                        .SetProperty(s => s.CalculatedRating, avgRating), cancellationToken)
                    .ConfigureAwait(false);

                await _songService.ClearCacheAsync(userSong.SongId, cancellationToken).ConfigureAwait(false);

                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetArtistRatingAsync(int userId, Guid artistApiKey,
        int rating, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var artist = await _artistService.GetByApiKeyAsync(artistApiKey, cancellationToken).ConfigureAwait(false);
            if (artist.Data != null)
            {
                var userArtist = await scopedContext.UserArtists
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.ArtistId == artist.Data.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (userArtist == null)
                {
                    userArtist = new UserArtist
                    {
                        UserId = userId,
                        ArtistId = artist.Data.Id,
                        CreatedAt = now
                    };
                    scopedContext.UserArtists.Add(userArtist);
                }

                userArtist.Rating = rating;
                userArtist.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

                var avgRating = await scopedContext.UserArtists
                    .Where(ua => ua.ArtistId == userArtist.ArtistId)
                    .AverageAsync(ua => (decimal?)ua.Rating, cancellationToken)
                    .ConfigureAwait(false) ?? 0;

                await scopedContext.Artists
                    .Where(a => a.Id == userArtist.ArtistId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(a => a.LastUpdatedAt, now)
                        .SetProperty(a => a.CalculatedRating, avgRating), cancellationToken)
                    .ConfigureAwait(false);

                await _artistService.ClearCacheAsync(userArtist.ArtistId, cancellationToken).ConfigureAwait(false);

                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetAlbumRatingAsync(int userId, Guid albumApiKey, int rating,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var album = await _albumService.GetByApiKeyAsync(albumApiKey, cancellationToken).ConfigureAwait(false);
        if (album.Data != null)
        {
            return await SetAlbumRatingAsync(userId, album.Data.Id, rating, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = false,
            Type = MelodeeModels.OperationResponseType.NotFound
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetSongRatingAsync(
        int userId,
        Guid songApiKey,
        int rating,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var song = await _songService.GetByApiKeyAsync(songApiKey, cancellationToken).ConfigureAwait(false);
        if (song.Data != null)
        {
            return await SetSongRatingAsync(userId, song.Data.Id, rating, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = false,
            Type = MelodeeModels.OperationResponseType.NotFound
        };
    }

    private void ClearUserCache(User user)
    {
        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(user.Id));
        CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(user.ApiKey));
        CacheManager.Remove(CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(user.EmailNormalized));
        CacheManager.Remove(CacheKeyDetailByUsernameTemplate.FormatSmart(user.UserNameNormalized));
    }
}
