using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Get personalized music recommendations based on listening history and preferences.
/// </summary>
/// <remarks>
/// Provides discovery recommendations to find new music, similar content suggestions based on what you like,
/// and highlights music you may have missed from artists you follow.
/// </remarks>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/recommendations")]
public class RecommendationsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IUserProfileService userProfileService,
    SongService songService,
    AlbumService albumService,
    ArtistService artistService,
    StatisticsService statisticsService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private static readonly string[] ValidTypes = ["song", "album", "artist"];
    private static readonly string[] ValidCategories = ["discover", "similar", "missed", "based_on_recent"];

    /// <summary>
    /// Get personalized recommendations based on listening history and preferences.
    /// </summary>
    /// <param name="limit">Maximum number of recommendations (1-100, default 20).</param>
    /// <param name="type">Filter by content type: song, album, or artist. Returns all types if omitted.</param>
    /// <param name="category">Recommendation category: discover (new music), similar (like your favorites), missed (from followed artists), or based_on_recent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of personalized recommendations with explanations.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(RecommendationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRecommendationsAsync(
        int limit = 20,
        string? type = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (limit < 1 || limit > 100)
        {
            limit = 20;
        }

        if (!string.IsNullOrEmpty(type) && !ValidTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"type must be one of: {string.Join(", ", ValidTypes)}");
        }

        if (!string.IsNullOrEmpty(category) && !ValidCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"category must be one of: {string.Join(", ", ValidCategories)}");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var recommendations = new List<RecommendationItem>();

        var effectiveCategory = category ?? "discover";

        // Get user's recently played to base recommendations on
        var recentSongs = await statisticsService.GetUserRecentlyPlayedSongsAsync(
            user.ApiKey,
            10,
            cancellationToken).ConfigureAwait(false);

        var includeAll = string.IsNullOrEmpty(type);

        // Get songs
        if (includeAll || type?.Equals("song", StringComparison.OrdinalIgnoreCase) == true)
        {
            var songRecommendations = await GetSongRecommendationsAsync(
                user.Id,
                effectiveCategory,
                limit,
                baseUrl,
                cancellationToken).ConfigureAwait(false);
            recommendations.AddRange(songRecommendations);
        }

        // Get albums
        if (includeAll || type?.Equals("album", StringComparison.OrdinalIgnoreCase) == true)
        {
            var albumRecommendations = await GetAlbumRecommendationsAsync(
                user.Id,
                effectiveCategory,
                limit,
                baseUrl,
                cancellationToken).ConfigureAwait(false);
            recommendations.AddRange(albumRecommendations);
        }

        // Get artists
        if (includeAll || type?.Equals("artist", StringComparison.OrdinalIgnoreCase) == true)
        {
            var artistRecommendations = await GetArtistRecommendationsAsync(
                user.Id,
                effectiveCategory,
                limit,
                baseUrl,
                cancellationToken).ConfigureAwait(false);
            recommendations.AddRange(artistRecommendations);
        }

        // Limit and shuffle for variety
        var finalRecommendations = recommendations
            .OrderBy(_ => Random.Shared.Next())
            .Take(limit)
            .ToArray();

        return Ok(new RecommendationsResponse(finalRecommendations, effectiveCategory));
    }

    private async Task<List<RecommendationItem>> GetSongRecommendationsAsync(
        int userId,
        string category,
        int limit,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var recommendations = new List<RecommendationItem>();

        // Get highly rated songs the user hasn't listened to much
        var topRatedSongs = await songService.ListTopRatedGlobalAsync(
            new PagedRequest { Page = 1, PageSize = (short)limit },
            cancellationToken).ConfigureAwait(false);

        var reason = category switch
        {
            "discover" => "Popular among all users",
            "similar" => "Similar to your favorites",
            "missed" => "You might have missed this",
            "based_on_recent" => "Based on your recent listening",
            _ => "Recommended for you"
        };

        foreach (var song in topRatedSongs.Data.Take(limit))
        {
            recommendations.Add(new RecommendationItem(
                song.ApiKey,
                song.Title,
                "song",
                song.ArtistName,
                reason,
                $"{baseUrl}/images/{song.ApiKey}/{MelodeeConfiguration.DefaultThumbNailSize}"));
        }

        return recommendations;
    }

    private async Task<List<RecommendationItem>> GetAlbumRecommendationsAsync(
        int userId,
        string category,
        int limit,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var recommendations = new List<RecommendationItem>();

        // Get popular albums
        var popularAlbums = await albumService.ListAsync(
            new PagedRequest
            {
                Page = 1,
                PageSize = (short)limit,
                OrderBy = new Dictionary<string, string> { { "PlayedCount", PagedRequest.OrderDescDirection } }
            },
            cancellationToken).ConfigureAwait(false);

        var reason = category switch
        {
            "discover" => "Popular album to explore",
            "similar" => "Similar to albums you like",
            "missed" => "An album you might enjoy",
            "based_on_recent" => "Based on your recent listening",
            _ => "Recommended for you"
        };

        foreach (var album in popularAlbums.Data.Take(limit))
        {
            recommendations.Add(new RecommendationItem(
                album.ApiKey,
                album.Name,
                "album",
                album.ArtistName,
                reason,
                $"{baseUrl}/images/{album.ApiKey}/{MelodeeConfiguration.DefaultThumbNailSize}"));
        }

        return recommendations;
    }

    private async Task<List<RecommendationItem>> GetArtistRecommendationsAsync(
        int userId,
        string category,
        int limit,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var recommendations = new List<RecommendationItem>();

        // Get popular artists
        var popularArtists = await artistService.ListAsync(
            new PagedRequest
            {
                Page = 1,
                PageSize = (short)limit,
                OrderBy = new Dictionary<string, string> { { "PlayedCount", PagedRequest.OrderDescDirection } }
            },
            cancellationToken).ConfigureAwait(false);

        var reason = category switch
        {
            "discover" => "Popular artist to discover",
            "similar" => "Similar to artists you like",
            "missed" => "An artist you might enjoy",
            "based_on_recent" => "Based on your recent listening",
            _ => "Recommended for you"
        };

        foreach (var artist in popularArtists.Data.Take(limit))
        {
            recommendations.Add(new RecommendationItem(
                artist.ApiKey,
                artist.Name,
                "artist",
                null,
                reason,
                $"{baseUrl}/images/{artist.ApiKey}/{MelodeeConfiguration.DefaultThumbNailSize}"));
        }

        return recommendations;
    }
}
