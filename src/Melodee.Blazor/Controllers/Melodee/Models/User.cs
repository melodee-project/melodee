namespace Melodee.Blazor.Controllers.Melodee.Models;

public record User(
    Guid Id,
    string ThumbnailUrl,
    string ImageUrl,
    string Username,
    string Email,
    bool IsAdmin,
    bool IsEditor,
    string[] Roles,
    int SongsPlayed,
    int ArtistsLiked,
    int ArtistsDisliked,
    int AlbumsLiked,
    int AlbumsDisliked,
    int SongsLiked,
    int SongsDisliked,
    string CreatedAt,
    string UpdatedAt);
