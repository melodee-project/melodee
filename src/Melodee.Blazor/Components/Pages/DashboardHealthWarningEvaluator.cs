using System.Security.Claims;
using Melodee.Blazor.Security.Extensions;
using BlazorDoctorService = Melodee.Blazor.Services.IDoctorService;
using DoctorCheckResult = Melodee.Common.Services.Doctor.DoctorCheckResult;

namespace Melodee.Blazor.Components.Pages;

internal static class DashboardHealthWarningEvaluator
{
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

        return issues.Any(IsUnsupportedDecentDbIssue);
    }

    public static IReadOnlyList<DoctorCheckResult> GetUnsupportedDecentDbIssues(IEnumerable<DoctorCheckResult> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        return issues.Where(IsUnsupportedDecentDbIssue).ToArray();
    }

    private static bool IsUnsupportedDecentDbIssue(DoctorCheckResult issue)
    {
        if (issue.Success || !IsDecentDbCheck(issue.Name))
        {
            return false;
        }

        var details = issue.Details;
        if (details.Contains("ERR_UNSUPPORTED_FORMAT_VERSION", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var describesUnsupportedFormat =
            details.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("not supported", StringComparison.OrdinalIgnoreCase);

        return details.Contains("DecentDB", StringComparison.OrdinalIgnoreCase) &&
               describesUnsupportedFormat &&
               details.Contains("format", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDecentDbCheck(string checkName)
    {
        return checkName is "MusicBrainzDatabase" or "ArtistSearchEngineDatabase";
    }
}
