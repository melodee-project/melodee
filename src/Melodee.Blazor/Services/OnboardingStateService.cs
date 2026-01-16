using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Setup;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace Melodee.Blazor.Services;

/// <summary>
/// Scoped service that manages onboarding state and provides setup check functionality.
/// Uses static caching for onboarding completion status to avoid repeated database checks.
/// Cache is invalidated when:
/// - An admin logs in (to catch config changes)
/// - Doctor check fails (reactive health check)
/// - ResetOnboardingCache() is called explicitly
/// </summary>
public sealed class OnboardingStateService
{
    private readonly ISetupCheckService _setupCheckService;
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;
    private readonly Serilog.ILogger _logger;
    private readonly ICacheManager _cacheManager;
    
    // Static cache for onboarding completion - shared across all requests
    // Once onboarding is complete, we don't need to check the database again until cache expires
    private static bool? _isOnboardingComplete;
    private static DateTimeOffset _onboardingCacheExpiry = DateTimeOffset.MinValue;
    private static readonly object _lockObject = new();
    
    // Cache duration for onboarding completion check (1 hour default, reset on admin login or doctor failure)
    private static readonly TimeSpan OnboardingCacheDuration = TimeSpan.FromHours(1);
    
    // Instance-level cache for setup status (within same request scope)
    private SetupStatus? _cachedStatus;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private static readonly TimeSpan SetupStatusCacheDuration = TimeSpan.FromSeconds(30);

    private Melodee.Common.Services.ImportData? _importData;

    public string? LastSetupErrorMessage { get; private set; }

    public OnboardingStateService(
        ISetupCheckService setupCheckService,
        IMelodeeConfigurationFactory configurationFactory,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        Serilog.ILogger logger,
        ICacheManager cacheManager)
    {
        _setupCheckService = setupCheckService;
        _configurationFactory = configurationFactory;
        _contextFactory = contextFactory;
        _logger = logger;
        _cacheManager = cacheManager;
    }

    /// <summary>
    /// Gets whether onboarding is required based on completion marker and setup status.
    /// Uses static caching to avoid database hits after onboarding is complete.
    /// </summary>
    public async Task<bool> IsOnboardingRequiredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Fast path: if we've already determined onboarding is complete and cache hasn't expired
            lock (_lockObject)
            {
                if (_isOnboardingComplete == true && DateTimeOffset.UtcNow < _onboardingCacheExpiry)
                {
                    return false;
                }
            }

            _logger.Debug("[OnboardingStateService] Checking if onboarding is required...");
            var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
            var onboardingCompletedAt = config.GetValue<string?>(SettingRegistry.SystemOnboardingCompletedAt);
            _logger.Debug("[OnboardingStateService] OnboardingCompletedAt value: {Value}", onboardingCompletedAt ?? "(null/empty)");

            if (string.IsNullOrWhiteSpace(onboardingCompletedAt))
            {
                _logger.Debug("[OnboardingStateService] Onboarding not completed, returning true");
                return true;
            }

            // Onboarding timestamp exists - cache this fact statically with expiry
            lock (_lockObject)
            {
                _isOnboardingComplete = true;
                _onboardingCacheExpiry = DateTimeOffset.UtcNow.Add(OnboardingCacheDuration);
            }
            
            _logger.Debug("[OnboardingStateService] Onboarding is complete, checking setup status...");
            var status = await GetSetupStatusAsync(cancellationToken);
            _logger.Debug("[OnboardingStateService] Setup status IsReady={IsReady}, BlockingItems={BlockingCount}", 
                status.IsReady, status.BlockingItems.Count);
            return !status.IsReady;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[OnboardingStateService] Exception in IsOnboardingRequiredAsync");
            LastSetupErrorMessage = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// Gets the current setup status, using cached value if recent.
    /// </summary>
    public async Task<SetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedStatus != null && DateTimeOffset.UtcNow - _lastCheck < SetupStatusCacheDuration)
        {
            _logger.Debug("[OnboardingStateService] Returning cached setup status");
            return _cachedStatus;
        }

        try
        {
            _logger.Debug("[OnboardingStateService] Calling SetupCheckService.SetupCheckAsync...");
            _cachedStatus = await _setupCheckService.SetupCheckAsync(cancellationToken);
            _logger.Debug("[OnboardingStateService] SetupCheckAsync completed: IsReady={IsReady}", _cachedStatus.IsReady);
            LastSetupErrorMessage = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[OnboardingStateService] Exception in GetSetupStatusAsync from SetupCheckService");
            LastSetupErrorMessage = ex.Message;
            throw;
        }
        _lastCheck = DateTimeOffset.UtcNow;
        return _cachedStatus;
    }

    /// <summary>
    /// Refreshes the cached setup status.
    /// </summary>
    public async Task RefreshSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _cachedStatus = await _setupCheckService.SetupCheckAsync(cancellationToken);
            LastSetupErrorMessage = null;
        }
        catch (Exception ex)
        {
            LastSetupErrorMessage = ex.Message;
            throw;
        }
        _lastCheck = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets only the blocking items that need to be resolved.
    /// </summary>
    public async Task<IReadOnlyList<SetupItem>> GetBlockingItemsAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetSetupStatusAsync(cancellationToken);
        return status.BlockingItems;
    }

    /// <summary>
    /// Marks onboarding as completed by setting the completion timestamp.
    /// </summary>
    public async Task MarkOnboardingCompletedAsync(CancellationToken cancellationToken = default)
    {
        var settingService = new SettingService(_logger, _cacheManager, _configurationFactory, _contextFactory);
        var instant = SystemClock.Instance.GetCurrentInstant();
        var serialized = InstantPattern.ExtendedIso.Format(instant);
        await settingService.SetAsync(SettingRegistry.SystemOnboardingCompletedAt, serialized, cancellationToken);

        // Update static cache immediately
        lock (_lockObject)
        {
            _isOnboardingComplete = true;
        }
        _cachedStatus = null;
    }
    
    /// <summary>
    /// Resets the static onboarding completion cache. Used for testing or when settings are reset.
    /// </summary>
    public static void ResetOnboardingCache()
    {
        lock (_lockObject)
        {
            _isOnboardingComplete = null;
            _onboardingCacheExpiry = DateTimeOffset.MinValue;
        }
    }
    
    /// <summary>
    /// Called when an admin logs in to force re-evaluation of system health.
    /// This ensures any configuration changes are detected promptly.
    /// </summary>
    public static void InvalidateCacheOnAdminLogin()
    {
        lock (_lockObject)
        {
            _onboardingCacheExpiry = DateTimeOffset.MinValue;
        }
    }
    
    /// <summary>
    /// Called when doctor check fails to force re-evaluation on next request.
    /// </summary>
    public static void InvalidateCacheOnDoctorFailure()
    {
        lock (_lockObject)
        {
            _isOnboardingComplete = null;
            _onboardingCacheExpiry = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Stores imported settings and libraries for use in the onboarding wizard.
    /// </summary>
    public void StoreImportData(Melodee.Common.Services.ImportData data)
    {
        _importData = data;
    }

    /// <summary>
    /// Gets any stored import data.
    /// </summary>
    public Melodee.Common.Services.ImportData? GetImportData() => _importData;

    /// <summary>
    /// Clears stored import data.
    /// </summary>
    public void ClearImportData() => _importData = null;

    public async Task<Melodee.Common.Services.ImportResult> ImportSettingsAndLibrariesAsync(string jsonContent, CancellationToken cancellationToken = default)
    {
        var importService = new SystemImportService(
            _logger,
            _cacheManager,
            _configurationFactory,
            _contextFactory);
        var result = await importService.ImportAsync(jsonContent, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            await RefreshSetupStatusAsync(cancellationToken);
        }
        return result;
    }
}
