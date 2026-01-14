using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;

namespace Melodee.Tests.Blazor;

/// <summary>
/// Tests for AuthService authentication state behavior.
/// </summary>
public class AuthServiceTests
{
    /// <summary>
    /// Ensures authentication state is read once when unauthenticated.
    /// </summary>
    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenUnauthenticated_UsesProviderOnce()
    {
        var provider = new TestAuthenticationStateProvider(new ClaimsPrincipal());
        var service = new AuthService(provider);

        var first = await service.EnsureAuthenticatedAsync();
        var second = await service.EnsureAuthenticatedAsync();

        first.Should().BeFalse();
        second.Should().BeFalse();
        provider.CallCount.Should().Be(1);
    }

    /// <summary>
    /// Ensures login sets the current user and skips provider checks.
    /// </summary>
    [Fact]
    public async Task Login_WithAuthenticatedUser_SetsCurrentUserAndSkipsProvider()
    {
        var provider = new TestAuthenticationStateProvider(new ClaimsPrincipal());
        var service = new AuthService(provider);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test")], "test");
        var user = new ClaimsPrincipal(identity);

        await service.Login(user);
        var result = await service.EnsureAuthenticatedAsync();

        result.Should().BeTrue();
        service.CurrentUser.Identity?.IsAuthenticated.Should().BeTrue();
        provider.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Ensures logout resets the cached auth state and consults the provider again.
    /// </summary>
    [Fact]
    public async Task LogoutAsync_WhenCalled_ResetsCachedAuthState()
    {
        var provider = new TestAuthenticationStateProvider(new ClaimsPrincipal());
        var service = new AuthService(provider);
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test")], "test");
        var user = new ClaimsPrincipal(identity);

        await service.Login(user);
        await service.LogoutAsync();
        var result = await service.EnsureAuthenticatedAsync();

        result.Should().BeFalse();
        provider.CallCount.Should().Be(1);
    }

    /// <summary>
    /// Ensures GetStateFromTokenAsync updates the current principal from the provider.
    /// </summary>
    [Fact]
    public async Task GetStateFromTokenAsync_WhenAuthenticated_UpdatesCurrentUser()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test")], "test");
        var user = new ClaimsPrincipal(identity);
        var provider = new TestAuthenticationStateProvider(user);
        var service = new AuthService(provider);

        var result = await service.GetStateFromTokenAsync();

        result.Should().BeTrue();
        service.CurrentUser.Identity?.IsAuthenticated.Should().BeTrue();
        provider.CallCount.Should().Be(1);
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal user;

        /// <summary>
        /// Initializes the provider with a fixed user principal.
        /// </summary>
        public TestAuthenticationStateProvider(ClaimsPrincipal user)
        {
            this.user = user;
        }

        public int CallCount { get; private set; }

        /// <summary>
        /// Returns the current authentication state and tracks call count.
        /// </summary>
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            CallCount++;
            return Task.FromResult(new AuthenticationState(user));
        }
    }
}
