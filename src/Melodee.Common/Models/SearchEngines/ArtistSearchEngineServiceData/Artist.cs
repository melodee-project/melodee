using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;

[Index(nameof(Name))]
[Index(nameof(NameNormalized))]
[Index(nameof(SortName))]
[Index(nameof(ItunesId), IsUnique = true)]
[Index(nameof(AmgId), IsUnique = true)]
[Index(nameof(DiscogsId), IsUnique = true)]
[Index(nameof(WikiDataId), IsUnique = true)]
[Index(nameof(MusicBrainzId), IsUnique = true)]
[Index(nameof(LastFmId), IsUnique = true)]
[Index(nameof(SpotifyId), IsUnique = true)]
[Index(nameof(IsLocked), nameof(LastRefreshed))]
public sealed class Artist
{
    [Key] public int Id { get; set; }

    [Required][MaxLength(2000)] public required string Name { get; set; }

    [Required][MaxLength(2000)] public required string NameNormalized { get; set; }

    /// <summary>
    ///     Alternate names in tag form
    /// </summary>
    [MaxLength(4000)]
    public string? AlternateNames { get; set; }

    [Required][MaxLength(2000)] public required string SortName { get; set; }

    [MaxLength(255)] public string? ItunesId { get; set; }

    [MaxLength(255)] public string? AmgId { get; set; }

    [MaxLength(255)] public string? DiscogsId { get; set; }

    [MaxLength(255)] public string? WikiDataId { get; set; }

    public Guid? MusicBrainzId { get; set; }

    [NotMapped] public string? MusicBrainzIdValue { get; set; }

    [MaxLength(255)] public string? LastFmId { get; set; }

    [MaxLength(255)] public string? SpotifyId { get; set; }

    public ICollection<Album> Albums { get; set; } = [];

    [NotMapped] public int Rank { get; set; }

    [NotMapped] public int AlbumCount { get; set; }

    public bool? IsLocked { get; set; }

    [NotMapped] public bool IsLockedValue { get; set; }

    /// <summary>
    ///     Last time the Artist albums where refreshed from search engine plugins.
    /// </summary>
    public DateTimeOffset? LastRefreshed { get; set; }

    public override string ToString()
    {
        return $"{Name} ({Id}) MusicBrainzId: [{MusicBrainzId}] SpotifyId: [{SpotifyId}]";
    }
}
