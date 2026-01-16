using System.Globalization;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Blazor.Services;

public sealed class ChecklistService
{
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly ICacheManager _cacheManager;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILocalizationService _localizationService;

    public ChecklistService(
        IMelodeeConfigurationFactory configurationFactory,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        IHostEnvironment hostEnvironment,
        ILocalizationService localizationService)
    {
        _configurationFactory = configurationFactory;
        _cacheManager = cacheManager;
        _contextFactory = contextFactory;
        _hostEnvironment = hostEnvironment;
        _localizationService = localizationService;
    }

    public async Task<string> GenerateChecklistAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var siteName = config.GetValue<string>(SettingRegistry.SystemSiteName) ?? "Melodee";
        var baseUrl = config.GetValue<string>(SettingRegistry.SystemBaseUrl) ?? "http://localhost:5000";

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var libraries = await db.Libraries.ToListAsync(cancellationToken);

        var inboundLib = libraries.FirstOrDefault(l => l.TypeValue == LibraryType.Inbound);
        var stagingLib = libraries.FirstOrDefault(l => l.TypeValue == LibraryType.Staging);
        var storageLibs = libraries.Where(l => l.TypeValue == LibraryType.Storage).ToList();

        var now = DateTime.UtcNow;
        var culture = _localizationService.CurrentCulture ?? CultureInfo.CurrentCulture;
        var generatedAt = now.ToString("yyyy-MM-dd HH:mm:ss", culture);
        var dateStamp = now.ToString("yyyy-MM-dd", culture);
        var inboundPath = GetPathDisplay(inboundLib?.Path);
        var stagingPath = GetPathDisplay(stagingLib?.Path);
        var storagePaths = string.Join(", ", storageLibs.Select(l => GetPathDisplay(l.Path)));

        return _localizationService.Localize(
            "Onboarding.ChecklistTemplate",
            generatedAt,
            siteName,
            inboundPath,
            stagingPath,
            storagePaths,
            baseUrl,
            dateStamp);
    }

    private string GetPathDisplay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return _localizationService.Localize("Onboarding.ChecklistNotConfigured");
        }
        return path;
    }
}
