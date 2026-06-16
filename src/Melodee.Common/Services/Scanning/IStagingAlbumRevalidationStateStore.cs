using Melodee.Common.Models;

namespace Melodee.Common.Services.Scanning;

public interface IStagingAlbumRevalidationStateStore
{
    Task<IStagingAlbumRevalidationStateSession> OpenAsync(
        string stagingPath,
        IReadOnlyCollection<Album> currentAlbums,
        CancellationToken cancellationToken);
}

public interface IStagingAlbumRevalidationStateSession : IAsyncDisposable
{
    StagingAlbumRevalidationDecision GetDecision(Album album, DateTimeOffset now, bool force);

    void RecordAttempt(Album album, DateTimeOffset now, string outcome);

    void RecordSuccess(Album album);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record StagingAlbumRevalidationDecision(
    bool IsDue,
    int AttemptCount = 0,
    DateTimeOffset? NextAttemptAt = null,
    string? Reason = null);
