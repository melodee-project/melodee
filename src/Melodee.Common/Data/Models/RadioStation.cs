using System.ComponentModel.DataAnnotations;
using Melodee.Common.Data.Constants;
using Melodee.Common.Enums;
using NodaTime;

namespace Melodee.Common.Data.Models;

[Serializable]
public class RadioStation : DataModelBase
{
    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    [Required]
    public required string Name { get; set; }

    [Required]
    [MaxLength(MaxLengthDefinitions.MaxIndexableLength)]
    public required string StreamUrl { get; set; }

    [MaxLength(MaxLengthDefinitions.MaxIndexableLength)]
    public string? HomePageUrl { get; set; }

    // Metadata fields
    [MaxLength(2)]
    public string? CountryCode { get; set; }

    [MaxLength(12)]
    public string? LanguageCode { get; set; }

    // Logo fields
    [MaxLength(MaxLengthDefinitions.MaxIndexableLength)]
    public string? LogoUrl { get; set; }

    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public string? LogoCacheKey { get; set; }

    // Health check fields
    public Instant? LastHealthCheckAt { get; set; }

    public Instant? LastHealthOkAt { get; set; }

    public RadioStationHealthStatus LastHealthStatus { get; set; } = RadioStationHealthStatus.Unknown;

    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public string? LastHealthError { get; set; }

    [MaxLength(MaxLengthDefinitions.MaxIndexableLength)]
    public string? LastResolvedStreamUrl { get; set; }

    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public string? LastContentType { get; set; }

    public int? LastBitrateKbps { get; set; }

    // Now Playing fields
    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public string? NowPlayingRaw { get; set; }

    public Instant? NowPlayingCapturedAt { get; set; }
}
