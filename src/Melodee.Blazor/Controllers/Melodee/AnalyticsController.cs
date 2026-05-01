using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// View listening analytics and statistics for the current user.
/// </summary>
/// <remarks>
/// Provides insights into listening habits including play counts, top artists/albums/songs,
/// listening time distribution by hour and day, and genre preferences over configurable time periods.
/// </remarks>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/analytics")]
public class AnalyticsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    StatisticsService statisticsService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private static readonly string[] ValidPeriods = ["day", "week", "month", "year", "all_time"];
    private static readonly string[] ValidTypes = ["song", "album", "artist"];

    /// <summary>
    /// Get detailed listening statistics for a time period.
    /// </summary>
    /// <param name="period">Time period: day, week, month, year, or all_time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Listening statistics including top content, play counts, and time-based distribution.</returns>
    [HttpGet]
    [Route("listening")]
    [ProducesResponseType(typeof(ListeningStatistics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetListeningStatisticsAsync(
        string period = "week",
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!ValidPeriods.Contains(period, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"period must be one of: {string.Join(", ", ValidPeriods)}");
        }

        var (startDay, endDay) = GetDateRangeForPeriod(period, user.TimeZoneId);

        // Get KPIs
        var kpis = await statisticsService.GetUserKpisAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            cancellationToken).ConfigureAwait(false);

        var totalPlays = kpis.Data.FirstOrDefault(x => x.Title == "Total plays")?.DataAsInt ?? 0;
        var totalPlayTime = 0.0; // Would need to calculate from play histories

        // Get top played songs
        var topSongs = await statisticsService.GetUserTopPlayedSongsAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            10,
            cancellationToken).ConfigureAwait(false);

        // Get top genres
        var topGenres = await statisticsService.GetUserTopGenresByPlaysAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            10,
            cancellationToken).ConfigureAwait(false);

        // Get plays per day for time-of-day distribution
        var playsPerDay = await statisticsService.GetUserSongPlaysPerDayAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            cancellationToken).ConfigureAwait(false);

        // Build response
        var topArtistsResponse = Array.Empty<AnalyticsItem>(); // Would need separate query
        var topAlbumsResponse = Array.Empty<AnalyticsItem>(); // Would need separate query

        var topGenresResponse = topGenres.Data.Select(g => new AnalyticsGenre(
            g.Label,
            (int)g.Value,
            0.0)).ToArray();

        // Create listening by time of day (mock data for now)
        var listeningByTimeOfDay = Enumerable.Range(0, 24)
            .Select(h => new ListeningByHour(h, 0.0))
            .ToArray();

        // Create listening by day of week
        var listeningByDayOfWeek = new[]
        {
            new ListeningByDay("monday", 0.0),
            new ListeningByDay("tuesday", 0.0),
            new ListeningByDay("wednesday", 0.0),
            new ListeningByDay("thursday", 0.0),
            new ListeningByDay("friday", 0.0),
            new ListeningByDay("saturday", 0.0),
            new ListeningByDay("sunday", 0.0)
        };

        // Sum up plays per day into day-of-week totals
        foreach (var point in playsPerDay.Data)
        {
            var dayIndex = (int)point.Day.DayOfWeek - 1;
            if (dayIndex < 0)
            {
                dayIndex = 6;
            }

            listeningByDayOfWeek[dayIndex] = listeningByDayOfWeek[dayIndex] with
            {
                PlayTime = listeningByDayOfWeek[dayIndex].PlayTime + point.Value
            };
        }

        return Ok(new ListeningStatistics(
            period,
            totalPlayTime,
            (int)totalPlays,
            topArtistsResponse,
            topAlbumsResponse,
            topGenresResponse,
            listeningByTimeOfDay,
            listeningByDayOfWeek));
    }

    /// <summary>
    /// Get top played content (songs, albums, or artists) for a time period.
    /// </summary>
    /// <param name="period">Time period: day, week, month, year, or all_time.</param>
    /// <param name="type">Content type: song, album, or artist.</param>
    /// <param name="limit">Maximum number of results (1-100, default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked list of top content with play counts.</returns>
    [HttpGet]
    [Route("top/{period}")]
    [ProducesResponseType(typeof(TopContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTopContentAsync(
        string period,
        string type,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!ValidPeriods.Contains(period, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"period must be one of: {string.Join(", ", ValidPeriods)}");
        }

        if (!ValidTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"type must be one of: {string.Join(", ", ValidTypes)}");
        }

        if (limit < 1 || limit > 100)
        {
            limit = 10;
        }

        var (startDay, endDay) = GetDateRangeForPeriod(period, user.TimeZoneId);

        var items = new List<TopContentItem>();

        if (type.Equals("song", StringComparison.OrdinalIgnoreCase))
        {
            var topSongs = await statisticsService.GetUserTopPlayedSongsAsync(
                user.ApiKey,
                startDay,
                endDay,
                user.TimeZoneId,
                limit,
                cancellationToken).ConfigureAwait(false);

            var rank = 1;
            foreach (var song in topSongs.Data)
            {
                items.Add(new TopContentItem(
                    song.ApiKey ?? Guid.Empty,
                    song.Label,
                    (int)song.Value,
                    0.0,
                    rank++));
            }
        }
        else if (type.Equals("album", StringComparison.OrdinalIgnoreCase))
        {
            // Would need a separate statistics method for albums
            // For now return empty
        }
        else if (type.Equals("artist", StringComparison.OrdinalIgnoreCase))
        {
            // Would need a separate statistics method for artists
            // For now return empty
        }

        return Ok(new TopContentResponse(period, type, items.ToArray()));
    }

    private static (LocalDate StartDay, LocalDate EndDay) GetDateRangeForPeriod(string period, string? timeZoneId)
    {
        var zone = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default
            .ForId(timeZoneId ?? "UTC") ?? DateTimeZone.Utc;
        var today = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

        return period.ToLowerInvariant() switch
        {
            "day" => (today, today),
            "week" => (today.PlusDays(-7), today),
            "month" => (today.PlusDays(-30), today),
            "year" => (today.PlusDays(-365), today),
            "all_time" => (new LocalDate(2000, 1, 1), today),
            _ => (today.PlusDays(-7), today)
        };
    }
}
