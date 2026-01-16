using Melodee.Common.Data.Models;

namespace Melodee.Common.Services.Doctor;

/// <summary>
/// Interface for running system health checks.
/// This interface defines the core checks that can be run without host-specific dependencies.
/// </summary>
public interface IDoctorService
{
    /// <summary>
    /// Runs all core diagnostic checks and returns detailed results.
    /// Core checks include: Database connectivity, Settings, Libraries, and other
    /// checks that don't require ASP.NET or Blazor specific services.
    /// </summary>
    Task<DoctorCheckResults> RunCoreChecksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs configuration checks (required settings, etc.)
    /// </summary>
    Task<DoctorCheckResult> RunConfigurationCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs database connectivity checks
    /// </summary>
    Task<DoctorCheckResult> RunDatabaseCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs library path checks including existence and overlap detection
    /// </summary>
    Task<(DoctorCheckResult Check, IReadOnlyList<LibraryPathResult> Paths, IReadOnlyList<string> Overlaps)> RunLibraryPathCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs checks on configurable services (enabled/disabled status)
    /// </summary>
    Task<(DoctorCheckResult Check, IReadOnlyList<ConfigurableServiceResult> Services)> RunConfigurableServicesCheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating doctor services with appropriate dependencies based on the hosting environment.
/// </summary>
public interface IDoctorServiceFactory
{
    /// <summary>
    /// Creates a doctor service appropriate for the current hosting environment.
    /// </summary>
    IDoctorService CreateService();
}

/// <summary>
/// Hosting environment type for doctor service selection.
/// </summary>
public enum DoctorServiceHostType
{
    Blazor,
    Cli
}
