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
/// User listening statistics and analytics endpoints.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/user/stats")]
public sealed class UserStatsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IUserProfileService userProfileService,
    StatisticsService statisticsService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// Get summary statistics for the current user (favorites, plays, ratings).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatsAsync(int? days, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var daysValue = days ?? 30;
        if (daysValue < 1)
        {
            daysValue = 30;
        }

        if (daysValue > 365)
        {
            daysValue = 365;
        }

        var endDay = LocalDate.FromDateTime(DateTime.UtcNow);
        var startDay = endDay.PlusDays(-daysValue);

        var kpisResult = await statisticsService.GetUserKpisAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            cancellationToken).ConfigureAwait(false);

        if (!kpisResult.IsSuccess)
        {
            return Ok(new
            {
                periodDays = daysValue,
                totalPlays = 0,
                favoriteSongs = 0,
                favoriteAlbums = 0,
                favoriteArtists = 0,
                ratedSongs = 0
            });
        }

        var stats = kpisResult.Data;

        return Ok(new
        {
            periodDays = daysValue,
            totalPlays = stats.FirstOrDefault(s => s.Title == "Total plays")?.DataAsInt ?? 0,
            favoriteSongs = stats.FirstOrDefault(s => s.Title == "Favorites: Songs")?.DataAsInt ?? 0,
            favoriteAlbums = stats.FirstOrDefault(s => s.Title == "Favorites: Albums")?.DataAsInt ?? 0,
            favoriteArtists = stats.FirstOrDefault(s => s.Title == "Favorites: Artists")?.DataAsInt ?? 0,
            ratedSongs = stats.FirstOrDefault(s => s.Title == "Rated Songs")?.DataAsInt ?? 0
        });
    }

    /// <summary>
    /// Get the user's top played songs within a time period.
    /// </summary>
    [HttpGet]
    [Route("top-songs")]
    [ProducesResponseType(typeof(TopItemResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTopSongsAsync(int? days, int? limit, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var daysValue = days ?? 30;
        var limitValue = limit ?? 10;

        if (daysValue < 1)
        {
            daysValue = 30;
        }

        if (daysValue > 365)
        {
            daysValue = 365;
        }

        if (limitValue < 1)
        {
            limitValue = 10;
        }

        if (limitValue > 100)
        {
            limitValue = 100;
        }

        var endDay = LocalDate.FromDateTime(DateTime.UtcNow);
        var startDay = endDay.PlusDays(-daysValue);

        var topSongsResult = await statisticsService.GetUserTopPlayedSongsAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            limitValue,
            cancellationToken).ConfigureAwait(false);

        if (!topSongsResult.IsSuccess)
        {
            return Ok(new { periodDays = daysValue, data = Array.Empty<object>() });
        }

        return Ok(new
        {
            periodDays = daysValue,
            data = topSongsResult.Data.Select(s => new
            {
                name = s.Label,
                playCount = (int)s.Value,
                songId = s.ApiKey
            }).ToArray()
        });
    }

    /// <summary>
    /// Get the user's top genres by play count within a time period.
    /// </summary>
    [HttpGet]
    [Route("top-genres")]
    [ProducesResponseType(typeof(TopItemResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTopGenresAsync(int? days, int? limit, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var daysValue = days ?? 30;
        var limitValue = limit ?? 10;

        if (daysValue < 1)
        {
            daysValue = 30;
        }

        if (daysValue > 365)
        {
            daysValue = 365;
        }

        if (limitValue < 1)
        {
            limitValue = 10;
        }

        if (limitValue > 100)
        {
            limitValue = 100;
        }

        var endDay = LocalDate.FromDateTime(DateTime.UtcNow);
        var startDay = endDay.PlusDays(-daysValue);

        var topGenresResult = await statisticsService.GetUserTopGenresByPlaysAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            limitValue,
            cancellationToken).ConfigureAwait(false);

        if (!topGenresResult.IsSuccess)
        {
            return Ok(new { periodDays = daysValue, data = Array.Empty<object>() });
        }

        return Ok(new
        {
            periodDays = daysValue,
            data = topGenresResult.Data.Select(g => new
            {
                name = g.Label,
                playCount = (int)g.Value
            }).ToArray()
        });
    }

    /// <summary>
    /// Get the user's play activity per day within a time period (for charts).
    /// </summary>
    [HttpGet]
    [Route("plays-per-day")]
    [ProducesResponseType(typeof(TimeSeriesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPlaysPerDayAsync(int? days, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var daysValue = days ?? 30;
        if (daysValue < 1)
        {
            daysValue = 30;
        }

        if (daysValue > 365)
        {
            daysValue = 365;
        }

        var endDay = LocalDate.FromDateTime(DateTime.UtcNow);
        var startDay = endDay.PlusDays(-daysValue);

        var playsResult = await statisticsService.GetUserSongPlaysPerDayAsync(
            user.ApiKey,
            startDay,
            endDay,
            user.TimeZoneId,
            cancellationToken).ConfigureAwait(false);

        if (!playsResult.IsSuccess)
        {
            return Ok(new { periodDays = daysValue, data = Array.Empty<object>() });
        }

        return Ok(new
        {
            periodDays = daysValue,
            data = playsResult.Data.Select(p => new
            {
                date = p.Day.ToString(),
                plays = (int)p.Value
            }).ToArray()
        });
    }

    /// <summary>
    /// Get the user's listening history (recently played songs).
    /// </summary>
    [HttpGet]
    [Route("history")]
    [ProducesResponseType(typeof(HistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetHistoryAsync(int? limit, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        var limitValue = limit ?? 50;
        if (limitValue < 1)
        {
            limitValue = 50;
        }

        if (limitValue > 500)
        {
            limitValue = 500;
        }

        var historyResult = await statisticsService.GetUserRecentlyPlayedSongsAsync(
            user.ApiKey,
            limitValue,
            cancellationToken).ConfigureAwait(false);

        if (!historyResult.IsSuccess)
        {
            return Ok(new { data = Array.Empty<object>() });
        }

        return Ok(new
        {
            data = historyResult.Data.Select(h => new
            {
                songId = h.ApiKey,
                name = h.Label,
                playedAt = h.Extra
            }).ToArray()
        });
    }
}

// Response models for OpenAPI documentation
public record UserStatsResponse(int PeriodDays, int TotalPlays, int FavoriteSongs, int FavoriteAlbums, int FavoriteArtists, int RatedSongs);

public record TopItemResponse(string Name, int PlayCount, Guid? SongId);

public record TimeSeriesResponse(int PeriodDays, TimeSeriesDataPoint[] Data);

public record TimeSeriesDataPoint(string Date, int Plays);

public record HistoryResponse(HistoryItem[] Data);

public record HistoryItem(Guid? SongId, string Name, string PlayedAt);
