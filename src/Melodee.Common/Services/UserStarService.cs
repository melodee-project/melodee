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

public sealed class UserStarService(
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

    public async Task<MelodeeModels.OperationResult<bool>> ToggleArtistHatedAsync(int userId, Guid artistApiKey,
        bool isHated, CancellationToken cancellationToken = default)
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

                userArtist.IsHated = isHated;
                if (isHated)
                {
                    userArtist.IsStarred = false;
                    userArtist.StarredAt = null;
                }

                userArtist.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleArtistStarAsync(int userId, Guid artistApiKey,
        bool isStarred, CancellationToken cancellationToken = default)
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

                userArtist.StarredAt = isStarred ? now : null;
                userArtist.IsStarred = isStarred;
                if (isStarred)
                {
                    userArtist.IsHated = false;
                }

                userArtist.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleAlbumHatedAsync(int userId, Guid albumApiKey,
        bool isHated, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var album = await _albumService.GetByApiKeyAsync(albumApiKey, cancellationToken).ConfigureAwait(false);
            if (album.Data != null)
            {
                var userAlbum = await scopedContext.UserAlbums
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.AlbumId == album.Data.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (userAlbum == null)
                {
                    userAlbum = new UserAlbum
                    {
                        UserId = userId,
                        AlbumId = album.Data.Id,
                        CreatedAt = now,
                        LastPlayedAt = null
                    };
                    scopedContext.UserAlbums.Add(userAlbum);
                }

                userAlbum.IsHated = isHated;
                if (isHated)
                {
                    userAlbum.IsStarred = false;
                    userAlbum.StarredAt = null;
                }

                userAlbum.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleAlbumStarAsync(int userId, Guid albumApiKey,
        bool isStarred, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var album = await _albumService.GetByApiKeyAsync(albumApiKey, cancellationToken).ConfigureAwait(false);
            if (album.Data != null)
            {
                var userAlbum = await scopedContext.UserAlbums
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.AlbumId == album.Data.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (userAlbum == null)
                {
                    userAlbum = new UserAlbum
                    {
                        UserId = userId,
                        AlbumId = album.Data.Id,
                        CreatedAt = now,
                        LastPlayedAt = null
                    };
                    scopedContext.UserAlbums.Add(userAlbum);
                }

                userAlbum.StarredAt = isStarred ? now : null;
                userAlbum.IsStarred = isStarred;
                if (isStarred)
                {
                    userAlbum.IsHated = false;
                }

                userAlbum.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleSongHatedAsync(int userId, Guid songApiKey,
        bool isHated, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var song = await _songService.GetByApiKeyAsync(songApiKey, cancellationToken).ConfigureAwait(false);
            if (song.Data != null)
            {
                var userSong = await scopedContext.UserSongs
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == song.Data.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (userSong == null)
                {
                    userSong = new UserSong
                    {
                        UserId = userId,
                        SongId = song.Data.Id,
                        CreatedAt = now,
                        LastPlayedAt = null
                    };
                    scopedContext.UserSongs.Add(userSong);
                }

                userSong.IsHated = isHated;
                if (isHated)
                {
                    userSong.IsStarred = false;
                    userSong.StarredAt = null;
                }

                userSong.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleSongStarAsync(int userId, Guid songApiKey, bool isStarred, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var song = await _songService.GetByApiKeyAsync(songApiKey, cancellationToken).ConfigureAwait(false);
            if (song.Data != null)
            {
                var userSong = await scopedContext.UserSongs
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == song.Data.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (userSong == null)
                {
                    userSong = new UserSong
                    {
                        UserId = userId,
                        SongId = song.Data.Id,
                        CreatedAt = now,
                        LastPlayedAt = null
                    };
                    scopedContext.UserSongs.Add(userSong);
                }

                userSong.StarredAt = isStarred ? now : null;
                userSong.IsStarred = isStarred;
                if (isStarred)
                {
                    userSong.IsHated = false;
                }

                userSong.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearUserCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
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
