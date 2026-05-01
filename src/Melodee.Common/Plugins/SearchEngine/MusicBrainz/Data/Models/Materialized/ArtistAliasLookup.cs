using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;

/// <summary>
/// Exact-match alias lookup rows for the large materialized MusicBrainz store.
/// </summary>
[Table("ArtistAlias")]
[PrimaryKey(nameof(MusicBrainzArtistId), nameof(NameNormalized))]
[Index(nameof(NameNormalized))]
[Index(nameof(MusicBrainzArtistId))]
public sealed record ArtistAliasLookup
{
    public long MusicBrainzArtistId { get; init; }

    [MaxLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string NameNormalized { get; init; }
}
