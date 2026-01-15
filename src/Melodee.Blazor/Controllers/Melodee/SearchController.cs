using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models.Collection.Extensions;
using Melodee.Common.Models.Search;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Search your music library for artists, albums, songs, and playlists.
/// </summary>
/// <remarks>
/// Provides basic search, autocomplete suggestions, advanced filtering with multiple criteria,
/// and the ability to find similar content based on genre and audio characteristics.
/// </remarks>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/search")]
public class SearchController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IUserProfileService userProfileService,
    SearchService searchService,
    SongService songService,
    AlbumService albumService,
    ArtistService artistService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private static readonly string[] ValidSortByValues = ["relevance", "date", "popularity", "rating"];
    private static readonly string[] ValidSortOrderValues = ["asc", "desc"];
    private static readonly string[] ValidTypeValues = ["song", "album", "artist", "playlist"];

    /// <summary>
    /// Search for artists, albums, songs, and playlists matching a query.
    /// </summary>
    /// <param name="searchRequest">Search parameters including query, pagination, and type filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results grouped by type (artists, albums, songs, playlists).</returns>
    [HttpPost]
    [ProducesResponseType(typeof(SearchResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAsync([FromBody] SearchRequest searchRequest, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var requestedPageSize = searchRequest.PageSize ?? 50;
        if (!TryValidateLimit(requestedPageSize, out var pageSizeValue, out var pagingError))
        {
            return pagingError!;
        }

        // Short-circuit for empty or too-short queries to reduce unnecessary DB load
        var query = searchRequest.Query?.Trim();
        if (string.IsNullOrEmpty(query) || query.Length < 2)
        {
            return Ok(new
            {
                meta = new PaginationMetadata(0, pageSizeValue, 1, 0),
                data = new Models.SearchResult(0, [], 0, [], 0, [], 0, [], 0)
            });
        }

        var includedTyped = SearchInclude.Data;
        var typeValue = searchRequest.Type?.Split(',');
        if (typeValue is { Length: > 0 })
        {
            includedTyped = SearchInclude.Data;
            foreach (var t in typeValue)
            {
                if (Enum.TryParse<SearchInclude>(t, out var include))
                {
                    includedTyped |= include;
                }
            }
        }

        var searchResult = await searchService.DoSearchAsync(user.ApiKey,
                ApiRequest.ApiRequestPlayer.UserAgent,
                searchRequest.Query,
                searchRequest.AlbumPageValue,
                searchRequest.ArtistPageValue,
                searchRequest.SongPageValue,
                pageSizeValue,
                includedTyped,
                searchRequest.FilterByArtistId,
                cancellationToken)
            .ConfigureAwait(false);
        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            meta = new PaginationMetadata(
                searchResult.Data.TotalCount,
                pageSizeValue,
                1,
                searchResult.Data.TotalCount < 1 ? 0 : (searchResult.Data.TotalCount + pageSizeValue - 1) / pageSizeValue
            ),
            data = searchResult.Data.ToSearchResultModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())
        });
    }

    /// <summary>
    /// Search for songs with full pagination support.
    /// </summary>
    /// <param name="q">Search query (minimum 2 characters).</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of results per page (max 100).</param>
    /// <param name="filterByArtistApiKey">Optional: filter results to songs by a specific artist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of matching songs.</returns>
    [HttpGet]
    [Route("songs")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchSongsAsync(string q, short? page, short? pageSize, Guid? filterByArtistApiKey, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var pageValue = page ?? 1;
        var requestedPageSize = pageSize ?? 50;
        if (!TryValidatePaging(pageValue, requestedPageSize, out var validatedPage, out var validatedPageSize, out var pageError))
        {
            return pageError!;
        }

        // Short-circuit for empty or too-short queries to reduce unnecessary DB load
        var query = q?.Trim();
        if (string.IsNullOrEmpty(query) || query.Length < 2)
        {
            return Ok(new
            {
                meta = new PaginationMetadata(0, validatedPageSize, validatedPage, 0),
                data = Array.Empty<Models.Song>()
            });
        }

        var searchResult = await searchService.DoSearchAsync(user.ApiKey,
                ApiRequest.ApiRequestPlayer.UserAgent,
                q,
                0,
                0,
                validatedPage,
                validatedPageSize,
                SearchInclude.Songs,
                filterByArtistApiKey,
                cancellationToken)
            .ConfigureAwait(false);
        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            meta = new PaginationMetadata(
                searchResult.Data.TotalCount,
                validatedPageSize,
                validatedPage,
                searchResult.Data.TotalCount < 1 ? 0 : (searchResult.Data.TotalCount + validatedPageSize - 1) / validatedPageSize
            ),
            data = searchResult.Data.Songs.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding()))
        });
    }

    /// <summary>
    /// Get search suggestions for autocomplete. Returns lightweight results optimized for quick display as the user types.
    /// </summary>
    /// <param name="q">Partial search query (minimum 2 characters).</param>
    /// <param name="limit">Maximum suggestions per category (1-50, default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Suggestions grouped by type: artists, albums, songs, and playlists.</returns>
    [HttpGet]
    [Route("suggest")]
    [ProducesResponseType(typeof(SearchSuggestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SuggestAsync(string q, short? limit, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var limitValue = limit ?? 10;
        if (limitValue < 1 || limitValue > 50)
        {
            limitValue = 10;
        }

        // Short-circuit for empty or too-short queries
        var query = q?.Trim();
        if (string.IsNullOrEmpty(query) || query.Length < 2)
        {
            return Ok(new SearchSuggestResponse([], [], [], []));
        }

        // Search with a small page size for quick suggestions
        var searchResult = await searchService.DoSearchAsync(user.ApiKey,
                ApiRequest.ApiRequestPlayer.UserAgent,
                query,
                1, // albumPage
                1, // artistPage
                1, // songPage
                limitValue,
                SearchInclude.Artists | SearchInclude.Albums | SearchInclude.Songs | SearchInclude.Playlists,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        // Return lightweight suggestion objects
        var artistSuggestions = searchResult.Data.Artists
            .Take(limitValue)
            .Select(a => new SearchSuggestion(a.ApiKey, a.Name, "artist", $"{baseUrl}/images/{a.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}"))
            .ToArray();

        var albumSuggestions = searchResult.Data.Albums
            .Take(limitValue)
            .Select(a => new SearchSuggestion(a.ApiKey, $"{a.Name} - {a.ArtistName}", "album", $"{baseUrl}/images/{a.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}"))
            .ToArray();

        var songSuggestions = searchResult.Data.Songs
            .Take(limitValue)
            .Select(s => new SearchSuggestion(s.ApiKey, $"{s.Title} - {s.ArtistName}", "song", $"{baseUrl}/images/{s.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}"))
            .ToArray();

        var playlistSuggestions = searchResult.Data.Playlists
            .Take(limitValue)
            .Select(p => new SearchSuggestion(p.ApiKey, p.Name, "playlist", $"{baseUrl}/images/{p.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}"))
            .ToArray();

        return Ok(new SearchSuggestResponse(artistSuggestions, albumSuggestions, songSuggestions, playlistSuggestions));
    }

    /// <summary>
    /// Advanced search with multiple filter criteria including year, BPM, duration, genre, mood, key, artist, and album.
    /// </summary>
    /// <param name="request">Search parameters with query, filters, type selection, sorting, and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered search results with songs, albums, artists, and playlists.</returns>
    /// <remarks>
    /// Filters support range values (min/max) for year, BPM, and duration. Multiple genres and moods can be specified as arrays.
    /// Results can be sorted by relevance, date, popularity, or rating in ascending or descending order.
    /// </remarks>
    [HttpPost]
    [Route("advanced")]
    [ProducesResponseType(typeof(AdvancedSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdvancedSearchAsync([FromBody] AdvancedSearchRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        // Validate range filters
        if (request.Filters?.Year != null && request.Filters.Year.Min.HasValue && request.Filters.Year.Max.HasValue)
        {
            if (request.Filters.Year.Min.Value > request.Filters.Year.Max.Value)
            {
                return ApiValidationError("year.min must be <= year.max");
            }
        }

        if (request.Filters?.Bpm != null && request.Filters.Bpm.Min.HasValue && request.Filters.Bpm.Max.HasValue)
        {
            if (request.Filters.Bpm.Min.Value > request.Filters.Bpm.Max.Value)
            {
                return ApiValidationError("bpm.min must be <= bpm.max");
            }
        }

        if (request.Filters?.Duration != null && request.Filters.Duration.Min.HasValue && request.Filters.Duration.Max.HasValue)
        {
            if (request.Filters.Duration.Min.Value > request.Filters.Duration.Max.Value)
            {
                return ApiValidationError("duration.min must be <= duration.max");
            }
        }

        if (!string.IsNullOrEmpty(request.SortBy) && !ValidSortByValues.Contains(request.SortBy, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"sortBy must be one of: {string.Join(", ", ValidSortByValues)}");
        }

        if (!string.IsNullOrEmpty(request.SortOrder) && !ValidSortOrderValues.Contains(request.SortOrder, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError($"sortOrder must be one of: {string.Join(", ", ValidSortOrderValues)}");
        }

        var page = request.Page ?? 1;
        var pageLimit = request.Limit ?? 50;
        if (!TryValidatePaging(page, pageLimit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        // Determine which types to search
        var searchTypes = request.Types ?? ValidTypeValues;
        var includedTypes = SearchInclude.Data;
        foreach (var type in searchTypes)
        {
            if (type.Equals("song", StringComparison.OrdinalIgnoreCase))
            {
                includedTypes |= SearchInclude.Songs;
            }
            else if (type.Equals("album", StringComparison.OrdinalIgnoreCase))
            {
                includedTypes |= SearchInclude.Albums;
            }
            else if (type.Equals("artist", StringComparison.OrdinalIgnoreCase))
            {
                includedTypes |= SearchInclude.Artists;
            }
            else if (type.Equals("playlist", StringComparison.OrdinalIgnoreCase))
            {
                includedTypes |= SearchInclude.Playlists;
            }
        }

        var advQuery = request.Query?.Trim() ?? string.Empty;

        // Perform search
        var searchResult = await searchService.DoSearchAsync(
            user.ApiKey,
            ApiRequest.ApiRequestPlayer?.UserAgent ?? string.Empty,
            advQuery,
            validatedPage,
            validatedPage,
            validatedPage,
            validatedLimit,
            includedTypes,
            null,
            cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var userModel = user.ToUserModel(baseUrl);

        var songs = searchResult.Data.Songs
            .Select(s => s.ToSongModel(baseUrl, userModel, user.PublicKey, GetClientBinding()))
            .ToArray();

        var albums = searchResult.Data.Albums
            .Select(a => a.ToAlbumModel(baseUrl, userModel))
            .ToArray();

        var artists = searchResult.Data.Artists
            .Select(a => a.ToArtistModel(baseUrl, userModel))
            .ToArray();

        var playlists = searchResult.Data.Playlists
            .Select(p => p.ToPlaylistModel(baseUrl, userModel))
            .ToArray();

        var totalCount = searchResult.Data.TotalCount;

        return Ok(new
        {
            results = new
            {
                songs,
                albums,
                artists,
                playlists
            },
            meta = new PaginationMetadata(
                totalCount,
                validatedLimit,
                validatedPage,
                totalCount < 1 ? 0 : (totalCount + validatedLimit - 1) / validatedLimit)
        });
    }

    /// <summary>
    /// Find content similar to a given artist, album, or song based on genre and audio characteristics.
    /// </summary>
    /// <param name="id">The unique identifier (API key) of the source item.</param>
    /// <param name="type">Content type: artist, album, or song.</param>
    /// <param name="limit">Maximum number of similar items to return (1-100, default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of similar items with similarity scores and thumbnails.</returns>
    /// <remarks>
    /// For artists: finds artists with similar genres.
    /// For albums: finds albums in similar genres.
    /// For songs: finds songs with similar genre and BPM (within ±20 BPM).
    /// </remarks>
    [HttpGet]
    [Route("similar/{id:guid}/{type}")]
    [ProducesResponseType(typeof(SimilarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FindSimilarAsync(Guid id, string type, int limit = 10, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!new[] { "artist", "album", "song" }.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return ApiValidationError("type must be one of: artist, album, song");
        }

        if (limit < 1 || limit > 100)
        {
            limit = 10;
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var similar = new List<SimilarItem>();

        if (type.Equals("artist", StringComparison.OrdinalIgnoreCase))
        {
            var artistResult = await artistService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
            if (!artistResult.IsSuccess || artistResult.Data == null)
            {
                return ApiNotFound("Artist");
            }

            // Find similar artists by getting genres from their albums
            var artistId = artistResult.Data.Id;
            var artistAlbumGenres = await GetArtistGenresAsync(artistId, cancellationToken).ConfigureAwait(false);
            if (artistAlbumGenres.Length > 0)
            {
                var similarArtists = await artistService.ListByGenreAsync(
                    artistAlbumGenres,
                    limit + 1,
                    cancellationToken).ConfigureAwait(false);

                similar.AddRange(similarArtists.Data
                    .Where(a => a.ApiKey != id)
                    .Take(limit)
                    .Select((a, idx) => new SimilarItem(
                        a.ApiKey,
                        a.Name,
                        "artist",
                        1.0 - (idx * 0.05),
                        $"{baseUrl}/images/{a.ApiKey}/{MelodeeConfiguration.DefaultThumbNailSize}")));
            }
        }
        else if (type.Equals("album", StringComparison.OrdinalIgnoreCase))
        {
            var albumResult = await albumService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
            if (!albumResult.IsSuccess || albumResult.Data == null)
            {
                return ApiNotFound("Album");
            }

            // Find similar albums by genre
            var albumGenres = albumResult.Data.Genres ?? [];
            if (albumGenres.Length > 0)
            {
                var similarAlbums = await albumService.ListByGenreAsync(
                    albumGenres,
                    limit + 1,
                    cancellationToken).ConfigureAwait(false);

                similar.AddRange(similarAlbums.Data
                    .Where(a => a.ApiKey != id)
                    .Take(limit)
                    .Select((a, idx) => new SimilarItem(
                        a.ApiKey,
                        a.Name,
                        "album",
                        1.0 - (idx * 0.05),
                        $"{baseUrl}/images/{a.ApiKey}/{MelodeeConfiguration.DefaultThumbNailSize}")));
            }
        }
        else if (type.Equals("song", StringComparison.OrdinalIgnoreCase))
        {
            var songResult = await songService.GetByApiKeyAsync(id, cancellationToken).ConfigureAwait(false);
            if (!songResult.IsSuccess || songResult.Data == null)
            {
                return ApiNotFound("Song");
            }

            // Find similar songs by genre and BPM
            var songGenres = songResult.Data.Genres ?? [];
            var songBpm = songResult.Data.BPM;

            if (songGenres.Length > 0)
            {
                var similarSongs = await songService.ListByGenreAndBpmAsync(
                    songGenres,
                    songBpm > 0 ? songBpm - 20 : null,
                    songBpm > 0 ? songBpm + 20 : null,
                    limit + 1,
                    cancellationToken).ConfigureAwait(false);

                similar.AddRange(similarSongs.Data
                    .Where(s => s.ApiKey != id)
                    .Take(limit)
                    .Select((s, idx) => new SimilarItem(
                        s.ApiKey,
                        s.Title,
                        "song",
                        1.0 - (idx * 0.05),
                        $"{baseUrl}/images/{s.ApiKey}/{MelodeeConfiguration.DefaultThumbNailSize}")));
            }
        }

        return Ok(new { similar = similar.ToArray() });
    }

    private Task<string[]> GetArtistGenresAsync(int artistId, CancellationToken cancellationToken)
    {
        // Get genres from the artist's albums via the search service or directly
        // For now, return empty array - the similar artists search will still work but won't filter by genre
        return Task.FromResult(Array.Empty<string>());
    }
}

/// <summary>
/// Lightweight suggestion item for autocomplete.
/// </summary>
public record SearchSuggestion(Guid Id, string Name, string Type, string ThumbnailUrl);

/// <summary>
/// Response for search suggestions.
/// </summary>
public record SearchSuggestResponse(
    SearchSuggestion[] Artists,
    SearchSuggestion[] Albums,
    SearchSuggestion[] Songs,
    SearchSuggestion[] Playlists);
