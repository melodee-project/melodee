using Melodee.Blazor.Controllers.Jellyfin.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Blazor.Controllers.Jellyfin;

[ApiController]
[Route("api/jf/[controller]")]
[ApiExplorerSettings(GroupName = "jellyfin")]
[EnableRateLimiting("jellyfin-api")]
public class UsersController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory,
    UserService userService,
    ILogger<UsersController> logger) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    /// <summary>
    /// Gets public users for the login screen. Finamp calls this to show available users.
    /// </summary>
    [HttpGet("Public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicUsersAsync(CancellationToken cancellationToken)
    {
        // Return an empty array - Melodee doesn't support public user display for security
        // Clients will fall back to username/password login
        return Ok(Array.Empty<JellyfinUser>());
    }

    [HttpPost("AuthenticateByName")]
    [AllowAnonymous]
    [EnableRateLimiting("jellyfin-auth")]
    public async Task<IActionResult> AuthenticateByNameAsync(
        [FromBody] JellyfinAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Pw))
        {
            logger.LogWarning("JellyfinAuthFailed UserName={UserName} RemoteIp={RemoteIp} Reason={Reason}",
                LogSanitizer.Sanitize(request.Username ?? "[empty]"), LogSanitizer.Sanitize(GetClientBinding()), "Missing credentials");
            return JellyfinBadRequest("Username and password are required.");
        }

        var authenticateResult = await userService.LoginUserByUsernameAsync(request.Username, request.Pw, cancellationToken);
        if (!authenticateResult.IsSuccess || authenticateResult.Data == null)
        {
            logger.LogWarning("JellyfinAuthFailed UserName={UserName} RemoteIp={RemoteIp} Reason={Reason}",
                LogSanitizer.Sanitize(request.Username), LogSanitizer.Sanitize(GetClientBinding()), "Invalid credentials");
            return JellyfinUnauthorized("Invalid username or password.");
        }

        var user = authenticateResult.Data;
        if (user.IsLocked)
        {
            logger.LogWarning("JellyfinAuthFailed UserName={UserName} RemoteIp={RemoteIp} Reason={Reason}",
                LogSanitizer.Sanitize(request.Username), LogSanitizer.Sanitize(GetClientBinding()), "User locked");
            return JellyfinForbidden("User account is locked.");
        }

        var tokenInfo = JellyfinTokenParser.ParseFromRequest(Request);
        var token = JellyfinTokenParser.GenerateToken();
        var tokenPrefix = JellyfinTokenParser.GetTokenPrefix(token);
        var salt = JellyfinTokenParser.GenerateSalt();
        var pepper = await GetTokenPepperAsync(cancellationToken);
        var tokenHash = JellyfinTokenParser.HashToken(token, salt, pepper);

        var config = await GetConfigurationAsync(cancellationToken);
        var expiresHours = config.GetValue<int>(SettingRegistry.JellyfinTokenExpiresAfterHours);
        if (expiresHours <= 0) expiresHours = 168;

        var maxTokens = config.GetValue<int>(SettingRegistry.JellyfinTokenMaxActivePerUser);
        if (maxTokens <= 0) maxTokens = 10;

        var now = Clock.GetCurrentInstant();
        var expiresAt = now.Plus(Duration.FromHours(expiresHours));

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var activeTokenCount = await dbContext.JellyfinAccessTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > now))
            .CountAsync(cancellationToken);

        if (activeTokenCount >= maxTokens)
        {
            var oldestToken = await dbContext.JellyfinAccessTokens
                .Where(t => t.UserId == user.Id && t.RevokedAt == null)
                .OrderBy(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (oldestToken != null)
            {
                oldestToken.RevokedAt = now;
            }
        }

        var jellyfinToken = new JellyfinAccessToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            TokenPrefixHash = tokenPrefix,
            TokenSalt = salt,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            Client = tokenInfo.Client?.Length > 255 ? tokenInfo.Client[..255] : tokenInfo.Client,
            Device = tokenInfo.Device?.Length > 255 ? tokenInfo.Device[..255] : tokenInfo.Device,
            DeviceId = tokenInfo.DeviceId?.Length > 255 ? tokenInfo.DeviceId[..255] : tokenInfo.DeviceId,
            Version = tokenInfo.Version?.Length > 255 ? tokenInfo.Version[..255] : tokenInfo.Version
        };

        dbContext.JellyfinAccessTokens.Add(jellyfinToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("JellyfinTokenIssued UserId={UserId} TokenId={TokenId} Client={Client} DeviceId={DeviceId}",
            LogSanitizer.Sanitize(user.Id.ToString()), jellyfinToken.Id,
            LogSanitizer.Sanitize(tokenInfo.Client ?? "unknown"), LogSanitizer.Sanitize(tokenInfo.DeviceId ?? "unknown"));

        var sessionId = Guid.NewGuid().ToString("N");
        var result = new JellyfinAuthenticationResult
        {
            User = MapUserToJellyfin(user),
            AccessToken = token,
            ServerId = GetServerId(),
            SessionInfo = new JellyfinSessionInfo
            {
                Id = sessionId,
                UserId = ToJellyfinId(user.ApiKey),
                UserName = user.UserName,
                Client = tokenInfo.Client,
                DeviceName = tokenInfo.Device,
                DeviceId = tokenInfo.DeviceId,
                ApplicationVersion = tokenInfo.Version,
                RemoteEndPoint = GetClientBinding(),
                LastActivityDate = FormatInstantForJellyfin(now),
                IsActive = true,
                PlayableMediaTypes = ["Audio"],
                ServerId = GetServerId(),
                SupportedCommands = []
            }
        };

        return Ok(result);
    }

    [HttpGet("Me")]
    public async Task<IActionResult> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        return Ok(MapUserToJellyfin(user));
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetUserByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(userId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid user ID format.");
        }

        if (user.ApiKey != apiKey && !user.IsAdmin)
        {
            return JellyfinForbidden("Cannot access other users.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var targetUser = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.ApiKey == apiKey, cancellationToken);

        if (targetUser == null)
        {
            return JellyfinNotFound("User not found.");
        }

        return Ok(MapUserToJellyfin(targetUser));
    }

    [HttpGet]
    public async Task<IActionResult> GetUsersAsync(CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Users.AsNoTracking();

        if (!user.IsAdmin)
        {
            query = query.Where(u => u.Id == user.Id);
        }

        var users = await query
            .OrderBy(u => u.UserName)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(MapUserToJellyfin).ToArray());
    }

    /// <summary>
    /// Gets the user's library views. Finamp calls /Users/{userId}/Views.
    /// </summary>
    [HttpGet("{userId}/Views")]
    public async Task<IActionResult> GetUserViewsAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var libraries = await dbContext.Libraries
            .AsNoTracking()
            .Where(l => l.Type == (int)Common.Enums.LibraryType.Storage && !l.IsLocked)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync(cancellationToken);

        var views = libraries.Select(lib => new JellyfinUserView
        {
            Name = lib.Name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(lib.ApiKey),
            Etag = ComputeEtag(lib.ApiKey, lib.LastUpdatedAt ?? lib.CreatedAt),
            DateCreated = FormatInstantForJellyfin(lib.CreatedAt),
            CanDelete = false,
            CanDownload = user.HasDownloadRole,
            SortName = lib.Name,
            IsFolder = true,
            Type = "UserView",
            CollectionType = "music",
            PlayAccess = user.HasStreamRole ? "Full" : "None",
            LocationType = "Virtual",
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            UserData = new JellyfinUserItemData
            {
                PlaybackPositionTicks = 0,
                PlayCount = 0,
                IsFavorite = false,
                Played = false,
                Key = lib.ApiKey.ToString("N")
            }
        }).ToArray();

        if (views.Length == 0)
        {
            var defaultViewId = Guid.NewGuid();
            views =
            [
                new JellyfinUserView
                {
                    Name = "Music",
                    ServerId = GetServerId(),
                    Id = defaultViewId.ToString("N"),
                    CanDownload = user.HasDownloadRole,
                    IsFolder = true,
                    Type = "UserView",
                    CollectionType = "music",
                    PlayAccess = user.HasStreamRole ? "Full" : "None",
                    LocationType = "Virtual"
                }
            ];
        }

        return Ok(new JellyfinUserViewsResult
        {
            Items = views,
            TotalRecordCount = views.Length,
            StartIndex = 0
        });
    }

    /// <summary>
    /// Gets items for a user. Finamp calls /Users/{userId}/Items with query params.
    /// Redirects to /Items endpoint which has the full implementation.
    /// </summary>
    [HttpGet("{userId}/Items")]
    public async Task<IActionResult> GetUserItemsAsync(
        string userId,
        [FromQuery] string? includeItemTypes,
        [FromQuery] string? parentId,
        [FromQuery] string? albumArtistIds,
        [FromQuery] string? artistIds,
        [FromQuery] string? albumIds,
        [FromQuery] bool? recursive,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? fields,
        [FromQuery] string? searchTerm,
        [FromQuery] string? genreIds,
        [FromQuery] string? filters,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] bool? enableUserData,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        // Build redirect URL to Items endpoint
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(includeItemTypes)) queryParams.Add($"includeItemTypes={Uri.EscapeDataString(includeItemTypes)}");
        if (!string.IsNullOrEmpty(parentId)) queryParams.Add($"parentId={Uri.EscapeDataString(parentId)}");
        if (!string.IsNullOrEmpty(albumArtistIds)) queryParams.Add($"albumArtistIds={Uri.EscapeDataString(albumArtistIds)}");
        if (!string.IsNullOrEmpty(artistIds)) queryParams.Add($"artistIds={Uri.EscapeDataString(artistIds)}");
        if (!string.IsNullOrEmpty(albumIds)) queryParams.Add($"albumIds={Uri.EscapeDataString(albumIds)}");
        if (recursive.HasValue) queryParams.Add($"recursive={recursive.Value}");
        if (!string.IsNullOrEmpty(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (!string.IsNullOrEmpty(sortOrder)) queryParams.Add($"sortOrder={Uri.EscapeDataString(sortOrder)}");
        if (!string.IsNullOrEmpty(fields)) queryParams.Add($"fields={Uri.EscapeDataString(fields)}");
        if (!string.IsNullOrEmpty(searchTerm)) queryParams.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");
        if (!string.IsNullOrEmpty(genreIds)) queryParams.Add($"genreIds={Uri.EscapeDataString(genreIds)}");
        if (!string.IsNullOrEmpty(filters)) queryParams.Add($"filters={Uri.EscapeDataString(filters)}");
        if (startIndex.HasValue) queryParams.Add($"startIndex={startIndex.Value}");
        if (limit.HasValue) queryParams.Add($"limit={limit.Value}");
        if (enableUserData.HasValue) queryParams.Add($"enableUserData={enableUserData.Value}");
        queryParams.Add($"userId={Uri.EscapeDataString(userId)}");

        var redirectUrl = "/Items" + (queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "");

        // Use internal redirect by returning redirect result
        // Note: This preserves auth headers since it's same origin
        return RedirectPreserveMethod(redirectUrl);
    }

    /// <summary>
    /// Gets a specific item for a user. Finamp calls /Users/{userId}/Items/{itemId}.
    /// Redirects to /Items/{itemId} endpoint.
    /// </summary>
    [HttpGet("{userId}/Items/{itemId}")]
    public async Task<IActionResult> GetUserItemByIdAsync(
        string userId,
        string itemId,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        // Validate itemId to prevent open redirect vulnerability
        // Use strict GUID parsing and reconstruct URL from the parsed value
        if (!Guid.TryParse(itemId, out var validatedItemId))
        {
            return BadRequest(new { error = "Invalid item ID format" });
        }

        // Use validated GUID to construct redirect URL (not the original user input)
        // lgtm[cs/web/unvalidated-url-redirection]
        return RedirectPreserveMethod($"/Items/{validatedItemId}");
    }

    /// <summary>
    /// Marks an item as a favorite. Used by Finamp for favoriting songs/albums/artists.
    /// </summary>
    [HttpPost("{userId}/FavoriteItems/{itemId}")]
    public async Task<IActionResult> AddFavoriteAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(userId, out var userApiKey) || user.ApiKey != userApiKey)
        {
            return JellyfinForbidden("Cannot modify favorites for other users.");
        }

        if (!TryParseJellyfinGuid(itemId, out var itemApiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = Clock.GetCurrentInstant();

        // Check if it's a song first
        var song = await dbContext.Songs
            .FirstOrDefaultAsync(s => s.ApiKey == itemApiKey && !s.IsLocked, cancellationToken);

        if (song != null)
        {
            var existingFavorite = await dbContext.UserSongs
                .FirstOrDefaultAsync(us => us.UserId == user.Id && us.SongId == song.Id, cancellationToken);

            if (existingFavorite == null)
            {
                dbContext.UserSongs.Add(new Common.Data.Models.UserSong
                {
                    UserId = user.Id,
                    SongId = song.Id,
                    IsStarred = true,
                    StarredAt = now,
                    CreatedAt = now
                });
            }
            else
            {
                existingFavorite.IsStarred = true;
                existingFavorite.StarredAt = now;
                existingFavorite.LastUpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new JellyfinUserItemData
            {
                IsFavorite = true,
                Key = itemApiKey.ToString("N")
            });
        }

        // Check if it's an album
        var album = await dbContext.Albums
            .FirstOrDefaultAsync(a => a.ApiKey == itemApiKey && !a.IsLocked, cancellationToken);

        if (album != null)
        {
            var existingFavorite = await dbContext.UserAlbums
                .FirstOrDefaultAsync(ua => ua.UserId == user.Id && ua.AlbumId == album.Id, cancellationToken);

            if (existingFavorite == null)
            {
                dbContext.UserAlbums.Add(new Common.Data.Models.UserAlbum
                {
                    UserId = user.Id,
                    AlbumId = album.Id,
                    IsStarred = true,
                    StarredAt = now,
                    CreatedAt = now
                });
            }
            else
            {
                existingFavorite.IsStarred = true;
                existingFavorite.StarredAt = now;
                existingFavorite.LastUpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new JellyfinUserItemData
            {
                IsFavorite = true,
                Key = itemApiKey.ToString("N")
            });
        }

        // Check if it's an artist
        var artist = await dbContext.Artists
            .FirstOrDefaultAsync(a => a.ApiKey == itemApiKey && !a.IsLocked, cancellationToken);

        if (artist != null)
        {
            var existingFavorite = await dbContext.UserArtists
                .FirstOrDefaultAsync(ua => ua.UserId == user.Id && ua.ArtistId == artist.Id, cancellationToken);

            if (existingFavorite == null)
            {
                dbContext.UserArtists.Add(new Common.Data.Models.UserArtist
                {
                    UserId = user.Id,
                    ArtistId = artist.Id,
                    IsStarred = true,
                    StarredAt = now,
                    CreatedAt = now
                });
            }
            else
            {
                existingFavorite.IsStarred = true;
                existingFavorite.StarredAt = now;
                existingFavorite.LastUpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new JellyfinUserItemData
            {
                IsFavorite = true,
                Key = itemApiKey.ToString("N")
            });
        }

        return JellyfinNotFound("Item not found.");
    }

    /// <summary>
    /// Removes an item from favorites.
    /// </summary>
    [HttpDelete("{userId}/FavoriteItems/{itemId}")]
    public async Task<IActionResult> RemoveFavoriteAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(userId, out var userApiKey) || user.ApiKey != userApiKey)
        {
            return JellyfinForbidden("Cannot modify favorites for other users.");
        }

        if (!TryParseJellyfinGuid(itemId, out var itemApiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = Clock.GetCurrentInstant();

        // Check song favorites
        var song = await dbContext.Songs
            .FirstOrDefaultAsync(s => s.ApiKey == itemApiKey && !s.IsLocked, cancellationToken);

        if (song != null)
        {
            var favorite = await dbContext.UserSongs
                .FirstOrDefaultAsync(us => us.UserId == user.Id && us.SongId == song.Id, cancellationToken);

            if (favorite != null)
            {
                favorite.IsStarred = false;
                favorite.StarredAt = null;
                favorite.LastUpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Ok(new JellyfinUserItemData
            {
                IsFavorite = false,
                Key = itemApiKey.ToString("N")
            });
        }

        // Check album favorites
        var album = await dbContext.Albums
            .FirstOrDefaultAsync(a => a.ApiKey == itemApiKey && !a.IsLocked, cancellationToken);

        if (album != null)
        {
            var favorite = await dbContext.UserAlbums
                .FirstOrDefaultAsync(ua => ua.UserId == user.Id && ua.AlbumId == album.Id, cancellationToken);

            if (favorite != null)
            {
                favorite.IsStarred = false;
                favorite.StarredAt = null;
                favorite.LastUpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Ok(new JellyfinUserItemData
            {
                IsFavorite = false,
                Key = itemApiKey.ToString("N")
            });
        }

        // Check artist favorites
        var artist = await dbContext.Artists
            .FirstOrDefaultAsync(a => a.ApiKey == itemApiKey && !a.IsLocked, cancellationToken);

        if (artist != null)
        {
            var favorite = await dbContext.UserArtists
                .FirstOrDefaultAsync(ua => ua.UserId == user.Id && ua.ArtistId == artist.Id, cancellationToken);

            if (favorite != null)
            {
                favorite.IsStarred = false;
                favorite.StarredAt = null;
                favorite.LastUpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Ok(new JellyfinUserItemData
            {
                IsFavorite = false,
                Key = itemApiKey.ToString("N")
            });
        }

        return JellyfinNotFound("Item not found.");
    }

    /// <summary>
    /// Marks an item as played. Used by Finamp/Streamyfin for tracking playback status.
    /// </summary>
    [HttpPost("{userId}/PlayedItems/{itemId}")]
    public async Task<IActionResult> MarkPlayedAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(userId, out var userApiKey) || user.ApiKey != userApiKey)
        {
            return JellyfinForbidden("Cannot modify played status for other users.");
        }

        if (!TryParseJellyfinGuid(itemId, out var itemApiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = Clock.GetCurrentInstant();

        // Check if it's a song
        var song = await dbContext.Songs
            .FirstOrDefaultAsync(s => s.ApiKey == itemApiKey && !s.IsLocked, cancellationToken);

        if (song != null)
        {
            var existingUserSong = await dbContext.UserSongs
                .FirstOrDefaultAsync(us => us.UserId == user.Id && us.SongId == song.Id, cancellationToken);

            if (existingUserSong == null)
            {
                dbContext.UserSongs.Add(new Common.Data.Models.UserSong
                {
                    UserId = user.Id,
                    SongId = song.Id,
                    PlayedCount = 1,
                    LastPlayedAt = now,
                    CreatedAt = now
                });
            }
            else
            {
                existingUserSong.PlayedCount = existingUserSong.PlayedCount + 1;
                existingUserSong.LastPlayedAt = now;
                existingUserSong.LastUpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new JellyfinUserItemData
            {
                Played = true,
                PlayCount = existingUserSong?.PlayedCount ?? 1,
                LastPlayedDate = FormatInstantForJellyfin(now),
                Key = itemApiKey.ToString("N")
            });
        }

        // Check if it's an album
        var album = await dbContext.Albums
            .FirstOrDefaultAsync(a => a.ApiKey == itemApiKey && !a.IsLocked, cancellationToken);

        if (album != null)
        {
            var existingUserAlbum = await dbContext.UserAlbums
                .FirstOrDefaultAsync(ua => ua.UserId == user.Id && ua.AlbumId == album.Id, cancellationToken);

            if (existingUserAlbum == null)
            {
                dbContext.UserAlbums.Add(new Common.Data.Models.UserAlbum
                {
                    UserId = user.Id,
                    AlbumId = album.Id,
                    PlayedCount = 1,
                    LastPlayedAt = now,
                    CreatedAt = now
                });
            }
            else
            {
                existingUserAlbum.PlayedCount = existingUserAlbum.PlayedCount + 1;
                existingUserAlbum.LastPlayedAt = now;
                existingUserAlbum.LastUpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new JellyfinUserItemData
            {
                Played = true,
                PlayCount = existingUserAlbum?.PlayedCount ?? 1,
                LastPlayedDate = FormatInstantForJellyfin(now),
                Key = itemApiKey.ToString("N")
            });
        }

        return JellyfinNotFound("Item not found.");
    }

    /// <summary>
    /// Marks an item as unplayed. Used by Finamp/Streamyfin for resetting playback status.
    /// </summary>
    [HttpDelete("{userId}/PlayedItems/{itemId}")]
    public async Task<IActionResult> MarkUnplayedAsync(string userId, string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(userId, out var userApiKey) || user.ApiKey != userApiKey)
        {
            return JellyfinForbidden("Cannot modify played status for other users.");
        }

        if (!TryParseJellyfinGuid(itemId, out var itemApiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = Clock.GetCurrentInstant();

        // Check song
        var song = await dbContext.Songs
            .FirstOrDefaultAsync(s => s.ApiKey == itemApiKey && !s.IsLocked, cancellationToken);

        if (song != null)
        {
            var userSong = await dbContext.UserSongs
                .FirstOrDefaultAsync(us => us.UserId == user.Id && us.SongId == song.Id, cancellationToken);

            if (userSong != null)
            {
                userSong.PlayedCount = 0;
                userSong.LastPlayedAt = null;
                userSong.LastUpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Ok(new JellyfinUserItemData
            {
                Played = false,
                PlayCount = 0,
                Key = itemApiKey.ToString("N")
            });
        }

        // Check album
        var album = await dbContext.Albums
            .FirstOrDefaultAsync(a => a.ApiKey == itemApiKey && !a.IsLocked, cancellationToken);

        if (album != null)
        {
            var userAlbum = await dbContext.UserAlbums
                .FirstOrDefaultAsync(ua => ua.UserId == user.Id && ua.AlbumId == album.Id, cancellationToken);

            if (userAlbum != null)
            {
                userAlbum.PlayedCount = 0;
                userAlbum.LastPlayedAt = null;
                userAlbum.LastUpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Ok(new JellyfinUserItemData
            {
                Played = false,
                PlayCount = 0,
                Key = itemApiKey.ToString("N")
            });
        }

        return JellyfinNotFound("Item not found.");
    }

    private static string ComputeEtag(Guid apiKey, Instant lastUpdated)
    {
        var input = $"{apiKey:N}-{lastUpdated.ToUnixTimeTicks()}";
        // NOTE: MD5 is used here for generating ETag values for HTTP caching in Jellyfin API compatibility.
        // This is NOT a cryptographic use - ETags are public cache identifiers, not security tokens.
        // lgtm[cs/weak-crypto] MD5 used for non-cryptographic ETag generation, not for security
        return HashHelper.CreateMd5(input) ?? string.Empty;
    }
}
