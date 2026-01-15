using System.Security.Claims;
using Melodee.Blazor.Controllers;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Services;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Melodee.Blazor.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireCapabilityAttribute(UserCapability capability) : Attribute
{
    public UserCapability Capability { get; } = capability;
}

public enum UserCapability
{
    Stream,
    Playlist,
    Admin,
    Scrobble,
    Share
}

/// <summary>
/// Centralizes authentication, blacklist, lock, and capability enforcement for Melodee API controllers.
/// </summary>
public sealed class MelodeeApiAuthFilter(
    UserProfileService userProfileService,
    IBlacklistService blacklistService,
    ILogger<MelodeeApiAuthFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            await next().ConfigureAwait(false);
            return;
        }

        var correlationId = context.HttpContext.TraceIdentifier;

        var principal = context.HttpContext.User;
        if (!(principal?.Identity?.IsAuthenticated ?? false))
        {
            context.Result = new UnauthorizedObjectResult(
                new ApiError(ApiError.Codes.Unauthorized, "Authorization token is invalid", correlationId));
            return;
        }

        var userIdClaim = principal.FindFirstValue(ClaimTypes.Sid);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedObjectResult(
                new ApiError(ApiError.Codes.Unauthorized, "Authorization token is invalid", correlationId));
            return;
        }

        var userResult = await userProfileService.GetByApiKeyAsync(userId, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            context.Result = new UnauthorizedObjectResult(
                new ApiError(ApiError.Codes.Unauthorized, "Authorization token is invalid", correlationId));
            return;
        }

        var user = userResult.Data;
        if (user.IsLocked)
        {
            context.Result = new ObjectResult(
                new ApiError(ApiError.Codes.UserLocked, "User is locked", correlationId))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        var clientIp = CommonBase.GetRequestIp(context.HttpContext, tryUseXForwardHeader: false);
        if (await blacklistService.IsEmailBlacklistedAsync(user.Email).ConfigureAwait(false) ||
            await blacklistService.IsIpBlacklistedAsync(clientIp).ConfigureAwait(false))
        {
            logger.LogWarning("Blocked request for blacklisted user {Email} from {Ip}", user.Email, clientIp);
            context.Result = new ObjectResult(
                new ApiError(ApiError.Codes.Blacklisted, "User is blacklisted", correlationId))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        foreach (var requiredCapability in context.ActionDescriptor.EndpointMetadata.OfType<RequireCapabilityAttribute>())
        {
            if (!UserHasCapability(user, requiredCapability.Capability))
            {
                logger.LogWarning("Capability check failed for user {UserId} requiring {Capability}", user.Id, requiredCapability.Capability);
                context.Result = new ObjectResult(
                    new ApiError(ApiError.Codes.Forbidden, $"User does not have required capability: {requiredCapability.Capability}", correlationId))
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return;
            }
        }

        context.HttpContext.Items["MelodeeAuthenticatedUser"] = user;
        context.HttpContext.Items["MelodeeClientIp"] = clientIp;

        await next().ConfigureAwait(false);
    }

    private static bool UserHasCapability(Common.Data.Models.User user, UserCapability capability) =>
        capability switch
        {
            UserCapability.Admin => user.IsAdmin,
            UserCapability.Stream => user.HasStreamRole,
            UserCapability.Playlist => user.HasPlaylistRole,
            UserCapability.Scrobble => user.IsScrobblingEnabled,
            UserCapability.Share => user.HasShareRole,
            _ => false
        };
}
