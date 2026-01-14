using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;

namespace Melodee.Blazor.Services;

/// <summary>
/// Service to provide the application base URL for components that need it
/// </summary>
public interface IBaseUrlService
{
    /// <summary>
    /// Gets the base URL for the application synchronously.
    /// This method may fall back to HttpContext for internal requests.
    /// </summary>
    /// <returns>The base URL or null if not available</returns>
    string? GetBaseUrl();

    /// <summary>
    /// Gets the base URL for the application asynchronously.
    /// Returns null if SystemBaseUrl is not configured.
    /// Host header is NOT used as a fallback for external links.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The configured base URL or null if not available</returns>
    Task<string?> GetBaseUrlAsync(CancellationToken cancellationToken = default);
}
