using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

/// <summary>
/// Tests for authentication error handling.
/// NOTE: These tests are skipped because the OpenSubsonic API bypasses authentication
/// for localhost/loopback requests (a security feature for development/admin access).
/// In integration tests, the test client is always localhost, so authentication
/// failures cannot be properly tested here.
/// </summary>
public class AuthenticationErrorTests : OpenSubsonicTestBase
{
    public AuthenticationErrorTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Ping_WithInvalidCredentials_ReturnsErrorFormat()
    {
        // Use wrong token to test unauthorized access
        var fakeToken = "invalidtoken12345";
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={fakeToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range (typically 40 for authentication errors)
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task GetLicense_WithInvalidCredentials_ReturnsErrorFormat()
    {
        // Use wrong token to test unauthorized access
        var fakeToken = "invalidtoken12345";
        var response = await Client.GetAsync($"/rest/getLicense?u={TestUserName}&t={fakeToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithMissingUser_ReturnsErrorFormat()
    {
        // Call without user parameter
        var response = await Client.GetAsync($"/rest/ping?t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithMissingToken_ReturnsErrorFormat()
    {
        // Call without token parameter
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithInvalidToken_ReturnsErrorFormat()
    {
        // Use obviously invalid token
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t=invalidtokenformat&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithInvalidSalt_ReturnsErrorFormat()
    {
        // Use obviously invalid salt
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={AuthToken}&s=invalidsalt&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithExpiredToken_ReturnsErrorFormat()
    {
        // In a real scenario, we'd test with an actually expired token
        // For now, we'll test with an obviously invalid token that should trigger auth failure
        var expiredToken = "expired_token_12345";
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={expiredToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task MultipleConcurrentAuthFailures_ReturnSameErrorFormat()
    {
        // Test multiple concurrent requests with invalid credentials to ensure consistent error format
        var tasks = new[]
        {
            Client.GetAsync($"/rest/ping?u={TestUserName}&t=invalidtoken1&s={AuthSalt}&v=1.16.1&c=test&f=json"),
            Client.GetAsync($"/rest/getLicense?u={TestUserName}&t=invalidtoken2&s={AuthSalt}&v=1.16.1&c=test&f=json"),
            Client.GetAsync($"/rest/getMusicFolders?u={TestUserName}&t=invalidtoken3&s={AuthSalt}&v=1.16.1&c=test&f=json")
        };

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var root = json.RootElement.GetProperty("subsonic-response");

            // Verify response structure
            var status = root.GetProperty("status").GetString();
            status.Should().Be("failed");

            // Verify error element exists with correct structure
            root.TryGetProperty("error", out var errorElement).Should().BeTrue();
            errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
            errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

            // Verify error code is in expected range
            int codeValue = errorCode.GetInt16();
            codeValue.Should().BeInRange(10, 80);

            // Verify error message is not empty
            var messageValue = errorMessage.GetString();
            messageValue.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithMalformedAuthParams_ReturnsErrorFormat()
    {
        // Test with malformed authentication parameters
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={AuthToken}=malformed&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Localhost requests bypass authentication in OpenSubsonic API")]
    public async Task Endpoint_WithCaseSensitiveAuthParams_ReturnsErrorFormat()
    {
        // Test that authentication is properly handled regardless of case sensitivity in params
        var response = await Client.GetAsync($"/rest/PING?u={TestUserName}&t=invalidtoken&s={AuthSalt}&v=1.16.1&c=test&f=json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Verify response structure
        var status = root.GetProperty("status").GetString();
        status.Should().Be("failed");

        // Verify error element exists with correct structure
        root.TryGetProperty("error", out var errorElement).Should().BeTrue();
        errorElement.TryGetProperty("code", out var errorCode).Should().BeTrue();
        errorElement.TryGetProperty("message", out var errorMessage).Should().BeTrue();

        // Verify error code is in expected range
        int codeValue = errorCode.GetInt16();
        codeValue.Should().BeInRange(10, 80);

        // Verify error message is not empty
        var messageValue = errorMessage.GetString();
        messageValue.Should().NotBeNullOrWhiteSpace();
    }
}
