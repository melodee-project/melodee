using System.Text.Json.Serialization;
using System.Text;

namespace Melodee.Common.Models.OpenSubsonic.DTO;

public record StarredInfo2(
    [property: JsonPropertyName("artist")] ArtistID3[] Artists,
    [property: JsonPropertyName("album")] AlbumID3[] Albums,
    [property: JsonPropertyName("song")] Child[] Songs
) : IOpenSubsonicToXml
{
    public string ToXml(string? nodeName = null)
    {
        var result = new StringBuilder();
        foreach (var artist in Artists)
        {
            result.Append(artist.ToXml());
        }

        foreach (var album in Albums)
        {
            result.Append(album.ToXml());
        }

        foreach (var song in Songs)
        {
            result.Append(song.ToXml());
        }

        return result.ToString();
    }
}
