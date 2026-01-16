using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.Setup;
using Melodee.Common.Services.Caching;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Blazor.Services;

/// <summary>
/// Scoped service that manages onboarding state and provides setup check functionality.
/// </summary>
public sealed class OnboardingStateService
{
    private readonly ISetupCheckService _setupCheckService;
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;
    private readonly Serilog.ILogger _logger;
    private readonly ICacheManager _cacheManager;
    private SetupStatus? _cachedStatus;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

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
    /// </summary>
    public async Task<bool> IsOnboardingRequiredAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var onboardingCompletedAt = config.GetValue<string?>(SettingRegistry.SystemOnboardingCompletedAt);

        if (string.IsNullOrWhiteSpace(onboardingCompletedAt))
        {
            return true;
        }

        var status = await GetSetupStatusAsync(cancellationToken);
        return !status.IsReady;
    }

    /// <summary>
    /// Gets the current setup status, using cached value if recent.
    /// </summary>
    public async Task<SetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedStatus != null && DateTimeOffset.UtcNow - _lastCheck < CacheDuration)
        {
            return _cachedStatus;
        }

        _cachedStatus = await _setupCheckService.SetupCheckAsync(cancellationToken);
        _lastCheck = DateTimeOffset.UtcNow;
        return _cachedStatus;
    }

    /// <summary>
    /// Refreshes the cached setup status.
    /// </summary>
    public async Task RefreshSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        _cachedStatus = await _setupCheckService.SetupCheckAsync(cancellationToken);
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
        await settingService.SetAsync(SettingRegistry.SystemOnboardingCompletedAt, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

        _cachedStatus = null;
    }
}
