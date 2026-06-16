using Melodee.Common.Services.Doctor;

namespace Melodee.Blazor.Services;

/// <summary>
/// Blazor-specific extension of the shared Doctor service interface.
/// </summary>
public interface IDoctorService : Common.Services.Doctor.IDoctorService
{
    /// <summary>
    /// Runs all diagnostic checks and returns detailed results including Blazor-specific info.
    /// Used by the Doctor page to display comprehensive health information.
    /// </summary>
    Task<BlazorDoctorCheckResults> RunAllChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Quickly checks for lightweight health issues that should surface on the dashboard.
    /// Used by the Dashboard to show/hide the health warning banner without running full diagnostics.
    /// </summary>
    Task<bool> NeedsAttentionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns dashboard-safe health issues that should be shown to administrators immediately after login.
    /// </summary>
    Task<IReadOnlyList<DoctorCheckResult>> GetAttentionChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the MusicBrainz database is empty or not properly initialized.
    /// </summary>
    Task<bool> IsMusicBrainzDatabaseEmptyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Blazor-specific doctor check results with additional information.
/// </summary>
public sealed record BlazorDoctorCheckResults : DoctorCheckResults
{
    public required IReadOnlyList<SerilogLogPathInfo> SerilogLogPaths { get; init; }
    public required IReadOnlyList<ConnectionStringInfo> ConnectionStrings { get; init; }
    public required IReadOnlyList<EnvironmentVariableInfo> EnvironmentVariables { get; init; }
    public required IReadOnlyList<DiskSpaceInfo> DiskSpaceInfo { get; init; }
    public required IReadOnlyList<SearchEngineApiKeyInfo> SearchEngineApiKeys { get; init; }
    public bool IsMusicBrainzEmpty { get; init; }
}
