using System.Text;
using Mapster;
using Melodee.Common.Extensions;

namespace Melodee.Common.Models.OpenSubsonic;

public record ArtistIndex(
    string Name,
    Artist[] Artist
) : IOpenSubsonicToXml
{
    public string ToXml(string? nodeName = null)
    {
        var result = new StringBuilder($"<index name=\"{Name.ToSafeXmlString()}\">");
        foreach (var artist in Artist)
        {
            result.Append(artist.ToXml());
        }
        result.Append("</index>");
        return result.ToString();
    }
}
