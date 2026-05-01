using System.Data.Common;
using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.Doctor;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;
using Serilog.Events;
using SerilogTimings;

namespace Melodee.Blazor.Services;

/// <summary>
/// Blazor-specific Doctor service that extends the shared DoctorServiceBase
/// with additional host-specific checks.
/// </summary>
/// <summary>
/// Blazor-specific Doctor service that extends the shared DoctorServiceBase
/// with additional host-specific checks.
/// </summary>
#pragma warning disable CS9124 // Suppress warning - parameters are intentionally captured for derived class use
public sealed class DoctorService(
    IConfiguration configuration,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IDbContextFactory<MusicBrainzDbContext> musicBrainzDbContextFactory,
    IDbContextFactory<ArtistSearchEngineServiceDbContext> artistSearchEngineDbContextFactory,
    LibraryService libraryService,
    IMelodeeConfigurationFactory configurationFactory,
    IWebHostEnvironment webHostEnvironment,
    IHttpContextAccessor httpContextAccessor,
    ISchedulerFactory schedulerFactory) : DoctorServiceBase(dbContextFactory, libraryService, configurationFactory), Melodee.Blazor.Services.IDoctorService
{
    private readonly IDbContextFactory<MelodeeDbContext> _dbContextFactory = dbContextFactory;
    private readonly IDbContextFactory<MusicBrainzDbContext> _musicBrainzDbContextFactory = musicBrainzDbContextFactory;
    private readonly IDbContextFactory<ArtistSearchEngineServiceDbContext> _artistSearchEngineDbContextFactory = artistSearchEngineDbContextFactory;
    private readonly LibraryService _libraryService = libraryService;
#pragma warning restore CS9124
    private static readonly string[] RequiredConnectionStrings =
    [
        "DefaultConnection",
        "MusicBrainzConnection",
        "ArtistSearchEngineConnection"
    ];

    private static readonly string[] RequiredEnvironmentVariables =
    [
        // Add any required environment variables here if needed
    ];

    private const long DiskSpaceWarningThresholdBytes = 10L * 1024 * 1024 * 1024; // 10 GB
    private const long DiskSpaceCriticalThresholdBytes = 1L * 1024 * 1024 * 1024; // 1 GB
    private const int MinJwtKeyLength = 64; // For HMAC-SHA512
    private const long MemoryPressureWarningBytes = 500L * 1024 * 1024; // 500 MB
    private const long MemoryPressureCriticalBytes = 100L * 1024 * 1024; // 100 MB available
    private const int JobStalenessHours = 48; // Jobs should run within this period

    public async Task<bool> NeedsAttentionAsync(CancellationToken cancellationToken = default)
    {
        // Dashboard uses this fast path on first render, so keep it limited to
        // cheap checks that still catch obviously unhealthy startup state.
        using (Operation.At(LogEventLevel.Debug).Time("[{Service}] HasMissingConnectionStrings", nameof(DoctorService)))
        {
            if (HasMissingConnectionStrings())
            {
                return true;
            }
        }

        // Fast-path: only verify the MusicBrainz file exists and is non-empty.
        // Full probe (query) is deferred to RunAllChecksAsync.
        using (Operation.At(LogEventLevel.Debug).Time("[{Service}] HasMusicBrainzFileIssues", nameof(DoctorService)))
        {
            if (!HasConfiguredFileBackedDatabase("MusicBrainzConnection"))
            {
                return true;
            }
        }

        // Fast-path: only verify the ArtistSearch file exists and is non-empty.
        // Full probe (query) is deferred to RunAllChecksAsync.
        using (Operation.At(LogEventLevel.Debug).Time("[{Service}] HasArtistSearchFileIssues", nameof(DoctorService)))
        {
            if (!HasConfiguredFileBackedDatabase("ArtistSearchEngineConnection"))
            {
                return true;
            }
        }

        using (Operation.At(LogEventLevel.Debug).Time("[{Service}] DefaultConnectionCanConnect", nameof(DoctorService)))
        {
            try
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
                return !await db.Database.CanConnectAsync(cancellationToken);
            }
            catch
            {
                return true;
            }
        }
    }

    public async Task<bool> IsMusicBrainzDatabaseEmptyAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("MusicBrainzConnection");
        if (!HasNonEmptyFileBackedDatabase(connectionString))
        {
            return true;
        }

        var (canQuery, hasArtistData, _) = await ProbeMusicBrainzDatabaseAsync(cancellationToken);
        return !canQuery || !hasArtistData;
    }

    public async Task<BlazorDoctorCheckResults> RunAllChecksAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<DoctorCheckResult>();
        var libraryPaths = new List<LibraryPathResult>();
        var configurableServices = new List<ConfigurableServiceResult>();
        var serilogLogPaths = new List<SerilogLogPathInfo>();
        var connectionStrings = new List<ConnectionStringInfo>();
        var environmentVariables = new List<EnvironmentVariableInfo>();
        var diskSpaceInfo = new List<DiskSpaceInfo>();
        var searchEngineApiKeys = new List<SearchEngineApiKeyInfo>();

        // Run core checks using base class implementation
        var coreResults = await RunCoreChecksAsync(cancellationToken);
        checks.AddRange(coreResults.Checks);
        libraryPaths.AddRange(coreResults.LibraryPaths);
        configurableServices.AddRange(coreResults.ConfigurableServices);

        // Run Blazor-specific checks
        var (musicBrainzCheck, isMusicBrainzEmpty) = await RunMusicBrainzDbCheckAsync(cancellationToken);
        checks.Add(musicBrainzCheck);

        checks.Add(await RunArtistSearchEngineDbCheckAsync(cancellationToken));

        var (serilogCheck, logPaths) = RunSerilogCheckAsync();
        checks.Add(serilogCheck);
        serilogLogPaths.AddRange(logPaths);

        // Disk space and path checks
        var (diskSpaceCheck, diskInfo) = await RunDiskSpaceCheckAsync(cancellationToken);
        checks.Add(diskSpaceCheck);
        diskSpaceInfo.AddRange(diskInfo);

        var (pathOverlapCheck, _) = await RunLibraryPathOverlapCheckAsync(cancellationToken);
        checks.Add(pathOverlapCheck);

        var (apiKeysCheck, apiKeys) = await RunSearchEngineApiKeysCheckAsync(cancellationToken);
        checks.Add(apiKeysCheck);
        searchEngineApiKeys.AddRange(apiKeys);

        // Security and configuration checks
        checks.Add(await RunSmtpConfigurationCheckAsync(cancellationToken));
        checks.Add(RunJwtTokenStrengthCheck());
        checks.Add(RunHttpsCheck());
        checks.Add(await RunDefaultAdminPasswordCheckAsync(cancellationToken));

        // Scheduler and background jobs check
        checks.Add(await RunSchedulerCheckAsync(cancellationToken));

        // Infrastructure checks
        checks.Add(RunFFmpegCheck());
        checks.Add(RunMemoryCheck());
        checks.Add(RunTempDirectoryCheck());
        checks.Add(await RunDatabaseLatencyCheckAsync(cancellationToken));

        // Optional feature checks (Jukebox and Podcast)
        checks.Add(await RunJukeboxConfigurationCheckAsync(cancellationToken));
        checks.Add(await RunPodcastConfigurationCheckAsync(cancellationToken));

        // Gather connection string info
        connectionStrings.AddRange(await GatherConnectionStringInfoAsync(cancellationToken));

        // Gather environment variable info
        environmentVariables.AddRange(GatherEnvironmentVariableInfo());

        return new BlazorDoctorCheckResults
        {
            Checks = checks,
            LibraryPaths = libraryPaths,
            ConfigurableServices = configurableServices,
            SerilogLogPaths = serilogLogPaths,
            ConnectionStrings = connectionStrings,
            EnvironmentVariables = environmentVariables,
            DiskSpaceInfo = diskSpaceInfo,
            SearchEngineApiKeys = searchEngineApiKeys,
            IsMusicBrainzEmpty = isMusicBrainzEmpty
        };
    }

    private bool HasMissingConnectionStrings()
    {
        foreach (var connStr in RequiredConnectionStrings)
        {
            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(connStr)))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<bool> HasLibraryPathIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var libs = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            if (!libs.IsSuccess)
            {
                return true;
            }

            foreach (var lib in libs.Data)
            {
                if (!Directory.Exists(lib.Path))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<(DoctorCheckResult Check, bool IsEmpty)> RunMusicBrainzDbCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var connectionString = configuration.GetConnectionString("MusicBrainzConnection") ?? "";
            var fileInfo = DescribeFileDatabasePath(connectionString);
            if (!HasNonEmptyFileBackedDatabase(connectionString))
            {
                return (new DoctorCheckResult("MusicBrainzDatabase", false, "MusicBrainz database is empty or not initialized", sw.Elapsed), true);
            }

            var (canQuery, hasArtistData, error) = await ProbeMusicBrainzDatabaseAsync(cancellationToken);
            if (!canQuery)
            {
                var details = string.IsNullOrWhiteSpace(error)
                    ? $"Unable to query; {fileInfo}"
                    : $"{error}; {fileInfo}";
                return (new DoctorCheckResult("MusicBrainzDatabase", false, details, sw.Elapsed), true);
            }

            if (!hasArtistData)
            {
                return (new DoctorCheckResult("MusicBrainzDatabase", false, $"MusicBrainz database is empty or not initialized; {fileInfo}", sw.Elapsed), true);
            }

            return (new DoctorCheckResult("MusicBrainzDatabase", true, $"OK; {fileInfo}", sw.Elapsed), false);
        }
        catch (Exception ex)
        {
            return (new DoctorCheckResult("MusicBrainzDatabase", false, ex.Message, sw.Elapsed), true);
        }
    }

    private async Task<DoctorCheckResult> RunArtistSearchEngineDbCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var connectionString = configuration.GetConnectionString("ArtistSearchEngineConnection") ?? "";
            var fileInfo = DescribeFileDatabasePath(connectionString);
            if (!HasNonEmptyFileBackedDatabase(connectionString))
            {
                return new DoctorCheckResult(
                    "ArtistSearchEngineDatabase",
                    false,
                    $"Artist search engine database is empty or not initialized; {fileInfo}",
                    sw.Elapsed);
            }

            var (canQuery, error) = await ProbeArtistSearchDatabaseAsync(cancellationToken);
            var details = canQuery
                ? $"OK; {fileInfo}"
                : string.IsNullOrWhiteSpace(error)
                    ? $"Unable to query; {fileInfo}"
                    : $"{error}; {fileInfo}";

            return new DoctorCheckResult("ArtistSearchEngineDatabase", canQuery, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("ArtistSearchEngineDatabase", false, ex.Message, sw.Elapsed);
        }
    }

    private (DoctorCheckResult Check, List<SerilogLogPathInfo> Paths) RunSerilogCheckAsync()
    {
        var sw = Stopwatch.StartNew();
        var paths = new List<SerilogLogPathInfo>();

        try
        {
            var filePaths = GetSerilogFilePathsFromConfig();

            foreach (var (sinkName, path) in filePaths)
            {
                var directoryPath = Path.GetDirectoryName(path);

                if (string.IsNullOrEmpty(directoryPath))
                {
                    var lastSeparator = path.LastIndexOfAny(['/', '\\']);
                    if (lastSeparator > 0)
                    {
                        directoryPath = path[..lastSeparator];
                    }
                }

                if (!string.IsNullOrEmpty(directoryPath))
                {
                    if (!Path.IsPathRooted(directoryPath))
                    {
                        directoryPath = Path.GetFullPath(Path.Combine(webHostEnvironment.ContentRootPath, directoryPath));
                    }

                    var dirExists = Directory.Exists(directoryPath);
                    var writable = dirExists && IsDirectoryWritable(directoryPath);

                    paths.Add(new SerilogLogPathInfo(sinkName, directoryPath, dirExists, writable));
                }
            }

            var hasIssues = paths.Any(p => !p.DirectoryExists || !p.Writable);
            var detailMessage = paths.Count == 0
                ? "No file logging configured"
                : hasIssues
                    ? "Some log paths have issues"
                    : "All log paths OK";

            return (new DoctorCheckResult("SerilogLogging", !hasIssues || paths.Count == 0, detailMessage, sw.Elapsed), paths);
        }
        catch (Exception ex)
        {
            return (new DoctorCheckResult("SerilogLogging", false, ex.Message, sw.Elapsed), paths);
        }
    }

    private async Task<List<ConnectionStringInfo>> GatherConnectionStringInfoAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<ConnectionStringInfo>();

        foreach (var name in RequiredConnectionStrings)
        {
            var value = configuration.GetConnectionString(name) ?? "";
            var isValid = !string.IsNullOrWhiteSpace(value);
            var isFileBased = value.Contains("Data Source=") && !value.Contains("Host=");
            bool? fileExists = null;
            bool? fileWritable = null;
            string? filePath = null;
            bool? canConnect = null;
            string? connectionError = null;

            if (isFileBased && isValid)
            {
                try
                {
                    var builder = new DbConnectionStringBuilder { ConnectionString = value };
                    filePath = builder.ContainsKey("Data Source") ? builder["Data Source"]?.ToString() : null;
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        fileExists = File.Exists(filePath);
                        if (fileExists == true)
                        {
                            var dir = Path.GetDirectoryName(filePath);
                            fileWritable = !string.IsNullOrEmpty(dir) && IsDirectoryWritable(dir);
                        }
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }

            if (isFileBased && HasNonEmptyFileBackedDatabase(value))
            {
                switch (name)
                {
                    case "MusicBrainzConnection":
                        var musicBrainzState = await ProbeMusicBrainzDatabaseAsync(cancellationToken);
                        canConnect = musicBrainzState.CanQuery;
                        connectionError = musicBrainzState.Error;
                        break;
                    case "ArtistSearchEngineConnection":
                        (canConnect, connectionError) = await ProbeArtistSearchDatabaseAsync(cancellationToken);
                        break;
                }
            }

            results.Add(new ConnectionStringInfo(
                name,
                MaskConnectionString(value),
                isValid,
                isFileBased,
                fileExists,
                fileWritable,
                filePath,
                canConnect,
                connectionError));
        }

        return results;
    }

    private List<EnvironmentVariableInfo> GatherEnvironmentVariableInfo()
    {
        var results = new List<EnvironmentVariableInfo>();

        foreach (var name in RequiredEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name) ?? "";
            var isSet = !string.IsNullOrWhiteSpace(value);
            results.Add(new EnvironmentVariableInfo(name, isSet ? MaskConnectionString(value) : "(not set)", isSet));
        }

        return results;
    }

    private static string DescribeFileDatabasePath(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "No connection string";
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var path = builder.ContainsKey("Data Source") ? builder["Data Source"]?.ToString() : null;
            if (string.IsNullOrEmpty(path))
            {
                return "No data source in connection string";
            }

            if (!File.Exists(path))
            {
                return $"File not found: {path}";
            }

            var fi = new FileInfo(path);
            return $"{path} ({FormatFileSize(fi.Length)})";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string FormatFileSize(long bytes)
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

    private bool HasConfiguredFileBackedDatabase(string connectionStringName)
    {
        return HasNonEmptyFileBackedDatabase(configuration.GetConnectionString(connectionStringName));
    }

    private async Task<bool> HasMusicBrainzConnectionIssuesAsync(CancellationToken cancellationToken)
    {
        if (!HasConfiguredFileBackedDatabase("MusicBrainzConnection"))
        {
            return true;
        }

        var (canQuery, _, _) = await ProbeMusicBrainzDatabaseAsync(cancellationToken);
        return !canQuery;
    }

    private async Task<bool> HasArtistSearchConnectionIssuesAsync(CancellationToken cancellationToken)
    {
        if (!HasConfiguredFileBackedDatabase("ArtistSearchEngineConnection"))
        {
            return true;
        }

        var (canQuery, _) = await ProbeArtistSearchDatabaseAsync(cancellationToken);
        return !canQuery;
    }

    private static bool HasNonEmptyFileBackedDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var path = builder.ContainsKey("Data Source") ? builder["Data Source"]?.ToString() : null;
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool CanQuery, string? Error)> ProbeArtistSearchDatabaseAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _artistSearchEngineDbContextFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    CREATE INDEX IF NOT EXISTS "IX_Artists_IsLocked_LastRefreshed"
                    ON "Artists" ("IsLocked", "LastRefreshed")
                    """,
                    cancellationToken);
            }

            _ = await db.Artists
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            _ = await db.Albums
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(bool CanQuery, bool HasArtistData, string? Error)> ProbeMusicBrainzDatabaseAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _musicBrainzDbContextFactory.CreateDbContextAsync(cancellationToken);
            var firstArtistId = await db.Artists
                .AsNoTracking()
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return (true, firstArtistId != 0, null);
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    private static bool IsDirectoryWritable(string path)
    {
        try
        {
            var testFile = Path.Combine(path, $".melodee-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetLibraryPathDetails(bool exists, bool writable)
    {
        return (exists, writable) switch
        {
            (false, _) => "Path missing",
            (true, false) => "Path not writable",
            (true, true) => "OK"
        };
    }

    private static string MaskConnectionString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty)";
        }

        if (value.Length <= 10)
        {
            return new string('*', value.Length);
        }

        return $"{value[..5]}...{value[^5..]}";
    }

    private List<(string SinkName, string Path)> GetSerilogFilePathsFromConfig()
    {
        var results = new List<(string SinkName, string Path)>();

        try
        {
            var writeToSection = configuration.GetSection("Serilog:WriteTo");
            var sinks = writeToSection.GetChildren().ToList();

            foreach (var sink in sinks)
            {
                var sinkName = sink.GetValue<string>("Name") ?? "Unknown";

                if (sinkName.Equals("File", StringComparison.OrdinalIgnoreCase))
                {
                    var path = sink.GetValue<string>("Args:path");
                    if (!string.IsNullOrEmpty(path))
                    {
                        results.Add((sinkName, path));
                    }
                }
            }
        }
        catch
        {
            // Ignore config parsing errors
        }

        return results;
    }

    private async Task<bool> HasDiskSpaceIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var libs = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            if (!libs.IsSuccess)
            {
                return false;
            }

            foreach (var lib in libs.Data)
            {
                if (!Directory.Exists(lib.Path))
                {
                    continue;
                }

                try
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(lib.Path) ?? lib.Path);
                    if (driveInfo.AvailableFreeSpace < DiskSpaceCriticalThresholdBytes)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore drive info errors
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> HasLibraryPathOverlapAsync(CancellationToken cancellationToken)
    {
        try
        {
            var libs = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            if (!libs.IsSuccess || libs.Data.Count() < 2)
            {
                return false;
            }

            var paths = libs.Data
                .Select(l => NormalizePath(l.Path))
                .ToList();

            for (var i = 0; i < paths.Count; i++)
            {
                for (var j = i + 1; j < paths.Count; j++)
                {
                    if (IsPathOverlap(paths[i], paths[j]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> HasSearchEngineApiKeyIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.SearchEngineSpotifyEnabled,
                SettingRegistry.SearchEngineSpotifyApiKey,
                SettingRegistry.SearchEngineSpotifyClientSecret
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var spotifyEnabled = settings.TryGetValue(SettingRegistry.SearchEngineSpotifyEnabled, out var se)
                && bool.TryParse(se, out var seb) && seb;
            var spotifyApiKey = settings.TryGetValue(SettingRegistry.SearchEngineSpotifyApiKey, out var sak) ? sak : "";
            var spotifySecret = settings.TryGetValue(SettingRegistry.SearchEngineSpotifyClientSecret, out var ss) ? ss : "";

            if (spotifyEnabled && (string.IsNullOrWhiteSpace(spotifyApiKey) || string.IsNullOrWhiteSpace(spotifySecret)))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(DoctorCheckResult Check, List<DiskSpaceInfo> Info)> RunDiskSpaceCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var diskInfo = new List<DiskSpaceInfo>();

        try
        {
            var libs = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            if (!libs.IsSuccess)
            {
                return (new DoctorCheckResult("DiskSpace", false, "Failed to list libraries", sw.Elapsed), diskInfo);
            }

            foreach (var lib in libs.Data)
            {
                if (!Directory.Exists(lib.Path))
                {
                    diskInfo.Add(new DiskSpaceInfo(
                        lib.Name,
                        lib.Path,
                        0,
                        0,
                        0,
                        0,
                        DiskSpaceStatus.Unknown));
                    continue;
                }

                try
                {
                    // Get disk space for the actual path (handles symlinks correctly)
                    var (totalBytes, availableBytes) = GetDiskSpaceForPath(lib.Path);
                    var usedBytes = totalBytes - availableBytes;
                    var usedPercent = totalBytes > 0 ? (double)usedBytes / totalBytes * 100 : 0;

                    var status = availableBytes switch
                    {
                        < DiskSpaceCriticalThresholdBytes => DiskSpaceStatus.Critical,
                        < DiskSpaceWarningThresholdBytes => DiskSpaceStatus.Warning,
                        _ => DiskSpaceStatus.Ok
                    };

                    diskInfo.Add(new DiskSpaceInfo(
                        lib.Name,
                        lib.Path,
                        totalBytes,
                        availableBytes,
                        usedBytes,
                        usedPercent,
                        status));
                }
                catch
                {
                    diskInfo.Add(new DiskSpaceInfo(
                        lib.Name,
                        lib.Path,
                        0,
                        0,
                        0,
                        0,
                        DiskSpaceStatus.Unknown));
                }
            }

            var hasCritical = diskInfo.Any(d => d.Status == DiskSpaceStatus.Critical);
            var hasWarning = diskInfo.Any(d => d.Status == DiskSpaceStatus.Warning);

            var detailMessage = (hasCritical, hasWarning) switch
            {
                (true, _) => "Critical: Low disk space on one or more drives",
                (false, true) => "Warning: Disk space is getting low on one or more drives",
                _ => "All drives have adequate space"
            };

            return (new DoctorCheckResult("DiskSpace", !hasCritical, detailMessage, sw.Elapsed), diskInfo);
        }
        catch (Exception ex)
        {
            return (new DoctorCheckResult("DiskSpace", false, ex.Message, sw.Elapsed), diskInfo);
        }
    }

    private static (long TotalBytes, long AvailableBytes) GetDiskSpaceForPath(string path)
    {
        // Resolve the actual path (follows symlinks)
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
            // On Linux/macOS, use statfs via DriveInfo on the actual mount point
            // DriveInfo works with any path, not just mount points
            try
            {
                // Find the drive that contains this path
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

            // Fallback: use root
            var rootPath = Path.GetPathRoot(resolvedPath) ?? "/";
            var rootDrive = new DriveInfo(rootPath);
            return (rootDrive.TotalSize, rootDrive.AvailableFreeSpace);
        }

        return (0, 0);
    }

    private async Task<(DoctorCheckResult Check, List<string> Overlaps)> RunLibraryPathOverlapCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var overlaps = new List<string>();

        try
        {
            var libs = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            if (!libs.IsSuccess)
            {
                return (new DoctorCheckResult("LibraryPathOverlap", false, "Failed to list libraries", sw.Elapsed), overlaps);
            }

            var libList = libs.Data.ToList();

            for (var i = 0; i < libList.Count; i++)
            {
                for (var j = i + 1; j < libList.Count; j++)
                {
                    var path1 = NormalizePath(libList[i].Path);
                    var path2 = NormalizePath(libList[j].Path);

                    if (IsPathOverlap(path1, path2))
                    {
                        overlaps.Add($"{libList[i].Name} ({path1}) overlaps with {libList[j].Name} ({path2})");
                    }
                }
            }

            var hasOverlaps = overlaps.Count > 0;
            var detailMessage = hasOverlaps
                ? $"Warning: {overlaps.Count} library path overlap(s) detected"
                : "No library path overlaps detected";

            return (new DoctorCheckResult("LibraryPathOverlap", !hasOverlaps, detailMessage, sw.Elapsed), overlaps);
        }
        catch (Exception ex)
        {
            return (new DoctorCheckResult("LibraryPathOverlap", false, ex.Message, sw.Elapsed), overlaps);
        }
    }

    private async Task<(DoctorCheckResult Check, List<SearchEngineApiKeyInfo> ApiKeys)> RunSearchEngineApiKeysCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var apiKeys = new List<SearchEngineApiKeyInfo>();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var searchEngineDefinitions = new (string Name, string EnabledKey, string? ApiKeyKey, string? SecretKey)[]
            {
                ("Spotify", SettingRegistry.SearchEngineSpotifyEnabled, SettingRegistry.SearchEngineSpotifyApiKey, SettingRegistry.SearchEngineSpotifyClientSecret),
                ("Brave", SettingRegistry.SearchEngineBraveEnabled, SettingRegistry.SearchEngineBraveApiKey, null),
                ("Last.fm (Scrobbling)", SettingRegistry.ScrobblingLastFmEnabled, SettingRegistry.ScrobblingLastFmApiKey, SettingRegistry.ScrobblingLastFmSharedSecret),
            };

            var allKeys = searchEngineDefinitions
                .SelectMany(d => new[] { d.EnabledKey, d.ApiKeyKey, d.SecretKey })
                .Where(k => k != null)
                .Cast<string>()
                .ToList();

            var settings = await db.Settings
                .Where(s => allKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            foreach (var (name, enabledKey, apiKeyKey, secretKey) in searchEngineDefinitions)
            {
                var isEnabled = settings.TryGetValue(enabledKey, out var enabledValue)
                    && bool.TryParse(enabledValue, out var eb) && eb;

                var hasApiKey = apiKeyKey != null
                    && settings.TryGetValue(apiKeyKey, out var apiKeyValue)
                    && !string.IsNullOrWhiteSpace(apiKeyValue);

                var hasSecret = secretKey == null
                    || (settings.TryGetValue(secretKey, out var secretValue)
                        && !string.IsNullOrWhiteSpace(secretValue));

                var isConfigured = hasApiKey && hasSecret;

                var status = (isEnabled, isConfigured) switch
                {
                    (true, true) => "Enabled and configured",
                    (true, false) => "⚠️ Enabled but missing API key/secret",
                    (false, true) => "Disabled (API key configured)",
                    (false, false) => "Disabled"
                };

                apiKeys.Add(new SearchEngineApiKeyInfo(
                    name,
                    enabledKey,
                    isEnabled,
                    isConfigured,
                    status));
            }

            var hasIssues = apiKeys.Any(k => k.IsEnabled && !k.IsConfigured);
            var detailMessage = hasIssues
                ? "Some enabled search engines are missing API keys"
                : "All enabled search engines are properly configured";

            return (new DoctorCheckResult("SearchEngineApiKeys", !hasIssues, detailMessage, sw.Elapsed), apiKeys);
        }
        catch (Exception ex)
        {
            return (new DoctorCheckResult("SearchEngineApiKeys", false, ex.Message, sw.Elapsed), apiKeys);
        }
    }

    private static string NormalizePath(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.EndsWith(Path.DirectorySeparatorChar))
        {
            path += Path.DirectorySeparatorChar;
        }
        return path.ToLowerInvariant();
    }

    private static bool IsPathOverlap(string path1, string path2)
    {
        return path1.StartsWith(path2) || path2.StartsWith(path1);
    }

    private async Task<bool> HasSmtpConfigurationIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.EmailEnabled,
                SettingRegistry.EmailSmtpHost,
                SettingRegistry.EmailSmtpPort
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var emailEnabled = settings.TryGetValue(SettingRegistry.EmailEnabled, out var ee)
                && bool.TryParse(ee, out var eeb) && eeb;

            if (!emailEnabled)
            {
                return false;
            }

            var smtpHost = settings.TryGetValue(SettingRegistry.EmailSmtpHost, out var host) ? host : "";
            var smtpPort = settings.TryGetValue(SettingRegistry.EmailSmtpPort, out var port) ? port : "";

            return string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpPort);
        }
        catch
        {
            return false;
        }
    }

    private async Task<DoctorCheckResult> RunSmtpConfigurationCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.EmailEnabled,
                SettingRegistry.EmailSmtpHost,
                SettingRegistry.EmailSmtpPort,
                SettingRegistry.EmailFromEmail
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var emailEnabled = settings.TryGetValue(SettingRegistry.EmailEnabled, out var ee)
                && bool.TryParse(ee, out var eeb) && eeb;

            if (!emailEnabled)
            {
                return new DoctorCheckResult("SmtpConfiguration", true, "Email is disabled", sw.Elapsed);
            }

            var smtpHost = settings.TryGetValue(SettingRegistry.EmailSmtpHost, out var host) ? host : "";
            var smtpPort = settings.TryGetValue(SettingRegistry.EmailSmtpPort, out var port) ? port : "";
            var fromEmail = settings.TryGetValue(SettingRegistry.EmailFromEmail, out var from) ? from : "";

            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                issues.Add("SMTP host not configured");
            }

            if (string.IsNullOrWhiteSpace(smtpPort) || !int.TryParse(smtpPort, out _))
            {
                issues.Add("SMTP port not configured");
            }

            if (string.IsNullOrWhiteSpace(fromEmail))
            {
                issues.Add("From email not configured");
            }

            if (issues.Count > 0)
            {
                return new DoctorCheckResult("SmtpConfiguration", false, $"Email enabled but: {string.Join(", ", issues)}", sw.Elapsed);
            }

            return new DoctorCheckResult("SmtpConfiguration", true, $"SMTP configured: {smtpHost}:{smtpPort}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("SmtpConfiguration", false, ex.Message, sw.Elapsed);
        }
    }

    private bool HasJwtTokenStrengthIssues()
    {
        var jwtKey = configuration.GetValue<string>("Jwt:Key") ?? "";
        return jwtKey.Length < MinJwtKeyLength;
    }

    private DoctorCheckResult RunJwtTokenStrengthCheck()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Check multiple configuration sources (supports container, non-container, and .env environments)
            // Priority order: Jwt:Key (appsettings/env) -> MelodeeAuthSettings:Token (appsettings/env) -> MELODEE_AUTH_TOKEN (env/.env)
            var jwtKey = configuration.GetValue<string>("Jwt:Key");

            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                jwtKey = configuration.GetValue<string>("MelodeeAuthSettings:Token");
            }

            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                jwtKey = Environment.GetEnvironmentVariable("MELODEE_AUTH_TOKEN");
            }

            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                return new DoctorCheckResult("JwtTokenStrength", false,
                    "JWT key is not configured (checked Jwt:Key, MelodeeAuthSettings:Token, and MELODEE_AUTH_TOKEN environment variable)", sw.Elapsed);
            }

            if (jwtKey.Length < MinJwtKeyLength)
            {
                return new DoctorCheckResult("JwtTokenStrength", false,
                    $"JWT key too short ({jwtKey.Length} chars). Minimum {MinJwtKeyLength} chars required for HMAC-SHA512", sw.Elapsed);
            }

            return new DoctorCheckResult("JwtTokenStrength", true, $"JWT key length: {jwtKey.Length} chars (OK)", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("JwtTokenStrength", false, ex.Message, sw.Elapsed);
        }
    }

    private bool HasHttpsIssues()
    {
        if (!webHostEnvironment.IsProduction())
        {
            return false;
        }

        var httpContext = httpContextAccessor.HttpContext;
        return httpContext is { Request.IsHttps: false };
    }

    private DoctorCheckResult RunHttpsCheck()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var isProduction = webHostEnvironment.IsProduction();
            var httpContext = httpContextAccessor.HttpContext;
            var isHttps = httpContext?.Request.IsHttps ?? true;

            if (!isProduction)
            {
                return new DoctorCheckResult("HttpsSecurity", true, $"Non-production environment ({webHostEnvironment.EnvironmentName})", sw.Elapsed);
            }

            if (isHttps)
            {
                return new DoctorCheckResult("HttpsSecurity", true, "HTTPS is enabled in production", sw.Elapsed);
            }

            return new DoctorCheckResult("HttpsSecurity", false, "⚠️ Running in production without HTTPS", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("HttpsSecurity", false, ex.Message, sw.Elapsed);
        }
    }

    private async Task<bool> HasDefaultAdminPasswordAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var adminUser = await db.Users
                .Where(u => u.IsAdmin && u.UserNameNormalized == "ADMIN")
                .FirstOrDefaultAsync(cancellationToken);

            if (adminUser == null)
            {
                return false;
            }

            return adminUser.LastLoginAt == null && adminUser.LastActivityAt == null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<DoctorCheckResult> RunDefaultAdminPasswordCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var adminUser = await db.Users
                .Where(u => u.IsAdmin && u.UserNameNormalized == "ADMIN")
                .FirstOrDefaultAsync(cancellationToken);

            if (adminUser == null)
            {
                return new DoctorCheckResult("AdminPassword", true, "No default 'admin' user found", sw.Elapsed);
            }

            if (adminUser.LastLoginAt == null && adminUser.LastActivityAt == null)
            {
                return new DoctorCheckResult("AdminPassword", false,
                    "⚠️ Default admin account has never logged in - consider changing the password", sw.Elapsed);
            }

            return new DoctorCheckResult("AdminPassword", true, "Admin account has been used", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("AdminPassword", false, ex.Message, sw.Elapsed);
        }
    }

    private async Task<bool> HasSchedulerIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

            if (scheduler.IsShutdown)
            {
                return true;
            }

            if (!scheduler.IsStarted)
            {
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<DoctorCheckResult> RunSchedulerCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            var metadata = await scheduler.GetMetaData(cancellationToken);

            if (scheduler.IsShutdown)
            {
                return new DoctorCheckResult("Scheduler", false, "Scheduler is shut down", sw.Elapsed);
            }

            if (!scheduler.IsStarted)
            {
                return new DoctorCheckResult("Scheduler", false, "Scheduler is not started", sw.Elapsed);
            }

            if (scheduler.InStandbyMode)
            {
                return new DoctorCheckResult("Scheduler", false, "Scheduler is in standby mode (paused)", sw.Elapsed);
            }

            var jobGroups = await scheduler.GetJobGroupNames(cancellationToken);
            var totalJobs = 0;

            foreach (var group in jobGroups)
            {
                var jobKeys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.GroupEquals(group), cancellationToken);
                totalJobs += jobKeys.Count;
            }

            var runningSince = metadata.RunningSince?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var jobsExecuted = metadata.NumberOfJobsExecuted;

            var details = $"Running since {runningSince}; {totalJobs} jobs registered; {jobsExecuted} executions this session";

            return new DoctorCheckResult("Scheduler", true, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("Scheduler", false, $"Scheduler error: {ex.Message}", sw.Elapsed);
        }
    }

    private static bool HasFFmpegIssues()
    {
        try
        {
            var ffmpegPath = FindExecutable("ffmpeg");
            return string.IsNullOrEmpty(ffmpegPath);
        }
        catch
        {
            return true;
        }
    }

    private DoctorCheckResult RunFFmpegCheck()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var ffmpegPath = FindExecutable("ffmpeg");
            var ffprobePath = FindExecutable("ffprobe");

            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return new DoctorCheckResult("FFmpeg", false, "FFmpeg not found in PATH", sw.Elapsed);
            }

            string? version = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadLine();
                    process.WaitForExit(5000);

                    if (!string.IsNullOrEmpty(output) && output.StartsWith("ffmpeg version"))
                    {
                        var parts = output.Split(' ');
                        if (parts.Length >= 3)
                        {
                            version = parts[2];
                        }
                    }
                }
            }
            catch
            {
                // Version detection failed, but FFmpeg exists
            }

            var hasFFprobe = !string.IsNullOrEmpty(ffprobePath);
            var details = version != null
                ? $"FFmpeg {version}; FFprobe: {(hasFFprobe ? "OK" : "Missing")}"
                : $"FFmpeg found; FFprobe: {(hasFFprobe ? "OK" : "Missing")}";

            return new DoctorCheckResult("FFmpeg", true, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("FFmpeg", false, $"FFmpeg check failed: {ex.Message}", sw.Elapsed);
        }
    }

    private static string? FindExecutable(string name)
    {
        var isWindows = OperatingSystem.IsWindows();
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathSeparator = isWindows ? ';' : ':';
        var extensions = isWindows ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };

        foreach (var dir in pathVar.Split(pathSeparator))
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, name + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static bool HasMemoryPressureIssues()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var availableMemory = gcInfo.TotalAvailableMemoryBytes - gcInfo.MemoryLoadBytes;
            return availableMemory < MemoryPressureCriticalBytes;
        }
        catch
        {
            return false;
        }
    }

    private DoctorCheckResult RunMemoryCheck()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var processMemory = Process.GetCurrentProcess().WorkingSet64;
            var gen0Collections = GC.CollectionCount(0);
            var gen1Collections = GC.CollectionCount(1);
            var gen2Collections = GC.CollectionCount(2);

            // Get system memory info (Linux/Unix specific)
            long totalMemory = 0;
            long availableMemory = 0;
            double usedPercent = 0;

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // Read from /proc/meminfo on Linux
                if (File.Exists("/proc/meminfo"))
                {
                    var lines = File.ReadAllLines("/proc/meminfo");
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("MemTotal:"))
                        {
                            totalMemory = ParseMemInfoValue(line) * 1024; // Convert KB to bytes
                        }
                        else if (line.StartsWith("MemAvailable:"))
                        {
                            availableMemory = ParseMemInfoValue(line) * 1024; // Convert KB to bytes
                        }
                    }

                    if (totalMemory > 0)
                    {
                        var usedMemory = totalMemory - availableMemory;
                        usedPercent = (double)usedMemory / totalMemory * 100;
                    }
                }
            }
            else
            {
                // Fallback to GC memory info for Windows
                var gcInfo = GC.GetGCMemoryInfo();
                totalMemory = gcInfo.TotalAvailableMemoryBytes;
                var memoryLoad = gcInfo.MemoryLoadBytes;

                // On Windows, use a safer calculation
                if (totalMemory > 0)
                {
                    // MemoryLoadBytes is the total committed memory, not necessarily less than TotalAvailableMemoryBytes
                    // Calculate available as a percentage-based estimate
                    availableMemory = Math.Max(0, totalMemory - memoryLoad);
                    usedPercent = (double)memoryLoad / totalMemory * 100;
                }
            }

            var status = availableMemory switch
            {
                < MemoryPressureCriticalBytes => "Critical",
                < MemoryPressureWarningBytes => "Warning",
                _ => "OK"
            };

            var isHealthy = availableMemory >= MemoryPressureCriticalBytes;

            var details = totalMemory > 0
                ? $"Process: {FormatFileSize(processMemory)}; " +
                  $"System: {usedPercent:F1}% used ({FormatFileSize(availableMemory)} available); " +
                  $"GC: {gen0Collections}/{gen1Collections}/{gen2Collections} (Gen0/1/2)"
                : $"Process: {FormatFileSize(processMemory)}; " +
                  $"GC: {gen0Collections}/{gen1Collections}/{gen2Collections} (Gen0/1/2)";

            return new DoctorCheckResult("Memory", isHealthy, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("Memory", true, $"Memory check unavailable: {ex.Message}", sw.Elapsed);
        }
    }

    private static long ParseMemInfoValue(string line)
    {
        // Format: "MemTotal:       6067416 kB"
        var parts = line.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return 0;
        }

        var valuePart = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (valuePart.Length > 0 && long.TryParse(valuePart[0], out var value))
        {
            return value;
        }

        return 0;
    }

    private DoctorCheckResult RunTempDirectoryCheck()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var tempPath = Path.GetTempPath();
            var exists = Directory.Exists(tempPath);

            if (!exists)
            {
                return new DoctorCheckResult("TempDirectory", false, $"Temp directory does not exist: {tempPath}", sw.Elapsed);
            }

            var writable = IsDirectoryWritable(tempPath);
            if (!writable)
            {
                return new DoctorCheckResult("TempDirectory", false, $"Temp directory not writable: {tempPath}", sw.Elapsed);
            }

            long? availableSpace = null;
            try
            {
                var root = Path.GetPathRoot(tempPath);
                if (!string.IsNullOrEmpty(root))
                {
                    var driveInfo = new DriveInfo(root);
                    availableSpace = driveInfo.AvailableFreeSpace;
                }
            }
            catch
            {
                // Ignore drive info errors
            }

            var spaceInfo = availableSpace.HasValue
                ? $"; {FormatFileSize(availableSpace.Value)} available"
                : "";

            return new DoctorCheckResult("TempDirectory", true, $"OK: {tempPath}{spaceInfo}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("TempDirectory", false, $"Temp directory check failed: {ex.Message}", sw.Elapsed);
        }
    }

    private async Task<DoctorCheckResult> RunDatabaseLatencyCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var queryStart = Stopwatch.StartNew();

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Simple query to measure latency
            _ = await db.Settings.Take(1).CountAsync(cancellationToken);

            queryStart.Stop();
            var latencyMs = queryStart.Elapsed.TotalMilliseconds;

            var status = latencyMs switch
            {
                > 1000 => "Slow",
                > 500 => "Warning",
                _ => "OK"
            };

            var isHealthy = latencyMs <= 1000;

            return new DoctorCheckResult("DatabaseLatency", isHealthy, $"Query latency: {latencyMs:F1}ms ({status})", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("DatabaseLatency", false, $"Latency check failed: {ex.Message}", sw.Elapsed);
        }
    }

    #region Jukebox Configuration Checks

    private async Task<bool> HasJukeboxConfigurationIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.JukeboxEnabled,
                SettingRegistry.JukeboxBackendType,
                SettingRegistry.MpvPath,
                SettingRegistry.MpdHost,
                SettingRegistry.MpdPort
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var jukeboxEnabled = settings.TryGetValue(SettingRegistry.JukeboxEnabled, out var je)
                && bool.TryParse(je, out var jeb) && jeb;

            if (!jukeboxEnabled)
            {
                return false;
            }

            var backendType = settings.TryGetValue(SettingRegistry.JukeboxBackendType, out var bt) ? bt?.ToLowerInvariant() : "";

            if (string.IsNullOrWhiteSpace(backendType) || (backendType != "mpv" && backendType != "mpd"))
            {
                return true;
            }

            if (backendType == "mpv")
            {
                var mpvPath = settings.TryGetValue(SettingRegistry.MpvPath, out var mp) ? mp : "";
                if (!string.IsNullOrWhiteSpace(mpvPath) && !File.Exists(mpvPath))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(mpvPath))
                {
                    var foundPath = FindExecutable("mpv");
                    if (string.IsNullOrEmpty(foundPath))
                    {
                        return true;
                    }
                }
            }
            else if (backendType == "mpd")
            {
                var mpdHost = settings.TryGetValue(SettingRegistry.MpdHost, out var host) ? host : "";
                var mpdPort = settings.TryGetValue(SettingRegistry.MpdPort, out var port) ? port : "";

                if (string.IsNullOrWhiteSpace(mpdHost))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(mpdPort) || !int.TryParse(mpdPort, out var portNum) || portNum <= 0 || portNum > 65535)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<DoctorCheckResult> RunJukeboxConfigurationCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.JukeboxEnabled,
                SettingRegistry.JukeboxBackendType,
                SettingRegistry.MpvPath,
                SettingRegistry.MpvAudioDevice,
                SettingRegistry.MpvSocketPath,
                SettingRegistry.MpvInitialVolume,
                SettingRegistry.MpdHost,
                SettingRegistry.MpdPort,
                SettingRegistry.MpdInstanceName,
                SettingRegistry.MpdTimeoutMs,
                SettingRegistry.MpdInitialVolume
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var jukeboxEnabled = settings.TryGetValue(SettingRegistry.JukeboxEnabled, out var je)
                && bool.TryParse(je, out var jeb) && jeb;

            if (!jukeboxEnabled)
            {
                return new DoctorCheckResult("JukeboxConfiguration", true, "Jukebox is disabled", sw.Elapsed);
            }

            var backendType = settings.TryGetValue(SettingRegistry.JukeboxBackendType, out var bt) ? bt?.ToLowerInvariant() : "";
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(backendType))
            {
                issues.Add("Backend type not configured");
            }
            else if (backendType != "mpv" && backendType != "mpd")
            {
                issues.Add($"Invalid backend type '{backendType}' (must be 'mpv' or 'mpd')");
            }

            if (backendType == "mpv")
            {
                var mpvPath = settings.TryGetValue(SettingRegistry.MpvPath, out var mp) ? mp : "";

                if (!string.IsNullOrWhiteSpace(mpvPath))
                {
                    if (!File.Exists(mpvPath))
                    {
                        issues.Add($"MPV path '{mpvPath}' does not exist");
                    }
                }
                else
                {
                    var foundPath = FindExecutable("mpv");
                    if (string.IsNullOrEmpty(foundPath))
                    {
                        issues.Add("MPV not found in PATH and mpv.path not configured");
                    }
                }

                var initialVolume = settings.TryGetValue(SettingRegistry.MpvInitialVolume, out var vol) ? vol : "";
                if (!string.IsNullOrWhiteSpace(initialVolume) && double.TryParse(initialVolume, out var volNum))
                {
                    if (volNum < 0 || volNum > 1)
                    {
                        issues.Add($"MPV initial volume {volNum} is out of range (0.0-1.0)");
                    }
                }
            }
            else if (backendType == "mpd")
            {
                var mpdHost = settings.TryGetValue(SettingRegistry.MpdHost, out var host) ? host : "";
                var mpdPort = settings.TryGetValue(SettingRegistry.MpdPort, out var port) ? port : "";

                if (string.IsNullOrWhiteSpace(mpdHost))
                {
                    issues.Add("MPD host not configured");
                }

                if (string.IsNullOrWhiteSpace(mpdPort))
                {
                    issues.Add("MPD port not configured");
                }
                else if (!int.TryParse(mpdPort, out var portNum) || portNum <= 0 || portNum > 65535)
                {
                    issues.Add($"MPD port '{mpdPort}' is invalid");
                }

                var timeoutMs = settings.TryGetValue(SettingRegistry.MpdTimeoutMs, out var timeout) ? timeout : "";
                if (!string.IsNullOrWhiteSpace(timeoutMs) && int.TryParse(timeoutMs, out var timeoutNum) && timeoutNum < 1000)
                {
                    issues.Add($"MPD timeout {timeoutNum}ms may be too short (recommended: 10000ms)");
                }

                var initialVolume = settings.TryGetValue(SettingRegistry.MpdInitialVolume, out var vol) ? vol : "";
                if (!string.IsNullOrWhiteSpace(initialVolume) && double.TryParse(initialVolume, out var volNum))
                {
                    if (volNum < 0 || volNum > 1)
                    {
                        issues.Add($"MPD initial volume {volNum} is out of range (0.0-1.0)");
                    }
                }
            }

            if (issues.Count > 0)
            {
                return new DoctorCheckResult("JukeboxConfiguration", false,
                    $"Jukebox enabled ({backendType}) but: {string.Join("; ", issues)}", sw.Elapsed);
            }

            var details = backendType == "mpv"
                ? $"MPV backend configured"
                : $"MPD backend configured ({settings.GetValueOrDefault(SettingRegistry.MpdHost, "localhost")}:{settings.GetValueOrDefault(SettingRegistry.MpdPort, "6600")})";

            return new DoctorCheckResult("JukeboxConfiguration", true, details, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("JukeboxConfiguration", false, ex.Message, sw.Elapsed);
        }
    }

    #endregion

    #region Podcast Configuration Checks

    private async Task<bool> HasPodcastConfigurationIssuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.PodcastEnabled,
                SettingRegistry.PodcastHttpTimeoutSeconds,
                SettingRegistry.PodcastHttpMaxRedirects,
                SettingRegistry.PodcastDownloadMaxConcurrentGlobal,
                SettingRegistry.PodcastDownloadMaxConcurrentPerUser
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var podcastEnabled = settings.TryGetValue(SettingRegistry.PodcastEnabled, out var pe)
                && bool.TryParse(pe, out var peb) && peb;

            if (!podcastEnabled)
            {
                return false;
            }

            var timeoutSeconds = settings.TryGetValue(SettingRegistry.PodcastHttpTimeoutSeconds, out var ts) ? ts : "";
            if (!string.IsNullOrWhiteSpace(timeoutSeconds) && int.TryParse(timeoutSeconds, out var timeoutNum) && timeoutNum < 5)
            {
                return true;
            }

            var maxRedirects = settings.TryGetValue(SettingRegistry.PodcastHttpMaxRedirects, out var mr) ? mr : "";
            if (!string.IsNullOrWhiteSpace(maxRedirects) && int.TryParse(maxRedirects, out var redirectNum) && redirectNum < 0)
            {
                return true;
            }

            var maxConcurrentGlobal = settings.TryGetValue(SettingRegistry.PodcastDownloadMaxConcurrentGlobal, out var mcg) ? mcg : "";
            if (!string.IsNullOrWhiteSpace(maxConcurrentGlobal) && int.TryParse(maxConcurrentGlobal, out var mcgNum) && mcgNum <= 0)
            {
                return true;
            }

            var maxConcurrentPerUser = settings.TryGetValue(SettingRegistry.PodcastDownloadMaxConcurrentPerUser, out var mcu) ? mcu : "";
            if (!string.IsNullOrWhiteSpace(maxConcurrentPerUser) && int.TryParse(maxConcurrentPerUser, out var mcuNum) && mcuNum <= 0)
            {
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<DoctorCheckResult> RunPodcastConfigurationCheckAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var relevantKeys = new[]
            {
                SettingRegistry.PodcastEnabled,
                SettingRegistry.PodcastHttpAllowHttp,
                SettingRegistry.PodcastHttpTimeoutSeconds,
                SettingRegistry.PodcastHttpMaxRedirects,
                SettingRegistry.PodcastHttpMaxFeedBytes,
                SettingRegistry.PodcastRefreshMaxItemsPerChannel,
                SettingRegistry.PodcastDownloadMaxConcurrentGlobal,
                SettingRegistry.PodcastDownloadMaxConcurrentPerUser,
                SettingRegistry.PodcastDownloadMaxEnclosureBytes,
                SettingRegistry.PodcastQuotaMaxBytesPerUser,
                SettingRegistry.PodcastRetentionDownloadedEpisodesInDays,
                SettingRegistry.PodcastRetentionKeepLastNEpisodes,
                SettingRegistry.JobsPodcastRefreshCronExpression,
                SettingRegistry.JobsPodcastDownloadCronExpression,
                SettingRegistry.JobsPodcastCleanupCronExpression,
                SettingRegistry.JobsPodcastRecoveryCronExpression
            };

            var settings = await db.Settings
                .Where(s => relevantKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var podcastEnabled = settings.TryGetValue(SettingRegistry.PodcastEnabled, out var pe)
                && bool.TryParse(pe, out var peb) && peb;

            if (!podcastEnabled)
            {
                return new DoctorCheckResult("PodcastConfiguration", true, "Podcasts are disabled", sw.Elapsed);
            }

            var issues = new List<string>();
            var warnings = new List<string>();

            var allowHttp = settings.TryGetValue(SettingRegistry.PodcastHttpAllowHttp, out var ah)
                && bool.TryParse(ah, out var ahb) && ahb;
            if (allowHttp)
            {
                warnings.Add("HTTP allowed (security risk)");
            }

            var timeoutSeconds = settings.TryGetValue(SettingRegistry.PodcastHttpTimeoutSeconds, out var ts) ? ts : "";
            if (!string.IsNullOrWhiteSpace(timeoutSeconds) && int.TryParse(timeoutSeconds, out var timeoutNum))
            {
                if (timeoutNum < 5)
                {
                    issues.Add($"HTTP timeout {timeoutNum}s is too short (minimum: 5s)");
                }
                else if (timeoutNum > 120)
                {
                    warnings.Add($"HTTP timeout {timeoutNum}s is very long");
                }
            }

            var maxRedirects = settings.TryGetValue(SettingRegistry.PodcastHttpMaxRedirects, out var mr) ? mr : "";
            if (!string.IsNullOrWhiteSpace(maxRedirects) && int.TryParse(maxRedirects, out var redirectNum))
            {
                if (redirectNum < 0)
                {
                    issues.Add("Max redirects cannot be negative");
                }
                else if (redirectNum == 0)
                {
                    warnings.Add("Redirects disabled (may break some feeds)");
                }
            }

            var maxConcurrentGlobal = settings.TryGetValue(SettingRegistry.PodcastDownloadMaxConcurrentGlobal, out var mcg) ? mcg : "";
            if (!string.IsNullOrWhiteSpace(maxConcurrentGlobal) && int.TryParse(maxConcurrentGlobal, out var mcgNum))
            {
                if (mcgNum <= 0)
                {
                    issues.Add("Global max concurrent downloads must be positive");
                }
                else if (mcgNum > 20)
                {
                    warnings.Add($"High global concurrency ({mcgNum}) may impact performance");
                }
            }

            var maxConcurrentPerUser = settings.TryGetValue(SettingRegistry.PodcastDownloadMaxConcurrentPerUser, out var mcu) ? mcu : "";
            if (!string.IsNullOrWhiteSpace(maxConcurrentPerUser) && int.TryParse(maxConcurrentPerUser, out var mcuNum))
            {
                if (mcuNum <= 0)
                {
                    issues.Add("Per-user max concurrent downloads must be positive");
                }
            }

            var maxFeedBytes = settings.TryGetValue(SettingRegistry.PodcastHttpMaxFeedBytes, out var mfb) ? mfb : "";
            if (!string.IsNullOrWhiteSpace(maxFeedBytes) && long.TryParse(maxFeedBytes, out var mfbNum))
            {
                if (mfbNum < 100_000)
                {
                    warnings.Add($"Max feed size {FormatFileSize(mfbNum)} may be too small for some feeds");
                }
            }

            var maxEnclosureBytes = settings.TryGetValue(SettingRegistry.PodcastDownloadMaxEnclosureBytes, out var meb) ? meb : "";
            if (!string.IsNullOrWhiteSpace(maxEnclosureBytes) && long.TryParse(maxEnclosureBytes, out var mebNum))
            {
                if (mebNum < 10_000_000)
                {
                    warnings.Add($"Max enclosure size {FormatFileSize(mebNum)} may be too small for podcast episodes");
                }
            }

            var refreshCron = settings.TryGetValue(SettingRegistry.JobsPodcastRefreshCronExpression, out var rc) ? rc : "";
            var downloadCron = settings.TryGetValue(SettingRegistry.JobsPodcastDownloadCronExpression, out var dc) ? dc : "";
            var cleanupCron = settings.TryGetValue(SettingRegistry.JobsPodcastCleanupCronExpression, out var cc) ? cc : "";

            if (string.IsNullOrWhiteSpace(refreshCron))
            {
                warnings.Add("Podcast refresh job not scheduled");
            }
            if (string.IsNullOrWhiteSpace(downloadCron))
            {
                warnings.Add("Podcast download job not scheduled");
            }
            if (string.IsNullOrWhiteSpace(cleanupCron))
            {
                warnings.Add("Podcast cleanup job not scheduled");
            }

            if (issues.Count > 0)
            {
                var detail = $"Podcasts enabled but: {string.Join("; ", issues)}";
                if (warnings.Count > 0)
                {
                    detail += $" | Warnings: {string.Join("; ", warnings)}";
                }
                return new DoctorCheckResult("PodcastConfiguration", false, detail, sw.Elapsed);
            }

            if (warnings.Count > 0)
            {
                return new DoctorCheckResult("PodcastConfiguration", true,
                    $"Podcasts configured with warnings: {string.Join("; ", warnings)}", sw.Elapsed);
            }

            return new DoctorCheckResult("PodcastConfiguration", true, "Podcasts configured correctly", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new DoctorCheckResult("PodcastConfiguration", false, ex.Message, sw.Elapsed);
        }
    }

    #endregion
}
