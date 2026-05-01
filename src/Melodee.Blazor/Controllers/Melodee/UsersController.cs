using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Admin user management endpoints.
/// For authentication, use /api/v1/auth.
/// For current user's personal data, use /api/v1/user.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/admin")]
public class UsersController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// List all users (admin only).
    /// Returns basic user information without sensitive data (no passwords, tokens, etc.).
    /// </summary>
    [HttpGet]
    [Route("users")]
    [RequireCapability(UserCapability.Admin)]
    [ProducesResponseType(typeof(AdminUserInfo[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var currentUser = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (currentUser == null)
        {
            return ApiUnauthorized();
        }

        if (!currentUser.IsAdmin)
        {
            return ApiForbidden("Admin privileges required");
        }

        // Get all users (reasonable limit for admin operations)
        var result = await userProfileService.ListAsync(new PagedRequest
        {
            PageSize = 1000,
            OrderBy = new Dictionary<string, string> { { "UserName", "ASC" } }
        }, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return Ok(Array.Empty<AdminUserInfo>());
        }

        var users = result.Data.Select(u => new AdminUserInfo(
            u.ApiKey, // Use ApiKey as the unique identifier
            u.UserName,
            u.Email,
            u.IsAdmin,
            !u.IsLocked, // IsEnabled is inverse of IsLocked
            u.CreatedAt.ToDateTimeUtc().ToString("o"),
            u.LastLoginAt?.ToDateTimeUtc().ToString("o")
        )).ToArray();

        return Ok(users);
    }
}
