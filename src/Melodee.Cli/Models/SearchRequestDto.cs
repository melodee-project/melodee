namespace Melodee.Cli.Models;

/// <summary>
/// Search request for POST /api/v1/search
/// </summary>
public record SearchRequestDto(
    string Query,
    string? Type = null,
    short? PageSize = null,
    short? AlbumPage = null,
    short? ArtistPage = null,
    short? SongPage = null);
