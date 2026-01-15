using System.Linq.Expressions;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Melodee.Mql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using AlbumEntity = Melodee.Common.Data.Models.Album;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/albums")]
public sealed class AlbumsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    UserProfileService userProfileService,
    AlbumService albumService,
    IBlacklistService blacklistService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<AlbumsController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<AlbumsController> Logger { get; } = logger;
    private static readonly HashSet<string> AlbumOrderFields =
    [
        nameof(AlbumDataInfo.Name),
        nameof(AlbumDataInfo.ReleaseDate),
        nameof(AlbumDataInfo.SongCount),
        nameof(AlbumDataInfo.Duration),
        nameof(AlbumDataInfo.LastPlayedAt),
        nameof(AlbumDataInfo.PlayedCount),
        nameof(AlbumDataInfo.CalculatedRating)
    ];

    /// <summary>
    /// Get an album by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(Models.Album), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AlbumById(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var albumResult = await albumService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (!albumResult.IsSuccess || albumResult.Data == null)
        {
            return ApiNotFound("Album");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(albumResult.Data.ToAlbumDataInfo().ToAlbumModel(
            baseUrl,
            user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// List all albums with pagination and ordering.
    /// Optional MQL query parameter for advanced filtering (e.g., q="artist:Beatles AND year:>=1970").
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
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

        if (!TryValidateOrdering(orderBy, orderDirection, AlbumOrderFields, out var validatedOrder, out var orderError))
        {
            return orderError!;
        }

        PagedResult<AlbumDataInfo> listResult;
        if (!string.IsNullOrWhiteSpace(q))
        {
            listResult = await SearchAlbumsWithMqlAsync(
                q,
                new PagedRequest
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
            listResult = await albumService.ListAsync(new PagedRequest
            {
                Page = validatedPage,
                PageSize = validatedPageSize,
                OrderBy = new Dictionary<string, string> { { validatedOrder.field, validatedOrder.direction } }
            }, cancellationToken).ConfigureAwait(false);
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
            data = listResult.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray()
        });
    }

    private async Task<PagedResult<AlbumDataInfo>> SearchAlbumsWithMqlAsync(
        string mqlQuery,
        PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var validator = new MqlValidator();
        var validationResult = validator.Validate(mqlQuery, "albums");

        if (!validationResult.IsValid)
        {
            Logger.LogWarning("[AlbumsController] MQL validation failed for query: {Query}. Errors: {Errors}",
                LogSanitizer.Sanitize(mqlQuery),
                string.Join("; ", validationResult.Errors.Select(e => LogSanitizer.Sanitize(e.Message))));

            return new PagedResult<AlbumDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        var tokenizer = new MqlTokenizer();
        var tokens = tokenizer.Tokenize(mqlQuery).ToList();

        var parser = new MqlParser();
        var parseResult = parser.Parse(tokens, "albums");

        if (!parseResult.IsValid || parseResult.Ast == null)
        {
            Logger.LogWarning("[AlbumsController] MQL parse failed for query: {Query}. Errors: {Errors}",
                LogSanitizer.Sanitize(mqlQuery),
                string.Join("; ", parseResult.Errors.Select(e => LogSanitizer.Sanitize(e.Message))));

            return new PagedResult<AlbumDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        var baseQuery = scopedContext.Albums
            .Include(a => a.Artist)
            .Include(a => a.UserAlbums.Where(ua => ua.UserId == userId))
            .AsNoTracking();

        var compiler = new MqlAlbumCompiler();
        Expression<Func<AlbumEntity, bool>> predicate;
        try
        {
            predicate = compiler.Compile(parseResult.Ast, userId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AlbumsController] MQL compilation failed for query: {Query}", LogSanitizer.Sanitize(mqlQuery));
            return new PagedResult<AlbumDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        var filteredQuery = baseQuery.Where(predicate);

        var albumCount = await filteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        AlbumDataInfo[] albums = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var orderedQuery = ApplyAlbumOrdering(filteredQuery, pagedRequest);

            var rawAlbums = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            albums = rawAlbums.Select(a => new AlbumDataInfo(
                a.Id,
                a.ApiKey,
                a.IsLocked,
                a.Name,
                a.NameNormalized,
                null,
                a.Artist.ApiKey,
                a.Artist.Name,
                a.SongCount ?? 0,
                a.Duration,
                a.CreatedAt,
                null,
                a.ReleaseDate,
                (short)a.AlbumStatus
            )
            {
                UserStarred = a.UserAlbums.FirstOrDefault()?.IsStarred ?? false,
                UserRating = a.UserAlbums.FirstOrDefault()?.Rating ?? 0,
                LastPlayedAt = a.LastPlayedAt,
                PlayedCount = a.PlayedCount,
                CalculatedRating = a.CalculatedRating
            }).ToArray();
        }

        stopwatch.Stop();
        Logger.LogDebug("[AlbumsController] MQL search completed in {ElapsedMs}ms. Query: {Query}, Results: {Count}",
            stopwatch.ElapsedMilliseconds,
            LogSanitizer.Sanitize(mqlQuery),
            albumCount);

        return new PagedResult<AlbumDataInfo>
        {
            TotalCount = albumCount,
            TotalPages = pagedRequest.TotalPages(albumCount),
            Data = albums
        };
    }

    private static IQueryable<AlbumEntity> ApplyAlbumOrdering(IQueryable<AlbumEntity> query, PagedRequest pagedRequest)
    {
        var orderByClause = pagedRequest.OrderByValue("Name", PagedRequest.OrderAscDirection);
        var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
        var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

        return fieldName switch
        {
            "name" or "namenormalized" => isDescending ? query.OrderByDescending(a => a.Name) : query.OrderBy(a => a.Name),
            "releasedate" => isDescending ? query.OrderByDescending(a => a.ReleaseDate) : query.OrderBy(a => a.ReleaseDate),
            "songcount" => isDescending ? query.OrderByDescending(a => a.SongCount) : query.OrderBy(a => a.SongCount),
            "duration" => isDescending ? query.OrderByDescending(a => a.Duration) : query.OrderBy(a => a.Duration),
            "createdat" => isDescending ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
            "lastplayedat" => isDescending ? query.OrderByDescending(a => a.LastPlayedAt) : query.OrderBy(a => a.LastPlayedAt),
            "playedcount" => isDescending ? query.OrderByDescending(a => a.PlayedCount) : query.OrderBy(a => a.PlayedCount),
            "calculatedrating" => isDescending ? query.OrderByDescending(a => a.CalculatedRating) : query.OrderBy(a => a.CalculatedRating),
            _ => query.OrderBy(a => a.Name)
        };
    }

    /// <summary>
    /// Get recently added albums.
    /// </summary>
    [HttpGet]
    [Route("recent")]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentlyAddedAsync(short limit, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidateLimit(limit, out var validatedLimit, out var limitError))
        {
            return limitError!;
        }

        var albumRecentResult = await albumService.ListAsync(new PagedRequest
        {
            Page = 1,
            PageSize = validatedLimit,
            OrderBy = new Dictionary<string, string> { { nameof(AlbumDataInfo.CreatedAt), PagedRequest.OrderDescDirection } }
        }, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            meta = new PaginationMetadata(
                albumRecentResult.TotalCount,
                validatedLimit,
                1,
                albumRecentResult.TotalPages
            ),
            data = albumRecentResult.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray()
        });
    }

    /// <summary>
    /// Get songs for an album.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}/songs")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AlbumSongsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var albumResult = await albumService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (!albumResult.IsSuccess || albumResult.Data == null)
        {
            return ApiNotFound("Album");
        }

        var userSongsForAlbum = await userService.UserSongsForAlbumAsync(user.Id, albumResult.Data!.ApiKey, cancellationToken);
        if (userSongsForAlbum != null)
        {
            // Now set the userrating on songs for the album 
            foreach (var song in albumResult.Data.Songs)
            {
                var userSong = userSongsForAlbum.FirstOrDefault(x => x.Song.ApiKey == song.ApiKey);
                if (userSong != null)
                {
                    song.UserSongs.Add(userSong);
                }
            }
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            meta = new PaginationMetadata(
                albumResult.Data.Songs.Count,
                albumResult.Data.SongCount ?? 0,
                1,
                1
            ),
            data = albumResult.Data.Songs.Select(x => x.ToSongDataInfo(x.UserSongs.FirstOrDefault()).ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray()
        });
    }

    /// <summary>
    /// Toggle starred status for an album.
    /// </summary>
    [HttpPost]
    [Route("starred/{apiKey:guid}/{isStarred:bool}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleAlbumStarred(Guid apiKey, bool isStarred, CancellationToken cancellationToken = default)
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

        var toggleStarredResult = await userService.ToggleAlbumStarAsync(user.Id, apiKey, isStarred, cancellationToken).ConfigureAwait(false);
        if (toggleStarredResult.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to toggle star for album for user.");
    }

    /// <summary>
    /// Set rating for an album.
    /// </summary>
    [HttpPost]
    [Route("setrating/{apiKey:guid}/{rating:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetAlbumRating(Guid apiKey, int rating, CancellationToken cancellationToken = default)
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

        var setRatingResult = await userService.SetAlbumRatingAsync(user.Id, apiKey, rating, cancellationToken).ConfigureAwait(false);
        if (setRatingResult.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to set rating for album for user.");
    }

    /// <summary>
    /// Toggle hated status for an album.
    /// </summary>
    [HttpPost]
    [Route("hated/{apiKey:guid}/{isHated:bool}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleAlbumHated(Guid apiKey, bool isHated, CancellationToken cancellationToken = default)
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

        var toggleHatedResult = await userService.ToggleAlbumHatedAsync(user.Id, apiKey, isHated, cancellationToken).ConfigureAwait(false);
        if (toggleHatedResult.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to toggle hated for album for user.");
    }
}
