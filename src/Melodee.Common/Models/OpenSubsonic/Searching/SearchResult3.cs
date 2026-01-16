using System.Text;
using System.Text.Json.Serialization;

namespace Melodee.Common.Models.OpenSubsonic.Searching;

// Property order matters! System.Text.Json serializes records in declaration order,
// and the OpenSubsonicResponseModelConvertor uses the first property as the data container.
// We need to ensure the first property matches the expected element name.
public record SearchResult3(
    [property: JsonPropertyName("album")] AlbumSearchResult[] Album,
    [property: JsonPropertyName("song")] SongSearchResult[] Song,
    [property: JsonPropertyName("artist")] ArtistSearchResult[] Artist
)
    : IOpenSubsonicToXml
{
    public string ToXml(string? nodeName = null)
    {
        var result = new StringBuilder("<searchResult3>");
        foreach (var artist in Artist)
        {
            result.Append(artist.ToXml());
        }

        foreach (var album in Album)
        {
            result.Append(album.ToXml());
        }

        foreach (var song in Song)
        {
            result.Append(song.ToXml());
        }

        result.Append("</searchResult3>");
        return result.ToString();
    }
}
