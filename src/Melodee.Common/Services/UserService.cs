using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;
using CsvHelper;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Importing;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

/// <summary>
///     User data domain service (main facade).
/// </summary>
public sealed class UserService : ServiceBase
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:user:apikey:{0}";
    private const string CacheKeyDetailByEmailAddressKeyTemplate = "urn:user:emailaddress:{0}";
    private const string CacheKeyDetailByUsernameTemplate = "urn:user:username:{0}";
    private const string CacheKeyDetailTemplate = "urn:user:{0}";

    private readonly IUserAuthenticationService _userAuthenticationService;
    private readonly IUserProfileService _userProfileService;
    private readonly LibraryService _libraryService;
    private readonly ArtistService _artistService;
    private readonly AlbumService _albumService;
    private readonly SongService _songService;
    private readonly PlaylistService _playlistService;
    private readonly PodcastService _podcastService;
    private readonly IBus _bus;
    private readonly IMelodeeConfigurationFactory _configurationFactory;

    public UserService(
        ILogger logger,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        IMelodeeConfigurationFactory configurationFactory,
        LibraryService libraryService,
        ArtistService artistService,
        AlbumService albumService,
        SongService songService,
        PlaylistService playlistService,
        PodcastService podcastService,
        IBus bus,
        IPasswordHashService? passwordHashService = null,
        IOpenSubsonicSecretProtector? openSubsonicSecretProtector = null)
        : this(logger, cacheManager, contextFactory, configurationFactory, libraryService, artistService,
              albumService, songService, playlistService, podcastService, bus, passwordHashService,
              openSubsonicSecretProtector, null, null)
    {
        // Legacy constructor - create services
        _libraryService = libraryService;
        _artistService = artistService;
        _albumService = albumService;
        _songService = songService;
        _playlistService = playlistService;
        _podcastService = podcastService;
        _bus = bus;
        _configurationFactory = configurationFactory;
    }

    public UserService(
        ILogger logger,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        IMelodeeConfigurationFactory configurationFactory,
        LibraryService libraryService,
        ArtistService artistService,
        AlbumService albumService,
        SongService songService,
        PlaylistService playlistService,
        PodcastService podcastService,
        IBus bus,
        IPasswordHashService? passwordHashService,
        IOpenSubsonicSecretProtector? openSubsonicSecretProtector,
        IUserAuthenticationService? userAuthenticationService,
        IUserProfileService? userProfileService)
        : base(logger, cacheManager, contextFactory)
    {
        _libraryService = libraryService;
        _artistService = artistService;
        _albumService = albumService;
        _songService = songService;
        _playlistService = playlistService;
        _podcastService = podcastService;
        _bus = bus;
        _configurationFactory = configurationFactory;

        _userAuthenticationService = userAuthenticationService
            ?? (IUserAuthenticationService)new UserAuthenticationService(
                logger, passwordHashService, openSubsonicSecretProtector, bus,
                userProfileService ?? new UserProfileService(logger, cacheManager, contextFactory, configurationFactory,
                    _libraryService, _artistService, _albumService, _songService, _playlistService,
                    _podcastService, bus, passwordHashService, openSubsonicSecretProtector),
                configurationFactory);
        _userProfileService = userProfileService ?? new UserProfileService(logger, cacheManager, contextFactory, configurationFactory,
            _libraryService, _artistService, _albumService, _songService, _playlistService,
            _podcastService, bus, passwordHashService, openSubsonicSecretProtector);
    }

    public async Task<MelodeeModels.PagedResult<UserDataInfo>> ListAsync(
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.ListAsync(pagedRequest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserSong?> GetUserSongAsync(int userId, Guid songApiKey, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await scopedContext.UserSongs
            .AsNoTracking()
            .Include(us => us.Song)
            .FirstOrDefaultAsync(us => us.UserId == userId && us.Song.ApiKey == songApiKey, cancellationToken)
            .ConfigureAwait(false);
    }



    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] userIds,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.DeleteAsync(userIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetByEmailAddressAsync(
        string emailAddress,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.GetByEmailAddressAsync(emailAddress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _userProfileService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsUserAdminAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _userProfileService.IsUserAdminAsync(username, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetByApiKeyAsync(Guid apiKey, CancellationToken cancellationToken = default)
    {
        return await _userProfileService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserArtist?> UserArtistAsync(int userId, Guid artistApiKey,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.UserArtistAsync(userId, artistApiKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserAlbum?> UserAlbumAsync(int userId, Guid albumApiKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.UserAlbums
            .AsNoTracking()
            .Include(ua => ua.Album)
            .Where(ua => ua.UserId == userId && ua.Album.ApiKey == albumApiKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UserSong?> UserSongAsync(int userId, Guid songApiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.UserSongs
            .AsNoTracking()
            .Include(us => us.Song)
            .Where(us => us.UserId == userId && us.Song.ApiKey == songApiKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<UserSong?[]>> UserLastPlayedSongsAsync(int userId, int count, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var data = await scopedContext.UserSongs
            .AsNoTracking()
            .Where(us => us.UserId == userId)
            .OrderByDescending(us => us.LastPlayedAt)
            .Take(count)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<UserSong?[]>
        {
            Data = data.Cast<UserSong?>().ToArray()
        };
    }

    public async Task<UserSong[]?> UserSongsForAlbumAsync(int userId, Guid albumApiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.UserSongs
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .Where(us => us.UserId == userId && us.Song.Album.ApiKey == albumApiKey)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<UserSong[]?> UserSongsForPlaylistAsync(int userId, Guid playlistApiKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Get all song IDs for the given playlist through Songs relationship
        var playlistSongIds = await scopedContext.Playlists
            .Where(p => p.ApiKey == playlistApiKey)
            .SelectMany(p => p.Songs)
            .Select(ps => ps.SongId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!playlistSongIds.Any())
        {
            return [];
        }

        // Get user songs for songs in the playlist
        return await scopedContext.UserSongs
            .AsNoTracking()
            .Include(us => us.Song)
            .Where(us => us.UserId == userId && playlistSongIds.Contains(us.SongId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Return all shares that user created.
    /// </summary>
    public async Task<Share[]?> UserSharesAsync(int userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.Shares
            .Include(s => s.User)
            .Where(s => s.UserId == userId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Generate a salt.</summary>
    /// <param name="saltLength">Length of the salt to generate</param>
    /// <param name="logRounds">
    ///     The log2 of the number of rounds of hashing to apply. The work factor therefore increases as (2
    ///     ** logRounds).
    /// </param>
    /// <returns>An encoded salt value.</returns>
    public static string GenerateSalt(int saltLength = 16, int logRounds = 10)
    {
        var randomBytes = new byte[saltLength];
        RandomNumberGenerator.Create().GetBytes(randomBytes);

        var rs = new StringBuilder(randomBytes.Length * 2 + 8);

        rs.Append("$2a$");
        if (logRounds < 10)
        {
            rs.Append('0');
        }

        rs.Append(logRounds);
        rs.Append('$');
        rs.Append(Encoding.UTF8.GetString(randomBytes).ToBase64());

        return rs.ToString();
    }

    public async Task<bool> IsPinned(int userId, UserPinType pinType, int pinId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.UserPins
            .Where(up => up.UserId == userId && up.PinId == pinId && up.PinType == (int)pinType)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

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

                // Update album calculated rating using EF Core instead of raw SQL
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

                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
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

                // Update song calculated rating using EF Core instead of raw SQL
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

                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
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

                // Update artist calculated rating using EF Core instead of raw SQL
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

                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
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
                    .ConfigureAwait(false);

                await scopedContext.Albums
                    .Where(a => a.Id == userAlbum.AlbumId)
                    .ExecuteUpdateAsync(a =>
                        a.SetProperty(aa => aa.CalculatedRating, avgRating), cancellationToken)
                    .ConfigureAwait(false);

                await _albumService.ClearCacheAsync(userAlbum.AlbumId, cancellationToken).ConfigureAwait(false);

                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetSongRatingAsync(
        int userId,
        Guid songApiKey,
        int rating,
        CancellationToken cancellationToken = default)
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
                    .ConfigureAwait(false);

                await scopedContext.Songs
                    .Where(s => s.Id == userSong.SongId)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(ss => ss.CalculatedRating, avgRating), cancellationToken)
                    .ConfigureAwait(false);

                await _songService.ClearCacheAsync(userSong.SongId, cancellationToken).ConfigureAwait(false);

                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SaveProfileImageAsync(int userId, byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.SaveProfileImageAsync(userId, imageBytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a user in using their username and password.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> LoginUserByUsernameAsync(
        string userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return await _userAuthenticationService.LoginUserByUsernameAsync(userName, password, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a user in using their email address and password.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> LoginUserAsync(
        string emailAddress,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return await _userAuthenticationService.LoginUserAsync(emailAddress, password, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates credentials and applies login side effects for an identified user.
    /// </summary>
    private async Task<MelodeeModels.OperationResult<User?>> CompleteLoginAsync(
        User user,
        string password,
        string identifier,
        CancellationToken cancellationToken)
    {
        return await _userAuthenticationService.CompleteLoginAsync(user, password, identifier, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<User?>> ValidateTokenAsync(string username, string token, string salt, CancellationToken cancellationToken = default)
    {
        return await _userAuthenticationService.ValidateTokenAsync(username, token, salt, cancellationToken).ConfigureAwait(false);
    }

    private static string GenerateOpenSubsonicSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public async Task<MelodeeModels.OperationResult<int>> ImportUserFavoriteSongs(
        UserFavoriteSongConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(configuration, nameof(configuration));

        var user = await GetByApiKeyAsync(configuration.UserApiKey, cancellationToken).ConfigureAwait(false);
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

            using (var reader = new StreamReader(csvFilenfo.OpenRead(), Encoding.UTF8))
            {
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    using (var dr = new CsvDataReader(csv))
                    {
                        var dt = new DataTable();
                        dt.Columns.Add(configuration.ArtistColumn, typeof(string));
                        dt.Columns.Add(configuration.AlbumColumn, typeof(string));
                        dt.Columns.Add(configuration.SongColumn, typeof(string));

                        dt.Load(dr);
                        songsFromCsv = dt.Rows.Count;
                        foreach (DataRow row in dt.Rows)
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
                                        "[{ServiceName}] ImportUserFavoriteSongs failed : UNKNOWN ARTIST : [{ArtistName}] [{AlbumName}] [{SongName}]",
                                        nameof(UserService),
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
                                        "[{ServiceName}] ImportUserFavoriteSongs failed : UNKNOWN ALBUM : [{ArtistName}] [{AlbumName}] [{SongName}]",
                                        nameof(UserService),
                                        artist,
                                        album,
                                        song);
                                    continue;
                                }

                                var dbSongInfo =
                                    await DatabaseSongInfosForAlbumApiKey(artistAlbum.ApiKey, user.Data.Id,
                                        cancellationToken).ConfigureAwait(false);
                                var albumSong = dbSongInfo?.FirstOrDefault(x => x.Name.ToNormalizedString() == song);
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
                                            ?.FirstOrDefault(x => x.Name.ToNormalizedString() == song);
                                    }

                                    if (albumSong == null)
                                    {
                                        Log.Warning(
                                            "[{ServiceName}] ImportUserFavoriteSongs failed : UNKNOWN SONG : [{ArtistName}] [{AlbumName}] [{SongName}]",
                                            nameof(UserService),
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
            "[{ServiceName}] ImportUserFavoriteSongs {Pretend} [{UserApiKey}] Songs From Csv [{CsvSongCount}] found {RecordsFound} created {RecordsCreated} records, updated {RecordsUpdated} records, missing [{MissingCount}]",
            nameof(UserService),
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

    public async Task<MelodeeModels.OperationResult<User?>> RegisterAsync(string username,
        string emailAddress,
        string plainTextPassword,
        string? registerPrivateCode,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.RegisterAsync(username, emailAddress, plainTextPassword, registerPrivateCode, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(User currentUser, User detailToUpdate,
        CancellationToken cancellationToken = default)
    {
        return await _userProfileService.UpdateAsync(currentUser, detailToUpdate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateLastLogin(UserLoginEvent eventData,
        CancellationToken cancellationToken = default)
    {
        using (Operation.At(LogEventLevel.Debug)
                   .Time("[{ServiceName}]: Data [{EventData}]", nameof(UserService), eventData.ToString()))
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken))
            {
                var user = await GetAsync(eventData.UserId, cancellationToken).ConfigureAwait(false);
                if (user.Data != null)
                {
                    Trace.WriteLine($"[{nameof(UpdateLastLogin)}]: {eventData}");
                    await scopedContext.Users
                        .Where(x => x.Id == eventData.UserId)
                        .ExecuteUpdateAsync(setters =>
                            setters.SetProperty(x => x.LastActivityAt, now)
                                .SetProperty(x => x.LastLoginAt, now), cancellationToken).ConfigureAwait(false);
                    ClearCache(user.Data.Email, user.Data.ApiKey, user.Data.Id, user.Data.UserName);
                    // Prefetch as the user is clearly active
                    await GetAsync(eventData.UserId, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    private void ClearCache(User user)
    {
        ClearCache(user.Email, user.ApiKey, user.Id, user.UserName);
    }

    private void ClearCache(string? emailAddress, Guid? apiKey, int? id, string? username)
    {
        if (emailAddress != null)
        {
            CacheManager.Remove(
                CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(emailAddress.ToNormalizedString() ?? emailAddress));
        }

        if (apiKey != null)
        {
            CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey));
        }

        if (id != null)
        {
            CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(id));
        }

        if (username != null)
        {
            CacheManager.Remove(CacheKeyDetailByUsernameTemplate.FormatSmart(username));
        }
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleGenreHatedAsync(int userId, string genre,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var user = await scopedContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
                .ConfigureAwait(false);
            if (user != null)
            {
                var normalizedGenre = genre.ToNormalizedString() ?? genre;
                var hatedGenres = user.HatedGenres.ToTags()?.ToList() ?? [];
                if (hatedGenres.Contains(normalizedGenre))
                {
                    hatedGenres.Remove(normalizedGenre);
                }
                else
                {
                    hatedGenres.Add(normalizedGenre);
                }

                user.HatedGenres = "".AddTags(hatedGenres);
                user.LastUpdatedAt = now;
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
                ClearCache(user);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> SetLastFmSessionKeyAsync(int userId, string? sessionKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var user = await scopedContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
                .ConfigureAwait(false);
            if (user == null)
            {
                return new MelodeeModels.OperationResult<bool>([$"User {userId} not found"])
                {
                    Data = false,
                    Type = MelodeeModels.OperationResponseType.NotFound
                };
            }

            user.LastFmSessionKey = sessionKey.Nullify();
            user.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            ClearCache(user);
            await GetAsync(user.Id, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleAristHatedAsync(int userId, Guid artistApiKey,
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleAristStarAsync(int userId, Guid artistApiKey,
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleArtistStarAsync(int userId, Guid albumApiKey,
        bool isStarred, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = false;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var artist = await _artistService.GetByApiKeyAsync(albumApiKey, cancellationToken).ConfigureAwait(false);
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> TogglePinnedAsync(int userId, UserPinType pinType, int pinId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        bool result;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userPinTypeValue = (int)pinType;
            var userPin = await scopedContext
                .UserPins
                .Where(x => x.UserId == userId && x.PinId == pinId && x.PinType == userPinTypeValue)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (userPin == null)
            {
                userPin = new UserPin
                {
                    UserId = userId,
                    PinId = pinId,
                    PinType = userPinTypeValue,
                    CreatedAt = now
                };
                scopedContext.UserPins.Add(userPin);
            }
            else
            {
                scopedContext.UserPins.Remove(userPin);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
            ClearCache(user.Data!);
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
                var user = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
                ClearCache(user.Data!);
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Gets all bookmarks for a user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<Bookmark[]>> GetBookmarksAsync(int userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bookmarks = await scopedContext.Bookmarks
            .Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
            .Include(x => x.Song).ThenInclude(x => x.UserSongs.Where(ua => ua.UserId == userId))
            .Where(x => x.UserId == userId)
            .AsSplitQuery()
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();
        Logger.Debug("[UserService] GetBookmarksAsync loaded {Count} bookmarks for user {UserId} in {ElapsedMs} ms",
            bookmarks.Length, userId, sw.ElapsedMilliseconds);

        return new MelodeeModels.OperationResult<Bookmark[]>
        {
            Data = bookmarks
        };
    }

    /// <summary>
    /// Creates a bookmark for a song at a specific position.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> CreateBookmarkAsync(int userId, Guid songApiKey, int positionMs, string? comment, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x == Guid.Empty, songApiKey, nameof(songApiKey));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var song = await scopedContext.Songs
            .FirstOrDefaultAsync(x => x.ApiKey == songApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (song != null)
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var existingBookmark = await scopedContext.Bookmarks
                .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == song.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existingBookmark != null)
            {
                existingBookmark.LastUpdatedAt = now;
                existingBookmark.Comment = comment;
                existingBookmark.Position = positionMs;
            }
            else
            {
                var newBookmark = new Bookmark
                {
                    CreatedAt = now,
                    UserId = userId,
                    SongId = song.Id,
                    Comment = comment,
                    Position = positionMs
                };
                scopedContext.Bookmarks.Add(newBookmark);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Deletes a bookmark for a song.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> DeleteBookmarkAsync(int userId, Guid songApiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x == Guid.Empty, songApiKey, nameof(songApiKey));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var song = await scopedContext.Songs
            .FirstOrDefaultAsync(x => x.ApiKey == songApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (song != null)
        {
            var existingBookmark = await scopedContext.Bookmarks
                .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == song.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existingBookmark != null)
            {
                scopedContext.Bookmarks.Remove(existingBookmark);
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    #region Password Reset

    /// <summary>
    ///     Generates a password reset token for the user with the given email.
    ///     Returns the token if successful, null if user not found.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<string?>> GeneratePasswordResetTokenAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        var emailNormalized = email.ToNormalizedString() ?? email.ToLowerInvariant();

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.EmailNormalized == emailNormalized, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<string?>("User not found")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        if (user.IsLocked)
        {
            return new MelodeeModels.OperationResult<string?>("User is locked")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.AccessDenied
            };
        }

        // Generate a secure random token
        var tokenBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        // Get token expiry from settings (default 60 minutes)
        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var expiryMinutes = configuration.GetValue<int?>(SettingRegistry.SecurityPasswordResetTokenExpiryMinutes) ?? 60;

        // Set token expiration
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(expiryMinutes));
        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(user);

        return new MelodeeModels.OperationResult<string?>
        {
            Data = token
        };
    }

    /// <summary>
    ///     Validates a password reset token and returns the user if valid.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> ValidatePasswordResetTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(token, nameof(token));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.PasswordResetToken == token, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<User?>("Invalid token")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        if (user.PasswordResetTokenExpiresAt == null ||
            user.PasswordResetTokenExpiresAt < SystemClock.Instance.GetCurrentInstant())
        {
            return new MelodeeModels.OperationResult<User?>("Token has expired")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        return new MelodeeModels.OperationResult<User?>
        {
            Data = user
        };
    }

    /// <summary>
    ///     Resets the user's password using a valid reset token.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> ResetPasswordWithTokenAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(token, nameof(token));
        Guard.Against.NullOrWhiteSpace(newPassword, nameof(newPassword));

        var validationResult = await ValidatePasswordResetTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsSuccess || validationResult.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>(validationResult.Messages ?? ["Invalid or expired token"])
            {
                Data = false,
                Type = validationResult.Type
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstAsync(u => u.Id == validationResult.Data.Id, cancellationToken)
            .ConfigureAwait(false);

        // Encrypt the new password using the user's public key
        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var encryptionKey = configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey);
        user.PasswordEncrypted = EncryptionHelper.Encrypt(encryptionKey!, newPassword, user.PublicKey);

        // Clear the reset token
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;

        // Confirm email when user successfully resets password via email link
        // This validates they have access to the email account
        if (user.EmailConfirmedDate == null)
        {
            user.EmailConfirmedDate = SystemClock.Instance.GetCurrentInstant();
        }

        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(user);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    #endregion

    #region Genre Favorites

    /// <summary>
    ///     Toggles the starred status for a genre for the user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> ToggleGenreStarAsync(
        int userId,
        string genreName,
        bool isStarred,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(genreName, nameof(genreName));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<bool>("User not found")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var normalizedGenre = genreName.ToUpperInvariant().Trim();
        var starredGenres = ParsePipeSeparatedList(user.StarredGenres);

        if (isStarred)
        {
            if (!starredGenres.Contains(normalizedGenre))
            {
                starredGenres.Add(normalizedGenre);
            }
        }
        else
        {
            starredGenres.Remove(normalizedGenre);
        }

        user.StarredGenres = starredGenres.Count > 0 ? string.Join("|", starredGenres) : null;
        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(user);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    /// <summary>
    ///     Toggles the hated status for a genre for the user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> ToggleGenreHatedAsync(
        int userId,
        string genreName,
        bool isHated,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(genreName, nameof(genreName));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<bool>("User not found")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var normalizedGenre = genreName.ToUpperInvariant().Trim();
        var hatedGenres = ParsePipeSeparatedList(user.HatedGenres);

        if (isHated)
        {
            if (!hatedGenres.Contains(normalizedGenre))
            {
                hatedGenres.Add(normalizedGenre);
            }
        }
        else
        {
            hatedGenres.Remove(normalizedGenre);
        }

        user.HatedGenres = hatedGenres.Count > 0 ? string.Join("|", hatedGenres) : null;
        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(user);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    /// <summary>
    ///     Gets the list of starred genres for the user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<string[]>> GetStarredGenresAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<string[]>("User not found")
            {
                Data = [],
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<string[]>
        {
            Data = ParsePipeSeparatedList(user.StarredGenres).ToArray()
        };
    }

    /// <summary>
    ///     Gets the list of hated genres for the user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<string[]>> GetHatedGenresAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<string[]>("User not found")
            {
                Data = [],
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<string[]>
        {
            Data = ParsePipeSeparatedList(user.HatedGenres).ToArray()
        };
    }

    private static List<string> ParsePipeSeparatedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToList();
    }

    #endregion

    #region Social Login

    /// <summary>
    /// Gets a user by their social login provider and subject.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> GetUserBySocialLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(subject, nameof(subject));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogin = await scopedContext.UserSocialLogins
            .Include(sl => sl.User)
            .ThenInclude(u => u.Pins)
            .AsNoTracking()
            .FirstOrDefaultAsync(sl => sl.Provider == provider && sl.Subject == subject, cancellationToken)
            .ConfigureAwait(false);

        if (socialLogin == null)
        {
            return new MelodeeModels.OperationResult<User?>("Social login not found")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<User?>
        {
            Data = socialLogin.User
        };
    }

    /// <summary>
    /// Links a social login to an existing user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> LinkSocialLoginAsync(
        int userId,
        string provider,
        string subject,
        string? email,
        string? displayName,
        string? hostedDomain,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(subject, nameof(subject));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Check if this social login is already linked to another user
        var existingLink = await scopedContext.UserSocialLogins
            .FirstOrDefaultAsync(sl => sl.Provider == provider && sl.Subject == subject, cancellationToken)
            .ConfigureAwait(false);

        if (existingLink != null)
        {
            if (existingLink.UserId == userId)
            {
                // Already linked to this user - update last login
                existingLink.LastLoginAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new MelodeeModels.OperationResult<bool> { Data = true };
            }

            return new MelodeeModels.OperationResult<bool>("This social account is already linked to another user")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var socialLogin = new UserSocialLogin
        {
            UserId = userId,
            Provider = provider,
            Subject = subject,
            Email = email,
            DisplayName = displayName,
            HostedDomain = hostedDomain,
            LastLoginAt = now,
            CreatedAt = now
        };

        scopedContext.UserSocialLogins.Add(socialLogin);
        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    /// <summary>
    /// Unlinks a social login from a user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> UnlinkSocialLoginAsync(
        int userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogin = await scopedContext.UserSocialLogins
            .FirstOrDefaultAsync(sl => sl.UserId == userId && sl.Provider == provider, cancellationToken)
            .ConfigureAwait(false);

        if (socialLogin == null)
        {
            return new MelodeeModels.OperationResult<bool>("Social login not found")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        scopedContext.UserSocialLogins.Remove(socialLogin);
        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    /// <summary>
    /// Gets all social logins for a user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<UserSocialLogin[]>> GetUserSocialLoginsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogins = await scopedContext.UserSocialLogins
            .Where(sl => sl.UserId == userId)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<UserSocialLogin[]> { Data = socialLogins };
    }

    /// <summary>
    /// Gets linked providers for a user in a simple format (provider name and email).
    /// </summary>
    public async Task<MelodeeModels.OperationResult<MelodeeModels.LinkedProviderInfo[]>> GetLinkedProvidersAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogins = await scopedContext.UserSocialLogins
            .Where(sl => sl.UserId == userId)
            .Select(sl => new MelodeeModels.LinkedProviderInfo
            {
                Provider = sl.Provider,
                Email = sl.Email,
                LinkedAt = sl.CreatedAt.ToDateTimeUtc()
            })
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<MelodeeModels.LinkedProviderInfo[]> { Data = socialLogins };
    }

    /// <summary>
    /// Updates the last login timestamp for a social login.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<bool>> UpdateSocialLoginLastLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(subject, nameof(subject));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var updated = await scopedContext.UserSocialLogins
            .Where(sl => sl.Provider == provider && sl.Subject == subject)
            .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.LastLoginAt, now), cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<bool> { Data = updated > 0 };
    }

    /// <summary>
    /// Creates a new user from Google identity.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> CreateUserFromGoogleAsync(
        string googleSubject,
        string email,
        string displayName,
        string? hostedDomain,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(googleSubject, nameof(googleSubject));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));
        Guard.Against.NullOrWhiteSpace(displayName, nameof(displayName));

        // Check if user with this email already exists
        var existingUser = await GetByEmailAddressAsync(email, cancellationToken).ConfigureAwait(false);
        if (existingUser.IsSuccess && existingUser.Data != null)
        {
            return new MelodeeModels.OperationResult<User?>("User with this email already exists. Please log in with password and link your Google account.")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        // Generate a unique username from email or display name
        var baseUsername = email.Split('@')[0].Replace(".", "_").Replace("+", "_");
        var username = baseUsername;

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Ensure username is unique
        var usernameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant();
        var counter = 1;
        while (await scopedContext.Users.AnyAsync(u => u.UserNameNormalized == usernameNormalized, cancellationToken).ConfigureAwait(false))
        {
            username = $"{baseUsername}{counter}";
            usernameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant();
            counter++;
        }

        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        // Generate a random password (user can reset if needed, or continue using Google)
        var randomPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var newUser = new User
        {
            UserName = username,
            UserNameNormalized = usernameNormalized,
            Email = email,
            EmailNormalized = email.ToNormalizedString() ?? email.ToUpperInvariant(),
            PublicKey = usersPublicKey,
            PasswordEncrypted = EncryptionHelper.Encrypt(
                configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                randomPassword,
                usersPublicKey),
            CreatedAt = now,
            LastActivityAt = now,
            LastLoginAt = now
        };

        scopedContext.Users.Add(newUser);

        if (await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) < 1)
        {
            return new MelodeeModels.OperationResult<User?>("Failed to create user")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Error
            };
        }

        // Check if this is the first user - make them admin
        var dbUserCount = await scopedContext.Users.CountAsync(cancellationToken).ConfigureAwait(false);
        if (dbUserCount == 1)
        {
            await scopedContext.Users
                .Where(x => x.Id == newUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsAdmin, true), cancellationToken)
                .ConfigureAwait(false);
            newUser.IsAdmin = true;
        }

        // Link the Google account
        var socialLogin = new UserSocialLogin
        {
            UserId = newUser.Id,
            Provider = "Google",
            Subject = googleSubject,
            Email = email,
            DisplayName = displayName,
            HostedDomain = hostedDomain,
            LastLoginAt = now,
            CreatedAt = now
        };

        scopedContext.UserSocialLogins.Add(socialLogin);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache(newUser.EmailNormalized, newUser.ApiKey, newUser.Id, newUser.UserNameNormalized);

        return new MelodeeModels.OperationResult<User?> { Data = newUser };
    }

    #endregion

    private void ClearUserCache(User user)
    {
        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(user.Id));
        CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(user.ApiKey));
        CacheManager.Remove(CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(user.EmailNormalized));
        CacheManager.Remove(CacheKeyDetailByUsernameTemplate.FormatSmart(user.UserNameNormalized));
    }
}
