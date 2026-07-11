using System.Text;
using Melodee.Common.Extensions;

namespace Melodee.Common.Models.OpenSubsonic;

/// <summary>
///     Artist record used in list and index operations.
/// </summary>
/// <param name="Id">Artist ApiKey</param>
/// <param name="Name">Artist name</param>
/// <param name="UserRating">Artist rating [1-5]</param>
/// <param name="AverageRating">Artist average rating [1.0-5.0]</param>
/// <param name="CoverArt">A covertArt id.</param>
/// <param name="AlbumCount">Artist album count</param>
/// <param name="UserStarred">Timestamp when user starred artist</param>
/// <param name="ArtistImageUrl">Artist image url</param>
/// <param name="Album">Albums associated with the artist.</param>
public record Artist(
    string Id,
    string Name,
    int AlbumCount,
    int? UserRating,
    decimal AverageRating,
    string CoverArt,
    string? ArtistImageUrl = null,
    string? UserStarred = null,
    AlbumList2[]? Album = null
) : IOpenSubsonicToXml
{
    /// <summary>
    ///     Sometimes its "Starred" and sometimes its "UserStarred" in the responses.
    /// </summary>
    public string? Starred => UserStarred;

    public string ToXml(string? nodeName = null)
    {
        var starredAttribute = string.Empty;
        if (Starred != null)
        {
            starredAttribute = $" starred=\"{Starred}\" starredAt=\"{Starred}\"";
        }

        var result = new StringBuilder(
            $"<artist id=\"{Id}\" name=\"{Name.ToSafeXmlString()}\" {starredAttribute} artistImageUrl=\"{ArtistImageUrl}\" averageRating=\"{AverageRating}\" userRating=\"{UserRating}\" coverArt=\"{CoverArt}\" albumCount=\"{AlbumCount}\">");
        if (Album != null)
        {
            foreach (var album in Album)
            {
                // result.Append($"<album id=\"{ album.Id }\" name=\"{ album.Name.ToSafeXmlString() }\" coverArt=\"{ album.CoverArt }\" songCount=\"{ album.SongCount }\" created=\"{ album.Created }\" duration=\"{ album.Duration }\" artist=\"{ album.Artist.ToSafeXmlString() }\" artistId=\"{ album.ArtistId }\"/>");
                result.Append(album.ToXml());
            }
        }

        result.Append("</artist>");
        return result.ToString();
    }
}
