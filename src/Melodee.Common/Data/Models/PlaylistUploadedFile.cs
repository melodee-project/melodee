using System.ComponentModel.DataAnnotations;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Validators;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Data.Models;

/// <summary>
/// Represents an uploaded M3U/M3U8 playlist file.
/// </summary>
[Serializable]
[Index(nameof(UserId), nameof(OriginalFileName))]
public class PlaylistUploadedFile : DataModelBase
{
    [RequiredGreaterThanZero]
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    [Required]
    public required string OriginalFileName { get; set; }

    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    public string? ContentType { get; set; }

    [RequiredGreaterThanZero]
    public required long Length { get; set; }

    /// <summary>
    /// The original file content stored as bytes for re-processing.
    /// </summary>
    [Required]
    public required byte[] Content { get; set; }

    public ICollection<PlaylistUploadedFileItem> Items { get; set; } = new List<PlaylistUploadedFileItem>();

    /// <summary>
    /// Reference to the created Playlist (if import succeeded).
    /// </summary>
    public int? PlaylistId { get; set; }

    public Playlist? Playlist { get; set; }
}
