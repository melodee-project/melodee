using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
///     This controller is used to get meta-information about the API.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/system")]
public sealed class SystemController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    StatisticsService statisticsService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// Get server information.
    /// </summary>
    [HttpGet]
    [Route("info")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ServerInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetServerInfo(CancellationToken cancellationToken = default)
    {
        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var version = typeof(Program).Assembly.GetName().Version;
        var majorVersion = version?.Major ?? 0;
        var minorVersion = version?.Minor ?? 0;
        var patchVersion = version?.Build ?? 0;

        return Ok(new ServerInfo(configuration.GetValue<string>(SettingRegistry.OpenSubsonicServerType) ?? SettingDefaults.DefaultSiteName,
            "Melodee API",
            majorVersion,
            minorVersion,
            patchVersion));
    }

    /// <summary>
    ///     Return some statistics about the system.
    /// </summary>
    [HttpGet]
    [Route("stats")]
    [RequireCapability(UserCapability.Admin)]
    [ProducesResponseType(typeof(Statistic[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSystemStatsAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var statsResult = await statisticsService.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);

        return Ok(statsResult.Data.Where(x => x.IncludeInApiResult ?? false).Select(x => x.ToStatisticModel()).ToArray());
    }

    /// <summary>
    ///     Test endpoint that throws an exception for testing global exception handler.
    ///     This endpoint is only available in development/test environments.
    /// </summary>
    [HttpGet]
    [Route("throw")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status500InternalServerError)]
    public IActionResult ThrowException()
    {
        throw new InvalidOperationException("Test exception for global exception handler testing");
    }
}
