using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melodee.Blazor.Services;

/// <summary>
/// Service to provide the application base URL for components that need it
/// </summary>
public sealed class BaseUrlService : IBaseUrlService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly ILogger<BaseUrlService> _logger;

    private string? _cachedBaseUrl;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private const int CacheDurationMinutes = 5;

    public BaseUrlService(
        IHttpContextAccessor httpContextAccessor,
        IMelodeeConfigurationFactory configurationFactory,
        ILogger<BaseUrlService>? logger = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _configurationFactory = configurationFactory;
        _logger = logger ?? NullLogger<BaseUrlService>.Instance;
    }

    public string? GetBaseUrl()
    {
        var configuration = _configurationFactory.GetConfigurationAsync().GetAwaiter().GetResult();
        var configuredBaseUrl = configuration.GetValue<string>(SettingRegistry.SystemBaseUrl);

        if (configuredBaseUrl.Nullify() != null && configuredBaseUrl != MelodeeConfiguration.RequiredNotSetValue)
        {
            return configuredBaseUrl!.TrimEnd('/');
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            return $"{httpContext.Request.Scheme}://{httpContext.Request.Host.Value}";
        }

        return null;
    }

    public async Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheExpiry > DateTime.UtcNow && _cachedBaseUrl != null)
        {
            return _cachedBaseUrl;
        }

        try
        {
            var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            var configuredBaseUrl = configuration.GetValue<string?>(SettingRegistry.SystemBaseUrl);

            if (!string.IsNullOrWhiteSpace(configuredBaseUrl) && configuredBaseUrl != MelodeeConfiguration.RequiredNotSetValue)
            {
                _cachedBaseUrl = configuredBaseUrl.TrimEnd('/');
                _cacheExpiry = DateTime.UtcNow.AddMinutes(CacheDurationMinutes);
                return _cachedBaseUrl;
            }

            _logger.LogWarning("[BaseUrlService] SystemBaseUrl is not configured. External URLs will fail. Set {Setting} to a valid URL.",
                SettingRegistry.SystemBaseUrl);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BaseUrlService] Failed to retrieve SystemBaseUrl configuration");
            return null;
        }
    }
}
