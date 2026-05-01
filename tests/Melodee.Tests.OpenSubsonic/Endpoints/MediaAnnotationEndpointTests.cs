using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class MediaAnnotationEndpointTests : OpenSubsonicTestBase
{
    public MediaAnnotationEndpointTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Star_Item_ReturnsOk()
    {
        var response = await GetAsync($"star?id=song_{TestSongApiKey}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Unstar_Item_ReturnsOk()
    {
        var response = await GetAsync($"unstar?id=song_{TestSongApiKey}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task SetRating_WithValidRating_ReturnsOk()
    {
        var response = await GetAsync($"setRating?id=song_{TestSongApiKey}&rating=5");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Scrobble_WithSubmission_ReturnsOk()
    {
        var response = await GetAsync($"scrobble?id=song_{TestSongApiKey}&submission=true");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"ok\"");
    }

    [Fact]
    public async Task Scrobble_WithNowPlaying_ReturnsOk()
    {
        var response = await GetAsync($"scrobble?id=song_{TestSongApiKey}&submission=false");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"ok\"");
    }
}
