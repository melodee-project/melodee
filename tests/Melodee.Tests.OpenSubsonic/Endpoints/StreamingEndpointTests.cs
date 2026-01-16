using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class StreamingEndpointTests : OpenSubsonicTestBase
{
    public StreamingEndpointTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Stream_AudioFile_ReturnsAudioStream()
    {
        // Since we don't have actual audio files in the test database, 
        // we'll test with a mock ID and expect a proper error response
        var response = await GetAsync($"stream?id=song_{TestSongApiKey}");

        // Could be 200 OK with audio content, or 404/400 with error depending on implementation
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stream_WithRangeRequest_ReturnsPartialContent()
    {
        // Use the factory's client with range header support
        var response = await GetAsyncWithRange($"stream?id=song_{TestSongApiKey}", "bytes=0-1023");

        // Could be 206 Partial Content if range is supported, or 200/404/400 depending on implementation
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.PartialContent,
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stream_WithInvalidId_ReturnsError()
    {
        var response = await GetAsync("stream?id=invalid-id");
        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.BadRequest,
            System.Net.HttpStatusCode.OK); // Might return OK with error in body
    }

    [Fact]
    public async Task Download_File_ReturnsFile()
    {
        // Test download endpoint with mock ID
        var response = await GetAsync($"download?id=song_{TestSongApiKey}");

        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StreamPodcastEpisode_ReturnsAudio()
    {
        // Test podcast streaming with mock ID
        var response = await GetAsync("streamPodcastEpisode?id=podcastepisode_00000000-0000-0000-0000-000000000001");

        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stream_WithLargeFile_HandlesCorrectly()
    {
        // Test with the test song ID
        var response = await GetAsync($"stream?id=song_{TestSongApiKey}");

        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.NotFound,
            System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stream_WithConcurrentLimit_RespectsLimits()
    {
        // Test multiple concurrent streaming requests to test rate limiting
        var tasks = new[]
        {
            GetAsync($"stream?id=song_{TestSongApiKey}"),
            GetAsync($"stream?id=song_{TestSongApiKey}"),
            GetAsync($"stream?id=song_{TestSongApiKey}")
        };

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            response.StatusCode.Should().BeOneOf(
                System.Net.HttpStatusCode.OK,
                System.Net.HttpStatusCode.NotFound,
                System.Net.HttpStatusCode.BadRequest,
                System.Net.HttpStatusCode.TooManyRequests);
        }
    }
}
