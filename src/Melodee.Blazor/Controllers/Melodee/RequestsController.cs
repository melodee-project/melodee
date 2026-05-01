using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Filtering;
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
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/requests")]
public sealed class RequestsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    RequestService requestService,
    RequestCommentService commentService,
    RequestActivityService activityService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// List requests with optional filtering and pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RequestPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] short page = 1,
        [FromQuery] short pageSize = 20,
        [FromQuery] string? query = null,
        [FromQuery] bool? mine = null,
        [FromQuery] RequestStatus? status = null,
        [FromQuery] Guid? artistApiKey = null,
        [FromQuery] Guid? albumApiKey = null,
        [FromQuery] Guid? songApiKey = null,
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

        var filters = new List<FilterOperatorInfo>();

        if (mine == true)
        {
            filters.Add(new FilterOperatorInfo("CreatedByUserId", FilterOperator.Equals, user.Id));
        }

        if (status.HasValue)
        {
            filters.Add(new FilterOperatorInfo("Status", FilterOperator.Equals, (int)status.Value));
        }

        if (artistApiKey.HasValue && artistApiKey.Value != Guid.Empty)
        {
            filters.Add(new FilterOperatorInfo("TargetArtistApiKey", FilterOperator.Equals, artistApiKey.Value));
        }

        if (albumApiKey.HasValue && albumApiKey.Value != Guid.Empty)
        {
            filters.Add(new FilterOperatorInfo("TargetAlbumApiKey", FilterOperator.Equals, albumApiKey.Value));
        }

        if (songApiKey.HasValue && songApiKey.Value != Guid.Empty)
        {
            filters.Add(new FilterOperatorInfo("TargetSongApiKey", FilterOperator.Equals, songApiKey.Value));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filters.Add(new FilterOperatorInfo("Query", FilterOperator.Contains, query));
        }

        var pagedRequest = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize,
            FilterBy = filters.ToArray()
        };

        var result = await requestService.ListAsync(pagedRequest, cancellationToken).ConfigureAwait(false);

        var summaries = new List<RequestSummary>();
        foreach (var request in result.Data)
        {
            var commentCount = await requestService.GetCommentCountAsync(request.Id, cancellationToken).ConfigureAwait(false);
            summaries.Add(ToSummary(request, commentCount));
        }

        return Ok(new RequestPagedResponse(
            new PaginationMetadata(result.TotalCount, validatedPageSize, validatedPage, result.TotalPages),
            summaries.ToArray()));
    }

    /// <summary>
    /// Get a request by API key.
    /// </summary>
    [HttpGet("{apiKey:guid}")]
    [ProducesResponseType(typeof(RequestDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByApiKeyAsync(Guid apiKey, CancellationToken cancellationToken = default)
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

        var result = await requestService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data == null)
        {
            return ApiNotFound("Request");
        }

        var commentCount = await requestService.GetCommentCountAsync(result.Data.Id, cancellationToken).ConfigureAwait(false);

        return Ok(ToDetail(result.Data, commentCount));
    }

    /// <summary>
    /// Create a new request.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RequestDetail), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequestRequest request, CancellationToken cancellationToken = default)
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

        if (request.Category == RequestCategory.NotSet)
        {
            return ApiValidationError("Category is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return ApiValidationError("Description is required.");
        }

        var dbRequest = new Request
        {
            Category = (int)request.Category,
            Description = request.Description,
            ArtistName = request.ArtistName,
            TargetArtistApiKey = request.TargetArtistApiKey,
            AlbumTitle = request.AlbumTitle,
            TargetAlbumApiKey = request.TargetAlbumApiKey,
            SongTitle = request.SongTitle,
            TargetSongApiKey = request.TargetSongApiKey,
            ReleaseYear = request.ReleaseYear,
            ExternalUrl = request.ExternalUrl,
            Notes = request.Notes
        };

        var result = await requestService.CreateAsync(dbRequest, user.Id, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data == null)
        {
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Unable to create request.");
        }

        return StatusCode(StatusCodes.Status201Created, ToDetail(result.Data, 0));
    }

    /// <summary>
    /// Update an existing request. Creator only.
    /// </summary>
    [HttpPut("{apiKey:guid}")]
    [ProducesResponseType(typeof(RequestDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(Guid apiKey, [FromBody] UpdateRequestRequest request, CancellationToken cancellationToken = default)
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

        var result = await requestService.UpdateAsync(apiKey, user.Id, r =>
        {
            if (request.Description != null)
            {
                r.Description = request.Description;
            }

            if (request.ArtistName != null)
            {
                r.ArtistName = request.ArtistName;
            }

            if (request.TargetArtistApiKey.HasValue)
            {
                r.TargetArtistApiKey = request.TargetArtistApiKey;
            }

            if (request.AlbumTitle != null)
            {
                r.AlbumTitle = request.AlbumTitle;
            }

            if (request.TargetAlbumApiKey.HasValue)
            {
                r.TargetAlbumApiKey = request.TargetAlbumApiKey;
            }

            if (request.SongTitle != null)
            {
                r.SongTitle = request.SongTitle;
            }

            if (request.TargetSongApiKey.HasValue)
            {
                r.TargetSongApiKey = request.TargetSongApiKey;
            }

            if (request.ReleaseYear.HasValue)
            {
                r.ReleaseYear = request.ReleaseYear;
            }

            if (request.ExternalUrl != null)
            {
                r.ExternalUrl = request.ExternalUrl;
            }

            if (request.Notes != null)
            {
                r.Notes = request.Notes;
            }
        }, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Request"),
                OperationResponseType.AccessDenied => ApiForbidden("Only the creator can update this request."),
                _ => ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Unable to update request.")
            };
        }

        var commentCount = await requestService.GetCommentCountAsync(result.Data.Id, cancellationToken).ConfigureAwait(false);

        return Ok(ToDetail(result.Data, commentCount));
    }

    /// <summary>
    /// Mark a request as completed. Creator only, idempotent.
    /// </summary>
    [HttpPost("{apiKey:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteAsync(Guid apiKey, CancellationToken cancellationToken = default)
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

        var result = await requestService.CompleteAsync(apiKey, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Request"),
                OperationResponseType.AccessDenied => ApiForbidden("Only the creator can complete this request."),
                _ => ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Unable to complete request.")
            };
        }

        return Ok();
    }

    /// <summary>
    /// Delete a request. Creator only, allowed only while Pending.
    /// </summary>
    [HttpDelete("{apiKey:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAsync(Guid apiKey, CancellationToken cancellationToken = default)
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

        var result = await requestService.DeleteAsync(apiKey, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Request"),
                OperationResponseType.AccessDenied => ApiForbidden("Only the creator can delete this request."),
                OperationResponseType.ValidationFailure => ApiBadRequest("Only requests with Pending status can be deleted."),
                _ => ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Unable to delete request.")
            };
        }

        return Ok();
    }

    #region Comments

    /// <summary>
    /// List comments for a request.
    /// </summary>
    [HttpGet("{requestApiKey:guid}/comments")]
    [ProducesResponseType(typeof(CommentPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCommentsAsync(
        Guid requestApiKey,
        [FromQuery] short page = 1,
        [FromQuery] short pageSize = 50,
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

        var requestResult = await requestService.GetByApiKeyAsync(requestApiKey, cancellationToken).ConfigureAwait(false);
        if (!requestResult.IsSuccess || requestResult.Data == null)
        {
            return ApiNotFound("Request");
        }

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
        {
            return pagingError!;
        }

        var pagedRequest = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize
        };

        var result = await commentService.ListAsync(requestResult.Data.Id, pagedRequest, cancellationToken).ConfigureAwait(false);

        var comments = result.Data.Select(ToCommentDto).ToArray();

        return Ok(new CommentPagedResponse(
            new PaginationMetadata(result.TotalCount, validatedPageSize, validatedPage, result.TotalPages),
            comments));
    }

    /// <summary>
    /// Create a comment on a request.
    /// </summary>
    [HttpPost("{requestApiKey:guid}/comments")]
    [ProducesResponseType(typeof(RequestCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCommentAsync(
        Guid requestApiKey,
        [FromBody] CreateCommentRequest request,
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

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return ApiValidationError("Comment body is required.");
        }

        var requestResult = await requestService.GetByApiKeyAsync(requestApiKey, cancellationToken).ConfigureAwait(false);
        if (!requestResult.IsSuccess || requestResult.Data == null)
        {
            return ApiNotFound("Request");
        }

        var result = await commentService.CreateAsync(
            requestResult.Data.Id,
            user.Id,
            request.Body,
            request.ParentCommentApiKey,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Request"),
                OperationResponseType.ValidationFailure => ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Invalid parent comment."),
                _ => ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Unable to create comment.")
            };
        }

        return StatusCode(StatusCodes.Status201Created, ToCommentDto(result.Data));
    }

    #endregion

    #region Activity

    /// <summary>
    /// Check if user has unread request activity.
    /// </summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(ActivityCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CheckActivityAsync(CancellationToken cancellationToken = default)
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

        var hasUnread = await activityService.HasUnreadAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return Ok(new ActivityCheckResponse(hasUnread));
    }

    /// <summary>
    /// Get unread requests for the current user.
    /// </summary>
    [HttpGet("activity/unread")]
    [ProducesResponseType(typeof(PagedResponse<UnreadRequestSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUnreadAsync(
        [FromQuery] short page = 1,
        [FromQuery] short pageSize = 10,
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

        var pagedRequest = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize
        };

        var result = await activityService.GetUnreadRequestsAsync(user.Id, pagedRequest, cancellationToken).ConfigureAwait(false);

        var summaries = result.Data.Select(r => new UnreadRequestSummary(
            r.ApiKey,
            r.CategoryValue.ToString(),
            r.StatusValue.ToString(),
            r.Description.Length > 100 ? r.Description[..100] + "..." : r.Description,
            r.LastActivityAt.ToString(),
            r.LastActivityTypeValue.ToString(),
            r.LastActivityUser != null
                ? new UserSummary(r.LastActivityUser.ApiKey, r.LastActivityUser.UserName)
                : null)).ToArray();

        return Ok(new PagedResponse<UnreadRequestSummary>(
            new PaginationMetadata(result.TotalCount, validatedPageSize, validatedPage, result.TotalPages),
            summaries));
    }

    /// <summary>
    /// Mark a request as seen.
    /// </summary>
    [HttpPost("{requestApiKey:guid}/seen")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkSeenAsync(Guid requestApiKey, CancellationToken cancellationToken = default)
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

        var result = await activityService.MarkSeenAsync(requestApiKey, user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Request"),
                OperationResponseType.AccessDenied => ApiForbidden("User is not a participant of this request."),
                _ => ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Unable to mark request as seen.")
            };
        }

        return Ok();
    }

    #endregion

    private static RequestSummary ToSummary(Request request, int commentCount)
    {
        return new RequestSummary(
            request.ApiKey,
            request.CategoryValue.ToString(),
            request.StatusValue.ToString(),
            request.Description.Length > 200 ? request.Description[..200] + "..." : request.Description,
            request.ArtistName,
            request.AlbumTitle,
            request.SongTitle,
            request.ReleaseYear,
            request.CreatedAt.ToString(),
            request.UpdatedAt.ToString(),
            request.LastActivityAt.ToString(),
            request.LastActivityTypeValue.ToString(),
            commentCount,
            new UserSummary(request.CreatedByUser.ApiKey, request.CreatedByUser.UserName));
    }

    private static RequestDetail ToDetail(Request request, int commentCount)
    {
        return new RequestDetail(
            request.ApiKey,
            request.CategoryValue.ToString(),
            request.StatusValue.ToString(),
            request.Description,
            request.ArtistName,
            request.TargetArtistApiKey,
            request.AlbumTitle,
            request.TargetAlbumApiKey,
            request.SongTitle,
            request.TargetSongApiKey,
            request.ReleaseYear,
            request.ExternalUrl,
            request.Notes,
            request.CreatedAt.ToString(),
            request.UpdatedAt.ToString(),
            request.LastActivityAt.ToString(),
            request.LastActivityTypeValue.ToString(),
            commentCount,
            new UserSummary(request.CreatedByUser.ApiKey, request.CreatedByUser.UserName),
            request.LastActivityUser != null
                ? new UserSummary(request.LastActivityUser.ApiKey, request.LastActivityUser.UserName)
                : null);
    }

    private static RequestCommentDto ToCommentDto(RequestComment comment)
    {
        return new RequestCommentDto(
            comment.ApiKey,
            comment.ParentComment?.ApiKey,
            comment.Body,
            comment.IsSystem,
            comment.CreatedAt.ToString(),
            comment.CreatedByUser != null
                ? new UserSummary(comment.CreatedByUser.ApiKey, comment.CreatedByUser.UserName)
                : null);
    }
}
