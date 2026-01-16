using System.Text.Json.Serialization;
using System.Text;

namespace Melodee.Common.Models.OpenSubsonic;

public record Artists(
    string IgnoredArticles,
    string LastModified,
    [property: JsonPropertyName("index")] ArtistIndex[] Index
) : IOpenSubsonicToXml
{
    public string ToXml(string? nodeName = null)
    {
        var result =
            new StringBuilder($"<artists lastModified=\"{LastModified}\" ignoredArticles=\"{IgnoredArticles}\">");
        if (Index.Length > 0)
        {
            foreach (var index in Index)
            {
                result.Append(index.ToXml());
            }
        }

        result.Append("</artists>");
        return result.ToString();
    }
}
