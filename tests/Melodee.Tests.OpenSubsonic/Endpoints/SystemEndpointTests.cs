using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class SystemEndpointTests : OpenSubsonicTestBase
{
    public SystemEndpointTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        var response = await GetAsync("ping");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        AssertOpenSubsonicResponse("ping", content);
        content.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Ping_WithDifferentClient_ReturnsOk()
    {
        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=symfonium&f=json");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        AssertOpenSubsonicResponse("ping (symfonium)", content);
        content.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task GetLicense_ReturnsValidLicense()
    {
        await AssertEndpointConformsToSubsonicSchemaAsync("getLicense", "getLicense", "license");

        var response = await GetAsync("getLicense");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"license\"");
        content.Should().Contain("\"valid\":true");
    }

    [Fact]
    public async Task GetOpenSubsonicExtensions_ReturnsExtensions()
    {
        await AssertEndpointConformsToSubsonicSchemaAsync("getOpenSubsonicExtensions", "getOpenSubsonicExtensions", "openSubsonicExtensions");

        var response = await GetAsync("getOpenSubsonicExtensions");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"openSubsonicExtensions\"");
        content.Should().Contain("\"name\":\"apiKeyAuthentication\"");
        content.Should().Contain("\"name\":\"formPost\"");
    }

    [Fact]
    public async Task GetLicense_ContainsServerInfo()
    {
        var response = await GetAsync("getLicense");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");
        var license = root.GetProperty("license");

        // serverVersion and type are on the root response, not on license
        root.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("type").GetString().Should().Be("Melodee");
        license.GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Ping_ResponseHasRequiredFields()
    {
        var content = await GetResponseContentAsync("ping");
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        root.GetProperty("status").GetString().Should().Be("ok");
        root.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("type").GetString().Should().Be("Melodee");
    }
}
