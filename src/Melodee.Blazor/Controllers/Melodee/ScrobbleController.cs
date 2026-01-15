using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ILogger = Serilog.ILogger;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[RequireCapability(UserCapability.Scrobble)]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/scrobble")]
public class ScrobbleController(
    ILogger logger,
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    IUserProfileService userProfileService,
    SongService songService,
    ScrobbleService scrobbleService,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private readonly SemaphoreSlim _scrobbleInitLock = new(1, 1);
    private bool _scrobbleInitialized;

    private async Task EnsureScrobbleInitializedAsync(IMelodeeConfiguration config, CancellationToken cancellationToken)
    {
        if (_scrobbleInitialized)
        {
            return;
        }

        await _scrobbleInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_scrobbleInitialized)
            {
                await scrobbleService.InitializeAsync(config, cancellationToken).ConfigureAwait(false);
                _scrobbleInitialized = true;
            }
        }
        finally
        {
            _scrobbleInitLock.Release();
        }
    }

    /// <summary>
    /// Scrobble a song (now playing or played).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ScrobbleSong([FromBody] ScrobbleRequest scrobbleRequest, CancellationToken cancellationToken = default)
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

        var songRequest = await songService.GetByApiKeyAsync(scrobbleRequest.SongId, cancellationToken).ConfigureAwait(false);
        if (!songRequest.IsSuccess || songRequest.Data == null)
        {
            logger.Warning("[{ControllerName}] [{MethodName}] Scrobble request for unknown song [{SongId}]",
                nameof(ScrobbleController),
                nameof(ScrobbleSong),
                scrobbleRequest.SongId);
            return ApiBadRequest("Unknown song");
        }

        var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        await EnsureScrobbleInitializedAsync(configuration, cancellationToken).ConfigureAwait(false);

        OperationResult<bool>? result = null;

        if (scrobbleRequest.ScrobbleTypeValue == ScrobbleRequestType.NowPlaying)
        {
            result = await scrobbleService.NowPlaying(
                     user.ToUserInfo(),
                     scrobbleRequest.SongId,
                     scrobbleRequest.PlayedDuration,
                     scrobbleRequest.PlayerName,
                    ApiRequest.ApiRequestPlayer?.UserAgent,
                    ApiRequest.IpAddress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (scrobbleRequest.ScrobbleTypeValue == ScrobbleRequestType.Played)
        {
            result = await scrobbleService.Scrobble(
                     user.ToUserInfo(),
                     scrobbleRequest.SongId,
                     false,
                     scrobbleRequest.PlayerName,
                    ApiRequest.ApiRequestPlayer?.UserAgent,
                    ApiRequest.IpAddress,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (result != null)
        {
            if (result.IsSuccess)
            {
                return Ok();
            }

            logger.Warning("[{ControllerName}] [{MethodName}] Scrobble failed.",
                nameof(ScrobbleController),
                nameof(ScrobbleSong));
            return ApiBadRequest(result.Messages?.First() ?? "Unknown error");
        }

        logger.Warning("[{ControllerName}] [{MethodName}] Unknown scrobble type.",
            nameof(ScrobbleController),
            nameof(ScrobbleSong));
        return ApiBadRequest($"Unknown scrobble type: {scrobbleRequest.ScrobbleType}");
    }

    /// <summary>
    /// Get Last.fm authentication URL.
    /// </summary>
    [HttpGet]
    [Route("lastfm/auth-url")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLastFmAuthUrl([FromQuery] string callback, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        if (!Uri.TryCreate(callback, UriKind.Absolute, out var callbackUri) || (callbackUri.Scheme != Uri.UriSchemeHttps && callbackUri.Scheme != Uri.UriSchemeHttp))
        {
            return ApiValidationError("Invalid callback url");
        }

        var config = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var apiKey = config.GetValue<string>(SettingRegistry.ScrobblingLastFmApiKey);
        if (apiKey.Nullify() == null)
        {
            return ApiBadRequest("Last.fm not configured");
        }

        var url = $"https://www.last.fm/api/auth/?api_key={apiKey}&cb={Uri.EscapeDataString(callbackUri.ToString())}";
        return Ok(new { url });
    }

    /// <summary>
    /// Exchange Last.fm token for session key.
    /// </summary>
    [HttpPost]
    [Route("lastfm/session")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExchangeLastFmSession([FromBody] LastFmSessionRequest request, CancellationToken cancellationToken = default)
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

        var sessionKey = await GetLastFmSessionKeyAsync(request.Token, cancellationToken).ConfigureAwait(false);
        if (sessionKey.Nullify() == null)
        {
            return ApiBadRequest("Unable to exchange Last.fm session key");
        }

        var updateResult = await userService.SetLastFmSessionKeyAsync(user.Id, sessionKey, cancellationToken)
            .ConfigureAwait(false);
        if (!updateResult.IsSuccess)
        {
            return ApiBadRequest(updateResult.Messages?.FirstOrDefault() ?? "Unable to save Last.fm session key");
        }

        return Ok(new { message = "Last.fm linked" });
    }

    /// <summary>
    /// Disconnect Last.fm account.
    /// </summary>
    [HttpPost]
    [Route("lastfm/disconnect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DisconnectLastFm(CancellationToken cancellationToken = default)
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

        await userService.SetLastFmSessionKeyAsync(user.Id, null, cancellationToken).ConfigureAwait(false);
        return Ok(new { message = "Last.fm disconnected" });
    }

    private async Task<string?> GetLastFmSessionKeyAsync(string token, CancellationToken cancellationToken)
    {
        var config = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var apiKey = config.GetValue<string>(SettingRegistry.ScrobblingLastFmApiKey);
        var secret = config.GetValue<string>(SettingRegistry.ScrobblingLastFmSharedSecret);
        if (apiKey.Nullify() == null || secret.Nullify() == null)
        {
            return null;
        }

        var signature = BuildApiSignature(apiKey!, secret!, token);
        var requestUri =
            $"2.0/?method=auth.getSession&api_key={Uri.EscapeDataString(apiKey!)}&token={Uri.EscapeDataString(token)}&api_sig={signature}&format=json";

        using var httpClient = httpClientFactory.CreateClient("LastFm");
        try
        {
            var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var sessionResponse = Serializer.Deserialize<LastFmSessionResponse>(content);
            return sessionResponse?.Session?.Key;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed exchanging Last.fm session key");
            return null;
        }
    }

    // NOTE: MD5 is required here by the Last.fm API specification for authentication signatures.
    // The API signature must be computed as MD5 per the Last.fm Web Services Authentication protocol.
    // This cannot be changed without breaking compatibility with Last.fm's API.
    // See: https://www.last.fm/api/authentication
    // lgtm[cs/weak-crypto] MD5 mandated by Last.fm API specification - cannot change
    private static string BuildApiSignature(string apiKey, string secret, string token)
    {
        var sigString = $"api_key{apiKey}methodauth.getSessiontoken{token}{secret}";
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(sigString);
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
