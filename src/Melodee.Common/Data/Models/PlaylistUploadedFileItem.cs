using System.ComponentModel.DataAnnotations;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Validators;
using Melodee.Common.Enums;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Represents a single song reference from an uploaded playlist file.
/// </summary>
[Serializable]
[Index(nameof(PlaylistUploadedFileId), nameof(SortOrder))]
[Index(nameof(Status))]
public class PlaylistUploadedFileItem : DataModelBase
{
    [RequiredGreaterThanZero]
    public int PlaylistUploadedFileId { get; set; }

    public PlaylistUploadedFile PlaylistUploadedFile { get; set; } = null!;

    /// <summary>
    /// Matched Song ID (null if not yet matched).
    /// </summary>
    public int? SongId { get; set; }

    public Song? Song { get; set; }

    [Required]
    public required PlaylistUploadedFileItemStatus Status { get; set; }

    /// <summary>
    /// The raw line from the M3U file.
    /// </summary>
    [MaxLength(MaxLengthDefinitions.MaxInputLength)]
    [Required]
    public required string RawReference { get; set; }

    /// <summary>
    /// Normalized reference (cleaned, URL-decoded).
    /// </summary>
    [MaxLength(MaxLengthDefinitions.MaxInputLength)]
    [Required]
    public required string NormalizedReference { get; set; }

    /// <summary>
    /// JSON-serialized hints for matching (filename, artist, album, etc.).
    /// </summary>
    [MaxLength(MaxLengthDefinitions.MaxInputLength)]
    public string? HintsJson { get; set; }

    /// <summary>
    /// Last time matching was attempted for this item.
    /// </summary>
    public Instant? LastAttemptAt { get; set; }
}
