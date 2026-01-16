using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Services.Setup;

/// <summary>
/// Severity levels for setup check items.
/// </summary>
public enum SetupCheckSeverity
{
    Blocking,
    Recommended,
    Informational
}

/// <summary>
/// Represents a single setup check item with its result.
/// </summary>
public sealed record SetupItem(
    string Id,
    string Name,
    SetupCheckSeverity Severity,
    bool Success,
    string Details,
    string? Remediation = null,
    string? FixRoute = null);

/// <summary>
/// Overall setup check status containing all items and blocking items.
/// </summary>
public sealed record SetupStatus(
    bool IsReady,
    IReadOnlyList<SetupItem> Items,
    IReadOnlyList<SetupItem> BlockingItems,
    DateTimeOffset CheckedAt);

/// <summary>
/// Interface for the setup check service.
/// </summary>
public interface ISetupCheckService
{
    /// <summary>
    /// Runs all setup checks and returns the overall status.
    /// </summary>
    Task<SetupStatus> SetupCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets only the blocking items that need to be resolved.
    /// </summary>
    Task<IReadOnlyList<SetupItem>> GetBlockingItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if onboarding is required (not completed or has blocking items).
    /// </summary>
    Task<bool> IsOnboardingRequiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service that performs system setup readiness checks.
/// </summary>
public sealed class SetupCheckService : ISetupCheckService
{
    private readonly IDbContextFactory<MelodeeDbContext> _dbContextFactory;
    private readonly LibraryService _libraryService;
    private readonly IMelodeeConfigurationFactory _configurationFactory;

    private const long DiskSpaceWarningBytes = 1L * 1024 * 1024 * 1024; // 1 GB

    public SetupCheckService(
        IDbContextFactory<MelodeeDbContext> dbContextFactory,
        LibraryService libraryService,
        IMelodeeConfigurationFactory configurationFactory)
    {
        _dbContextFactory = dbContextFactory;
        _libraryService = libraryService;
        _configurationFactory = configurationFactory;
    }

    public async Task<SetupStatus> SetupCheckAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SetupItem>();

        // Check required settings
        items.AddRange(await CheckRequiredSettingsAsync(cancellationToken));

        // Check library paths
        items.AddRange(await CheckLibraryPathsAsync(cancellationToken));

        // Check disk space (recommended, non-blocking)
        items.AddRange(await CheckDiskSpaceAsync(cancellationToken));

        var blockingItems = items.Where(i => i.Severity == SetupCheckSeverity.Blocking && !i.Success).ToList();

        return new SetupStatus(
            IsReady: !blockingItems.Any(),
            Items: items,
            BlockingItems: blockingItems,
            CheckedAt: DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<SetupItem>> GetBlockingItemsAsync(CancellationToken cancellationToken = default)
    {
        var status = await SetupCheckAsync(cancellationToken);
        return status.BlockingItems;
    }

    public async Task<bool> IsOnboardingRequiredAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var onboardingCompletedAt = config.GetValue<string?>(SettingRegistry.SystemOnboardingCompletedAt);

        if (string.IsNullOrWhiteSpace(onboardingCompletedAt))
        {
            return true;
        }

        var status = await SetupCheckAsync(cancellationToken);
        return !status.IsReady;
    }

    private async Task<List<SetupItem>> CheckRequiredSettingsAsync(CancellationToken cancellationToken)
    {
        var items = new List<SetupItem>();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);

        // Check settings with RequiredNotSetValue placeholder
        var settingsWithPlaceholders = await db.Settings
            .Where(s => s.Value == MelodeeConfiguration.RequiredNotSetValue)
            .ToListAsync(cancellationToken);

        foreach (var setting in settingsWithPlaceholders)
        {
            items.Add(new SetupItem(
                Id: $"setting-{setting.Key}",
                Name: GetSettingDisplayName(setting.Key),
                Severity: SetupCheckSeverity.Blocking,
                Success: false,
                Details: $"Setting '{setting.Key}' is not configured",
                Remediation: $"Set a value for {setting.Key}",
                FixRoute: "/onboarding/settings"));
        }

        // Check explicitly required keys
        var explicitRequiredKeys = new[]
        {
            (Key: SettingRegistry.SystemBaseUrl, Name: "Base URL", FixRoute: "/onboarding/branding"),
            (Key: SettingRegistry.SystemSiteName, Name: "Site Name", FixRoute: "/onboarding/branding"),
            (Key: SettingRegistry.SecuritySecretKey, Name: "Security Secret Key", FixRoute: "/onboarding/security")
        };

        foreach (var (key, name, fixRoute) in explicitRequiredKeys)
        {
            var value = config.GetValue<string>(key);
            var isMissing = string.IsNullOrWhiteSpace(value) || value == MelodeeConfiguration.RequiredNotSetValue;

            // Skip if already flagged by placeholder check
            if (settingsWithPlaceholders.Any(s => s.Key == key))
            {
                continue;
            }

            items.Add(new SetupItem(
                Id: $"setting-{key}",
                Name: name,
                Severity: SetupCheckSeverity.Blocking,
                Success: !isMissing,
                Details: isMissing ? $"{name} is not configured" : $"{name} is configured",
                Remediation: isMissing ? $"Set a value for {key}" : null,
                FixRoute: isMissing ? fixRoute : null));
        }

        return items;
    }

    private async Task<List<SetupItem>> CheckLibraryPathsAsync(CancellationToken cancellationToken)
    {
        var items = new List<SetupItem>();

        var libsResult = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
        if (!libsResult.IsSuccess)
        {
            items.Add(new SetupItem(
                Id: "library-list",
                Name: "Library Configuration",
                Severity: SetupCheckSeverity.Blocking,
                Success: false,
                Details: $"Failed to retrieve libraries: {libsResult.Messages?.FirstOrDefault()}",
                Remediation: "Check database connectivity and library configuration",
                FixRoute: null));
            return items;
        }

        var libraries = libsResult.Data.ToList();

        // Check for required library types
        var hasInbound = libraries.Any(l => l.TypeValue == LibraryType.Inbound);
        var hasStaging = libraries.Any(l => l.TypeValue == LibraryType.Staging);
        var hasStorage = libraries.Any(l => l.TypeValue == LibraryType.Storage);

        if (!hasInbound)
        {
            items.Add(new SetupItem(
                Id: "library-missing-inbound",
                Name: "Inbound Library",
                Severity: SetupCheckSeverity.Blocking,
                Success: false,
                Details: "Inbound library is not configured",
                Remediation: "Create an Inbound library for receiving media files",
                FixRoute: "/onboarding/paths"));
        }

        if (!hasStaging)
        {
            items.Add(new SetupItem(
                Id: "library-missing-staging",
                Name: "Staging Library",
                Severity: SetupCheckSeverity.Blocking,
                Success: false,
                Details: "Staging library is not configured",
                Remediation: "Create a Staging library for processed media files",
                FixRoute: "/onboarding/paths"));
        }

        if (!hasStorage)
        {
            items.Add(new SetupItem(
                Id: "library-missing-storage",
                Name: "Storage Library",
                Severity: SetupCheckSeverity.Blocking,
                Success: false,
                Details: "No Storage library is configured",
                Remediation: "Create at least one Storage library for your media collection",
                FixRoute: "/onboarding/paths"));
        }

        // Check library paths for each required library
        foreach (var lib in libraries)
        {
            // Check for path traversal sequences
            if (ContainsPathTraversal(lib.Path))
            {
                items.Add(new SetupItem(
                    Id: $"library-traversal-{lib.Id}",
                    Name: $"Library Path Security: {lib.Name}",
                    Severity: SetupCheckSeverity.Blocking,
                    Success: false,
                    Details: $"Library path '{lib.Path}' contains invalid path traversal sequences",
                    Remediation: "Remove '..' or '.' from library paths",
                    FixRoute: "/onboarding/paths"));
                continue;
            }

            // Resolve symlinks for further checks
            var resolvedPath = ResolveSymlinks(lib.Path);

            var exists = Directory.Exists(resolvedPath);
            var writable = false;

            if (exists)
            {
                try
                {
                    var testFile = Path.Combine(resolvedPath, $".setup-check-{Guid.NewGuid():N}.tmp");
                    await File.WriteAllTextAsync(testFile, string.Empty, cancellationToken);
                    File.Delete(testFile);
                    writable = true;
                }
                catch
                {
                    writable = false;
                }
            }

            var libraryTypeName = lib.TypeValue.ToString();

            if (!exists)
            {
                items.Add(new SetupItem(
                    Id: $"library-exists-{lib.Id}",
                    Name: $"{libraryTypeName} Library Path: {lib.Name}",
                    Severity: SetupCheckSeverity.Blocking,
                    Success: false,
                    Details: $"Library path does not exist: {lib.Path}",
                    Remediation: $"Create directory or fix path for {lib.Name}",
                    FixRoute: "/onboarding/paths"));
            }
            else if (!writable)
            {
                items.Add(new SetupItem(
                    Id: $"library-writable-{lib.Id}",
                    Name: $"{libraryTypeName} Library Write Access: {lib.Name}",
                    Severity: SetupCheckSeverity.Blocking,
                    Success: false,
                    Details: $"Library path is not writable: {lib.Path}",
                    Remediation: $"Check write permissions for {lib.Name}",
                    FixRoute: "/onboarding/paths"));
            }
        }

        // Check for path overlaps
        var pathOverlaps = DetectPathOverlaps(libraries);
        foreach (var overlap in pathOverlaps)
        {
            items.Add(new SetupItem(
                Id: $"library-overlap-{overlap.Library1Id}-{overlap.Library2Id}",
                Name: "Library Path Overlap",
                Severity: SetupCheckSeverity.Blocking,
                Success: false,
                Details: overlap.Message,
                Remediation: "Ensure library paths do not overlap",
                FixRoute: "/onboarding/paths"));
        }

        return items;
    }

    private async Task<List<SetupItem>> CheckDiskSpaceAsync(CancellationToken cancellationToken)
    {
        var items = new List<SetupItem>();

        var libsResult = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
        if (!libsResult.IsSuccess)
        {
            return items; // Skip disk space check if we can't get libraries
        }

        foreach (var lib in libsResult.Data)
        {
            var resolvedPath = ResolveSymlinks(lib.Path);
            if (!Directory.Exists(resolvedPath))
            {
                continue;
            }

            try
            {
                var (totalBytes, availableBytes) = GetDiskSpaceForPath(resolvedPath);

                if (availableBytes < DiskSpaceWarningBytes)
                {
                    items.Add(new SetupItem(
                        Id: $"disk-space-{lib.Id}",
                        Name: $"Disk Space: {lib.Name}",
                        Severity: SetupCheckSeverity.Recommended,
                        Success: false,
                        Details: $"Low disk space: {FormatBytes(availableBytes)} available on {lib.Path}",
                        Remediation: "Free up disk space or add more storage",
                        FixRoute: null));
                }
            }
            catch
            {
                // Ignore disk space check errors
            }
        }

        return items;
    }

    private static string GetSettingDisplayName(string key)
    {
        return key.Split('.').LastOrDefault()?.Replace('_', ' ') ?? key;
    }

    private static bool ContainsPathTraversal(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("../") || normalized.Contains("/./") || normalized.Contains("..\\");
    }

    private static string ResolveSymlinks(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static List<(int Library1Id, int Library2Id, string Message)> DetectPathOverlaps(List<Library> libraries)
    {
        var overlaps = new List<(int, int, string)>();

        for (var i = 0; i < libraries.Count; i++)
        {
            for (var j = i + 1; j < libraries.Count; j++)
            {
                var lib1 = libraries[i];
                var lib2 = libraries[j];

                var path1 = ResolveSymlinks(lib1.Path).TrimEnd('/');
                var path2 = ResolveSymlinks(lib2.Path).TrimEnd('/');

                if (path1.Equals(path2, StringComparison.OrdinalIgnoreCase))
                {
                    overlaps.Add((lib1.Id, lib2.Id, $"Libraries '{lib1.Name}' and '{lib2.Name}' use the same path"));
                    continue;
                }

                if (path1.StartsWith(path2 + '/', StringComparison.OrdinalIgnoreCase) ||
                    path2.StartsWith(path1 + '/', StringComparison.OrdinalIgnoreCase))
                {
                    overlaps.Add((lib1.Id, lib2.Id, $"Library '{lib1.Name}' ({path1}) is inside '{lib2.Name}' ({path2})"));
                }
            }
        }

        return overlaps;
    }

    private static (long TotalBytes, long AvailableBytes) GetDiskSpaceForPath(string path)
    {
        var resolvedPath = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows())
        {
            var root = Path.GetPathRoot(resolvedPath);
            if (!string.IsNullOrEmpty(root))
            {
                var driveInfo = new DriveInfo(root);
                return (driveInfo.TotalSize, driveInfo.AvailableFreeSpace);
            }
        }
        else
        {
            try
            {
                var drives = DriveInfo.GetDrives();
                DriveInfo? bestMatch = null;
                var bestMatchLength = 0;

                foreach (var drive in drives)
                {
                    try
                    {
                        if (!drive.IsReady)
                        {
                            continue;
                        }

                        var mountPoint = drive.Name;
                        if (resolvedPath.StartsWith(mountPoint, StringComparison.Ordinal) && mountPoint.Length > bestMatchLength)
                        {
                            bestMatch = drive;
                            bestMatchLength = mountPoint.Length;
                        }
                    }
                    catch
                    {
                        // Skip inaccessible drives
                    }
                }

                if (bestMatch != null)
                {
                    return (bestMatch.TotalSize, bestMatch.AvailableFreeSpace);
                }
            }
            catch
            {
                // Fall through to simple approach
            }

            var rootPath = Path.GetPathRoot(resolvedPath) ?? "/";
            var rootDrive = new DriveInfo(rootPath);
            return (rootDrive.TotalSize, rootDrive.AvailableFreeSpace);
        }

        return (0, 0);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
