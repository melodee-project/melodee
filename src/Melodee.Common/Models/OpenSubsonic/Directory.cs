using System.Text;
using Melodee.Common.Extensions;

namespace Melodee.Common.Models.OpenSubsonic;

/// <summary>
///     Directory.
/// </summary>
/// <param name="Id">
///     The ApiKey id (this is passed to the GetMusicDirectory method and the prefix is needed to know if
///     artist or album)
/// </param>
/// <param name="Parent">Parent item, is null when Directory is for Artist</param>
/// <param name="Name">The directory name</param>
/// <param name="Starred">Starred date [ISO 8601]</param>
/// <param name="UserRating">The user rating [1-5]</param>
/// <param name="AverageRating">The average rating [1.0-5.0]</param>
/// <param name="PlayCount">The play count</param>
/// <param name="Played">Last played date [ISO 8601]</param>
/// <param name="Child">The directory content (can be an album, can be a song)</param>
public record Directory(
    string Id,
    string? Parent,
    string Name,
    string? Starred,
    int? UserRating,
    decimal AverageRating,
    long PlayCount,
    string? Played,
    Child[] Child) : IOpenSubsonicToXml
{
    public string ToXml(string? nodeName = null)
    {
        var result =
            new StringBuilder(
                $"<directory id=\"{Id}\" parent=\"{Parent}\" name=\"{Name.ToSafeXmlString()}\" starred=\"{Starred}\" starredAt=\"{Starred}\">");
        if (Child.Length > 0)
        {
            foreach (var child in Child)
            {
                result.Append(child.ToXml("child"));
            }
        }

        result.Append("</directory>");
        return result.ToString();
    }
}
