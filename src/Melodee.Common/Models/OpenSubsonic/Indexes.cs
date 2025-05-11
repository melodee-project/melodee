using System.Text;

namespace Melodee.Common.Models.OpenSubsonic;

/// <summary>
///     A list operation of Artists
///     <remarks>
///         See https://opensubsonic.netlify.app/docs/responses/indexes/
///     </remarks>
/// </summary>
/// <param name="IgnoredArticles">The ignored articles</param>
/// <param name="LastModified">Last time the index was modified in milliseconds after January 1, 1970 UTC</param>
/// <param name="ShortCut">Shortcut</param>
/// <param name="Index">Indexed artists</param>
/// <param name="Child">Array of children</param>
public sealed record Indexes(
    string IgnoredArticles,
    string LastModified,
    NamedInfo[] ShortCut,
    ArtistIndex[] Index,
    Child[] Child
) : IOpenSubsonicToXml
{
    public string ToXml(string? nodeName = null)
    {
        var result =
            new StringBuilder($"<indexes lastModified=\"{LastModified}\" ignoredArticles=\"{IgnoredArticles}\">");
        if (ShortCut.Length > 0)
        {
            foreach (var shortCut in ShortCut)
            {
                result.Append(shortCut.ToXml("shortcut"));
            }
        }

        if (Index.Length > 0)
        {
            foreach (var index in Index)
            {
                result.Append(index.ToXml());
            }
        }

        if (Child.Length > 0)
        {
            foreach (var child in Child)
            {
                result.Append(child.ToXml("child"));
            }
        }

        result.Append("</indexes>");
        return result.ToString();
    }
}
