namespace Melodee.Cli.Models;

/// <summary>
/// User information from GET /api/v1/user/me
/// </summary>
public record UserMeDto(
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
