using System.Security.Claims;
using Melodee.Blazor.Security.Extensions;
using BlazorDoctorService = Melodee.Blazor.Services.IDoctorService;
using DoctorCheckResult = Melodee.Common.Services.Doctor.DoctorCheckResult;

namespace Melodee.Blazor.Components.Pages;

internal static class DashboardHealthWarningEvaluator
{
    public const string DecentDbMigrationGuideUrl = "https://melodee.org/decentdb/";

    public static Task<bool> ShouldShowAsync(ClaimsPrincipal? user, BlazorDoctorService doctorService, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doctorService);

        return user?.IsAdmin() == true
            ? doctorService.NeedsAttentionAsync(cancellationToken)
            : Task.FromResult(false);
    }

    public static async Task<IReadOnlyList<DoctorCheckResult>> GetIssuesAsync(
        ClaimsPrincipal? user,
        BlazorDoctorService doctorService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doctorService);

        return user?.IsAdmin() == true
            ? await doctorService.GetAttentionChecksAsync(cancellationToken)
            : [];
    }

    public static bool HasUnsupportedDecentDbIssue(IEnumerable<DoctorCheckResult> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        return issues.Any(issue =>
            IsDecentDbCheck(issue.Name) &&
            issue.Details.Contains("DecentDB", StringComparison.OrdinalIgnoreCase) &&
            issue.Details.Contains("file format", StringComparison.OrdinalIgnoreCase) &&
            issue.Details.Contains("not supported", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDecentDbCheck(string checkName)
    {
        return checkName is "MusicBrainzDatabase" or "ArtistSearchEngineDatabase";
    }
}
