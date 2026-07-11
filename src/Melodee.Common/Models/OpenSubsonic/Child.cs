using Melodee.Common.Extensions;

namespace Melodee.Common.Models.OpenSubsonic;

/// <summary>
///     A media.
/// </summary>
/// <param name="Id"></param>
/// <param name="Parent"></param>
/// <param name="IsDir"></param>
/// <param name="Title"></param>
/// <param name="Album"></param>
/// <param name="Artist"></param>
/// <param name="Song"></param>
/// <param name="Year"></param>
/// <param name="CoverArt"></param>
/// <param name="Size"></param>
/// <param name="ContentType"></param>
/// <param name="Suffix"></param>
/// <param name="Starred"></param>
/// <param name="Duration"></param>
/// <param name="BitRate"></param>
/// <param name="BitDepth"></param>
/// <param name="SamplingRate"></param>
/// <param name="ChannelCount"></param>
/// <param name="Path"></param>
/// <param name="PlayCount"></param>
/// <param name="Played"></param>
/// <param name="DiscNumber"></param>
/// <param name="Created"></param>
/// <param name="AlbumId"></param>
/// <param name="ArtistId"></param>
/// <param name="Type"></param>
/// <param name="MediaType"></param>
/// <param name="IsVideo"></param>
/// <param name="Bpm"></param>
/// <param name="Comment"></param>
/// <param name="SortName"></param>
/// <param name="MusicBrainzId"></param>
/// <param name="Genres"></param>
/// <param name="Artists"></param>
/// <param name="DisplayArtist"></param>
/// <param name="AlbumArtists"></param>
/// <param name="DisplayAlbumArtist"></param>
/// <param name="Contributors"></param>
/// <param name="DisplayComposer"></param>
/// <param name="Moods"></param>
/// <param name="ReplayGain"></param>
/// <param name="AverageRating"></param>
/// <param name="UserRating"></param>
/// <param name="Username"></param>
/// <param name="MinutesAgo"></param>
/// <param name="PlayerId"></param>
/// <param name="PlayerName"></param>
public record Child(
    string Id,
    string? Parent,
    bool? IsDir,
    string? Title,
    string? Album,
    string? Artist,
    int? Song,
    int? Year,
    string? CoverArt,
    long? Size,
    string? ContentType,
    string? Suffix,
    string? Starred,
    int? Duration,
    int? BitRate,
    int? BitDepth,
    int? SamplingRate,
    int? ChannelCount,
    string? Path,
    int? PlayCount,
    string? Played,
    int? DiscNumber,
    string? Created,
    string? AlbumId,
    string? ArtistId,
    string? Type,
    string? MediaType,
    bool? IsVideo,
    int? Bpm,
    string? Comment,
    string? SortName,
    string? MusicBrainzId,
    Genre[]? Genres,
    ArtistID3[]? Artists,
    string? DisplayArtist,
    ArtistID3[]? AlbumArtists,
    string? DisplayAlbumArtist,
    Contributor[]? Contributors,
    string? DisplayComposer,
    string[]? Moods,
    ReplayGain? ReplayGain,
    int? AverageRating = null,
    int? UserRating = null,
    string? Username = null,
    int? MinutesAgo = null,
    int? PlayerId = null,
    string? PlayerName = null) : IOpenSubsonicToXml
{
    public int? Track => Song;

    public string? Genre => Genres?.Length > 0 ? Genres[0].Value : null;

    public string ToXml(string? nodeName = null)
    {
        //     <xs:attribute name="transcodedContentType" type="xs:string" use="optional"/>
        //     <xs:attribute name="transcodedSuffix" type="xs:string" use="optional"/>
        //     <xs:attribute name="bookmarkPosition" type="xs:long" use="optional"/>  <!-- In millis. Added in 1.10.1 -->
        // </xs:complexType>

        var starredAttribute = string.Empty;
        if (Starred != null)
        {
            starredAttribute = $" starred=\"{Starred}\" starredAt=\"{Starred}\"";
        }

        return
            $"<{nodeName ?? "song"} id=\"{Id}\" parent=\"{Parent}\" title=\"{Title.ToSafeXmlString()}\" isDir=\"{(IsDir ?? false).ToLowerCaseString()}\" " +
            $"album=\"{Album.ToSafeXmlString()}\" artist=\"{Artist.ToSafeXmlString()}\" track=\"{Track}\" year=\"{Year}\" genre=\"{Genre.ToSafeXmlString()}\" " +
            $"isVideo=\"{(IsVideo ?? false).ToLowerCaseString()}\" playCount=\"{PlayCount}\" discNumber=\"{DiscNumber}\" " +
            $"averageRating=\"{AverageRating ?? 0}\" userRating=\"{UserRating ?? 0}\" " +
            $"created=\"{Created}\" {starredAttribute} albumId=\"{AlbumId}\" artistId=\"{ArtistId}\" type=\"{Type}\" " +
            $"coverArt=\"{CoverArt}\" size=\"{Size}\" contentType=\"{ContentType}\" suffix=\"{Suffix?.Replace(".", string.Empty)}\" " +
            $"duration=\"{Duration}\" bitRate=\"{BitRate}\" path=\"{Path.ToSafeXmlString()}\"/>";
    }
}
