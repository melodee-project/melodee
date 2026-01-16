using System.Text;
using System.Text.Json.Serialization;
using Melodee.Common.Extensions;

namespace Melodee.Common.Models.OpenSubsonic;

public record Share : IOpenSubsonicToXml
{
    /// <summary>
    ///     The share Id
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    ///     The share url
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    ///     A description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     The username
    /// </summary>
    [JsonPropertyName("username")]
    public required string UserName { get; init; }

    /// <summary>
    ///     Creation date [ISO 8601]
    /// </summary>
    public required string Created { get; init; }

    /// <summary>
    ///     Share expiration [ISO 8601]
    /// </summary>
    public string? Expires { get; init; }

    /// <summary>
    ///     Last visit [ISO 8601]
    /// </summary>
    public string? LastVisited { get; init; }

    /// <summary>
    ///     Visit count
    /// </summary>
    public required int VisitCount { get; init; }

    /// <summary>
    ///     A list of share
    /// </summary>
    public Child[]? Entry { get; init; }

    public string ToXml(string? nodeName = null)
    {
        var result = new StringBuilder(
            $"<share id=\"{Id}\" url=\"{Url}\" description=\"{Description.ToSafeXmlString()}\" username=\"{UserName.ToSafeXmlString()}\" created=\"{Created}\" expires=\"{Expires}\" lastVisited=\"{LastVisited}\" visitCount=\"{VisitCount}\">");
        if (Entry != null)
        {
            foreach (var song in Entry ?? [])
            {
                result.Append(song.ToXml("entry"));
            }
        }

        result.Append("</share>");

        return result.ToString();
    }
}
