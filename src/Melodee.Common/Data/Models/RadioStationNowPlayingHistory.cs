using System.ComponentModel.DataAnnotations;
using Melodee.Common.Data.Constants;
using Melodee.Common.Enums;
using NodaTime;

namespace Melodee.Common.Data.Models;

/// <summary>
/// History of now-playing metadata captured for radio stations
/// </summary>
[Serializable]
public class RadioStationNowPlayingHistory
{
    public int Id { get; set; }

    [Required]
    public int RadioStationId { get; set; }

    [Required]
    public required Instant CapturedAt { get; set; }

    [Required]
    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public required string NowPlayingRaw { get; set; }

    public NowPlayingSource Source { get; set; } = NowPlayingSource.Unknown;

    // Navigation property
    public RadioStation? RadioStation { get; set; }
}
