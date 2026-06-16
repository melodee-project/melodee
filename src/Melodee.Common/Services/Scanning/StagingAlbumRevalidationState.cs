using System.ComponentModel.DataAnnotations;

namespace Melodee.Common.Services.Scanning;

public sealed class StagingAlbumRevalidationState
{
    [Key]
    public string AlbumKey { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public string AlbumDirectory { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptedAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastOutcome { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
