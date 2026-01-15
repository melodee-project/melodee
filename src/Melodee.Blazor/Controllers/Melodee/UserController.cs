using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// User-specific endpoints for the authenticated user's library data.
/// Uses singular /user to distinguish from admin /users endpoints.
/// For authentication, use /api/v1/auth.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/user")]
public class UserController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserService userService,
    UserProfileService userProfileService,
    SongService songService,
    AlbumService albumService,
    ArtistService artistService,
    PlaylistService playlistService,
    IGoogleTokenService googleTokenService,
    IOptions<GoogleAuthOptions> googleAuthOptions,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    UserSocialLoginService userSocialLoginService) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private readonly GoogleAuthOptions _googleAuthOptions = googleAuthOptions.Value;

    #region User Profile

    /// <summary>
    /// Return information about the current user making the request.
    /// </summary>
    [HttpGet]
    [Route("me")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AboutMeAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        return Ok(user.ToUserModel(await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Return the last played songs by the user.
    /// </summary>
    [HttpGet]
    [Route("last-played")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LastPlayedSongsAsync(short page = 1, short pageSize = 3, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
        {
            return pagingError!;
        }

        var userLastPlayedResult = await userService.UserLastPlayedSongsAsync(user.Id, validatedPageSize, cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            data = userLastPlayedResult.Data.Where(x => x?.Song != null).Select(x => x!.Song.ToSongDataInfo()).ToArray(),
            meta = new
            {
                totalCount = userLastPlayedResult.Data.Length,
                pageSize = (int)validatedPageSize,
                currentPage = (int)validatedPage,
                totalPages = 1,
                hasNext = false,
                hasPrevious = false
            }
        });
    }

    /// <summary>
    /// Get playlists for the current user.
    /// </summary>
    [HttpGet]
    [Route("playlists")]
    [RequireCapability(UserCapability.Playlist)]
    [ProducesResponseType(typeof(PlaylistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlaylistsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var playlists = await playlistService.ListAsync(user.ToUserInfo(), new PagedRequest { Page = validatedPage, PageSize = validatedLimit }, cancellationToken).ConfigureAwait(false);
        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = playlists.Data.Select(x => x.ToPlaylistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = playlists.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = playlists.TotalPages,
                hasNext = validatedPage < playlists.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    #endregion

    #region User Songs

    /// <summary>
    /// Get songs the current user has liked (starred).
    /// </summary>
    [HttpGet]
    [Route("songs/liked")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LikedSongsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await songService.ListStarredAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get songs the current user has disliked.
    /// </summary>
    [HttpGet]
    [Route("songs/disliked")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DislikedSongsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await songService.ListHatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get all songs the current user has rated, sorted by rating descending.
    /// </summary>
    [HttpGet]
    [Route("songs/rated")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RatedSongsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await songService.ListRatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get songs the current user has rated 4+ stars.
    /// </summary>
    [HttpGet]
    [Route("songs/top-rated")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TopRatedSongsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await songService.ListTopRatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, 4, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get songs the current user has recently played, sorted by most recent first.
    /// </summary>
    [HttpGet]
    [Route("songs/recently-played")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentlyPlayedSongsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await songService.ListRecentlyPlayedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding())).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    #endregion

    #region User Albums

    /// <summary>
    /// Get albums the current user has liked (starred).
    /// </summary>
    [HttpGet]
    [Route("albums/liked")]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LikedAlbumsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await albumService.ListStarredAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get albums the current user has disliked.
    /// </summary>
    [HttpGet]
    [Route("albums/disliked")]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DislikedAlbumsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await albumService.ListHatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get all albums the current user has rated, sorted by rating descending.
    /// </summary>
    [HttpGet]
    [Route("albums/rated")]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RatedAlbumsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await albumService.ListRatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get albums the current user has rated 4+ stars.
    /// </summary>
    [HttpGet]
    [Route("albums/top-rated")]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TopRatedAlbumsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await albumService.ListTopRatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, 4, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get albums the current user has recently played, sorted by most recent first.
    /// </summary>
    [HttpGet]
    [Route("albums/recently-played")]
    [ProducesResponseType(typeof(AlbumPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentlyPlayedAlbumsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await albumService.ListRecentlyPlayedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToAlbumModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    #endregion

    #region User Artists

    /// <summary>
    /// Get artists the current user has liked (starred).
    /// </summary>
    [HttpGet]
    [Route("artists/liked")]
    [ProducesResponseType(typeof(ArtistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LikedArtistsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await artistService.ListStarredAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToArtistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get artists the current user has disliked.
    /// </summary>
    [HttpGet]
    [Route("artists/disliked")]
    [ProducesResponseType(typeof(ArtistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DislikedArtistsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await artistService.ListHatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToArtistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get all artists the current user has rated, sorted by rating descending.
    /// </summary>
    [HttpGet]
    [Route("artists/rated")]
    [ProducesResponseType(typeof(ArtistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RatedArtistsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await artistService.ListRatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToArtistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get artists the current user has rated 4+ stars.
    /// </summary>
    [HttpGet]
    [Route("artists/top-rated")]
    [ProducesResponseType(typeof(ArtistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TopRatedArtistsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await artistService.ListTopRatedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, 4, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToArtistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    /// <summary>
    /// Get artists the current user has recently played, sorted by most recent first.
    /// Artists are considered "recently played" based on songs the user has played from that artist.
    /// </summary>
    [HttpGet]
    [Route("artists/recently-played")]
    [ProducesResponseType(typeof(ArtistPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecentlyPlayedArtistsAsync(int page = 1, int limit = 50, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!TryValidatePaging(page, limit, out var validatedPage, out var validatedLimit, out var pagingError))
        {
            return pagingError!;
        }

        var result = await artistService.ListRecentlyPlayedAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedLimit
        }, user.Id, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            data = result.Data.Select(x => x.ToArtistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray(),
            meta = new
            {
                totalCount = result.TotalCount,
                pageSize = (int)validatedLimit,
                currentPage = (int)validatedPage,
                totalPages = result.TotalPages,
                hasNext = validatedPage < result.TotalPages,
                hasPrevious = validatedPage > 1
            }
        });
    }

    #endregion

    #region Social Login Linking

    /// <summary>
    /// Get linked social providers for the current user.
    /// </summary>
    /// <remarks>
    /// Returns a list of social login providers linked to the current user's account.
    /// </remarks>
    [HttpGet]
    [Route("me/linked-providers")]
    [ProducesResponseType(typeof(LinkedProviderInfo[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLinkedProvidersAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var socialLoginsResult = await userService.GetUserSocialLoginsAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var linkedProviders = socialLoginsResult.Data?.Select(sl => new LinkedProviderInfo
        {
            Provider = sl.Provider,
            Email = sl.Email,
            LinkedAt = sl.CreatedAt.ToDateTimeUtc(),
            LastLoginAt = sl.LastLoginAt?.ToDateTimeUtc()
        }).ToArray() ?? [];

        return Ok(linkedProviders);
    }

    /// <summary>
    /// Link a Google account to the current user.
    /// </summary>
    /// <remarks>
    /// Validates the Google ID token and links it to the authenticated user's account.
    /// The user must be authenticated via password before linking.
    /// </remarks>
    [HttpPost]
    [Route("me/link/google")]
    [EnableRateLimiting("melodee-auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkGoogleAsync([FromBody] GoogleLinkRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (!_googleAuthOptions.Enabled)
        {
            return ApiBadRequest("Google authentication is not enabled");
        }

        // Validate the Google ID token
        var validationResult = await googleTokenService.ValidateTokenAsync(request.IdToken, cancellationToken).ConfigureAwait(false);

        if (!validationResult.IsValid || validationResult.Payload == null)
        {
            var errorCode = validationResult.ErrorCode ?? ApiError.Codes.InvalidGoogleToken;
            return StatusCode(
                errorCode == ApiError.Codes.ExpiredGoogleToken ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest,
                new ApiError(errorCode, validationResult.ErrorMessage ?? "Google token validation failed", GetCorrelationId()));
        }

        var payload = validationResult.Payload;

        // Try to link
        var linkResult = await userSocialLoginService.LinkSocialLoginAsync(

            user.Id,
            "Google",
            payload.Subject,
            payload.Email,
            payload.Name,
            payload.HostedDomain,
            cancellationToken).ConfigureAwait(false);

        if (!linkResult.IsSuccess)
        {
            return StatusCode(StatusCodes.Status409Conflict,
                new ApiError(ApiError.Codes.GoogleAlreadyLinked,
                    linkResult.Messages?.FirstOrDefault() ?? "Failed to link Google account",
                    GetCorrelationId()));
        }

        return Ok(new
        {
            message = "Google account linked successfully",
            provider = new LinkedProviderInfo
            {
                Provider = "Google",
                Email = payload.Email,
                LinkedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            }
        });
    }

    /// <summary>
    /// Unlink a Google account from the current user.
    /// </summary>
    /// <remarks>
    /// Removes the Google social login link. The user can still log in with password
    /// and can re-link Google later if desired.
    /// </remarks>
    [HttpDelete]
    [Route("me/link/google")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkGoogleAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        var unlinkResult = await userSocialLoginService.UnlinkSocialLoginAsync(user.Id, "Google", cancellationToken).ConfigureAwait(false);

        if (!unlinkResult.IsSuccess)
        {
            return ApiNotFound("Google account is not linked");
        }

        return Ok(new { message = "Google account unlinked successfully" });
    }

    #endregion
}
