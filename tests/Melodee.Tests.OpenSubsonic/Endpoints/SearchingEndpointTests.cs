using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class SearchingEndpointTests : OpenSubsonicTestBase
{
    public SearchingEndpointTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Search2_ReturnsResults()
    {
        await AssertEndpointConformsToSubsonicSchemaAsync("search2", "search2?query=test", "searchResult2");

        var response = await GetAsync("search2?query=test");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"searchResult2\"");

        Directory.CreateDirectory("/tmp");
        System.IO.File.WriteAllText("/tmp/search2_response.json", content);
    }

    [Fact]
    public async Task Search3_ReturnsResults()
    {
        await AssertEndpointConformsToSubsonicSchemaAsync("search3", "search3?query=test", "searchResult3");

        var response = await GetAsync("search3?query=test");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"searchResult3\"");
    }

    [Fact]
    public async Task Search2_WithArtistFilter_ReturnsResults()
    {
        await AssertEndpointConformsToSubsonicSchemaAsync("search2 (with artist filter)", "search2?query=test&artistCount=5", "searchResult2");

        var response = await GetAsync("search2?query=test&artistCount=5");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"searchResult2\"");
    }

    [Fact]
    public async Task Search3_WithAlbumFilter_ReturnsResults()
    {
        await AssertEndpointConformsToSubsonicSchemaAsync("search3 (with album filter)", "search3?query=test&albumCount=5", "searchResult3");

        var response = await GetAsync("search3?query=test&albumCount=5");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"searchResult3\"");
    }
}
