using Melodee.Common.Data.Constants;

namespace Melodee.Common.Data.Models.Extensions;

public static class BookmarkExtensions
{
    public static string ToCoverArtId(this Bookmark bookmark)
    {
        return bookmark.ToApiKey();
    }

    public static string ToApiKey(this Bookmark bookmark)
    {
        return $"bookmark{OpenSubsonicServer.ApiIdSeparator}{bookmark.ApiKey}";
    }

    public static Common.Models.OpenSubsonic.Bookmark ToApiBookmark(this Bookmark bookmark)
    {
        return new Common.Models.OpenSubsonic.Bookmark(
            bookmark.Position,
            bookmark.User.UserName,
            bookmark.Comment,
            bookmark.CreatedAt.ToString(),
            bookmark.LastUpdatedAt?.ToString() ?? bookmark.CreatedAt.ToString(),
            [bookmark.Song.ToApiChild(bookmark.Song.Album, bookmark.Song.UserSongs.FirstOrDefault())]
        );
    }
}
