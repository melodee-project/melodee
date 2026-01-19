using Melodee.Cli.Models;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Models;
using Melodee.Common.Services;

namespace Melodee.Cli.Client;

/// <summary>
/// Local Melodee client that uses services directly (existing behavior).
/// </summary>
public class LocalMelodeeClient : IMelodeeClient
{
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly UserProfileService _userProfileService;

    public LocalMelodeeClient(
        IMelodeeConfigurationFactory configurationFactory,
        UserProfileService userProfileService)
    {
        _configurationFactory = configurationFactory;
        _userProfileService = userProfileService;
    }

    public async Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var version = typeof(Program).Assembly.GetName().Version;
        var majorVersion = version?.Major ?? 0;
        var minorVersion = version?.Minor ?? 0;
        var patchVersion = version?.Build ?? 0;

        var name = configuration.GetValue<string>(SettingRegistry.OpenSubsonicServerType) ?? SettingDefaults.DefaultSiteName;

        return new SystemInfoDto(name, "Melodee API", majorVersion, minorVersion, patchVersion);
    }

    public async Task<UserMeDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        // In local mode, we need to get the current user somehow
        // For now, we'll get the first admin user as a fallback
        // In reality, local mode might not have a "current user" context
        var usersResult = await _userProfileService.ListAsync(new PagedRequest { PageSize = 1 }, cancellationToken);
        
        if (usersResult.Data == null || !usersResult.Data.Any())
        {
            throw new InvalidOperationException("No users found in local database");
        }

        var user = usersResult.Data.First();
        
        return new UserMeDto(
            user.ApiKey, // Use ApiKey as Guid ID for consistency with API
            string.Empty, // ThumbnailUrl - not available in local mode without base URL
            string.Empty, // ImageUrl - not available in local mode without base URL
            user.UserName,
            user.Email,
            user.IsAdmin,
            false, // IsEditor - not in UserDataInfo
            [], // Roles - not in UserDataInfo
            0, // SongsPlayed - would need to query
            0, // ArtistsLiked - would need to query
            0, // ArtistsDisliked - would need to query
            0, // AlbumsLiked - would need to query
            0, // AlbumsDisliked - would need to query
            0, // SongsLiked - would need to query
            0, // SongsDisliked - would need to query
            user.CreatedAt.ToString(),
            (user.LastUpdatedAt ?? user.CreatedAt).ToString()
        );
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(CancellationToken cancellationToken = default)
    {
        var result = await _userProfileService.ListAsync(new PagedRequest { PageSize = 1000 }, cancellationToken);
        
        if (result.Data == null)
        {
            return [];
        }

        return result.Data.Select(u => new AdminUserDto(
            u.ApiKey, // Use ApiKey as Guid ID for consistency with API
            u.UserName,
            u.Email,
            u.IsAdmin,
            !u.IsLocked,
            u.CreatedAt.ToDateTimeUtc().ToString("o"),
            u.LastLoginAt?.ToDateTimeUtc().ToString("o")
        )).ToList();
    }

    public Task<SearchResultsDto> SearchAsync(SearchRequestDto request, CancellationToken cancellationToken = default)
    {
        // Search is not yet implemented for local mode in the MVP
        // This would require adding SearchService to the DI container
        throw new NotImplementedException("Search is not yet implemented in local mode. Use remote mode with --server flag.");
    }
}
