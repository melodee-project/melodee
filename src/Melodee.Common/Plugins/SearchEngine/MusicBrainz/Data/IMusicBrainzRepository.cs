using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Album = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
///     Callback for reporting import progress.
/// </summary>
/// <param name="phase">Current phase name (e.g., "Loading Files", "Creating Index", "Importing Artists")</param>
/// <param name="currentItem">Current item being processed (0-based)</param>
/// <param name="totalItems">Total items to process in this phase</param>
/// <param name="message">Optional message with additional details</param>
public delegate void ImportProgressCallback(string phase, int currentItem, int totalItems, string? message = null);

public sealed record MusicBrainzImportRequest(string? StoragePath = null, string? TargetDatabasePath = null);

public interface IMusicBrainzRepository
{
    Task<Album?> GetAlbumByMusicBrainzId(Guid musicBrainzId, CancellationToken cancellationToken = default);

    Task<PagedResult<ArtistSearchResult>> SearchArtist(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> ImportData(
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> ImportData(
        MusicBrainzImportRequest request,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);
}
