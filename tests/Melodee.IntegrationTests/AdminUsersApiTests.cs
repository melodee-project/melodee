using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Melodee.IntegrationTests;

/// <summary>
/// Integration tests for the admin users API endpoint.
/// Tests authentication, authorization, and response format.
/// </summary>
public class AdminUsersApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AdminUsersApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAdminUsers_WithoutAuth_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/admin/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAdminUsers_WithInvalidToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token-12345");

        // Act
        var response = await client.GetAsync("/api/v1/admin/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Note: Testing successful 200 response requires a valid admin token
    // which would require setting up test database with users
    // This is left as a TODO for full integration test suite
}
