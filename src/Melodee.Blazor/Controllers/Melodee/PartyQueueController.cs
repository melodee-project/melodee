using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for managing party queue operations.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/party-sessions/{sessionId:guid}/queue")]
public sealed class PartyQueueController(
    ISerializer serializer,
    EtagRepository etagRepository,
    PartySessionService partySessionService,
    PartyQueueService partyQueueService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<PartyQueueController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<PartyQueueController> Logger { get; } = logger;

    /// <summary>
    /// Gets the queue for a party session.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var result = await partyQueueService.GetQueueAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type == OperationResponseType.NotFound
                ? ApiNotFound("Party session")
                : ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to get queue");
        }

        return Ok(new QueueResponse(result.Data.Revision, result.Data.Items));
    }

    /// <summary>
    /// Adds songs to the queue.
    /// </summary>
    [HttpPost]
    [Route("items")]
    [ProducesResponseType(typeof(AddItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItems(
        Guid sessionId,
        [FromBody] AddItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyQueueService.AddItemsAsync(
            sessionId,
            request.SongApiKeys,
            userId,
            request.Source,
            request.ExpectedRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Party session"),
                OperationResponseType.Conflict => Conflict(new ApiError(ApiError.Codes.BadRequest,
                    result.Errors?.FirstOrDefault()?.Message ?? "Concurrent modification detected",
                    GetCorrelationId())),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to add items")
            };
        }

        return Ok(new AddItemsResponse(result.Data.NewRevision, result.Data.AddedItems));
    }

    /// <summary>
    /// Removes an item from the queue.
    /// </summary>
    [HttpDelete]
    [Route("items/{itemId:guid}")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(
        Guid sessionId,
        Guid itemId,
        [FromQuery] long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyQueueService.RemoveItemAsync(
            sessionId,
            itemId,
            userId,
            expectedRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Queue item"),
                OperationResponseType.Conflict => Conflict(new ApiError(ApiError.Codes.BadRequest,
                    result.Errors?.FirstOrDefault()?.Message ?? "Concurrent modification detected",
                    GetCorrelationId())),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to remove item")
            };
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Reorders an item in the queue.
    /// </summary>
    [HttpPost]
    [Route("items/{itemId:guid}/reorder")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReorderItem(
        Guid sessionId,
        Guid itemId,
        [FromBody] ReorderItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyQueueService.ReorderItemAsync(
            sessionId,
            itemId,
            request.NewIndex,
            userId,
            request.ExpectedRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Queue item"),
                OperationResponseType.Conflict => Conflict(new ApiError(ApiError.Codes.BadRequest,
                    result.Errors?.FirstOrDefault()?.Message ?? "Concurrent modification detected",
                    GetCorrelationId())),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to reorder item")
            };
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Clears the queue.
    /// </summary>
    [HttpPost]
    [Route("clear")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Clear(
        Guid sessionId,
        [FromQuery] long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyQueueService.ClearAsync(
            sessionId,
            userId,
            expectedRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Party session"),
                OperationResponseType.Conflict => Conflict(new ApiError(ApiError.Codes.BadRequest,
                    result.Errors?.FirstOrDefault()?.Message ?? "Concurrent modification detected",
                    GetCorrelationId())),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to clear queue")
            };
        }

        return Ok(result.Data);
    }
}

/// <summary>
/// Response model for queue operations.
/// </summary>
public record QueueResponse(long Revision, IEnumerable<Common.Data.Models.PartyQueueItem> Items);

/// <summary>
/// Request model for adding items to the queue.
/// </summary>
public record AddItemsRequest
{
    /// <summary>
    /// Song API keys to add to the queue.
    /// </summary>
    public required IEnumerable<Guid> SongApiKeys { get; init; }

    /// <summary>
    /// Source of the songs (e.g., "album", "playlist").
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Expected revision for optimistic concurrency.
    /// </summary>
    public long ExpectedRevision { get; init; }
}

/// <summary>
/// Response model for adding items to the queue.
/// </summary>
public record AddItemsResponse(long NewRevision, IEnumerable<Common.Data.Models.PartyQueueItem> AddedItems);

/// <summary>
/// Request model for reordering a queue item.
/// </summary>
public record ReorderItemRequest
{
    /// <summary>
    /// New sort order index for the item.
    /// </summary>
    public int NewIndex { get; init; }

    /// <summary>
    /// Expected revision for optimistic concurrency.
    /// </summary>
    public long ExpectedRevision { get; init; }
}
