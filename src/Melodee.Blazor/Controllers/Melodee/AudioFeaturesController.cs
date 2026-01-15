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
/// Access audio analysis features and find tracks by audio characteristics.
/// </summary>
/// <remarks>
/// Provides access to audio features like tempo (BPM), key, time signature, and various audio
/// characteristics. Also enables finding tracks within specific BPM ranges for workout playlists or DJ sets.
/// </remarks>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/audio")]
public class AudioFeaturesController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IUserProfileService userProfileService,
    SongService songService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// Get detailed audio analysis features for a specific song.
    /// </summary>
    /// <param name="id">The song's unique identifier (API key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audio features including tempo, key, time signature, and audio characteristics.</returns>
    [HttpGet]
    [Route("features/{id:guid}")]
    [ProducesResponseType(typeof(AudioFeatures), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudioFeaturesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var songResult = await songService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
        if (!songResult.IsSuccess || songResult.Data == null)
        {
            return ApiNotFound("Song");
        }

        var song = songResult.Data;

        // Most audio features aren't currently stored in the database,
        // so we return what we have and placeholder values for the rest
        var features = new AudioFeatures(
            song.ApiKey,
            song.BPM,
            null, // Musical key not stored
            null, // Mode not stored
            4, // Default time signature
            0.5, // Default acousticness
            0.5, // Default danceability
            0.5, // Default energy
            0.5, // Default instrumentalness
            0.2, // Default liveness
            song.ReplayGain ?? -10.0, // Use replay gain as proxy for loudness
            0.1, // Default speechiness
            0.5 // Default valence
        );

        return Ok(features);
    }

    /// <summary>
    /// Find tracks within a BPM (tempo) range. Useful for creating workout playlists or DJ sets.
    /// </summary>
    /// <param name="min">Minimum BPM (beats per minute).</param>
    /// <param name="max">Maximum BPM (beats per minute).</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="limit">Number of results per page (max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of tracks within the specified BPM range.</returns>
    [HttpGet]
    [Route("bpm")]
    [ProducesResponseType(typeof(BpmTracksResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTracksByBpmAsync(
        double min,
        double max,
        int page = 1,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (min > max)
        {
            return ApiValidationError("min must be <= max");
        }

        if (min < 0)
        {
            return ApiValidationError("min must be >= 0");
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await songService.ListByBpmRangeAsync(
            min,
            max,
            new PagedRequest { Page = validatedPage, PageSize = validatedLimit },
            cancellationToken).ConfigureAwait(false);

        var tracks = result.Data.Select(s => new BpmTrack(
            s.ApiKey,
            s.Title,
            s.Album?.Artist?.Name ?? "Unknown Artist",
            s.BPM)).ToArray();

        return Ok(new
        {
            tracks,
            meta = new PaginationMetadata(
                result.TotalCount,
                validatedLimit,
                validatedPage,
                result.TotalPages)
        });
    }
}
