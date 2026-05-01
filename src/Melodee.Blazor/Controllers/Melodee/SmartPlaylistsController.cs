using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Models;
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
[RequireCapability(UserCapability.Playlist)]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/playlists/smart")]
public sealed class SmartPlaylistsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    ISmartPlaylistService smartPlaylistService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private static string GetErrorMessage(OperationResult<SmartPlaylistDto> result)
    {
        return result.Errors?.FirstOrDefault()?.Message ?? "An error occurred";
    }

    private static string GetErrorMessage(OperationResult<bool> result)
    {
        return result.Errors?.FirstOrDefault()?.Message ?? "An error occurred";
    }

    /// <summary>
    /// Create a new smart playlist with an MQL query.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SmartPlaylistModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] Services.CreateSmartPlaylistRequest request,
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

        var result = await smartPlaylistService.CreateAsync(request, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return BadRequest(new ApiError("CreateFailed", GetErrorMessage(result)));
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetByIdAsync), new { id = result.Data.ApiKey },
            result.Data.ToSmartPlaylistModel(baseUrl, user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// Get a smart playlist by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(SmartPlaylistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
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

        var result = await smartPlaylistService.GetByApiKeyAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return NotFound(new ApiError("NotFound", "Smart playlist not found"));
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(result.Data.ToSmartPlaylistModel(baseUrl, user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// List all smart playlists for the current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SmartPlaylistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        short page,
        short pageSize,
        string? orderBy,
        string? orderDirection,
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

        var paging = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize,
            OrderBy = new Dictionary<string, string>
            {
                { orderBy ?? "CreatedAt", orderDirection ?? PagedRequest.OrderDescDirection }
            }
        };

        var result = await smartPlaylistService.ListByUserAsync(user.Id, paging, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            meta = new PaginationMetadata(
                result.TotalCount,
                validatedPageSize,
                validatedPage,
                result.TotalPages
            ),
            data = result.Data.Select(x => x.ToSmartPlaylistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray()
        });
    }

    /// <summary>
    /// Update a smart playlist.
    /// </summary>
    [HttpPut]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(SmartPlaylistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] Services.UpdateSmartPlaylistRequest request,
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

        var result = await smartPlaylistService.UpdateAsync(id, request, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            var errorMessage = GetErrorMessage(result);
            if (errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new ApiError("NotFound", errorMessage));
            }

            return BadRequest(new ApiError("UpdateFailed", errorMessage));
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(result.Data.ToSmartPlaylistModel(baseUrl, user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// Delete a smart playlist.
    /// </summary>
    [HttpDelete]
    [Route("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
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

        var result = await smartPlaylistService.DeleteAsync(id, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var errorMessage = GetErrorMessage(result);
            if (errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new ApiError("NotFound", errorMessage));
            }

            return BadRequest(new ApiError("DeleteFailed", errorMessage));
        }

        return NoContent();
    }

    /// <summary>
    /// Evaluate a smart playlist and return results.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}/evaluate")]
    [ProducesResponseType(typeof(SmartPlaylistEvaluateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EvaluateAsync(
        Guid id,
        short page,
        short pageSize,
        string? orderBy,
        string? orderDirection,
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

        var paging = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize,
            OrderBy = new Dictionary<string, string>
            {
                { orderBy ?? "CreatedAt", orderDirection ?? PagedRequest.OrderDescDirection }
            }
        };

        var playlistResult = await smartPlaylistService.GetByApiKeyAsync(id, user.Id, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return NotFound(new ApiError("NotFound", "Smart playlist not found"));
        }

        var result = await smartPlaylistService.EvaluateAsync(id, paging, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new SmartPlaylistEvaluateResponse(
            playlistResult.Data.ToSmartPlaylistModel(baseUrl, user.ToUserModel(baseUrl)),
            new PaginationMetadata(
                result.TotalCount,
                validatedPageSize,
                validatedPage,
                result.TotalPages
            ),
            result.Data.ToArray()
        ));
    }
}
