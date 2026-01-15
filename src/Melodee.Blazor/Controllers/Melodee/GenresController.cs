using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/genres")]
public sealed class GenresController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    UserProfileService userProfileService,
    AlbumService albumService,
    SongService songService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    internal static readonly HashSet<string> GenreOrderFields =
    [
        nameof(Genre.Name),
        nameof(Genre.SongCount),
        nameof(Genre.AlbumCount)
    ];

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
    /// List all genres with pagination and sorting.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GenreListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(int page = 1, int limit = 50, string? orderBy = null, string? orderDirection = null, CancellationToken cancellationToken = default)
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

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        if (!TryValidateOrdering(orderBy, orderDirection, GenreOrderFields, out var validatedOrder, out var orderError))
        {
            return orderError!;
        }

        var genresResult = await albumService.GetGenresAsync(cancellationToken).ConfigureAwait(false);
        if (!genresResult.IsSuccess)
        {
            return ApiBadRequest("Unable to retrieve genres");
        }

        // Convert dictionary to list of Genre models
        var allGenres = genresResult.Data
            .Select(g => new Genre(
                g.Key.ToBase64(), // Use base64 encoded genre name as ID for URL safety
                g.Key,
                g.Value.songCount,
                g.Value.albumCount))
            .ToList();

        // Apply sorting
        var isDescending = validatedOrder.direction.Equals(PagedRequest.OrderDescDirection, StringComparison.OrdinalIgnoreCase);
        allGenres = validatedOrder.field switch
        {
            nameof(Genre.Name) => isDescending
                ? allGenres.OrderByDescending(g => g.Name).ToList()
                : allGenres.OrderBy(g => g.Name).ToList(),
            nameof(Genre.SongCount) => isDescending
                ? allGenres.OrderByDescending(g => g.SongCount).ToList()
                : allGenres.OrderBy(g => g.SongCount).ToList(),
            nameof(Genre.AlbumCount) => isDescending
                ? allGenres.OrderByDescending(g => g.AlbumCount).ToList()
                : allGenres.OrderBy(g => g.AlbumCount).ToList(),
            _ => allGenres.OrderBy(g => g.Name).ToList()
        };

        var totalCount = allGenres.Count;

        // Apply pagination
        var pagedGenres = allGenres
            .Skip((validatedPage - 1) * validatedLimit)
            .Take(validatedLimit)
            .ToArray();

        return Ok(new
        {
            genres = pagedGenres,
            totalCount,
            page = (int)validatedPage,
            limit = (int)validatedLimit
        });
    }

    /// <summary>
    /// Get songs belonging to a specific genre.
    /// </summary>
    [HttpGet]
    [Route("{id}/songs")]
    [ProducesResponseType(typeof(GenreSongsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenreSongsAsync(string id, int page = 1, int limit = 50, CancellationToken cancellationToken = default)
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

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        // Decode genre name from base64 ID
        string genreName;
        try
        {
            genreName = id.FromBase64();
        }
        catch
        {
            return ApiNotFound("Genre");
        }

        if (string.IsNullOrWhiteSpace(genreName))
        {
            return ApiNotFound("Genre");
        }

        var songsResult = await songService.ListByGenreAsync(
            new PagedRequest
            {
                Page = validatedPage,
                PageSize = validatedLimit,
                OrderBy = new Dictionary<string, string> { { nameof(SongDataInfo.Title), PagedRequest.OrderAscDirection } }
            },
            genreName,
            user.Id,
            cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            songs = songsResult.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray(),
            totalCount = songsResult.TotalCount,
            page = (int)validatedPage,
            limit = (int)validatedLimit
        });
    }

    /// <summary>
    /// Toggle starred status for a genre.
    /// </summary>
    [HttpPost]
    [Route("starred/{id}/{isStarred:bool}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleGenreStarred(string id, bool isStarred, CancellationToken cancellationToken = default)
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

        // Decode genre name from base64 ID
        string genreName;
        try
        {
            genreName = id.FromBase64();
        }
        catch
        {
            return ApiBadRequest("Invalid genre ID");
        }

        if (string.IsNullOrWhiteSpace(genreName))
        {
            return ApiBadRequest("Invalid genre ID");
        }

        var result = await userService.ToggleGenreStarAsync(user.Id, genreName, isStarred, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to toggle star for genre.");
    }

    /// <summary>
    /// Toggle hated status for a genre.
    /// </summary>
    [HttpPost]
    [Route("hated/{id}/{isHated:bool}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleGenreHated(string id, bool isHated, CancellationToken cancellationToken = default)
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

        // Decode genre name from base64 ID
        string genreName;
        try
        {
            genreName = id.FromBase64();
        }
        catch
        {
            return ApiBadRequest("Invalid genre ID");
        }

        if (string.IsNullOrWhiteSpace(genreName))
        {
            return ApiBadRequest("Invalid genre ID");
        }

        var result = await userService.ToggleGenreHatedAsync(user.Id, genreName, isHated, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return Ok();
        }

        return ApiBadRequest("Unable to toggle hated for genre.");
    }

    /// <summary>
    /// Get the list of starred genres for the current user.
    /// </summary>
    [HttpGet]
    [Route("starred")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStarredGenres(CancellationToken cancellationToken = default)
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

        var result = await userService.GetStarredGenresAsync(user.Id, cancellationToken).ConfigureAwait(false);

        // Convert genre names to Genre objects with base64 IDs
        var genres = result.Data.Select(g => new Genre(g.ToBase64(), g, 0, 0)).ToArray();

        return Ok(new { genres });
    }

    /// <summary>
    /// Get the list of hated/disliked genres for the current user.
    /// </summary>
    [HttpGet]
    [Route("hated")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetHatedGenres(CancellationToken cancellationToken = default)
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

        var result = await userService.GetHatedGenresAsync(user.Id, cancellationToken).ConfigureAwait(false);

        // Convert genre names to Genre objects with base64 IDs
        var genres = result.Data.Select(g => new Genre(g.ToBase64(), g, 0, 0)).ToArray();

        return Ok(new { genres });
    }
}

// Response types for OpenAPI documentation
public record GenreListResponse(Genre[] Genres, int TotalCount, int Page, int Limit);
public record GenreSongsResponse(Models.Song[] Songs, int TotalCount, int Page, int Limit);
