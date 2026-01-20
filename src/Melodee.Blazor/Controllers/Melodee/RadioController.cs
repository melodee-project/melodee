using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Route("api/v1/radio")]
[Authorize]
public class RadioController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    RadioStationService radioStationService,
    RadioStationUserPreferenceService preferenceService,
    RadioStationProbeService probeService,
    UserProfileService userProfileService)
    : ControllerBase(etagRepository, serializer, configuration, configurationFactory)
{
    /// <summary>
    /// List all radio stations with user preferences
    /// </summary>
    [HttpGet("stations")]
    public async Task<IActionResult> ListStationsAsync(
        [FromQuery] string? q,
        [FromQuery] bool includeHidden = false,
        [FromQuery] bool favoritesOnly = false,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Get all stations
        var stationsQuery = dbContext.RadioStations.AsNoTracking();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(q))
        {
            var searchTerm = q.ToLower();
            stationsQuery = stationsQuery.Where(s =>
                s.Name.ToLower().Contains(searchTerm) ||
                (s.Tags != null && s.Tags.ToLower().Contains(searchTerm)) ||
                (s.CountryCode != null && s.CountryCode.ToLower().Contains(searchTerm)) ||
                (s.LanguageCode != null && s.LanguageCode.ToLower().Contains(searchTerm)));
        }

        var stations = await stationsQuery.ToListAsync(cancellationToken);

        // Get user preferences
        var preferencesResult = await preferenceService.GetUserPreferencesAsync(user.Id, cancellationToken);
        var preferences = preferencesResult.Data?.ToDictionary(p => p.RadioStationId) ?? new Dictionary<int, RadioStationUserPreference>();

        // Map to DTOs with preferences
        var dtos = stations.Select(s =>
        {
            var pref = preferences.GetValueOrDefault(s.Id);
            return new RadioStationDto
            {
                Id = s.Id,
                Name = s.Name,
                StreamUrl = s.StreamUrl,
                HomePageUrl = s.HomePageUrl,
                Description = s.Description,
                Tags = s.Tags,
                CountryCode = s.CountryCode,
                LanguageCode = s.LanguageCode,
                LogoUrl = s.LogoUrl,
                LogoCachedUrl = !string.IsNullOrWhiteSpace(s.LogoCacheKey)
                    ? $"/assets/radio-logos/{s.LogoCacheKey}"
                    : null,
                LastHealthStatus = s.LastHealthStatus,
                LastHealthCheckAt = s.LastHealthCheckAt,
                LastHealthOkAt = s.LastHealthOkAt,
                LastHealthError = s.LastHealthError,
                NowPlayingRaw = s.NowPlayingRaw,
                NowPlayingCapturedAt = s.NowPlayingCapturedAt,
                IsFavorite = pref?.IsFavorite ?? false,
                IsHidden = pref?.IsHidden ?? false,
                SortOrder = pref?.SortOrder ?? 1000
            };
        }).ToList();

        // Apply filters
        if (!includeHidden)
        {
            dtos = dtos.Where(d => !d.IsHidden).ToList();
        }

        if (favoritesOnly)
        {
            dtos = dtos.Where(d => d.IsFavorite).ToList();
        }

        // Sort: Favorites first, then SortOrder, then Name
        var sorted = dtos
            .OrderByDescending(d => d.IsFavorite)
            .ThenBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToArray();

        return Ok(sorted);
    }

    /// <summary>
    /// Update user preferences for a radio station
    /// </summary>
    [HttpPut("stations/{id}/preferences")]
    public async Task<IActionResult> UpdatePreferencesAsync(
        [FromRoute] int id,
        [FromBody] UpdateRadioStationPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        // Verify station exists
        var stationResult = await radioStationService.GetAsync(id, cancellationToken);
        if (!stationResult.IsSuccess || stationResult.Data == null)
        {
            return ApiNotFound("Radio station");
        }

        var result = await preferenceService.UpdatePreferenceAsync(
            user.Id,
            id,
            request.IsFavorite,
            request.IsHidden,
            request.SortOrder,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return ApiBadRequest("Failed to update preferences");
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Get diagnostics for a radio station
    /// </summary>
    [HttpGet("stations/{id}/diagnostics")]
    public async Task<IActionResult> GetDiagnosticsAsync(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);

        var station = await dbContext.RadioStations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (station == null)
        {
            return ApiNotFound("Radio station");
        }

        // Get recent history (last 25 entries)
        var history = await dbContext.RadioStationNowPlayingHistories
            .AsNoTracking()
            .Where(h => h.RadioStationId == id)
            .OrderByDescending(h => h.CapturedAt)
            .Take(25)
            .Select(h => new RadioStationNowPlayingHistoryDto
            {
                CapturedAt = h.CapturedAt,
                NowPlayingRaw = h.NowPlayingRaw,
                Source = h.Source
            })
            .ToArrayAsync(cancellationToken);

        var diagnostics = new RadioStationDiagnosticsDto
        {
            Id = station.Id,
            Name = station.Name,
            StreamUrl = station.StreamUrl,
            LastHealthStatus = station.LastHealthStatus,
            LastHealthCheckAt = station.LastHealthCheckAt,
            LastHealthOkAt = station.LastHealthOkAt,
            LastHealthError = station.LastHealthError,
            LastResolvedStreamUrl = station.LastResolvedStreamUrl,
            LastContentType = station.LastContentType,
            LastBitrateKbps = station.LastBitrateKbps,
            NowPlayingRaw = station.NowPlayingRaw,
            NowPlayingCapturedAt = station.NowPlayingCapturedAt,
            RecentHistory = history
        };

        return Ok(diagnostics);
    }

    /// <summary>
    /// Create a new radio station (admin only)
    /// </summary>
    [HttpPost("admin/stations")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateStationAsync(
        [FromBody] CreateRadioStationRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null || !user.IsAdmin)
        {
            return ApiForbidden("Admin access required");
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 2 || request.Name.Length > 120)
        {
            return ApiValidationError("Name must be between 2 and 120 characters");
        }

        if (string.IsNullOrWhiteSpace(request.StreamUrl) || !Uri.TryCreate(request.StreamUrl, UriKind.Absolute, out _))
        {
            return ApiValidationError("StreamUrl must be a valid absolute URL");
        }

        if (!string.IsNullOrWhiteSpace(request.HomePageUrl) && !Uri.TryCreate(request.HomePageUrl, UriKind.Absolute, out _))
        {
            return ApiValidationError("HomePageUrl must be a valid absolute URL if provided");
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode) && request.CountryCode.Length != 2)
        {
            return ApiValidationError("CountryCode must be exactly 2 characters");
        }

        if (!string.IsNullOrWhiteSpace(request.LanguageCode) && (request.LanguageCode.Length < 2 || request.LanguageCode.Length > 12))
        {
            return ApiValidationError("LanguageCode must be between 2 and 12 characters");
        }

        var station = new RadioStation
        {
            Name = request.Name,
            StreamUrl = request.StreamUrl,
            HomePageUrl = request.HomePageUrl,
            Description = request.Description,
            Tags = request.Tags,
            CountryCode = request.CountryCode,
            LanguageCode = request.LanguageCode,
            LogoUrl = request.LogoUrl,
            IsLocked = request.Locked,
            ApiKey = Guid.NewGuid(),
            CreatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
        };

        var result = await radioStationService.AddAsync(station, cancellationToken);
        if (!result.IsSuccess || result.Data == null)
        {
            return ApiBadRequest("Failed to create radio station");
        }

        return Ok(new { id = result.Data.Id, success = true });
    }

    /// <summary>
    /// Update an existing radio station (admin only)
    /// </summary>
    [HttpPut("admin/stations/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStationAsync(
        [FromRoute] int id,
        [FromBody] UpdateRadioStationRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null || !user.IsAdmin)
        {
            return ApiForbidden("Admin access required");
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 2 || request.Name.Length > 120)
        {
            return ApiValidationError("Name must be between 2 and 120 characters");
        }

        if (string.IsNullOrWhiteSpace(request.StreamUrl) || !Uri.TryCreate(request.StreamUrl, UriKind.Absolute, out _))
        {
            return ApiValidationError("StreamUrl must be a valid absolute URL");
        }

        if (!string.IsNullOrWhiteSpace(request.HomePageUrl) && !Uri.TryCreate(request.HomePageUrl, UriKind.Absolute, out _))
        {
            return ApiValidationError("HomePageUrl must be a valid absolute URL if provided");
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode) && request.CountryCode.Length != 2)
        {
            return ApiValidationError("CountryCode must be exactly 2 characters");
        }

        if (!string.IsNullOrWhiteSpace(request.LanguageCode) && (request.LanguageCode.Length < 2 || request.LanguageCode.Length > 12))
        {
            return ApiValidationError("LanguageCode must be between 2 and 12 characters");
        }

        var stationResult = await radioStationService.GetAsync(id, cancellationToken);
        if (!stationResult.IsSuccess || stationResult.Data == null)
        {
            return ApiNotFound("Radio station");
        }

        var station = stationResult.Data;
        station.Name = request.Name;
        station.StreamUrl = request.StreamUrl;
        station.HomePageUrl = request.HomePageUrl;
        station.Description = request.Description;
        station.Tags = request.Tags;
        station.CountryCode = request.CountryCode;
        station.LanguageCode = request.LanguageCode;
        station.LogoUrl = request.LogoUrl;
        station.IsLocked = request.Locked;

        var updateResult = await radioStationService.UpdateAsync(station, cancellationToken);
        if (!updateResult.IsSuccess)
        {
            return ApiBadRequest("Failed to update radio station");
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Delete a radio station (admin only)
    /// </summary>
    [HttpDelete("admin/stations/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteStationAsync(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null || !user.IsAdmin)
        {
            return ApiForbidden("Admin access required");
        }

        var stationResult = await radioStationService.GetAsync(id, cancellationToken);
        if (!stationResult.IsSuccess || stationResult.Data == null)
        {
            return ApiNotFound("Radio station");
        }

        if (stationResult.Data.IsLocked)
        {
            return StatusCode(409, new ApiError("CONFLICT", "Station is locked", GetCorrelationId()));
        }

        var deleteResult = await radioStationService.DeleteAsync(user.Id, [id], cancellationToken);
        if (!deleteResult.IsSuccess)
        {
            return ApiBadRequest("Failed to delete radio station");
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Test a radio station connection (admin only)
    /// </summary>
    [HttpPost("admin/stations/{id}/test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestStationAsync(
        [FromRoute] int id,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken);
        if (user == null || !user.IsAdmin)
        {
            return ApiForbidden("Admin access required");
        }

        var stationResult = await radioStationService.GetAsync(id, cancellationToken);
        if (!stationResult.IsSuccess || stationResult.Data == null)
        {
            return ApiNotFound("Radio station");
        }

        var probeResult = await probeService.ProbeStationAsync(stationResult.Data.StreamUrl, cancellationToken);

        return Ok(new
        {
            isHealthy = probeResult.Data?.IsHealthy ?? false,
            resolvedUrl = probeResult.Data?.ResolvedStreamUrl,
            contentType = probeResult.Data?.ContentType,
            bitrateKbps = probeResult.Data?.BitrateKbps,
            errorMessage = probeResult.Data?.ErrorMessage
        });
    }
}
