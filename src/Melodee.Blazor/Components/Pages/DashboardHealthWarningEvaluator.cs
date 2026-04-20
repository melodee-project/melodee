using System.Security.Claims;
using Melodee.Blazor.Security.Extensions;
using Melodee.Blazor.Services;

namespace Melodee.Blazor.Components.Pages;

internal static class DashboardHealthWarningEvaluator
{
    public static Task<bool> ShouldShowAsync(ClaimsPrincipal? user, IDoctorService doctorService, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(doctorService);

        return user?.IsAdmin() == true
            ? doctorService.NeedsAttentionAsync(cancellationToken)
            : Task.FromResult(false);
    }
}
