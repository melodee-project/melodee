using System.ComponentModel.DataAnnotations.Schema;
using Melodee.Common.Extensions;
using Melodee.Common.Utility;
using ServiceStack.DataAnnotations;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;

/// <summary>
///     This is a materialized record for MusicBrainz Artist from all the MusicBrainz export files.
/// </summary>
public sealed record Artist
{
    public const string TableName = "artists";

    private string[]? _alternateNames;

    [AutoIncrement] public long Id { get; set; }

    [StringLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required long MusicBrainzArtistId { get; init; }

    [Index(false)]
    [StringLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string Name { get; init; }

    [StringLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string SortName { get; init; }

    [Index(false)]
    [StringLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string NameNormalized { get; init; }

    [Index]
    [StringLength(MusicBrainzRepositoryBase.MaxIndexSize)]
    public required string MusicBrainzIdRaw { get; init; }

    [Ignore] [NotMapped] public Guid MusicBrainzId => SafeParser.ToGuid(MusicBrainzIdRaw) ?? Guid.Empty;

    [Index(false)] public string? AlternateNames { get; init; }

    [Ignore]
    [NotMapped]
    public string[] AlternateNamesValues => _alternateNames ??= AlternateNames?.ToTags()?.ToArray() ?? [];
}
