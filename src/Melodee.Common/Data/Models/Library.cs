using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Validators;
using Melodee.Common.Enums;
using Melodee.Common.Utility;
using NodaTime;

namespace Melodee.Common.Data.Models;

/// <summary>
///     Should be a single record for each type of library - except for Storage. See <see cref="MelodeeDbContext" /> for restriction configuration.
/// </summary>
[Serializable]
public class Library : DataModelBase
{
    [MaxLength(MaxLengthDefinitions.MaxGeneralInputLength)]
    [Required]
    public required string Name { get; set; }

    public int? ArtistCount { get; set; }

    public int? AlbumCount { get; set; }

    public int? SongCount { get; set; }

    [MaxLength(MaxLengthDefinitions.MaxIndexableLength)]
    [Required]
    public required string Path { get; set; }

    [RequiredGreaterThanZero] public required int Type { get; set; }

    [NotMapped] public LibraryType TypeValue => SafeParser.ToEnum<LibraryType>(Type);

    public Instant? LastScanAt { get; set; }

    public ICollection<LibraryScanHistory> ScanHistories { get; set; } = new List<LibraryScanHistory>();

    public ICollection<LibraryAccessControl> AccessControls { get; set; } = new List<LibraryAccessControl>();

    public Instant LastWriteTime()
    {
        return !Directory.Exists(Path)
            ? Instant.MinValue
            : Instant.FromDateTimeUtc(Directory.GetLastWriteTimeUtc(Path));
    }

    public bool NeedsScanning()
    {
        if (!Directory.Exists(Path))
        {
            return false;
        }

        return LastScanAt == null || LastScanAt < LastWriteTime();
    }

    public override string ToString()
    {
        return $"ApiKey [{ApiKey}] Name [{Name}] Path [{Path}]";
    }
}
