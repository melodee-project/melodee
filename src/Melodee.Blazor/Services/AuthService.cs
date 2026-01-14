using System.Security.Claims;
using Melodee.Common.Constants;
using Microsoft.AspNetCore.Components.Authorization;

namespace Melodee.Blazor.Services;

/// <summary>
///     Store and manage the current user's authentication state for Server Side Blazor.
/// </summary>
public class AuthService(AuthenticationStateProvider authenticationStateProvider) : IAuthService
{
    private ClaimsPrincipal? _currentUser;
    private bool _authStateChecked;

    public event Action<ClaimsPrincipal>? UserChanged;

    public ClaimsPrincipal CurrentUser
    {
        get => _currentUser ?? new ClaimsPrincipal();
        set
        {
            _currentUser = value;

            if (UserChanged is not null)
            {
                UserChanged(_currentUser);
            }
        }
    }

    public bool IsAdmin => CurrentUser.IsInRole(RoleNameRegistry.Administrator);

    public bool IsLoggedIn => CurrentUser.Identity?.IsAuthenticated ?? false;

    public async Task LogoutAsync()
    {
        CurrentUser = new ClaimsPrincipal();
        _authStateChecked = false;
        await Task.CompletedTask.ConfigureAwait(false);
    }


    /// <summary>
    ///     Refreshes the current authentication state from the server-side provider.
    /// </summary>
    /// <returns>True if the state was restored</returns>
    public async Task<bool> GetStateFromTokenAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        CurrentUser = authState.User;
        return IsLoggedIn;
    }


    /// <summary>
    ///     Ensures user is authenticated by validating cached state or token. Prevents duplicate validation calls.
    /// </summary>
    /// <returns>True if user is authenticated</returns>
    public async Task<bool> EnsureAuthenticatedAsync()
    {
        if (!_authStateChecked && !IsLoggedIn)
        {
            _authStateChecked = true;
            return await GetStateFromTokenAsync();
        }
        return IsLoggedIn;
    }

    public async Task Login(ClaimsPrincipal user, bool? doRememberMe = null)
    {
        CurrentUser = user;
        _authStateChecked = true;
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
