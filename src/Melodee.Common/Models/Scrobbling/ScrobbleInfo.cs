using NodaTime;

namespace Melodee.Common.Models.Scrobbling;

/// <summary>
///     Scrobble Info
/// </summary>
/// <param name="SongApiKey">The ApiKey of the Song to scrobble.</param>
/// <param name="ArtistId">The internal artist identifier.</param>
/// <param name="AlbumId">The internal album identifier.</param>
/// <param name="SongId">The internal song identifier.</param>
/// <param name="SongTitle"> The song name.</param>
/// <param name="ArtistName">The album artist name</param>
/// <param name="IsRandomizedScrobble">Flag if the user selected the song or played via some random operation.</param>
/// <param name="AlbumTitle">The album name</param>
/// <param name="SongDuration">The length of the track in seconds.</param>
/// <param name="SongMusicBrainzId">The MusicBrainz Track ID</param>
/// <param name="SongNumber">The song number of the song on the album.</param>
/// <param name="SongArtist">The album artist - if this differs from the song artist.</param>
/// <param name="CreatedAt">When the scrobble was created.</param>
/// <param name="PlayerName">The player that submitted the scrobble.</param>
/// <param name="UserAgent">The client user-agent value.</param>
/// <param name="IpAddress">The client IP address.</param>
/// <param name="SecondsPlayed">The number of seconds played.</param>
public record ScrobbleInfo(
    Guid SongApiKey,
    int ArtistId,
    int AlbumId,
    int SongId,
    string SongTitle,
    string ArtistName,
    bool IsRandomizedScrobble,
    string? AlbumTitle,
    int? SongDuration,
    Guid? SongMusicBrainzId,
    int? SongNumber,
    string? SongArtist,
    Instant CreatedAt,
    string PlayerName,
    string? UserAgent = null,
    string? IpAddress = null,
    int? SecondsPlayed = null
)
{
    public Instant LastScrobbledAt { get; set; } = Instant.FromDateTimeOffset(DateTimeOffset.UtcNow);

    public bool IsExpired => MinutesAgo > SongDuration;

    public int MinutesAgo => (LastScrobbledAt - CreatedAt).Minutes;
}
