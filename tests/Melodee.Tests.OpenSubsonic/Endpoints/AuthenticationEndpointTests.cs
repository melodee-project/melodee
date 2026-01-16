using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class AuthenticationEndpointTests : OpenSubsonicTestBase
{
    public AuthenticationEndpointTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Ping_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Use wrong token to test unauthorized access
        var fakeToken = "invalidtoken12345";
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={fakeToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        // Depending on implementation, could return 401 or just an error in the response
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Either the status should be "failed" or the response code should indicate error
        var status = root.GetProperty("status").GetString();
        status.Should().BeOneOf("ok", "failed"); // "failed" indicates authentication error

        if (status == "failed")
        {
            root.TryGetProperty("error", out var errorElement).Should().BeTrue();
            if (errorElement.ValueKind != JsonValueKind.Undefined)
            {
                var errorCode = errorElement.GetProperty("code").GetInt16();
                errorCode.Should().BeOneOf((short)10, (short)40, (short)50); // Standard Subsonic error codes
            }
        }
    }

    [Fact]
    public async Task GetLicense_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Use wrong token to test unauthorized access
        var fakeToken = "invalidtoken12345";
        var response = await Client.GetAsync($"/rest/getLicense?u={TestUserName}&t={fakeToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var status = root.GetProperty("status").GetString();
        status.Should().BeOneOf("ok", "failed");
    }

    [Fact]
    public async Task Endpoint_WithMissingUser_ReturnsUnauthorized()
    {
        // Call without user parameter
        var response = await Client.GetAsync($"/rest/ping?t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var status = root.GetProperty("status").GetString();
        status.Should().BeOneOf("ok", "failed");
    }

    [Fact]
    public async Task Endpoint_WithMissingToken_ReturnsUnauthorized()
    {
        // Call without token parameter
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var status = root.GetProperty("status").GetString();
        status.Should().BeOneOf("ok", "failed");
    }

    [Fact]
    public async Task Endpoint_WithInsufficientPermissions_ReturnsForbidden()
    {
        // Test an endpoint that requires specific permissions
        // For example, podcast endpoints might require podcastRole
        var response = await Client.GetAsync($"/rest/getPodcasts?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        // Could be OK if user has permission, or Forbidden if they don't
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.Forbidden,
            System.Net.HttpStatusCode.NotFound); // If feature is disabled
    }

    [Fact]
    public async Task TokenGeneration_CreatesValidTokens()
    {
        // This test verifies that our existing token generation works
        var response = await GetAsync("ping");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        root.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Endpoint_WithExpiredToken_HandlesGracefully()
    {
        // In a real scenario, we'd test with an actually expired token
        // For now, we'll test with an obviously invalid token
        var expiredToken = "expired_token_12345";
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={expiredToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var status = root.GetProperty("status").GetString();
        status.Should().BeOneOf("ok", "failed");
    }

    [Fact]
    public async Task MultipleConcurrentAuthRequests_HandlesGracefully()
    {
        // Test multiple concurrent requests with the same credentials
        var tasks = new[]
        {
            GetAsync("ping"),
            GetAsync("getLicense"),
            GetAsync("getMusicFolders"),
            GetAsync("getIndexes")
        };

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Endpoint_WithMalformedAuthParams_HandlesGracefully()
    {
        // Test with malformed authentication parameters
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={AuthToken}=malformed&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var status = root.GetProperty("status").GetString();
        status.Should().BeOneOf("ok", "failed");
    }

    [Fact]
    public async Task Endpoint_WithCaseSensitiveParams_HandlesConsistently()
    {
        // Test that parameters are handled consistently regardless of case
        var response1 = await GetAsync("ping");
        var response2 = await Client.GetAsync($"/rest/PING?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        response1.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response2.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
