using Melodee.Cli.Models;

namespace Melodee.Cli.Client;

/// <summary>
/// Abstraction for interacting with Melodee (local or remote).
/// Implementations:
/// - LocalMelodeeClient: uses local services directly
/// - RemoteMelodeeClient: uses HTTP client to call REST API
/// </summary>
public interface IMelodeeClient : IDisposable
{
    /// <summary>
    /// Get system information (version, name, description).
    /// Maps to GET /api/v1/system/info
    /// </summary>
    Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about the current authenticated user.
    /// Maps to GET /api/v1/user/me
    /// </summary>
    Task<UserMeDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get list of all users (admin only).
    /// Maps to GET /api/v1/admin/users
    /// </summary>
    Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for artists, albums, songs, and playlists.
    /// Maps to POST /api/v1/search
    /// </summary>
    Task<SearchResultsDto> SearchAsync(SearchRequestDto request, CancellationToken cancellationToken = default);
}
