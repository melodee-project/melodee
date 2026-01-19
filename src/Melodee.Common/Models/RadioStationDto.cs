using Melodee.Common.Enums;
using NodaTime;

namespace Melodee.Common.Models;

/// <summary>
/// DTO for radio station with user preferences and health information
/// </summary>
public record RadioStationDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
    public string? HomePageUrl { get; init; }
    public string? Description { get; init; }
    public string? Tags { get; init; }
    public string? CountryCode { get; init; }
    public string? LanguageCode { get; init; }
    
    // Logo
    public string? LogoUrl { get; init; }
    public string? LogoCachedUrl { get; init; }
    
    // Health
    public RadioStationHealthStatus LastHealthStatus { get; init; }
    public Instant? LastHealthCheckAt { get; init; }
    public Instant? LastHealthOkAt { get; init; }
    public string? LastHealthError { get; init; }
    
    // Now Playing
    public string? NowPlayingRaw { get; init; }
    public Instant? NowPlayingCapturedAt { get; init; }
    
    // User Preferences
    public bool IsFavorite { get; init; }
    public bool IsHidden { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Request to update user preferences for a radio station
/// </summary>
public record UpdateRadioStationPreferencesRequest
{
    public bool? IsFavorite { get; init; }
    public bool? IsHidden { get; init; }
    public int? SortOrder { get; init; }
}

/// <summary>
/// Request to create a new radio station (admin only)
/// </summary>
public record CreateRadioStationRequest
{
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
    public string? HomePageUrl { get; init; }
    public string? Description { get; init; }
    public string? Tags { get; init; }
    public string? CountryCode { get; init; }
    public string? LanguageCode { get; init; }
    public string? LogoUrl { get; init; }
    public bool Locked { get; init; }
}

/// <summary>
/// Request to update an existing radio station (admin only)
/// </summary>
public record UpdateRadioStationRequest
{
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
    public string? HomePageUrl { get; init; }
    public string? Description { get; init; }
    public string? Tags { get; init; }
    public string? CountryCode { get; init; }
    public string? LanguageCode { get; init; }
    public string? LogoUrl { get; init; }
    public bool Locked { get; init; }
}

/// <summary>
/// Diagnostics information for a radio station
/// </summary>
public record RadioStationDiagnosticsDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string StreamUrl { get; init; }
    
    // Health
    public RadioStationHealthStatus LastHealthStatus { get; init; }
    public Instant? LastHealthCheckAt { get; init; }
    public Instant? LastHealthOkAt { get; init; }
    public string? LastHealthError { get; init; }
    public string? LastResolvedStreamUrl { get; init; }
    public string? LastContentType { get; init; }
    public int? LastBitrateKbps { get; init; }
    
    // Current Now Playing
    public string? NowPlayingRaw { get; init; }
    public Instant? NowPlayingCapturedAt { get; init; }
    
    // Recent History
    public RadioStationNowPlayingHistoryDto[] RecentHistory { get; init; } = [];
}

/// <summary>
/// Now playing history entry
/// </summary>
public record RadioStationNowPlayingHistoryDto
{
    public required Instant CapturedAt { get; init; }
    public required string NowPlayingRaw { get; init; }
    public NowPlayingSource Source { get; init; }
}
