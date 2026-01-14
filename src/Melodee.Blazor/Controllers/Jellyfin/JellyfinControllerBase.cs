using Melodee.Blazor.Controllers.Jellyfin.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Blazor.Controllers.Jellyfin;

public abstract class JellyfinControllerBase(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory) : ControllerBase
{
    private const string ConfigCacheKey = "__jellyfin_config_cache";
    private const string ServerIdCacheKey = "__jellyfin_server_id";
    private const string UserCacheKey = "__jellyfin_user_cache";

    protected EtagRepository EtagRepository { get; } = etagRepository;
    protected ISerializer Serializer { get; } = serializer;
    protected IConfiguration Configuration { get; } = configuration;
    protected IMelodeeConfigurationFactory ConfigurationFactory { get; } = configurationFactory;
    protected IDbContextFactory<MelodeeDbContext> DbContextFactory { get; } = dbContextFactory;
    protected IClock Clock { get; } = clock;

    private ILogger? _jellyfinLogger;
    protected ILogger JellyfinLogger => _jellyfinLogger ??= loggerFactory.CreateLogger("Melodee.Jellyfin");

    protected async Task<IMelodeeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (HttpContext.Items.TryGetValue(ConfigCacheKey, out var cached) && cached is IMelodeeConfiguration config)
        {
            return config;
        }

        var resolved = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        HttpContext.Items[ConfigCacheKey] = resolved;
        return resolved;
    }

    protected string GetServerId()
    {
        if (HttpContext.Items.TryGetValue(ServerIdCacheKey, out var cached) && cached is string serverId)
        {
            return serverId;
        }

        var instanceId = Configuration.GetValue<string>("Jwt:Issuer") ?? "Melodee";
        var hash = HashHelper.CreateMd5(instanceId);
        var id = hash ?? Guid.NewGuid().ToString("N");
        HttpContext.Items[ServerIdCacheKey] = id;
        return id;
    }

    protected async Task<string> GetTokenPepperAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetConfigurationAsync(cancellationToken);
        var pepper = config.GetValue<string>(SettingRegistry.JellyfinTokenPepper);
        if (string.IsNullOrWhiteSpace(pepper))
        {
            pepper = Configuration.GetValue<string>("Jwt:Key") ?? "DefaultPepper_ChangeMeInProduction";
        }
        return pepper;
    }

    protected async Task<User?> AuthenticateJellyfinAsync(CancellationToken cancellationToken = default)
    {
        if (HttpContext.Items.TryGetValue(UserCacheKey, out var cached) && cached is User cachedUser)
        {
            return cachedUser;
        }

        var tokenInfo = JellyfinTokenParser.ParseFromRequest(Request);
        if (string.IsNullOrWhiteSpace(tokenInfo.Token))
        {
            JellyfinLogger.LogDebug("JellyfinAuth: No token in request. Client={Client} Device={Device}",
                tokenInfo.Client ?? "none", tokenInfo.Device ?? "none");
            return null;
        }

        var pepper = await GetTokenPepperAsync(cancellationToken);
        var now = Clock.GetCurrentInstant();
        var tokenPrefix = JellyfinTokenParser.GetTokenPrefix(tokenInfo.Token);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var candidateTokens = await dbContext.JellyfinAccessTokens
            .AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.TokenPrefixHash == tokenPrefix
                        && t.RevokedAt == null
                        && (t.ExpiresAt == null || t.ExpiresAt > now))
            .OrderByDescending(t => t.LastUsedAt ?? t.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var storedToken in candidateTokens)
        {
            if (JellyfinTokenParser.VerifyToken(tokenInfo.Token, storedToken.TokenSalt, pepper, storedToken.TokenHash))
            {
                if (storedToken.User.IsLocked)
                {
                    JellyfinLogger.LogWarning("JellyfinAuth: User {UserId}:{UserName} is locked",
                        storedToken.User.Id, storedToken.User.UserName);
                    return null;
                }

                JellyfinLogger.LogDebug("JellyfinAuth: Authenticated {UserId}:{UserName} Client={Client}",
                    storedToken.User.Id, storedToken.User.UserName, tokenInfo.Client ?? "unknown");

                HttpContext.Items[UserCacheKey] = storedToken.User;
                _ = UpdateTokenLastUsedAsync(storedToken.Id, now);
                return storedToken.User;
            }
        }

        JellyfinLogger.LogDebug("JellyfinAuth: Token not found or invalid. CandidatesChecked={Count}",
            candidateTokens.Count);
        return null;
    }

    private async Task UpdateTokenLastUsedAsync(int tokenId, Instant lastUsed)
    {
        try
        {
            await using var dbContext = await DbContextFactory.CreateDbContextAsync();
            await dbContext.JellyfinAccessTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.LastUsedAt, lastUsed));
        }
        catch
        {
            // Fire and forget; log silently if needed
        }
    }

    protected string GetClientBinding()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    protected string GetCorrelationId() => HttpContext.TraceIdentifier;

    /// <summary>
    /// Checks If-None-Match header and returns 304 Not Modified if the ETag matches.
    /// </summary>
    /// <param name="etag">The current ETag value for the resource.</param>
    /// <returns>True if the client already has the current version (should return 304).</returns>
    protected bool IsNotModified(string etag)
    {
        if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch))
        {
            var clientEtag = ifNoneMatch.ToString().Trim('"');
            return string.Equals(clientEtag, etag, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// Sets the ETag header on the response.
    /// </summary>
    protected void SetETagHeader(string etag)
    {
        Response.Headers.ETag = $"\"{etag}\"";
    }

    /// <summary>
    /// Returns a 304 Not Modified response with the ETag header set.
    /// </summary>
    protected IActionResult NotModified(string etag)
    {
        SetETagHeader(etag);
        return StatusCode(StatusCodes.Status304NotModified);
    }

    protected IActionResult JellyfinUnauthorized(string detail = "Missing or invalid authentication token.")
    {
        return Unauthorized(new JellyfinProblemDetails
        {
            Type = "about:blank",
            Title = "Unauthorized",
            Status = StatusCodes.Status401Unauthorized,
            Detail = detail,
            TraceId = GetCorrelationId()
        });
    }

    protected IActionResult JellyfinForbidden(string detail = "Access denied.")
    {
        return StatusCode(StatusCodes.Status403Forbidden, new JellyfinProblemDetails
        {
            Type = "about:blank",
            Title = "Forbidden",
            Status = StatusCodes.Status403Forbidden,
            Detail = detail,
            TraceId = GetCorrelationId()
        });
    }

    protected IActionResult JellyfinNotFound(string detail = "Resource not found.")
    {
        return NotFound(new JellyfinProblemDetails
        {
            Type = "about:blank",
            Title = "Not Found",
            Status = StatusCodes.Status404NotFound,
            Detail = detail,
            TraceId = GetCorrelationId()
        });
    }

    protected IActionResult JellyfinBadRequest(string detail)
    {
        return BadRequest(new JellyfinProblemDetails
        {
            Type = "about:blank",
            Title = "Bad Request",
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
            TraceId = GetCorrelationId()
        });
    }

    protected IActionResult JellyfinTooManyRequests(string detail = "Rate limit exceeded. Please retry later.")
    {
        Response.Headers.Append("Retry-After", "60");
        return StatusCode(StatusCodes.Status429TooManyRequests, new JellyfinProblemDetails
        {
            Type = "about:blank",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = detail,
            TraceId = GetCorrelationId()
        });
    }

    protected IActionResult JellyfinRangeNotSatisfiable(string detail = "Invalid range specified.")
    {
        return StatusCode(StatusCodes.Status416RangeNotSatisfiable, new JellyfinProblemDetails
        {
            Type = "about:blank",
            Title = "Range Not Satisfiable",
            Status = StatusCodes.Status416RangeNotSatisfiable,
            Detail = detail,
            TraceId = GetCorrelationId()
        });
    }

    protected string ToJellyfinId(int melodeeId) => melodeeId.ToString();

    protected string ToJellyfinId(Guid apiKey) => apiKey.ToString("N");

    protected bool TryParseJellyfinId(string jellyfinId, out int melodeeId)
    {
        return int.TryParse(jellyfinId, out melodeeId);
    }

    protected bool TryParseJellyfinGuid(string jellyfinId, out Guid apiKey)
    {
        return Guid.TryParse(jellyfinId, out apiKey);
    }

    protected static string FormatInstantForJellyfin(Instant? instant)
    {
        if (instant == null)
        {
            return string.Empty;
        }
        return instant.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", null);
    }

    protected JellyfinUser MapUserToJellyfin(User user)
    {
        return new JellyfinUser
        {
            Name = user.UserName,
            ServerId = GetServerId(),
            Id = ToJellyfinId(user.ApiKey),
            HasPassword = true,
            HasConfiguredPassword = true,
            LastLoginDate = FormatInstantForJellyfin(user.LastLoginAt),
            LastActivityDate = FormatInstantForJellyfin(user.LastActivityAt),
            Configuration = new JellyfinUserConfiguration(),
            Policy = new JellyfinUserPolicy
            {
                IsAdministrator = user.IsAdmin,
                IsDisabled = user.IsLocked,
                EnableMediaPlayback = user.HasStreamRole,
                EnableContentDownloading = user.HasDownloadRole
            }
        };
    }
}

public record JellyfinProblemDetails
{
    public string Type { get; init; } = "about:blank";
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Detail { get; init; }
    public string? TraceId { get; init; }
    public string? ErrorCode { get; init; }
}
