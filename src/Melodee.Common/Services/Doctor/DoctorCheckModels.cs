namespace Melodee.Common.Services.Doctor;

/// <summary>
/// Results from running all doctor checks.
/// </summary>
public record DoctorCheckResults
{
    public required IReadOnlyList<DoctorCheckResult> Checks { get; init; }
    public required IReadOnlyList<LibraryPathResult> LibraryPaths { get; init; }
    public required IReadOnlyList<ConfigurableServiceResult> ConfigurableServices { get; init; }
    public bool HasIssues => Checks.Any(c => !c.Success);
}

/// <summary>
/// Result of a single diagnostic check.
/// </summary>
public sealed record DoctorCheckResult(string Name, bool Success, string Details, TimeSpan Duration);

/// <summary>
/// Information about a library path.
/// </summary>
public sealed record LibraryPathResult(string Name, string Type, string Path, bool Exists, bool Writable, string Details);

/// <summary>
/// Information about a configurable service.
/// </summary>
public sealed record ConfigurableServiceResult(string Category, string Name, string SettingKey, bool Enabled);

/// <summary>
/// Status of disk space for a path.
/// </summary>
public enum DiskSpaceStatus
{
    Ok,
    Warning,
    Critical,
    Unknown
}

/// <summary>
/// Information about disk space for a storage path.
/// </summary>
public sealed record DiskSpaceInfo(
    string Name,
    string Path,
    long TotalBytes,
    long AvailableBytes,
    long UsedBytes,
    double UsedPercent,
    DiskSpaceStatus Status);

/// <summary>
/// Information about a search engine API key configuration.
/// </summary>
public sealed record SearchEngineApiKeyInfo(
    string EngineName,
    string SettingKey,
    bool IsEnabled,
    bool IsConfigured,
    string Status);

/// <summary>
/// Information about a Serilog log path.
/// </summary>
public sealed record SerilogLogPathInfo(string SinkName, string Path, bool DirectoryExists, bool Writable);

/// <summary>
/// Information about a connection string.
/// </summary>
public sealed record ConnectionStringInfo(
    string Name,
    string MaskedValue,
    bool IsValid,
    bool IsFileBased,
    bool? FileExists,
    bool? FileWritable,
    string? FilePath,
    bool? CanConnect,
    string? ConnectionError);

/// <summary>
/// Information about an environment variable.
/// </summary>
public sealed record EnvironmentVariableInfo(string Name, string MaskedValue, bool IsSet);
