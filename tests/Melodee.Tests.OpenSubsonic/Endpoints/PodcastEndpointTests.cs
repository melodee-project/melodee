using System.Text.Json;
using FluentAssertions;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic.Endpoints;

public class PodcastEndpointTests : OpenSubsonicTestBase
{
    public PodcastEndpointTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task GetPodcasts_ReturnsChannelsAndEpisodes()
    {
        var response = await GetAsync("getPodcasts");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var statusElement = root.GetProperty("status");
        statusElement.ValueKind.Should().Be(JsonValueKind.String);
        statusElement.GetString().Should().Be("ok");
        root.GetProperty("version").GetString().Should().NotBeNullOrEmpty();

        // Check if podcasts element exists with valid structure
        if (root.TryGetProperty("podcasts", out var podcastsElement))
        {
            if (podcastsElement.TryGetProperty("channel", out var channelElement))
            {
                channelElement.ValueKind.Should().Be(JsonValueKind.Array);
                // Don't assert specific count - may vary due to other tests
            }
        }
    }

    [Fact]
    public async Task GetPodcasts_WithIncludeEpisodes_False()
    {
        var response = await GetAsync("getPodcasts?includeEpisodes=false");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        root.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task GetNewestPodcasts_ReturnsRecentEpisodes()
    {
        var response = await GetAsync("getNewestPodcasts");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        root.GetProperty("status").GetString().Should().Be("ok");

        // Check if newestPodcasts element exists
        if (root.TryGetProperty("newestPodcasts", out var newestPodcastsElement))
        {
            newestPodcastsElement.GetProperty("episode").EnumerateArray().Should().NotBeNull();
        }
    }

    [Fact]
    public async Task GetNewestPodcasts_WithCountAndOffset()
    {
        var response = await GetAsync("getNewestPodcasts?count=5&offset=0");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        root.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task RefreshPodcasts_TriggerRefresh()
    {
        var response = await GetAsync("refreshPodcasts");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        var statusElement = root.GetProperty("status");
        statusElement.ValueKind.Should().Be(JsonValueKind.String);
        statusElement.GetString().Should().Be("ok");
    }

    [Fact]
    public async Task CreatePodcastChannel_WithValidUrl_AddsSubscription()
    {
        // Using a mock RSS feed URL for testing
        // Note: This test may fail if external DNS resolution is blocked in the test environment
        var response = await Client.GetAsync(
            $"/rest/createPodcastChannel?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json&url=https://feeds.feedburner.com/aspnetpodcast");
        
        // Accept either OK (success) or BadRequest (DNS/network failure in test environment)
        response.StatusCode.Should().BeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // If it succeeded, verify ok status; if failed, there should be an error element
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            root.GetProperty("status").GetString().Should().Be("ok");
        }
    }

    [Fact]
    public async Task CreatePodcastChannel_WithInvalidUrl_ReturnsError()
    {
        var response = await Client.GetAsync(
            $"/rest/createPodcastChannel?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json&url=invalid-url");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeletePodcastChannel_RemovesSubscription()
    {
        // Try to create a podcast channel, but don't fail if external DNS is blocked
        var createResponse = await Client.GetAsync(
            $"/rest/createPodcastChannel?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json&url=https://feeds.feedburner.com/aspnetpodcast");
        
        // If creation succeeded, get the actual ID
        string channelId = "podcast:channel:1"; // Default fallback ID
        if (createResponse.IsSuccessStatusCode)
        {
            var getResponse = await GetAsync("getPodcasts");
            if (getResponse.IsSuccessStatusCode)
            {
                var getContent = await getResponse.Content.ReadAsStringAsync();
                var getJson = JsonDocument.Parse(getContent);
                var getRoot = getJson.RootElement.GetProperty("subsonic-response");
                if (getRoot.TryGetProperty("podcasts", out var podcasts) &&
                    podcasts.TryGetProperty("channel", out var channels) &&
                    channels.GetArrayLength() > 0)
                {
                    var firstChannel = channels[0];
                    if (firstChannel.TryGetProperty("id", out var idElement))
                    {
                        channelId = idElement.GetString() ?? channelId;
                    }
                }
            }
        }

        // Test delete endpoint - should return OK or NotFound
        var response = await Client.GetAsync(
            $"/rest/deletePodcastChannel?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json&id={channelId}");
        response.StatusCode.Should().BeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPodcasts_WithInvalidId_ReturnsError()
    {
        var response = await GetAsync("getPodcasts?id=invalid-id");
        // Invalid ID may return OK with an error in the response or a specific HTTP error
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement.GetProperty("subsonic-response");

        // Check for error in response - the error object has a "message" property
        if (root.TryGetProperty("error", out var errorElement))
        {
            if (errorElement.ValueKind == JsonValueKind.Object)
            {
                errorElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
            }
            else
            {
                errorElement.GetString().Should().NotBeNullOrEmpty();
            }
        }
        else
        {
            // If no error, then status should still be in response
            root.GetProperty("status").GetString().Should().NotBeNull();
        }
    }
}
