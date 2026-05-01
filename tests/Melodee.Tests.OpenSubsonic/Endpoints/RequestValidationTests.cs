using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class RequestValidationTests : OpenSubsonicTestBase
{
    public RequestValidationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetMusicFolders_WithInvalidParameters_HandlesGracefully(string? param)
    {
        var url = string.IsNullOrEmpty(param) ? "getMusicFolders" : $"getMusicFolders?musicFolderId={param}";
        var response = await GetAsync(url);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Search2_WithEmptyQuery_HandlesGracefully(string? query)
    {
        var url = string.IsNullOrEmpty(query) ? "search2" : $"search2?query={query}";
        var response = await GetAsync(url);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task CreatePlaylist_WithoutName_HandlesGracefully(string? name)
    {
        var url = string.IsNullOrEmpty(name) ? "createPlaylist" : $"createPlaylist?name={name}";
        var response = await GetAsync(url);
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK, // May create with default name
            System.Net.HttpStatusCode.BadRequest); // Or return error
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("invalid-id")]
    public async Task Star_WithInvalidId_HandlesGracefully(string? id)
    {
        var url = string.IsNullOrEmpty(id) ? "star" : $"star?id={id}";
        var response = await GetAsync(url);
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK, // May return success with no-op
            System.Net.HttpStatusCode.BadRequest,
            System.Net.HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("invalid-id")]
    public async Task GetCoverArt_WithNonexistentId_HandlesGracefully(string? id)
    {
        var url = string.IsNullOrEmpty(id) ? "getCoverArt" : $"getCoverArt?id={id}";
        var response = await GetAsync(url);
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK, // May return default image
            System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Endpoint_WithExcessiveParameters_HandlesGracefully()
    {
        // Test with many extra parameters that shouldn't affect the request
        var response = await GetAsync("getMusicFolders?extraParam1=value1&extraParam2=value2&extraParam3=value3&extraParam4=value4&extraParam5=value5");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should ignore extra params
    }

    [Fact]
    public async Task Endpoint_WithInvalidParameterTypes_HandlesGracefully()
    {
        // Test with parameters of wrong type
        var response = await GetAsync("getMusicFolders?size=invalid_number");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Fact]
    public async Task Endpoint_WithVeryLongParameter_HandlesGracefully()
    {
        var longParam = new string('x', 10000); // Very long parameter
        var response = await GetAsync($"getMusicFolders?comment={longParam}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Fact]
    public async Task Endpoint_WithSpecialCharacters_HandlesGracefully()
    {
        var specialParam = "!@#$%^&*()_+-=[]{}|;':\",./<>?";
        var response = await GetAsync($"search2?query={Uri.EscapeDataString(specialParam)}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Fact]
    public async Task Endpoint_WithUnicodeCharacters_HandlesGracefully()
    {
        var unicodeParam = "测试Тестテスト";
        var response = await GetAsync($"search2?query={Uri.EscapeDataString(unicodeParam)}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Fact]
    public async Task Endpoint_WithNegativeNumbers_HandlesGracefully()
    {
        var response = await GetAsync("getMusicFolders?size=-1&offset=-5");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }

    [Fact]
    public async Task Endpoint_WithZeroValues_HandlesGracefully()
    {
        var response = await GetAsync("getMusicFolders?size=0&offset=0");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK); // Should handle gracefully
    }
}
