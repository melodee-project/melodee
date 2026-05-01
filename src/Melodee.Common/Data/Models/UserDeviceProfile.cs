using System.ComponentModel.DataAnnotations;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Validators;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Transcoding profile for a specific user device/player.
/// Determines how media is transcoded (or if direct play) for a specific client.
/// </summary>
[Serializable]
[Index(nameof(UserId), nameof(PlayerId), IsUnique = true)]
[Index(nameof(UserId), nameof(IsDefaultProfile))]
public class UserDeviceProfile : DataModelBase
{
    /// <summary>
    /// The user this profile belongs to
    /// </summary>
    [RequiredGreaterThanZero]
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>
    /// The specific player/device this profile applies to (null for user default)
    /// </summary>
    public int? PlayerId { get; set; }

    public Player? Player { get; set; }

    /// <summary>
    /// Whether this is the default profile for the user (only one per user)
    /// </summary>
    public bool IsDefaultProfile { get; set; }

    /// <summary>
    /// Profile name (e.g., "Mobile - High Quality", "Desktop - Lossless")
    /// </summary>
    [Required]
    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public required string Name { get; set; }

    /// <summary>
    /// Whether to use direct play (no transcoding)
    /// </summary>
    public bool DirectPlay { get; set; }

    /// <summary>
    /// Target codec for transcoding (e.g., "mp3", "opus", "aac")
    /// Null if DirectPlay is true
    /// </summary>
    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public string? TargetCodec { get; set; }

    /// <summary>
    /// Maximum bitrate in kbps (e.g., 96, 128, 192, 320)
    /// Null if DirectPlay is true
    /// </summary>
    public int? MaxBitrate { get; set; }

    /// <summary>
    /// Optional resample rate in Hz (e.g., 44100, 48000)
    /// Null for no resampling
    /// </summary>
    public int? ResampleRate { get; set; }

    /// <summary>
    /// Priority for profile selection (higher = higher priority)
    /// Used when multiple profiles could match
    /// </summary>
    public int Priority { get; set; } = 0;
}
