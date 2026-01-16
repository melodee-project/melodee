using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Services.Doctor;

/// <summary>
/// Base implementation of the doctor service with core checks that don't depend on ASP.NET.
/// This class can be extended by host-specific implementations.
/// </summary>
public abstract class DoctorServiceBase : IDoctorService
{
    private readonly IDbContextFactory<MelodeeDbContext> _dbContextFactory;
    private readonly LibraryService _libraryService;
    private readonly IMelodeeConfigurationFactory _configurationFactory;

    protected DoctorServiceBase(
        IDbContextFactory<MelodeeDbContext> dbContextFactory,
        LibraryService libraryService,
        IMelodeeConfigurationFactory configurationFactory)
    {
        _dbContextFactory = dbContextFactory;
        _libraryService = libraryService;
        _configurationFactory = configurationFactory;
    }

    public async Task<DoctorCheckResult> RunConfigurationCheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
            var missing = new List<string>();

            foreach (var key in OnboardingRequirements.RequiredSettingsKeys)
            {
                var value = config.GetValue<string>(key);
                if (string.IsNullOrWhiteSpace(value) || value == MelodeeConfiguration.RequiredNotSetValue)
                {
                    missing.Add(key);
                }
            }

            var success = missing.Count == 0;
            var details = success
                ? "All required settings are configured"
                : $"Missing required settings: {string.Join(", ", missing)}";

            return new DoctorCheckResult("Configuration", success, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("Configuration", false, ex.Message, sw.Elapsed);
        }
    }

    public async Task<DoctorCheckResult> RunDatabaseCheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            var details = canConnect
                ? $"OK ({db.Database.ProviderName})"
                : "Unable to connect";

            return new DoctorCheckResult("Database", canConnect, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("Database", false, ex.Message, sw.Elapsed);
        }
    }

    public async Task<(DoctorCheckResult Check, IReadOnlyList<LibraryPathResult> Paths, IReadOnlyList<string> Overlaps)> RunLibraryPathCheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var paths = new List<LibraryPathResult>();
        var overlaps = new List<string>();

        try
        {
            var libs = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            if (!libs.IsSuccess)
            {
                var libCheck = new DoctorCheckResult("LibraryPaths", false, libs.Messages?.FirstOrDefault() ?? "Failed to list libraries", sw.Elapsed);
                return (libCheck, paths, overlaps);
            }

            foreach (var lib in libs.Data)
            {
                var exists = Directory.Exists(lib.Path);
                var writable = false;
                var details = exists ? "Path exists" : "Path missing";

                if (exists)
                {
                    try
                    {
                        var testFile = Path.Combine(lib.Path, $".doctor-check-{Guid.NewGuid():N}.tmp");
                        await File.WriteAllTextAsync(testFile, string.Empty, cancellationToken);
                        File.Delete(testFile);
                        writable = true;
                        details = "Path exists; write OK";
                    }
                    catch
                    {
                        writable = false;
                        details = "Path exists; write failed";
                    }
                }

                paths.Add(new LibraryPathResult(
                    lib.Name,
                    lib.TypeValue.ToString(),
                    lib.Path,
                    exists,
                    writable,
                    details));
            }

            var anyMissing = paths.Any(p => !p.Exists);
            var pathCheckMessage = anyMissing
                ? "One or more library paths are missing"
                : "All library paths exist and are accessible";

            var hasOverlaps = false;
            var normalizedPaths = paths
                .Where(p => p.Exists)
                .Select(p => new
                {
                    Original = p,
                    Normalized = NormalizePath(p.Path)
                })
                .ToList();

            for (var i = 0; i < normalizedPaths.Count; i++)
            {
                for (var j = i + 1; j < normalizedPaths.Count; j++)
                {
                    var p1 = normalizedPaths[i];
                    var p2 = normalizedPaths[j];

                    if (IsSubpathOf(p1.Normalized, p2.Normalized) || IsSubpathOf(p2.Normalized, p1.Normalized))
                    {
                        hasOverlaps = true;
                        overlaps.Add($"{p1.Original.Name} ({p1.Original.Path}) overlaps with {p2.Original.Name} ({p2.Original.Path})");
                    }
                }
            }

            if (hasOverlaps)
            {
                pathCheckMessage = $"Library path overlaps detected: {overlaps.Count} overlap(s) found";
            }

            var success = !anyMissing && !hasOverlaps;
            var check = new DoctorCheckResult("LibraryPaths", success, pathCheckMessage, sw.Elapsed);
            return (check, paths, overlaps);
        }
        catch (Exception ex)
        {
            var check = new DoctorCheckResult("LibraryPaths", false, ex.Message, sw.Elapsed);
            return (check, paths, overlaps);
        }
    }

    public async Task<(DoctorCheckResult Check, IReadOnlyList<ConfigurableServiceResult> Services)> RunConfigurableServicesCheckAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var services = new List<ConfigurableServiceResult>();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);

            var serviceDefinitions = new (string Category, string Name, string SettingKey)[]
            {
                ("Search Engine", "Brave", SettingRegistry.SearchEngineBraveEnabled),
                ("Search Engine", "Deezer", SettingRegistry.SearchEngineDeezerEnabled),
                ("Search Engine", "iTunes", SettingRegistry.SearchEngineITunesEnabled),
                ("Search Engine", "Last.fm", SettingRegistry.SearchEngineLastFmEnabled),
                ("Search Engine", "MusicBrainz", SettingRegistry.SearchEngineMusicBrainzEnabled),
                ("Search Engine", "Spotify", SettingRegistry.SearchEngineSpotifyEnabled),
                ("Scrobbling", "Scrobbling", SettingRegistry.ScrobblingEnabled),
                ("Processing", "Magic", SettingRegistry.MagicEnabled),
                ("System", "Email", SettingRegistry.EmailEnabled),
            };

            foreach (var (category, name, settingKey) in serviceDefinitions)
            {
                var value = config.GetValue<string>(settingKey);
                var enabled = bool.TryParse(value, out var b) && b;
                services.Add(new ConfigurableServiceResult(category, name, settingKey, enabled));
            }

            var enabledCount = services.Count(s => s.Enabled);
            var check = new DoctorCheckResult(
                "ConfigurableServices",
                true,
                $"{enabledCount}/{services.Count} services enabled",
                sw.Elapsed);

            return (check, services);
        }
        catch (Exception ex)
        {
            var check = new DoctorCheckResult("ConfigurableServices", false, ex.Message, sw.Elapsed);
            return (check, services);
        }
    }

    public async Task<DoctorCheckResults> RunCoreChecksAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheckResult>();
        var libraryPaths = new List<LibraryPathResult>();
        var configurableServices = new List<ConfigurableServiceResult>();
        var overlaps = new List<string>();

        checks.Add(await RunConfigurationCheckAsync(cancellationToken));
        checks.Add(await RunDatabaseCheckAsync(cancellationToken));

        var (libCheck, paths, libOverlaps) = await RunLibraryPathCheckAsync(cancellationToken);
        checks.Add(libCheck);
        libraryPaths.AddRange(paths);
        overlaps.AddRange(libOverlaps);

        var (servicesCheck, services) = await RunConfigurableServicesCheckAsync(cancellationToken);
        checks.Add(servicesCheck);
        configurableServices.AddRange(services);

        return new DoctorCheckResults
        {
            Checks = checks,
            LibraryPaths = libraryPaths,
            ConfigurableServices = configurableServices
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    private static bool IsSubpathOf(string childPath, string parentPath)
    {
        if (string.IsNullOrWhiteSpace(childPath) || string.IsNullOrWhiteSpace(parentPath))
        {
            return false;
        }

        childPath = childPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        parentPath = parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return childPath.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || childPath.Equals(parentPath, StringComparison.OrdinalIgnoreCase);
    }
}
