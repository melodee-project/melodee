using System.Security.Claims;
using System.Text.RegularExpressions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Models.Scrobbling;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Melodee.Blazor.Controllers.Melodee;

public abstract class ControllerBase(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : CommonBase
{
    private const string ConfigCacheKey = "__melodee_config_cache";
    private const string CachedBaseUrlKey = "__melodee_base_url";
    private const string CachedUserKey = "MelodeeAuthenticatedUser";
    public EtagRepository EtagRepository { get; } = etagRepository;
    public ISerializer Serializer { get; } = serializer;
    protected IConfiguration Configuration { get; } = configuration;
    public IMelodeeConfigurationFactory ConfigurationFactory { get; } = configurationFactory;

    /// <summary>
    /// Populates request context data for authenticated API calls.
    /// </summary>
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var headers = new List<KeyValue>
        {
            new("Path", context.HttpContext.Request.Path),
            new("User-Agent", context.HttpContext.Request.Headers.UserAgent.ToString())
        };

        var principal = context.HttpContext.User;
        if (principal?.Identity?.IsAuthenticated ?? false)
        {
            var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString()
                ?? GetHeaderValueAs<string>(context.HttpContext, "REMOTE_ADDR");
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                ipAddress = "unknown";
            }
            ApiRequest = new ApiRequest
            (
                headers.ToArray(),
                true,
                principal.Identity?.Name,
                null,
                null,
                principal.FindFirstValue(ClaimTypes.Sid),
                null,
                null,
                null,
                null,
                null,
                new UserPlayer
                (
                    context.HttpContext.Request.Headers.UserAgent.ToString(),
                    context.HttpContext.Request.Headers["c"].ToString(),
                    context.HttpContext.Request.Headers.Host.ToString(),
                    ipAddress
                ),
                ipAddress
            );

            var logger = context.HttpContext.RequestServices.GetService(typeof(ILogger<ControllerBase>)) as ILogger<ControllerBase>;
            logger?.LogInformation("Authenticated request received.");
        }

        await next().ConfigureAwait(false);
    }

    protected async Task<IMelodeeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Items.TryGetValue(ConfigCacheKey, out var cached) && cached is IMelodeeConfiguration config)
        {
            return config;
        }

        var resolved = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        HttpContext.Items[ConfigCacheKey] = resolved;
        return resolved;
    }

    protected async Task<string> GetBaseUrlAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Items.TryGetValue(CachedBaseUrlKey, out var cached) && cached is string baseUrl)
        {
            return baseUrl;
        }

        var config = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var resolved = GetBaseUrl(config);
        HttpContext.Items[CachedBaseUrlKey] = resolved;
        return resolved;
    }

    protected async Task<Common.Data.Models.User?> ResolveUserAsync(IUserProfileService userProfileService, CancellationToken cancellationToken)
    {
        if (HttpContext.Items.TryGetValue(CachedUserKey, out var cachedUser) && cachedUser is Common.Data.Models.User cached)
        {
            return cached;
        }

        var apiKey = SafeParser.ToGuid(ApiRequest.ApiKey) ?? Guid.Empty;
        if (apiKey == Guid.Empty)
        {
            return null;
        }

        var result = await userProfileService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data == null)
        {
            return null;
        }

        HttpContext.Items[CachedUserKey] = result.Data;
        return result.Data;
    }

    protected bool TryValidatePaging(int page, int pageSize, out short normalizedPage, out short normalizedPageSize, out IActionResult? error)
    {
        normalizedPage = (short)Math.Max(page, 1);
        normalizedPageSize = (short)Math.Clamp(pageSize, 1, ApiDefaults.MaxPageSize);
        error = null;

        if (page < 1)
        {
            error = BadRequest(new ApiError(ApiError.Codes.ValidationError, "page must be >= 1", GetCorrelationId()));
        }
        else if (pageSize < 1)
        {
            error = BadRequest(new ApiError(ApiError.Codes.ValidationError, "pageSize must be >= 1", GetCorrelationId()));
        }
        else if (pageSize > ApiDefaults.MaxPageSize)
        {
            error = BadRequest(new ApiError(ApiError.Codes.ValidationError, $"pageSize {pageSize} exceeds maximum allowed value of {ApiDefaults.MaxPageSize}", GetCorrelationId()));
        }

        return error == null;
    }

    protected bool TryValidateLimit(int limit, out short normalizedLimit, out IActionResult? error)
    {
        normalizedLimit = (short)Math.Clamp(limit, 1, ApiDefaults.MaxPageSize);
        error = null;
        if (limit < 1 || limit > ApiDefaults.MaxPageSize)
        {
            error = BadRequest(new ApiError(ApiError.Codes.ValidationError, $"limit must be between 1 and {ApiDefaults.MaxPageSize}", GetCorrelationId()));
        }

        return error == null;
    }

    protected bool TryValidateOrdering(string? orderBy, string? orderDirection, IReadOnlySet<string> allowedFields, out (string field, string direction) validated, out IActionResult? error)
    {
        error = null;
        var candidateField = orderBy.Nullify() ?? allowedFields.First();
        if (!allowedFields.Contains(candidateField) || !Regex.IsMatch(candidateField, @"^[A-Za-z0-9_]+$"))
        {
            error = BadRequest(new ApiError(ApiError.Codes.ValidationError, "Invalid orderBy value", GetCorrelationId()));
            validated = default;
            return false;
        }

        var direction = (orderDirection.Nullify() ?? PagedRequest.OrderDescDirection).ToUpperInvariant();
        if (direction is not (PagedRequest.OrderAscDirection or PagedRequest.OrderDescDirection))
        {
            error = BadRequest(new ApiError(ApiError.Codes.ValidationError, "Invalid orderDirection value", GetCorrelationId()));
            validated = default;
            return false;
        }

        validated = (candidateField, direction);
        return true;
    }

    protected string GetClientBinding()
    {
        if (HttpContext.Items.TryGetValue("MelodeeClientIp", out var ipObj) && ipObj is string ipFromFilter && !string.IsNullOrWhiteSpace(ipFromFilter))
        {
            return ipFromFilter;
        }

        return GetRequestIp(HttpContext, tryUseXForwardHeader: false);
    }

    /// <summary>
    /// Gets the correlation ID from the current request context for error tracing.
    /// </summary>
    protected string? GetCorrelationId() =>
        HttpContext.TraceIdentifier;

    /// <summary>
    /// Creates a standardized unauthorized error response.
    /// </summary>
    protected IActionResult ApiUnauthorized(string message = "Authorization token is invalid") =>
        Unauthorized(new ApiError(ApiError.Codes.Unauthorized, message, GetCorrelationId()));

    /// <summary>
    /// Creates a standardized forbidden error response for locked users.
    /// </summary>
    protected IActionResult ApiUserLocked() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError(ApiError.Codes.UserLocked, "User is locked", GetCorrelationId()));

    /// <summary>
    /// Creates a standardized forbidden error response for blacklisted users.
    /// </summary>
    protected IActionResult ApiBlacklisted() =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError(ApiError.Codes.Blacklisted, "User is blacklisted", GetCorrelationId()));

    /// <summary>
    /// Creates a standardized not found error response.
    /// </summary>
    protected IActionResult ApiNotFound(string resource) =>
        NotFound(new ApiError(ApiError.Codes.NotFound, $"{resource} not found", GetCorrelationId()));

    /// <summary>
    /// Creates a standardized bad request error response.
    /// </summary>
    protected IActionResult ApiBadRequest(string message) =>
        BadRequest(new ApiError(ApiError.Codes.BadRequest, message, GetCorrelationId()));

    /// <summary>
    /// Creates a standardized validation error response.
    /// </summary>
    protected IActionResult ApiValidationError(string message) =>
        BadRequest(new ApiError(ApiError.Codes.ValidationError, message, GetCorrelationId()));

    /// <summary>
    /// Creates a standardized too many requests error response.
    /// </summary>
    protected IActionResult ApiTooManyRequests(string message = "Too many concurrent requests") =>
        StatusCode(StatusCodes.Status429TooManyRequests, new ApiError(ApiError.Codes.TooManyRequests, message, GetCorrelationId()));

    /// <summary>
    /// Creates a standardized forbidden error response.
    /// </summary>
    protected IActionResult ApiForbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new ApiError(ApiError.Codes.Forbidden, message, GetCorrelationId()));
}
