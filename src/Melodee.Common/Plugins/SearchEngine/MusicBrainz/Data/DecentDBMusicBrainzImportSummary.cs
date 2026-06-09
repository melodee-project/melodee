namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
/// Row-count summary produced by the DecentDB MusicBrainz streaming importer.
/// </summary>
public sealed record DecentDBMusicBrainzImportSummary(
    int Artists,
    int ArtistAliases,
    int Links,
    int ArtistArtistLinks,
    int ArtistRelations,
    int PrimaryArtistCredits,
    int ReleaseGroups,
    int ReleaseGroupMetaRows,
    int Releases,
    int Albums)
{
    public bool HasMaterializedData => Artists > 0 && Albums > 0;
}
