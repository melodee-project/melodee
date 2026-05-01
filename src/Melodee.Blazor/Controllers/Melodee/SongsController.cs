using System.Linq.Expressions;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Streaming;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Security;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Melodee.Mql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[RequireCapability(UserCapability.Stream)]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/songs")]
public class SongsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    UserProfileService userProfileService,
    SongService songService,
    StreamingLimiter streamingLimiter,
    IConfiguration configuration,
    IBlacklistService blacklistService,
    ILyricPlugin lyricPlugin,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<SongsController> logger,
    UserRatingService userRatingService,
    UserStarService userStarService) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<SongsController> Logger { get; } = logger;
    private static readonly HashSet<string> SongOrderFields =
    [
        nameof(SongDataInfo.Title),
        nameof(SongDataInfo.SongNumber),
        nameof(SongDataInfo.AlbumId),
        nameof(SongDataInfo.PlayedCount),
        nameof(SongDataInfo.Duration),
        nameof(SongDataInfo.LastPlayedAt),
        nameof(SongDataInfo.CalculatedRating)
    ];

    /// <summary>
    /// Get a song by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(Models.Song), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SongById(Guid id, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var songResult = await songService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (!songResult.IsSuccess || songResult.Data == null)
        {
            return ApiNotFound("Song");
        }

        // Try to enrich with user-specific data (rating/starred) via album scope for this song
        UserSong? userSong = null;
        try
        {
            userSong = await userService.GetUserSongAsync(user.Id, songResult.Data.ApiKey, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // best-effort; ignore enrichment failures
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(songResult.Data
            .ToSongDataInfo(userSong)
            .ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding()));
    }

    /// <summary>
    /// Get lyrics for a song by ID.
    /// Returns structured lyrics with optional timing information for synchronized display.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}/lyrics")]
    [ProducesResponseType(typeof(Models.Lyrics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLyricsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var songResult = await songService.GetSongWithPathInfoAsync(id, cancellationToken).ConfigureAwait(false);
        if (!songResult.IsSuccess)
        {
            return ApiNotFound("Song");
        }

        var (song, libraryPath, artistDirectory) = songResult.Data;

        // Try to get structured/synced lyrics first
        var lyricListResult = await lyricPlugin.GetLyricListAsync(
            Path.Combine(libraryPath, artistDirectory).ToFileSystemDirectoryInfo(),
            new FileSystemFileInfo
            {
                Name = song.FileName,
                Size = song.FileSize
            },
            cancellationToken).ConfigureAwait(false);

        if (lyricListResult.IsSuccess && lyricListResult.Data != null)
        {
            var lyricList = lyricListResult.Data;
            return Ok(new Models.Lyrics(
                id,
                lyricList.Lang,
                lyricList.Synced,
                lyricList.Synced ? null : string.Join("\n", lyricList.Line.Select(l => l.Value)),
                lyricList.Line.Select(l => new LyricsLine(l.Value, l.Start)).ToArray(),
                lyricList.DisplayArtist,
                lyricList.DisplayTitle,
                lyricList.Offset));
        }

        // Fall back to plain lyrics
        var lyricsResult = await lyricPlugin.GetLyricsAsync(
            Path.Combine(libraryPath, artistDirectory).ToFileSystemDirectoryInfo(),
            new FileSystemFileInfo
            {
                Name = song.FileName,
                Size = song.FileSize
            },
            cancellationToken).ConfigureAwait(false);

        if (lyricsResult.IsSuccess && lyricsResult.Data != null)
        {
            var lyrics = lyricsResult.Data;
            return Ok(new Models.Lyrics(
                id,
                "und", // undetermined language
                false,
                lyrics.Value,
                null,
                lyrics.Artist.Nullify(),
                lyrics.Title.Nullify(),
                null));
        }

        return ApiNotFound("Lyrics");
    }

    /// <summary>
    /// List all songs with pagination and ordering.
    /// Optional MQL query parameter for advanced filtering (e.g., q="artist:Beatles AND year:>=1970").
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        short page,
        short pageSize,
        string? orderBy,
        string? orderDirection,
        string? q,
        CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
        {
            return pagingError!;
        }

        if (!TryValidateOrdering(orderBy, orderDirection, SongOrderFields, out var validatedOrder, out var orderError))
        {
            return orderError!;
        }

        MelodeeModels.PagedResult<SongDataInfo> listResult;
        if (!string.IsNullOrWhiteSpace(q))
        {
            listResult = await SearchSongsWithMqlAsync(
                q,
                new MelodeeModels.PagedRequest
                {
                    Page = validatedPage,
                    PageSize = validatedPageSize,
                    OrderBy = new Dictionary<string, string> { { validatedOrder.field, validatedOrder.direction } }
                },
                user.Id,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            listResult = await songService.ListAsync(new MelodeeModels.PagedRequest
            {
                Page = validatedPage,
                PageSize = validatedPageSize,
                OrderBy = new Dictionary<string, string> { { validatedOrder.field, validatedOrder.direction } }
            }, user.Id, cancellationToken).ConfigureAwait(false);
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            meta = new PaginationMetadata(
                listResult.TotalCount,
                validatedPageSize,
                validatedPage,
                listResult.TotalPages
            ),
            data = listResult.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray()
        });
    }

    private async Task<MelodeeModels.PagedResult<SongDataInfo>> SearchSongsWithMqlAsync(
        string mqlQuery,
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var validator = new MqlValidator();
        var validationResult = validator.Validate(mqlQuery, "songs");

        if (!validationResult.IsValid)
        {
            Logger.LogWarning("[SongsController] MQL validation failed for query: {Query}. Errors: {Errors}",
                LogSanitizer.Sanitize(mqlQuery),
                string.Join("; ", validationResult.Errors.Select(e => LogSanitizer.Sanitize(e.Message))));

            return new MelodeeModels.PagedResult<SongDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        var tokenizer = new MqlTokenizer();
        var tokens = tokenizer.Tokenize(mqlQuery).ToList();

        var parser = new MqlParser();
        var parseResult = parser.Parse(tokens, "songs");

        if (!parseResult.IsValid || parseResult.Ast == null)
        {
            Logger.LogWarning("[SongsController] MQL parse failed for query: {Query}. Errors: {Errors}",
                LogSanitizer.Sanitize(mqlQuery),
                string.Join("; ", parseResult.Errors.Select(e => LogSanitizer.Sanitize(e.Message))));

            return new MelodeeModels.PagedResult<SongDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        var baseQuery = scopedContext.Songs
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Include(s => s.UserSongs.Where(us => us.UserId == userId))
            .AsNoTracking();

        var compiler = new MqlSongCompiler();
        Expression<Func<global::Melodee.Common.Data.Models.Song, bool>> predicate;
        try
        {
            predicate = compiler.Compile(parseResult.Ast, userId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SongsController] MQL compilation failed for query: {Query}", LogSanitizer.Sanitize(mqlQuery));
            return new MelodeeModels.PagedResult<SongDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        var filteredQuery = baseQuery.Where(predicate);

        var songCount = await filteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var orderedQuery = ApplySongOrdering(filteredQuery, pagedRequest);

            var rawSongs = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawSongs.Select(s => new SongDataInfo(
                s.Id,
                s.ApiKey,
                s.IsLocked,
                s.Title,
                s.TitleNormalized,
                s.SongNumber,
                s.Album.ReleaseDate,
                s.Album.Name,
                s.Album.ApiKey,
                s.Album.Artist.Name,
                s.Album.Artist.ApiKey,
                s.FileSize,
                s.Duration,
                s.CreatedAt,
                s.Tags ?? string.Empty,
                s.UserSongs.FirstOrDefault()?.IsStarred ?? false,
                s.UserSongs.FirstOrDefault()?.Rating ?? 0,
                s.AlbumId,
                s.LastPlayedAt,
                s.PlayedCount,
                s.CalculatedRating
            )).ToArray();
        }

        stopwatch.Stop();
        Logger.LogDebug("[SongsController] MQL search completed in {ElapsedMs}ms. Query: {Query}, Results: {Count}",
            stopwatch.ElapsedMilliseconds,
            LogSanitizer.Sanitize(mqlQuery),
            songCount);

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    private static IQueryable<global::Melodee.Common.Data.Models.Song> ApplySongOrdering(IQueryable<global::Melodee.Common.Data.Models.Song> query, PagedRequest pagedRequest)
    {
        var orderByClause = pagedRequest.OrderByValue("Title", MelodeeModels.PagedRequest.OrderAscDirection);
        var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
        var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

        return fieldName switch
        {
            "title" or "titlenormalized" => isDescending ? query.OrderByDescending(s => s.Title) : query.OrderBy(s => s.Title),
            "songnumber" => isDescending ? query.OrderByDescending(s => s.SongNumber) : query.OrderBy(s => s.SongNumber),
            "albumid" => isDescending ? query.OrderByDescending(s => s.AlbumId) : query.OrderBy(s => s.AlbumId),
            "albumname" => isDescending ? query.OrderByDescending(s => s.Album.Name) : query.OrderBy(s => s.Album.Name),
            "artistname" => isDescending ? query.OrderByDescending(s => s.Album.Artist.Name) : query.OrderBy(s => s.Album.Artist.Name),
            "duration" => isDescending ? query.OrderByDescending(s => s.Duration) : query.OrderBy(s => s.Duration),
            "filesize" => isDescending ? query.OrderByDescending(s => s.FileSize) : query.OrderBy(s => s.FileSize),
            "createdat" => isDescending ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt),
            "releasedate" => isDescending ? query.OrderByDescending(s => s.Album.ReleaseDate) : query.OrderBy(s => s.Album.ReleaseDate),
            "lastplayedat" => isDescending ? query.OrderByDescending(s => s.LastPlayedAt) : query.OrderBy(s => s.LastPlayedAt),
            "playedcount" => isDescending ? query.OrderByDescending(s => s.PlayedCount) : query.OrderBy(s => s.PlayedCount),
            "calculatedrating" => isDescending ? query.OrderByDescending(s => s.CalculatedRating) : query.OrderBy(s => s.CalculatedRating),
            _ => query.OrderBy(s => s.Title)
        };
    }

    /// <summary>
    /// Get recently added songs.
    /// </summary>
    [HttpGet]
    [Route("recent")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentlyAddedAsync(short limit, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (!TryValidateLimit(limit, out var validatedLimit, out var limitError))
        {
            return limitError!;
        }

        var songRecentResult = await songService.ListAsync(new PagedRequest
        {
            Page = 1,
            PageSize = validatedLimit,
            OrderBy = new Dictionary<string, string> { { nameof(AlbumDataInfo.CreatedAt), PagedRequest.OrderDescDirection } }
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            meta = new PaginationMetadata(
                songRecentResult.TotalCount,
                validatedLimit,
                1,
                songRecentResult.TotalPages
            ),
            data = songRecentResult.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray()
        });
    }

    /// <summary>
    /// Toggle starred status for a song.
    /// </summary>
    [HttpPost]
    [Route("starred/{apiKey:guid}/{isStarred:bool}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult>? ToggleSongStarred(Guid apiKey, bool isStarred, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return ApiBlacklisted();
        }

        var toggleStarredResult = await userStarService.ToggleSongStarAsync(user.Id, apiKey, isStarred, cancellationToken).ConfigureAwait(false);
        if (toggleStarredResult.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to toggle star for song for user.");
    }

    /// <summary>
    /// Set rating for a song.
    /// </summary>
    [HttpPost]
    [Route("setrating/{apiKey:guid}/{rating:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult>? SetSongRating(Guid apiKey, int rating, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return ApiBlacklisted();
        }
        var setRatingResult = await userRatingService.SetSongRatingAsync(user.Id, apiKey, rating, cancellationToken).ConfigureAwait(false);
        if (setRatingResult.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to set rating for song for user.");
    }

    /// <summary>
    /// Toggle hated status for a song.
    /// </summary>
    [HttpPost]
    [Route("hated/{apiKey:guid}/{isHated:bool}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleSongHated(Guid apiKey, bool isHated, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return ApiBlacklisted();
        }

        var toggleHatedResult = await userStarService.ToggleSongHatedAsync(user.Id, apiKey, isHated, cancellationToken).ConfigureAwait(false);
        if (toggleHatedResult.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to toggle hated for song for user.");
    }

    /// <summary>
    /// Stream a song. Requires a valid auth token.
    /// </summary>
    [HttpGet]
    [HttpHead]
    [AllowAnonymous]
    [Route("/song/stream/{apiKey:guid}/{userApiKey:guid}/{authToken}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status206PartialContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StreamSong(Guid apiKey, Guid userApiKey, string authToken, CancellationToken cancellationToken = default)
    {
        var userResult = await userProfileService.GetByApiKeyAsync(userApiKey, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return ApiUnauthorized();
        }

        if (userResult.Data.IsLocked)
        {
            return ApiUserLocked();
        }

        if (await blacklistService.IsEmailBlacklistedAsync(userResult.Data.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(GetRequestIp(HttpContext)).ConfigureAwait(false))
        {
            return ApiBlacklisted();
        }

        var clientBinding = GetClientBinding();
        var hmacService = new HmacTokenService(userResult.Data.PublicKey);
        if (!hmacService.TryValidateTimedToken(authToken.FromBase64(), out var tokenData, out _))
        {
            return ApiUnauthorized("Invalid Auth Token");
        }

        var tokenParts = tokenData.Split(':', 3);
        if (tokenParts.Length < 3 ||
            !Guid.TryParse(tokenParts[0], out var tokenUserId) ||
            !Guid.TryParse(tokenParts[1], out var tokenSongId) ||
            !string.Equals(tokenParts[2], clientBinding, StringComparison.OrdinalIgnoreCase) ||
            tokenUserId != userResult.Data.ApiKey ||
            tokenSongId != apiKey)
        {
            return ApiUnauthorized("Invalid Auth Token");
        }

        // Optional: enforce streaming concurrency limits
        var userKey = $"web:{userResult.Data.Id}";
        if (!await streamingLimiter.TryEnterAsync(userKey, cancellationToken).ConfigureAwait(false))
        {
            return ApiTooManyRequests("Too many concurrent streams");
        }

        // Ensure exit on request completion
        HttpContext.Response.OnCompleted(() => { streamingLimiter.Exit(userKey); return Task.CompletedTask; });

        // Parse range header if present
        var rangeHeader = Request.Headers["Range"].ToString();

        var melodeeConfig = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var useBuffered = melodeeConfig.GetValue<bool?>(SettingRegistry.StreamingUseBufferedResponses) ?? false;

        if (useBuffered)
        {
            // Buffered fallback using descriptor
            var bufferedDescriptorResult = await songService.GetStreamingDescriptorAsync(
                userResult.Data.ToUserInfo(),
                apiKey,
                rangeHeader,
                false,
                cancellationToken).ConfigureAwait(false);

            if (!bufferedDescriptorResult.IsSuccess || bufferedDescriptorResult.Data == null)
            {
                return ApiBadRequest("Unable to load song");
            }
            var bufferedDescriptor = bufferedDescriptorResult.Data;

            foreach (var header in RangeParser.CreateResponseHeaders(bufferedDescriptor, bufferedDescriptor.Range != null ? 206 : 200))
            {
                Response.Headers[header.Key] = header.Value;
            }
            Response.StatusCode = bufferedDescriptor.Range != null ? 206 : 200;

            byte[] bytes;
            if (bufferedDescriptor.Range != null)
            {
                await using var fs = new FileStream(bufferedDescriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(bufferedDescriptor.Range.Start, SeekOrigin.Begin);
                var len = (int)bufferedDescriptor.Range.GetContentLength(bufferedDescriptor.FileSize);
                bytes = new byte[len];
                var read = await fs.ReadAsync(bytes, 0, len, cancellationToken);
                if (read != len) Array.Resize(ref bytes, read);
            }
            else
            {
                bytes = await System.IO.File.ReadAllBytesAsync(bufferedDescriptor.FilePath, cancellationToken);
            }

            return new FileContentResult(bytes, bufferedDescriptor.ContentType ?? "application/octet-stream")
            {
                FileDownloadName = bufferedDescriptor.FileName
            };
        }

        var descriptorResult = await songService.GetStreamingDescriptorAsync(
            userResult.Data.ToUserInfo(),
            apiKey,
            rangeHeader,
            false, // not a download
            cancellationToken).ConfigureAwait(false);

        if (!descriptorResult.IsSuccess || descriptorResult.Data == null)
        {
            return ApiBadRequest("Unable to load song");
        }

        var descriptor = descriptorResult.Data;

        // Handle HEAD requests - return headers only without body
        if (HttpContext.Request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["Content-Length"] = descriptor.FileSize.ToString();
            Response.ContentType = descriptor.ContentType;
            return new EmptyResult();
        }

        // Return FileStreamResult for efficient streaming
        if (descriptor.Range != null)
        {
            // For range requests, manually handle the range and disable ASP.NET range processing
            // to avoid double range header issues
            var fileStream = new FileStream(descriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);

            // Seek to start position
            fileStream.Seek(descriptor.Range.Start, SeekOrigin.Begin);

            // Create a bounded stream for the range
            var rangeStream = new BoundedStream(fileStream, descriptor.Range.GetContentLength(descriptor.FileSize));

            // Set range response headers manually
            Response.StatusCode = 206;
            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["Content-Range"] = descriptor.Range.ToContentRangeHeader(descriptor.FileSize);

            return new FileStreamResult(rangeStream, descriptor.ContentType)
            {
                EnableRangeProcessing = false,
                FileDownloadName = descriptor.IsDownload ? descriptor.FileName : null
            };
        }
        else
        {
            // For full file requests, let ASP.NET handle range processing for future requests
            return new PhysicalFileResult(descriptor.FilePath, descriptor.ContentType)
            {
                EnableRangeProcessing = true,
                FileDownloadName = descriptor.IsDownload ? descriptor.FileName : null
            };
        }
    }

    /// <summary>
    /// Get random songs from the library. Useful for shuffle/radio features.
    /// </summary>
    [HttpGet]
    [Route("random")]
    [ProducesResponseType(typeof(Models.Song[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RandomSongsAsync(
        short? count,
        Guid? artistId,
        Guid? albumId,
        string? genre,
        int? fromYear,
        int? toYear,
        CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var requestedCount = count ?? 50;
        if (requestedCount < 1)
        {
            requestedCount = 50;
        }

        if (requestedCount > 500)
        {
            requestedCount = 500;
        }

        var randomResult = await songService.GetRandomSongsAsync(
            requestedCount,
            user.Id,
            artistId,
            albumId,
            genre,
            fromYear,
            toYear,
            cancellationToken).ConfigureAwait(false);

        if (!randomResult.IsSuccess)
        {
            return ApiBadRequest("Unable to get random songs.");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            count = randomResult.Data.Length,
            data = randomResult.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray()
        });
    }
}
