using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Models.DTOs;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.DTO;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Common.Models.OpenSubsonic.Searching;
using Melodee.Common.Models.Streaming;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Extensions;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Rebus.Bus;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Artist = Melodee.Common.Models.OpenSubsonic.Artist;
using dbModels = Melodee.Common.Data.Models;
using Directory = Melodee.Common.Models.OpenSubsonic.Directory;
using License = Melodee.Common.Models.OpenSubsonic.License;
using Playlist = Melodee.Common.Models.OpenSubsonic.Playlist;
using ScanStatus = Melodee.Common.Models.OpenSubsonic.ScanStatus;

namespace Melodee.Common.Services;

/// <summary>
///     Handles OpenSubsonic API calls.
/// </summary>
public class OpenSubsonicApiService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    DefaultImages defaultImages,
    IMelodeeConfigurationFactory configurationFactory,
    UserService userService,
    UserAuthenticationService userAuthenticationService,
    UserProfileService userProfileService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    SearchService searchService,
    ISchedulerFactory schedulerFactory,
    ScrobbleService scrobbleService,
    LibraryService libraryService,
    ArtistSearchEngineService artistSearchEngineService,
    PlaylistService playlistService,
    ChartService chartService,
    ShareService shareService,
    RadioStationService radioStationService,
    UserQueueService userQueueService,
    StatisticsService statisticsService,
    IBus bus,
    ILyricPlugin lyricPlugin,
    PodcastPlaybackService podcastPlaybackService,
    UserRatingService userRatingService,
    UserBookmarkService userBookmarkService)

    : ServiceBase(logger, cacheManager, contextFactory)
{
    public const string ImageCacheRegion = "urn:openSubsonic:artist-and-album-images";

    private Lazy<Task<IMelodeeConfiguration>> Configuration => new(() => configurationFactory.GetConfigurationAsync());

    private static bool IsApiIdForArtist(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"artist{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForAlbum(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"album{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForUser(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"user{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForSong(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"song{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForPlaylist(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"playlist{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForChart(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"chart{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForDynamicPlaylist(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith($"dpl{OpenSubsonicServer.ApiIdSeparator}") ?? false);
    }

    private static bool IsApiIdForPodcastChannel(string? id)
    {
        return id.Nullify() != null && (id?.StartsWith("podcast:channel:") ?? false);
    }

    private static int? PodcastChannelIdFromId(string? id)
    {
        if (id.Nullify() == null)
        {
            return null;
        }

        if (id!.StartsWith("podcast:channel:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = id["podcast:channel:".Length..];
            if (int.TryParse(idPart, out var channelId))
            {
                return channelId;
            }
        }

        return null;
    }

    private static bool IsPodcastEpisodeId(string? id) =>
        id?.StartsWith("podcast:episode:", StringComparison.OrdinalIgnoreCase) == true;

    private static int? ParsePodcastEpisodeIdFromApiId(string? id)
    {
        if (!IsPodcastEpisodeId(id)) return null;
        return int.TryParse(id!.Substring("podcast:episode:".Length), out var episodeId) ? episodeId : null;
    }

    private static Guid? ApiKeyFromId(string? id)
    {
        if (id.Nullify() == null)
        {
            return null;
        }

        var apiIdParts = id!.Split(OpenSubsonicServer.ApiIdSeparator);
        var toParse = id;
        if (apiIdParts.Length < 2)
        {
            Log.Warning("ApiKeyFromId: Invalid ApiKey [{Key}]", id);
        }
        else
        {
            toParse = apiIdParts[1];
        }

        return SafeParser.ToGuid(toParse);
    }

    /// <summary>
    ///     Get details about the software license.
    /// </summary>
    public async Task<ResponseModel> GetLicenseAsync(
        ApiRequest apiApiRequest,
        CancellationToken cancellationToken = default)
    {
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                DataPropertyName = "license",
                Data = new License(true,
                    (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerLicenseEmail) ??
                    ServiceUser.Instance.Value.Email,
                    DateTimeOffset.UtcNow.AddYears(50).ToXmlSchemaDateTimeFormat(),
                    DateTimeOffset.UtcNow.AddYears(50).ToXmlSchemaDateTimeFormat()
                )
            }
        };
    }

    /// <summary>
    ///     Returns information about shared media this user is allowed to manage.
    /// </summary>
    /// <param name="apiRequest">An API request containing the necessary details for authentication and filtering the request.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the asynchronous operation to complete.</param>
    /// <returns>A ResponseModel containing user information and the corresponding list of shares.</returns>
    public async Task<ResponseModel> GetSharesAsync(
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Share>();

        var dbSharesResult = await userService.UserSharesAsync(authResponse.UserInfo.Id, cancellationToken)
           .ConfigureAwait(false);
        foreach (var dbShare in dbSharesResult ?? [])
        {
            Child[] shareEntries = [];
            switch (dbShare.ShareTypeValue)
            {
                case ShareType.Song:
                    var song = await songService.GetAsync(dbShare.ShareId, cancellationToken).ConfigureAwait(false);
                    var userSong = await userService.UserSongAsync(authResponse.UserInfo.Id, song.Data!.ApiKey,
                        cancellationToken);
                    if (userSong != null)
                    {
                        shareEntries = [song.Data.ToApiChild(song.Data.Album, userSong)];
                    }

                    break;
                case ShareType.Album:
                    var album = await albumService.GetAsync(dbShare.ShareId, cancellationToken).ConfigureAwait(false);
                    var userSongsForAlbum = await userService.UserSongsForAlbumAsync(authResponse.UserInfo.Id, album.Data!.ApiKey, cancellationToken);
                    if (userSongsForAlbum != null)
                    {
                        shareEntries = album.Data.Songs.Select(ss =>
                                ss.ToApiChild(ss.Album, userSongsForAlbum.FirstOrDefault(x => x.SongId == ss.Id)))
                            .ToArray();
                    }

                    break;
                case ShareType.Playlist:
                    var playlist = await playlistService.GetAsync(dbShare.ShareId, cancellationToken)
                        .ConfigureAwait(false);
                    var userSongsForPlaylist = await userService.UserSongsForPlaylistAsync(authResponse.UserInfo.Id,
                        playlist.Data!.ApiKey, cancellationToken);
                    if (userSongsForPlaylist != null)
                    {
                        shareEntries = playlist.Data.Songs.Select(pls => pls.Song.ToApiChild(pls.Song.Album,
                            userSongsForPlaylist.FirstOrDefault(x => x.SongId == pls.Song.Id))).ToArray();
                    }

                    break;
            }

            data.Add(dbShare.ToApiShare(dbShare.ToUrl(await Configuration.Value), shareEntries));
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data.ToArray(),
                DataPropertyName = "shares",
                DataDetailPropertyName = apiRequest.IsXmlRequest ? string.Empty : "share"
            }
        };
    }

    /// <summary>
    ///     Creates a public URL that can be used by anyone to stream music.
    /// </summary>
    /// <param name="apiRequest">The API request containing authentication and other request-related details.</param>
    /// <param name="id">The unique identifier of the item to be shared (e.g., song, album, or playlist).</param>
    /// <param name="description">An optional description for the shared item.</param>
    /// <param name="expires">
    ///     The expiration timestamp (in milliseconds since UNIX epoch) for the share, or null if the share
    ///     does not expire.
    /// </param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A <see cref="ResponseModel" /> containing information about the created share, including success status and
    ///     data.
    /// </returns>
    public async Task<ResponseModel> CreateShareAsync(
        ApiRequest apiRequest,
        string id,
        string? description,
        long? expires,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        // The user must be authorized to share
        var user = await userProfileService.GetAsync(authResponse.UserInfo.Id, cancellationToken).ConfigureAwait(false);
        if (!user.Data?.CanShare() ?? false)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        var shareApiKey = ApiKeyFromId(id)!.Value;
        Child[] resultEntries;

        var dbShare = new dbModels.Share
        {
            UserId = user.Data!.Id,
            ShareId = 0,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
        if (IsApiIdForSong(id))
        {
            var song = await songService.GetByApiKeyAsync(shareApiKey, cancellationToken).ConfigureAwait(false);
            if (!song.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            dbShare.ShareType = SafeParser.ToNumber<int>(ShareType.Song);
            dbShare.ShareId = song.Data!.Id;
            var userSong =
                await userService.UserSongAsync(authResponse.UserInfo.Id, song.Data.ApiKey, cancellationToken);
            resultEntries = [song.Data.ToApiChild(song.Data.Album, userSong)];
        }
        else if (IsApiIdForAlbum(id))
        {
            var album = await albumService.GetByApiKeyAsync(shareApiKey, cancellationToken).ConfigureAwait(false);
            if (!album.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            dbShare.ShareType = SafeParser.ToNumber<int>(ShareType.Album);
            dbShare.ShareId = album.Data!.Id;
            var userSongsForAlbum =
                await userService.UserSongsForAlbumAsync(authResponse.UserInfo.Id, album.Data.ApiKey,
                    cancellationToken);
            resultEntries = album.Data.Songs.Select(song =>
                song.ToApiChild(song.Album, userSongsForAlbum?.FirstOrDefault(x => x.SongId == song.Id))).ToArray();
        }
        else if (IsApiIdForPlaylist(id))
        {
            var playlist = await playlistService.GetByApiKeyAsync(authResponse.UserInfo, shareApiKey, cancellationToken).ConfigureAwait(false);
            if (!playlist.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            dbShare.ShareType = SafeParser.ToNumber<int>(ShareType.Playlist);
            dbShare.ShareId = playlist.Data!.Id;
            var userSongsForPlaylist = await userService.UserSongsForPlaylistAsync(authResponse.UserInfo.Id,
                playlist.Data.ApiKey, cancellationToken);
            resultEntries = playlist.Data.Songs.Select(pls =>
                    pls.Song.ToApiChild(pls.Song.Album,
                        userSongsForPlaylist?.FirstOrDefault(x => x.SongId == pls.Song.Id)))
                .ToArray();
        }
        else
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty,
                    Error.RequiredParameterMissingError)
            };
        }

        dbShare.Description = description;
        dbShare.ExpiresAt = expires != null ? Instant.FromUnixTimeMilliseconds(expires.Value) : null;
        var addResult = await shareService.AddAsync(dbShare, cancellationToken).ConfigureAwait(false);
        var data = addResult.IsSuccess
            ? addResult.Data!.ToApiShare(addResult.Data!.ToUrl(await Configuration.Value), resultEntries)
            : null;

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                IsSuccess = data != null,
                Data = data,
                DataPropertyName = "shares",
                DataDetailPropertyName = apiRequest.IsXmlRequest ? string.Empty : "share"
            }
        };
    }

    /// <summary>
    ///     Updates the description and/or expiration date for an existing share.
    /// </summary>
    /// <param name="apiRequest">The API request with authentication and context information.</param>
    /// <param name="id">The unique identifier of the share to be updated.</param>
    /// <param name="description">An optional description to attach to the share.</param>
    /// <param name="expires">An optional expiration time for the share in Unix timestamp format.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A task representing the asynchronous operation, containing a response model with the result of the share
    ///     update.
    /// </returns>
    public async Task<ResponseModel> UpdateShareAsync(
        ApiRequest apiRequest,
        string id,
        string? description,
        long? expires,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        dbModels.Share? share = null;
        var apiKey = ApiKeyFromId(id);
        if (apiKey != null)
        {
            var shareResult = await shareService.GetByApiKeyAsync(apiKey.Value, cancellationToken).ConfigureAwait(false);
            share = shareResult.Data;
        }
        else if (int.TryParse(id, out var shareId))
        {
            var shareByIdResult = await shareService.GetAsync(shareId, cancellationToken).ConfigureAwait(false);
            share = shareByIdResult.Data;
        }

        if (share == null)
        {
            if (int.TryParse(id, out _))
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = true,
                    ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
                };
            }

            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        if (share != null)
        {
            share.Description = description;
            share.ExpiresAt = expires != null ? Instant.FromUnixTimeMilliseconds(expires.Value) : share.ExpiresAt;

            var updateResult = await shareService.UpdateAsync(share, cancellationToken).ConfigureAwait(false);
            notAuthorizedError =
                updateResult is
                {
                    IsSuccess: false, Type: OperationResponseType.Unauthorized or OperationResponseType.AccessDenied
                }
                    ? Error.UserNotAuthorizedError
                    : Error.InvalidApiKeyError;
            result = updateResult.IsSuccess;
        }

        if (!result && int.TryParse(id, out _))
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = true,
                ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
            };
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    /// <summary>
    ///     Deletes an existing share.
    /// </summary>
    /// <param name="id">The unique identifier of the shared resource to be deleted.</param>
    /// <param name="apiRequest">The API request containing authentication and other request details.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a <see cref="ResponseModel" />
    ///     indicating the success or failure of the operation.
    /// </returns>
    public async Task<ResponseModel> DeleteShareAsync(
        string id,
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null && int.TryParse(id, out var playlistId))
        {
            var playlistResult = await playlistService.GetAsync(playlistId, cancellationToken).ConfigureAwait(false);
            if (playlistResult.Data == null)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = true,
                    ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
                };
            }

            var deleteByIdResult = await playlistService.DeleteAsync(authResponse.UserInfo.Id, [playlistId], cancellationToken)
                .ConfigureAwait(false);
            if (!deleteByIdResult.Data)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = true,
                    ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
                };
            }

            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = deleteByIdResult.Data,
                ResponseData = await NewApiResponse(deleteByIdResult.Data, string.Empty, string.Empty,
                    deleteByIdResult.Data ? null : Error.InvalidApiKeyError)
            };
        }

        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        var shareResult = await shareService.GetByApiKeyAsync(apiKey.Value, cancellationToken).ConfigureAwait(false);

        if (shareResult.IsSuccess && shareResult.Data != null)
        {
            var deleteResult = await shareService
                .DeleteAsync(authResponse.UserInfo.Id, [shareResult.Data.Id], cancellationToken).ConfigureAwait(false);
            notAuthorizedError =
                deleteResult is
                {
                    IsSuccess: false, Type: OperationResponseType.Unauthorized or OperationResponseType.AccessDenied
                }
                    ? Error.UserNotAuthorizedError
                    : Error.InvalidApiKeyError;
            result = deleteResult.IsSuccess;
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    /// <summary>
    ///     Returns all playlists a user is allowed to play.
    /// </summary>
    public async Task<ResponseModel> GetPlaylistsAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Playlist>();
        Error? error = null;
        try
        {
            var playlistsResult = await playlistService.GetPlaylistsForUserAsync(authResponse.UserInfo.Id, cancellationToken);
            if (playlistsResult.IsSuccess)
            {
                data = playlistsResult.Data.Select(x => x.ToApiPlaylist(false)).ToList();
            }

            var dynamicPlaylists = await playlistService.DynamicListAsync(authResponse.UserInfo, new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            data.AddRange(dynamicPlaylists.Data.Select(x => x.ToApiPlaylist(false, true)));
        }
        catch (Exception e)
        {
            error = Error.GenericError($"Failed to get Playlist");
            Logger.Error(e, "Failed to get Playlists Request [{ApiResult}]", apiRequest);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Error = error,
                Data = data.ToArray(),
                DataPropertyName = "playlists",
                DataDetailPropertyName = "playlist"
            }
        };
    }

    public async Task<ResponseModel> UpdatePlaylistAsync(
        UpdatePlayListRequest updateRequest,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        var apiKey = ApiKeyFromId(updateRequest.PlaylistId);
        if (apiKey == null && int.TryParse(updateRequest.PlaylistId, out var playlistId))
        {
            var playlistResult = await playlistService.GetAsync(playlistId, cancellationToken).ConfigureAwait(false);
            apiKey = playlistResult.Data?.ApiKey;
        }

        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        // Handle song removal using PlaylistService
        if (updateRequest.SongIdToRemove?.Any() == true)
        {
            var songsToRemove = updateRequest.SongIdToRemove
                .Select(ApiKeyFromId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            if (songsToRemove.Any())
            {
                var removeResult = await playlistService.RemoveSongsFromPlaylistAsync(apiKey.Value, songsToRemove, cancellationToken).ConfigureAwait(false);
                if (!removeResult.IsSuccess)
                {
                    return new ResponseModel
                    {
                        UserInfo = UserInfo.BlankUserInfo,
                        IsSuccess = false,
                        ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                    };
                }
            }
        }

        // Handle song addition using PlaylistService
        if (updateRequest.SongIdToAdd?.Any() == true)
        {
            var songsToAdd = updateRequest.SongIdToAdd
                .Select(ApiKeyFromId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray();

            if (songsToAdd.Any())
            {
                var addResult = await playlistService.AddSongsToPlaylistAsync(apiKey.Value, songsToAdd, cancellationToken).ConfigureAwait(false);
                if (!addResult.IsSuccess)
                {
                    return new ResponseModel
                    {
                        UserInfo = UserInfo.BlankUserInfo,
                        IsSuccess = false,
                        ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                    };
                }
            }
        }

        // Handle metadata updates using PlaylistService
        if (updateRequest.Name != null || updateRequest.Comment != null || updateRequest.Public.HasValue)
        {
            var updateResult = await playlistService.UpdatePlaylistMetadataAsync(
                apiKey.Value,
                authResponse.UserInfo.Id,
                updateRequest.Name,
                updateRequest.Comment,
                updateRequest.Public,
                cancellationToken).ConfigureAwait(false);

            if (updateResult.IsSuccess)
            {
                result = true;
            }
            else if (updateResult.Type == OperationResponseType.AccessDenied)
            {
                notAuthorizedError = Error.UserNotAuthorizedError;
            }
        }
        else
        {
            result = true; // No metadata to update, consider successful
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    /// <summary>
    ///     Deletes a saved playlist.
    /// </summary>
    public async Task<ResponseModel> DeletePlaylistAsync(
        string id,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null && int.TryParse(id, out var playlistId))
        {
            var deleteByIdResult = await playlistService.DeleteAsync(authResponse.UserInfo.Id, [playlistId], cancellationToken)
                .ConfigureAwait(false);
            if (!deleteByIdResult.Data)
            {
                Logger.Warning("[{ServiceName}] Delete playlist by ID failed for {PlaylistId}.", nameof(OpenSubsonicApiService), playlistId);
            }

            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = true,
                ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
            };
        }

        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        var deleteResult = await playlistService.DeleteByApiKeyAsync(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
        Error? notAuthorizedError = null;

        if (!deleteResult.IsSuccess)
        {
            if (deleteResult.Messages?.Any(m => m.Contains("not authorized")) == true)
            {
                notAuthorizedError = Error.UserNotAuthorizedError;
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = deleteResult.Data,
            ResponseData = await NewApiResponse(deleteResult.Data, string.Empty, string.Empty,
                notAuthorizedError ?? (deleteResult.Data ? null : Error.InvalidApiKeyError))
        };
    }

    public async Task<ResponseModel> CreatePlaylistAsync(
        string? id,
        string? name,
        string[]? songId,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var playListId = string.Empty;
        var isCreatingPlaylist = id.Nullify() == null && name.Nullify() != null;

        if (isCreatingPlaylist)
        {
            // creating new with name and songs
            var songApiKeysForPlaylist = songId?
                .Where(x => x.Nullify() != null)
                .Select(ApiKeyFromId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray() ?? [];

            var createResult = await playlistService.CreatePlaylistAsync(name!, authResponse.UserInfo.Id, null, false, songApiKeysForPlaylist, returnPrefixedApiKey: true, cancellationToken);
            if (createResult.IsSuccess && createResult.Data != null)
            {
                playListId = createResult.Data;
            }
        }
        else if (!isCreatingPlaylist && id.Nullify() != null)
        {
            // Updating existing playlist - replace all songs
            playListId = id!;
            var apiKey = ApiKeyFromId(id);

            if (apiKey == null)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            // Get current playlist to clear songs
            var existingPlaylist = await playlistService.GetByApiKeyAsync(authResponse.UserInfo, apiKey.Value, cancellationToken);
            if (!existingPlaylist.IsSuccess || existingPlaylist.Data == null)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
                };
            }

            // Clear all songs and replace with new ones
            var currentSongApiKeys = existingPlaylist.Data.Songs.Select(ps => ps.Song.ApiKey).ToArray();
            if (currentSongApiKeys.Any())
            {
                await playlistService.RemoveSongsFromPlaylistAsync(apiKey.Value, currentSongApiKeys, cancellationToken);
            }

            // Add new songs if provided
            var songApiKeysForPlaylist = songId?
                .Where(x => x.Nullify() != null)
                .Select(ApiKeyFromId)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToArray() ?? [];

            if (songApiKeysForPlaylist.Any())
            {
                await playlistService.AddSongsToPlaylistAsync(apiKey.Value, songApiKeysForPlaylist, cancellationToken);
            }

            // Update name if provided
            if (name.Nullify() != null)
            {
                await playlistService.UpdatePlaylistMetadataAsync(
                    apiKey.Value,
                    authResponse.UserInfo.Id,
                    name,
                    null,
                    null,
                    cancellationToken);
            }
        }

        return await GetPlaylistAsync(playListId, apiRequest, cancellationToken);
    }

    /// <summary>
    ///     Returns a listing of files in a saved playlist.
    /// </summary>
    public async Task<ResponseModel> GetPlaylistAsync(
        string id,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            Logger.Warning("Invalid playlist id requested.");
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = authResponse.ResponseData with
                {
                    Error = Error.InvalidApiKeyError
                }
            };
        }

        Playlist? data = null;
        var playlistResult = await playlistService.GetByApiKeyAsync(authResponse.UserInfo, apiKey.Value, cancellationToken);
        var playlist = playlistResult.Data;

        if (playlist != null)
        {
            data = playlist.ToApiPlaylist(false);
            var playlistSongsResult = await playlistService.SongsForPlaylistAsync(playlist.ApiKey,
                authResponse.UserInfo,
                new PagedRequest
                {
                    PageSize = (await Configuration.Value).GetValue<short?>(SettingRegistry.PlaylistMaximumAllowedPageSize) ?? 1000
                },
                cancellationToken);
            if (playlistSongsResult.IsSuccess)
            {
                data.SongCount = SafeParser.ToNumber<short>(playlistSongsResult.Data.Count());
                await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var dbSongsForPlaylist = await scopedContext.Songs
                    .Where(s => playlistSongsResult.Data.Select(ps => ps.ApiKey).Contains(s.ApiKey))
                    .Include(s => s.Album).ThenInclude(x => x.Artist)
                    .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                data.Entry = dbSongsForPlaylist.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Error = playlistResult.IsSuccess ? null : Error.InvalidApiKeyError,
                IsSuccess = data != null,
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "playlist"
            }
        };
    }

    public async Task<ResponseModel> GetAlbumListAsync(
        GetAlbumListRequest albumListRequest,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };

        var result = await albumService.GetAlbumListAsync(albumListRequest, authResponse.UserInfo.Id, cancellationToken);

        Error? error = null;
        if (!result.IsSuccess)
        {
            error = Error.GenericError($"Failed to get AlbumList");
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = result.Data.totalCount,
            ResponseData = await DefaultApiResponse() with
            {
                Error = error,
                Data = result.Data.albums,
                DataPropertyName = "albumList",
                DataDetailPropertyName = "album"
            }
        };
    }

    public async Task<ResponseModel> GetAlbumList2Async(
        GetAlbumListRequest albumListRequest,
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var result = await albumService.GetAlbumList2Async(albumListRequest, authResponse.UserInfo.Id, cancellationToken);

        Error? error = null;
        if (!result.IsSuccess)
        {
            error = Error.GenericError($"Failed to get AlbumList2");
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = result.Data.totalCount,
            ResponseData = await DefaultApiResponse() with
            {
                Error = error,
                Data = result.Data.albums,
                DataPropertyName = "albumList2",
                DataDetailPropertyName = "album"
            }
        };
    }

    public async Task<ResponseModel> GetSongAsync(string apiKey, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var songId = ApiKeyFromId(apiKey) ?? Guid.Empty;
        if (songId == Guid.Empty)
        {
            Logger.Warning("Invalid song id requested.");
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = authResponse.ResponseData with
                {
                    Error = Error.InvalidApiKeyError
                }
            };
        }

        var songResponse = await songService.GetByApiKeyAsync(songId, cancellationToken);
        if (!songResponse.IsSuccess)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = authResponse.ResponseData with
                {
                    Error = Error.InvalidApiKeyError
                }
            };
        }

        var userSong = await userService.UserSongAsync(authResponse.UserInfo.Id, songId, cancellationToken);
        return new ResponseModel
        {
            IsSuccess = songResponse.IsSuccess,
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = songResponse.Data?.ToApiChild(songResponse.Data.Album, userSong),
                DataPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetAlbumAsync(string apiId, ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        AlbumId3WithSongs? data = null;

        var apiKey = ApiKeyFromId(apiId);
        if (apiKey != null)
        {
            var albumResponse = await albumService.GetByApiKeyAsync(apiKey.Value, cancellationToken);
            if (!albumResponse.IsSuccess)
            {
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    ResponseData = authResponse.ResponseData with
                    {
                        Error = Error.InvalidApiKeyError
                    }
                };
            }


            var album = albumResponse.Data!;
            var userAlbum = await userService.UserAlbumAsync(authResponse.UserInfo.Id, apiKey.Value, cancellationToken);
            var userSongsForAlbum =
                await userService.UserSongsForAlbumAsync(authResponse.UserInfo.Id, apiKey.Value, cancellationToken) ??
                [];
            data = new AlbumId3WithSongs
            {
                AlbumDate = album.ReleaseDate.ToItemDate(),
                Artist = album.Artist.Name,
                ArtistId = album.Artist.ToApiKey(),
                Artists = album.ContributingArtists(),
                CoverArt = album.ToApiKey(),
                Created = album.CreatedAt.ToString(),
                DiscTitles = [],
                DisplayArtist = album.Artist.Name,
                Duration = album.Duration.ToSeconds(),
                Genre = album.Genres?.ToCsv(),
                Genres = album.Genres?.Select(x => new ItemGenre(x)).ToArray() ?? [],
                Id = album.ToApiKey(),
                IsCompilation = album.IsCompilation,
                Moods = album.Moods ?? [],
                MusicBrainzId = album.MusicBrainzId?.ToString(),
                Name = album.Name,
                OriginalAlbumDate = album.OriginalReleaseDate?.ToItemDate() ?? album.ReleaseDate.ToItemDate(),
                OriginalReleaseDate = album.OriginalReleaseDate?.ToItemDate() ?? album.ReleaseDate.ToItemDate(),
                Parent = album.ToApiKey(),
                PlayCount = album.PlayedCount,
                Played = album.LastPlayedAt.ToString(),
                RecordLabels = album.RecordLabels(),
                Song = album.Songs.OrderBy(x => x.SortOrder)
                    .Select(x => x.ToApiChild(album, userSongsForAlbum.FirstOrDefault(us => us.SongId == x.Id)))
                    .ToArray(),
                SongCount = album.SongCount ?? 0,
                SortName = album.SortName,
                Starred = userAlbum?.StarredAt?.ToString(),
                Title = album.Name,
                UserRating = userAlbum?.Rating,
                Year = album.ReleaseDate.Year
            };
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "album"
            }
        };
    }

    /// <summary>
    ///     Returns all genres.
    /// </summary>
    public async Task<ResponseModel> GetGenresAsync(ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Genre>();

        var genresResult = await albumService.GetGenresAsync(cancellationToken);
        if (genresResult.IsSuccess)
        {
            var allGenresSet = new HashSet<string>();
            var genreCounts = genresResult.Data;

            // Create unique sorted genres
            foreach (var genre in genreCounts.Keys)
            {
                allGenresSet.Add(genre);
            }

            var allGenres = allGenresSet.OrderBy(g => g).ToArray();

            foreach (var genre in allGenres)
            {
                var genreNormalized = genre.ToNormalizedString() ?? genre;
                if (data.All(x => x.ValueNormalized != genreNormalized))
                {
                    var counts = genreCounts.TryGetValue(genre, out var count) ? count : (0, 0);
                    data.Add(new Genre
                    {
                        Value = genre.CleanString() ?? genre,
                        SongCount = counts.Item1,
                        AlbumCount = counts.Item2
                    }
                    );
                }
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = "genres",
                DataDetailPropertyName = "genre"
            }
        };
    }

    /// <summary>
    ///     Returns the avatar (personal image) for a user.
    /// </summary>
    public async Task<ResponseModel> GetAvatarAsync(
        string username,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        byte[]? avatarBytes = null;
        Error? error = null;
        try
        {
            avatarBytes = await CacheManager.GetAsync($"urn:openSubsonic:avatar:{username}", async () =>
            {
                using (Operation.At(LogEventLevel.Debug).Time("GetAvatarAsync: [{Username}]", username))
                {
                    var userLibraryResult = await libraryService.GetUserImagesLibraryAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (userLibraryResult.IsSuccess)
                    {
                        var userAvatarFilename = authResponse.UserInfo.ToAvatarFileName(userLibraryResult.Data.Path);
                        if (File.Exists(userAvatarFilename))
                        {
                            return await File.ReadAllBytesAsync(userAvatarFilename, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }

                    return null;
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            error = Error.GenericError($"Failed to get avatar");
            Logger.Error(e, "Failed to get avatar for user.");
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Error = error,
                Data = avatarBytes ?? defaultImages.UserAvatarBytes,
                DataPropertyName = string.Empty,
                DataDetailPropertyName = string.Empty
            }
        };
    }

    private static string GenerateImageCacheKeyForApiId(string apiId, ImageSize size)
    {
        return $"urn:openSubsonic:imageForApikey:{apiId}:{size}";
    }

    /// <summary>
    ///     Returns an artist, album, or song art image.
    /// </summary>
    public async Task<ResponseModel> GetImageForApiKeyId(
        string apiId,
        string? size,
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var isUserImageRequest = IsApiIdForUser(apiId);
        // If a user image request don't auth as it's used in the UI in the header (before auth'ed).
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess && !isUserImageRequest)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? error = null;
        var isForPlaylist = IsApiIdForDynamicPlaylist(apiId) || IsApiIdForPlaylist(apiId);

        var badEtag = Instant.MinValue.ToEtag();
        var sizeValue = size.Nullify() == null ? ImageSize.Large : SafeParser.ToEnum<ImageSize>(size);

        ImageBytesAndEtag imageBytesAndEtag;

        using (Operation.At(LogEventLevel.Debug)
                   .Time("GetImageForApiKeyId: [{ApiId}] Size [{Size}]", apiId, sizeValue))
        {
            var doCheckResize = true;
            byte[]? result = null;
            var eTag = string.Empty;
            try
            {
                var apiKey = ApiKeyFromId(apiId);
                if (apiKey == null)
                {
                    imageBytesAndEtag = new ImageBytesAndEtag(null, null);
                }
                else if (IsApiIdForArtist(apiId))
                {
                    // Artist service handles resizing and caching
                    var artistImageBytesAndEtag = await artistService.GetArtistImageBytesAndEtagAsync(apiKey, size, cancellationToken);
                    result = artistImageBytesAndEtag.Bytes ?? defaultImages.ArtistBytes;
                    eTag = artistImageBytesAndEtag.Etag ?? badEtag;
                    imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                    // Only skip resize if we got actual image bytes from service (not default)
                    doCheckResize = artistImageBytesAndEtag.Bytes == null;
                }
                else if (IsApiIdForDynamicPlaylist(apiId))
                {
                    // Dynamic playlists don't exist in the database they are created on demand from configured json files.
                    var dynamicPlaylist = await libraryService
                        .GetDynamicPlaylistAsync(apiKey.Value, cancellationToken).ConfigureAwait(false);
                    var playlistImageFileInfo = new FileInfo(dynamicPlaylist.Data?.ImageFileName ?? string.Empty);
                    if (playlistImageFileInfo.Exists)
                    {
                        result = await File.ReadAllBytesAsync(playlistImageFileInfo.FullName, cancellationToken)
                            .ConfigureAwait(false);
                        eTag = playlistImageFileInfo.LastWriteTimeUtc.ToEtag();
                    }
                    else
                    {
                        result = defaultImages.PlaylistImageBytes;
                        eTag = badEtag;
                    }
                    imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                }
                else if (IsApiIdForPlaylist(apiId))
                {
                    var playlistImageBytesAndEtag = await playlistService.GetPlaylistImageBytesAndEtagAsync(apiKey.Value, size, cancellationToken).ConfigureAwait(false);
                    result = playlistImageBytesAndEtag.Bytes ?? defaultImages.PlaylistImageBytes;
                    eTag = playlistImageBytesAndEtag.Etag ?? badEtag;
                    imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                }
                else if (IsApiIdForChart(apiId))
                {
                    var chartImageBytesAndEtab = await chartService.GetChartImageBytesAndEtagAsync(apiKey.Value, size, cancellationToken);
                    result = chartImageBytesAndEtab.Bytes ?? defaultImages.ChartImageBytes;
                    eTag = chartImageBytesAndEtab.Etag ?? badEtag;
                    imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                }
                else if (IsApiIdForPodcastChannel(apiId))
                {
                    var channelId = PodcastChannelIdFromId(apiId);
                    if (channelId.HasValue)
                    {
                        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                        var channel = await context.PodcastChannels
                            .FirstOrDefaultAsync(x => x.Id == channelId.Value, cancellationToken)
                            .ConfigureAwait(false);

                        if (channel != null && !string.IsNullOrEmpty(channel.CoverArtLocalPath))
                        {
                            var podcastLibraryResult = await libraryService.GetPodcastLibraryAsync(cancellationToken).ConfigureAwait(false);
                            var podcastLibrary = podcastLibraryResult.Data;
                            if (podcastLibrary != null)
                            {
                                var coverArtPath = Path.Combine(podcastLibrary.Path, channel.CoverArtLocalPath);
                                var coverArtFileInfo = new FileInfo(coverArtPath);
                                if (coverArtFileInfo.Exists)
                                {
                                    result = await File.ReadAllBytesAsync(coverArtFileInfo.FullName, cancellationToken).ConfigureAwait(false);
                                    eTag = coverArtFileInfo.LastWriteTimeUtc.ToEtag();
                                }
                            }
                        }

                        if (result == null)
                        {
                            result = defaultImages.PlaylistImageBytes;
                            eTag = badEtag;
                        }
                        imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                    }
                    else
                    {
                        imageBytesAndEtag = new ImageBytesAndEtag(null, null);
                    }
                }
                else if (isUserImageRequest)
                {
                    var userResult = await userProfileService.GetByApiKeyAsync(apiKey.Value, cancellationToken)
                        .ConfigureAwait(false);
                    var userImageLibrary = await libraryService.GetUserImagesLibraryAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var userImageFileName = userResult.Data?.ToAvatarFileName(userImageLibrary.Data.Path);
                    var userImageFileInfo = new FileInfo(userImageFileName ?? string.Empty);
                    if (userImageFileInfo.Exists)
                    {
                        result = await File.ReadAllBytesAsync(userImageFileInfo.FullName, cancellationToken)
                            .ConfigureAwait(false);
                        eTag = userImageFileInfo.LastWriteTimeUtc.ToEtag();
                    }
                    else
                    {
                        result = defaultImages.UserAvatarBytes;
                        eTag = badEtag;
                    }
                    imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                }
                else if (IsApiIdForSong(apiId) || IsApiIdForAlbum(apiId))
                {
                    if (IsApiIdForSong(apiId))
                    {
                        // If it's a song get the album ApiKey and proceed to get Album cover
                        var songInfo = await DatabaseSongIdsInfoForSongApiKey(apiKey.Value, cancellationToken)
                            .ConfigureAwait(false);
                        if (songInfo != null)
                        {
                            apiKey = songInfo.AlbumApiKey;
                        }
                    }

                    // Album service handles resizing and caching
                    var albumImageBytesAndEtag = await albumService.GetAlbumImageBytesAndEtagAsync(apiKey, size, cancellationToken);
                    result = albumImageBytesAndEtag.Bytes ?? defaultImages.AlbumCoverBytes;
                    eTag = albumImageBytesAndEtag.Etag ?? badEtag;
                    imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                    // Only skip resize if we got actual image bytes from service (not default)
                    doCheckResize = albumImageBytesAndEtag.Bytes == null;
                }
                else
                {
                    imageBytesAndEtag = new ImageBytesAndEtag(null, null);
                }

                result = imageBytesAndEtag.Bytes;
                eTag = imageBytesAndEtag.Etag ?? badEtag;

                if (result != null && !isForPlaylist && doCheckResize)
                {
                    if (sizeValue != ImageSize.Large)
                    {
                        var sizeParsedToInt = SafeParser.ToNumber<int>(size);
                        if (sizeParsedToInt > 0)
                        {
                            result = ImageConvertor.ResizeImageIfNeeded(result,
                                sizeParsedToInt,
                                sizeParsedToInt, isUserImageRequest);
                            eTag = HashHelper.CreateSha256(eTag + sizeParsedToInt);
                        }
                        else
                        {
                            switch (sizeValue)
                            {
                                case ImageSize.Thumbnail:
                                    var thumbnailSize = (await Configuration.Value).GetValue<int?>(SettingRegistry.ImagingThumbnailSize) ?? SafeParser.ToNumber<int>(ImageSize.Thumbnail);
                                    result = ImageConvertor.ResizeImageIfNeeded(result,
                                        thumbnailSize,
                                        thumbnailSize,
                                        isUserImageRequest);
                                    eTag = HashHelper.CreateSha256(eTag + nameof(ImageSize.Thumbnail));
                                    break;

                                case ImageSize.Small:
                                    var smallSize = (await Configuration.Value).GetValue<int?>(SettingRegistry.ImagingSmallSize) ??
                                                    throw new Exception($"Invalid configuration [{SettingRegistry.ImagingSmallSize}] not found.");
                                    result = ImageConvertor.ResizeImageIfNeeded(result,
                                        smallSize,
                                        smallSize,
                                        isUserImageRequest);
                                    eTag = HashHelper.CreateSha256(eTag + nameof(ImageSize.Small));
                                    break;

                                case ImageSize.Medium:
                                    var mediumSize =
                                        (await Configuration.Value).GetValue<int?>(
                                            SettingRegistry.ImagingMediumSize) ??
                                        throw new Exception(
                                            $"Invalid configuration [{SettingRegistry.ImagingMediumSize}] not found.");
                                    result = ImageConvertor.ResizeImageIfNeeded(result,
                                        mediumSize,
                                        mediumSize,
                                        isUserImageRequest);
                                    eTag = HashHelper.CreateSha256(eTag + nameof(ImageSize.Medium));
                                    break;
                            }
                        }

                        imageBytesAndEtag = new ImageBytesAndEtag(result, eTag);
                    }
                }
            }
            catch (Exception e)
            {
                error = Error.GenericError("Failed to get image for ApiKey.");
                Logger.Error(e, "Failed to get cover image for requested resource.");
                imageBytesAndEtag = new ImageBytesAndEtag(null, null);
            }
        }

        return new ResponseModel
        {
            ApiKeyId = apiId,
            IsSuccess = imageBytesAndEtag.Bytes != null,
            UserInfo = authResponse.UserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Error = error,
                Data = imageBytesAndEtag.Bytes,
                DataPropertyName = string.Empty,
                DataDetailPropertyName = string.Empty,
                Etag = imageBytesAndEtag.Etag,
                ContentType = isForPlaylist ? "image/gif" : "image/jpeg"
            }
        };
    }

    /// <summary>
    ///     List the OpenSubsonic extensions supported by this server.
    ///     <remarks>Unlike all other APIs getOpenSubsonicExtensions must be publicly accessible.</remarks>
    /// </summary>
    public async Task<ResponseModel> GetOpenSubsonicExtensionsAsync(
        ApiRequest apiApiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
        };
        var data = new List<OpenSubsonicExtension>
        {
            // Custom extensions added for Melodee
            new("melodeeExtensions", [1]),
            // Add support for POST request to the API (application/x-www-form-urlencoded).
            new("apiKeyAuthentication", [1]),
            // Add support for POST request to the API (application/x-www-form-urlencoded).
            new("formPost", [1]),
            // add support for synchronized lyrics, multiple languages, and retrieval by song ID
            new("songLyrics", [1]),
            // Add support for start offset for transcoding.
            new("transcodeOffset", [1])
        };
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = "openSubsonicExtensions"
            }
        };
    }

    public async Task<ResponseModel> StartScanAsync(ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        try
        {
            await scheduler.TriggerJob(JobKeyRegistry.LibraryProcessJobJobKey, cancellationToken);
        }
        catch (JobPersistenceException ex)
        {
            Logger.Warning(ex, "[{ServiceName}] Library process job missing; skipping scan trigger.", nameof(OpenSubsonicApiService));
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = new ScanStatus(true, 0),
                DataPropertyName = "scanStatus"
            }
        };
    }

    public async Task<ResponseModel> GetScanStatusAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var executingJobs = await scheduler.GetCurrentlyExecutingJobs(cancellationToken);
        var libraryProcessJob = executingJobs.FirstOrDefault(x => Equals(x.JobDetail.Key, JobKeyRegistry.LibraryProcessJobJobKey));
        Error? error = null;
        var data = new ScanStatus(false, 0);
        try
        {
            if (libraryProcessJob != null)
            {
                var dataMap = libraryProcessJob.JobDetail.JobDataMap;
                if (dataMap.ContainsKey(JobMapNameRegistry.ScanStatus) && dataMap.ContainsKey(JobMapNameRegistry.Count))
                {
                    data = new ScanStatus(
                        dataMap.GetString(JobMapNameRegistry.ScanStatus) == nameof(Enums.ScanStatus.InProcess),
                        dataMap.GetIntValue(JobMapNameRegistry.Count));
                }
            }
        }
        catch (Exception e)
        {
            error = Error.GenericError($"Failed to get Scan Status");
            Logger.Error(e, "Attempting to get Scan Status");
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Error = error,
                Data = data,
                DataPropertyName = "scanStatus"
            }
        };
    }

    /// <summary>
    ///     Test connectivity with the server.
    ///     <remarks>
    ///         This method does NOT do authentication as its only purpose is for a 'test' for the consumer to get a result
    ///         from the server.
    ///     </remarks>
    /// </summary>
    public async Task<ResponseModel> PingAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
    {
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
        };
    }

    public async Task<ResponseModel> AuthenticateSubsonicApiAsync(ApiRequest apiRequest, CancellationToken cancellationToken = default)
    {
        if (!apiRequest.RequiresAuthentication)
        {
            var user = apiRequest.Username == null
                ? null
                : await userProfileService.GetByUsernameAsync(apiRequest.Username, cancellationToken).ConfigureAwait(false);

            var userInfo = user?.Data?.ToUserInfo() ?? UserInfo.BlankUserInfo;

            Logger.Debug("[{MethodName}] Authentication bypassed (cookie auth). Username: {Username}, UserInfo: {UserInfo}, Roles: {Roles}",
                nameof(AuthenticateSubsonicApiAsync),
                apiRequest.Username ?? "null",
                userInfo.Id,
                string.Join(", ", userInfo.Roles ?? []));

            return new ResponseModel
            {
                UserInfo = userInfo,
                ResponseData = await NewApiResponse(true, string.Empty, string.Empty)
            };
        }

        if (apiRequest.Username?.Nullify() == null ||
            (apiRequest.Password?.Nullify() == null &&
             apiRequest.Token?.Nullify() == null))
        {
            Logger.Warning("[{MethodName}] [{ApiRequest}] is invalid",
                nameof(AuthenticateSubsonicApiAsync),
                apiRequest.ToString());
            return new ResponseModel
            {
                UserInfo = new UserInfo(0, Guid.Empty, string.Empty, string.Empty, string.Empty, "UTC"),
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
            };
        }

        using (Operation.At(LogEventLevel.Debug)
                   .Time("AuthenticateSubsonicApiAsync: username [{Username}]", apiRequest.Username))
        {
            try
            {
                OperationResult<Data.Models.User?> loginResult;

                if (apiRequest.Jwt.Nullify() != null)
                {
                    // JWT-based authentication (Navidrome semantics): parse token and map to user
                    // Prefer ApiKey (sid) -> user lookup, else fallback to username (sub/name)
                    try
                    {
                        var parts = apiRequest.Jwt!.Split('.');
                        if (parts.Length < 2)
                        {
                            return new ResponseModel
                            {
                                UserInfo = UserInfo.BlankUserInfo,
                                IsSuccess = false,
                                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
                            };
                        }

                        static byte[] FromBase64Url(string s)
                        {
                            s = s.Replace('-', '+').Replace('_', '/');
                            switch (s.Length % 4)
                            {
                                case 2: s += "=="; break;
                                case 3: s += "="; break;
                            }
                            return Convert.FromBase64String(s);
                        }

                        var payloadJson = System.Text.Encoding.UTF8.GetString(FromBase64Url(parts[1]));
                        using var payloadDoc = System.Text.Json.JsonDocument.Parse(payloadJson);
                        var root = payloadDoc.RootElement;

                        string? sid = null;
                        string? username = null;

                        // Try common claim names first
                        if (root.TryGetProperty("sid", out var sidEl)) sid = sidEl.GetString();
                        if (sid == null && root.TryGetProperty("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/sid", out var sid2)) sid = sid2.GetString();

                        if (root.TryGetProperty("name", out var nameEl)) username = nameEl.GetString();
                        if (string.IsNullOrWhiteSpace(username) && root.TryGetProperty("sub", out var subEl)) username = subEl.GetString();
                        if (string.IsNullOrWhiteSpace(username) && root.TryGetProperty("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", out var name2)) username = name2.GetString();

                        OperationResult<Data.Models.User?> jwtUserResult;
                        if (Guid.TryParse(sid, out var apiKeyGuid) && apiKeyGuid != Guid.Empty)
                        {
                            jwtUserResult = await userProfileService.GetByApiKeyAsync(apiKeyGuid, cancellationToken).ConfigureAwait(false);
                        }
                        else if (!string.IsNullOrWhiteSpace(username))
                        {
                            jwtUserResult = await userProfileService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            return new ResponseModel
                            {
                                UserInfo = UserInfo.BlankUserInfo,
                                IsSuccess = false,
                                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
                            };
                        }

                        return new ResponseModel
                        {
                            UserInfo = jwtUserResult.Data?.ToUserInfo() ?? UserInfo.BlankUserInfo,
                            IsSuccess = jwtUserResult.IsSuccess,
                            ResponseData = await NewApiResponse(jwtUserResult.IsSuccess, string.Empty, string.Empty, jwtUserResult.IsSuccess ? null : Error.AuthError)
                        };
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "JWT authentication failed.");
                        return new ResponseModel
                        {
                            UserInfo = UserInfo.BlankUserInfo,
                            IsSuccess = false,
                            ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
                        };
                    }
                }

                if (apiRequest.Token?.Nullify() != null && apiRequest.Salt?.Nullify() != null)
                {
                    // Use existing token validation method
                    loginResult = await userAuthenticationService.ValidateTokenAsync(apiRequest.Username, apiRequest.Token, apiRequest.Salt, cancellationToken);
                }
                else
                {
                    // Use existing password authentication
                    var password = apiRequest.Password;
                    if (apiRequest.Password?.StartsWith("enc:", StringComparison.Ordinal) ?? false)
                    {
                        password = password?.FromHexString();
                    }

                    loginResult = await userAuthenticationService.LoginUserByUsernameAsync(apiRequest.Username, password, cancellationToken);
                }

                return new ResponseModel
                {
                    UserInfo = loginResult.Data?.ToUserInfo() ?? UserInfo.BlankUserInfo,
                    IsSuccess = loginResult.IsSuccess,
                    ResponseData = await NewApiResponse(loginResult.IsSuccess, string.Empty, string.Empty, loginResult.IsSuccess ? null : Error.AuthError)
                };
            }
            catch (Exception e)
            {
                Logger.Error(e, "Error authenticating user.");
                return new ResponseModel
                {
                    UserInfo = UserInfo.BlankUserInfo,
                    IsSuccess = false,
                    ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
                };
            }
        }
    }

    private Task<ApiResponse> DefaultApiResponse()
    {
        return NewApiResponse(true, string.Empty, string.Empty);
    }

    public async Task<ApiResponse> NewApiResponse(bool isOk, string dataPropertyName, string dataDetailPropertyName,
        Error? error = null, object? data = null)
    {
        return new ApiResponse
        {
            IsSuccess = isOk,
            Version =
                (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerSupportedVersion) ??
                throw new InvalidOperationException(),
            Type = (await Configuration.Value).GetValue<string>(SettingRegistry.OpenSubsonicServerType) ??
                   throw new InvalidOperationException(),
            ServerVersion = typeof(OpenSubsonicApiService).Assembly.GetName().Version?.ToString() ?? "1.2.0.0",
            Error = error,
            Data = data,
            DataDetailPropertyName = dataDetailPropertyName,
            DataPropertyName = dataPropertyName
        };
    }

    public async Task<ResponseModel> GetPlayQueueAsync(ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = await userQueueService.GetPlayQueueForUserAsync(apiRequest.Username!, cancellationToken);
        data ??= new PlayQueue
        {
            Current = 0,
            Position = 0,
            ChangedBy = apiRequest.Username ?? string.Empty,
            Changed = DateTimeOffset.UtcNow.ToXmlSchemaDateTimeFormat(),
            Username = apiRequest.Username ?? string.Empty,
            Entry = []
        };

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = authResponse.ResponseData with
            {
                Data = data,
                DataPropertyName = "playQueue"
            }
        };
    }

    public async Task<ResponseModel> SavePlayQueueAsync(string[]? apiIds, string? currentApiId, double? position,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var result = await userQueueService.SavePlayQueueForUserAsync(
            apiRequest.Username!,
            apiIds,
            currentApiId,
            position,
            apiRequest.ApiRequestPlayer.Client,
            cancellationToken);

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty, result ? null : Error.AuthError)
        };
    }

    public async Task<ResponseModel> CreateUserAsync(CreateUserRequest request, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var requirePrivateCode = (await Configuration.Value).GetValue<string>(SettingRegistry.RegisterPrivateCode);
        if (requirePrivateCode.Nullify() != null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty,
                    new Error(10, "Private code is configured. User registration must be done via the server."))
            };
        }

        var registerResult = await userProfileService
            .RegisterAsync(request.Username, request.Email, request.Password, null, cancellationToken)
            .ConfigureAwait(false);
        var result = registerResult.IsSuccess;
        if (!result)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = result,
                ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                    new Error(10, "User creation failed."))
            };
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty, result ? null : Error.AuthError)
        };
    }

    public async Task<ResponseModel> ScrobbleAsync(string[] ids, double[]? times, bool? submission,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        if (times?.Length > 0 && times.Length != ids.Length)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty,
                    Error.GenericError("Wrong number of timestamps."))
            };
        }

        await scrobbleService.InitializeAsync(await Configuration.Value, cancellationToken).ConfigureAwait(false);

        // If not provided then default to this is a "submission" versus a "now playing" notification.
        var isSubmission = submission ?? true;

        if (!isSubmission)
        {
            foreach (var idAndIndex in ids.Select((id, index) => new { id, index }))
            {
                if (IsPodcastEpisodeId(idAndIndex.id))
                {
                    var episodeId = ParsePodcastEpisodeIdFromApiId(idAndIndex.id);
                    if (episodeId.HasValue)
                    {
                        await podcastPlaybackService.NowPlayingAsync(
                            authResponse.UserInfo.Id,
                            episodeId.Value,
                            times?.Length > idAndIndex.index ? (int?)times[idAndIndex.index] : null,
                            apiRequest.ApiRequestPlayer?.Client,
                            apiRequest.ApiRequestPlayer?.UserAgent,
                            apiRequest.IpAddress,
                            cancellationToken
                        ).ConfigureAwait(false);
                    }
                }
                else
                {
                    await scrobbleService.NowPlaying(authResponse.UserInfo, ApiKeyFromId(idAndIndex.id) ?? Guid.Empty,
                        times?.Length > idAndIndex.index ? times[idAndIndex.index] : null,
                        apiRequest.ApiRequestPlayer?.Client ?? string.Empty,
                        apiRequest.ApiRequestPlayer?.UserAgent,
                        apiRequest.IpAddress,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        else
        {
            foreach (var idAndIndex in ids.Select((id, index) => new { id, index }))
            {
                if (IsPodcastEpisodeId(idAndIndex.id))
                {
                    var episodeId = ParsePodcastEpisodeIdFromApiId(idAndIndex.id);
                    if (episodeId.HasValue)
                    {
                        await podcastPlaybackService.ScrobbleAsync(
                            authResponse.UserInfo.Id,
                            episodeId.Value,
                            times?.Length > idAndIndex.index ? (int?)times[idAndIndex.index] : null,
                            apiRequest.ApiRequestPlayer?.Client,
                            apiRequest.ApiRequestPlayer?.UserAgent,
                            apiRequest.IpAddress,
                            cancellationToken
                        ).ConfigureAwait(false);
                    }
                }
                else
                {
                    var id = ApiKeyFromId(idAndIndex.id) ?? Guid.Empty;
                    var uniqueId = SafeParser.Hash(authResponse.UserInfo.ApiKey.ToString(), id.ToString());
                    var nowPlayingInfo =
                        (await scrobbleService.GetNowPlaying(cancellationToken).ConfigureAwait(false)).Data
                        .FirstOrDefault(x => x.UniqueId == uniqueId);
                    if (nowPlayingInfo != null)
                    {
                        await scrobbleService.Scrobble(authResponse.UserInfo,
                                id,
                                false,
                                apiRequest.ApiRequestPlayer?.Client ?? string.Empty,
                                apiRequest.ApiRequestPlayer?.UserAgent,
                                apiRequest.IpAddress,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        Logger.Debug("Scrobble: Ignoring duplicate scrobble submission for [{UniqueId}]", uniqueId);
                    }
                }
            }
        }

        var result = true;
        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty, result ? null : Error.AuthError)
        };
    }

    /// <summary>
    /// Get streaming descriptor for song - memory-efficient approach
    /// </summary>
    public async Task<OperationResult<StreamingDescriptor>> GetStreamingDescriptorAsync(StreamRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return new OperationResult<StreamingDescriptor>("Authentication failed")
            {
                Type = OperationResponseType.Unauthorized,
                Data = null!
            };
        }

        if (request.IsDownloadingRequest)
        {
            var isDownloadingEnabled =
                (await Configuration.Value).GetValue<bool?>(SettingRegistry.SystemIsDownloadingEnabled) ?? false;
            if (!isDownloadingEnabled)
            {
                Logger.Warning("[{ServiceName}] Downloading is disabled [{SettingName}].",
                    nameof(OpenSubsonicApiService),
                    SettingRegistry.SystemIsDownloadingEnabled);
                return new OperationResult<StreamingDescriptor>("Downloading is disabled")
                {
                    Type = OperationResponseType.AccessDenied,
                    Data = null!
                };
            }
        }

        if (request is { IsDownloadingRequest: false, TimeOffset: not null })
        {
            Logger.Warning("[{ServiceName}] Stream request has TimeOffset.",
                nameof(OpenSubsonicApiService));
            return new OperationResult<StreamingDescriptor>("TimeOffset not supported for streaming")
            {
                Type = OperationResponseType.NotImplementedOrDisabled,
                Data = null!
            };
        }

        var apiKey = ApiKeyFromId(request.Id);
        if (apiKey == null)
        {
            Logger.Warning("[{ServiceName}] Invalid song ID requested.", nameof(OpenSubsonicApiService));
            return new OperationResult<StreamingDescriptor>("Invalid song ID")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        // Use proper range header parsing
        var rangeHeader = apiRequest.RequestHeaders.FirstOrDefault(x => x.Key.Equals("Range", StringComparison.OrdinalIgnoreCase))?.Value;

        // Use new streaming descriptor approach
        var descriptorResult = await songService.GetStreamingDescriptorAsync(
            authResponse.UserInfo,
            apiKey.Value,
            rangeHeader,
            request.IsDownloadingRequest,
            cancellationToken);

        if (!descriptorResult.IsSuccess || descriptorResult.Data == null)
        {
            Logger.Warning("[{ServiceName}] Failed to get streaming descriptor for song [{ApiKey}]: {Message}",
                nameof(OpenSubsonicApiService), apiKey.Value, descriptorResult.Messages?.FirstOrDefault());
            return new OperationResult<StreamingDescriptor>("Failed to get song stream")
            {
                Type = OperationResponseType.Error,
                Data = null!
            };
        }

        // Send stream event for scrobbling
        await bus.SendLocal(new UserStreamEvent(apiRequest, request)).ConfigureAwait(false);

        return descriptorResult;
    }

    /// <summary>
    /// DEPRECATED: Get bytes for song with support for chunking from request header values.
    /// Use GetStreamingDescriptorAsync for memory-efficient streaming.
    /// </summary>
    [Obsolete("Use GetStreamingDescriptorAsync instead for memory-efficient streaming")]
    public async Task<StreamResponse> StreamAsync(StreamRequest request, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        // Use the new streaming descriptor approach for better implementation
        var descriptorResult = await GetStreamingDescriptorAsync(request, apiRequest, cancellationToken);

        if (!descriptorResult.IsSuccess || descriptorResult.Data == null)
        {
            return new StreamResponse(new HeaderDictionary(), false, []);
        }

        var descriptor = descriptorResult.Data;

        // For backward compatibility, we need to read the file into a byte array
        // This defeats the memory efficiency but maintains API compatibility during transition
        try
        {
            byte[] fileBytes;
            if (descriptor.Range != null)
            {
                // Read only the requested range
                await using var fileStream = new FileStream(descriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fileStream.Seek(descriptor.Range.Start, SeekOrigin.Begin);

                var bytesToRead = (int)descriptor.Range.GetContentLength(descriptor.FileSize);
                fileBytes = new byte[bytesToRead];
                var bytesRead = await fileStream.ReadAsync(fileBytes, 0, bytesToRead, cancellationToken);

                if (bytesRead != bytesToRead)
                {
                    Array.Resize(ref fileBytes, bytesRead);
                }
            }
            else
            {
                // Read entire file - this is the problematic part we're trying to fix
                fileBytes = await File.ReadAllBytesAsync(descriptor.FilePath, cancellationToken);
            }

            // Create headers using the improved helper
            var statusCode = descriptor.Range != null ? 206 : 200;
            var headers = RangeParser.CreateResponseHeaders(descriptor, statusCode);

            return new StreamResponse(
                headers,
                true,
                fileBytes,
                descriptor.FileName,
                descriptor.ContentType
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error reading file [{FilePath}] for backward compatibility",
                nameof(OpenSubsonicApiService), descriptor.FilePath);
            return new StreamResponse(new HeaderDictionary(), false, []);
        }
    }

    public async Task<ResponseModel> GetNowPlayingAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var nowPlaying = await scrobbleService.GetNowPlaying(cancellationToken).ConfigureAwait(false);
        var data = new List<Child>();
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var nowPlayingSongApiKeys = nowPlaying.Data.Select(x => x.Scrobble.SongApiKey).ToList();
            var nowPlayingSongs = await (from s in scopedContext
                        .Songs.Include(x => x.Album)
                                         where nowPlayingSongApiKeys.Contains(s.ApiKey)
                                         select s)
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var nowPlayingSongIds = nowPlayingSongs.Select(x => x.Id).ToArray();
            var nowPlayingAlbumIds = nowPlayingSongs.Select(x => x.AlbumId).Distinct().ToArray();
            var nowPlayingSongsAlbums = await (from a in scopedContext.Albums.Include(x => x.Artist)
                                               where nowPlayingAlbumIds.Contains(a.Id)
                                               select a)
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var nowPlayingUserSongs = await (from us in scopedContext.UserSongs
                                             where us.UserId == authResponse.UserInfo.Id
                                             where nowPlayingSongIds.Contains(us.Id)
                                             select us)
                .AsNoTrackingWithIdentityResolution()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var nowPlayingSong in nowPlayingSongs)
            {
                var album = nowPlayingSongsAlbums.First(x => x.Id == nowPlayingSong.AlbumId);
                var userSong = nowPlayingUserSongs.FirstOrDefault(x => x.SongId == nowPlayingSong.Id);
                var nowPlayingSongUniqueId = SafeParser.Hash(authResponse.UserInfo.ApiKey.ToString(),
                    nowPlayingSong.ApiKey.ToString());
                data.Add(nowPlayingSong.ToApiChild(album, userSong,
                    nowPlaying.Data.FirstOrDefault(x => x.UniqueId == nowPlayingSongUniqueId)));
            }
        }

        var podcastNowPlaying = await podcastPlaybackService.GetNowPlayingAsync(authResponse.UserInfo.Id, cancellationToken).ConfigureAwait(false);
        if (podcastNowPlaying.IsSuccess && podcastNowPlaying.Data != null)
        {
            foreach (var nowPlayingHistory in podcastNowPlaying.Data)
            {
                var episode = nowPlayingHistory.PodcastEpisode;
                if (episode == null) continue;

                var episodeApiId = $"podcast:episode:{episode.Id}";
                var child = new Child(
                    episodeApiId,
                    $"podcast:channel:{episode.PodcastChannelId}",
                    false,
                    episode.Title,
                    null,
                    null,
                    null,
                    episode.PublishDate?.InUtc().Year ?? 0,
                    null,
                    episode.LocalFileSize ?? 0,
                    episode.MimeType ?? "audio/mpeg",
                    episode.MimeType?.Split('/').LastOrDefault() ?? "mp3",
                    null,
                    (int?)(episode.Duration?.TotalSeconds ?? 0),
                    0,
                    null,
                    null,
                    null,
                    episode.LocalPath,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                );
                data.Add(child);
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "nowPlaying",
                DataDetailPropertyName = "entry"
            }
        };
    }

    public async Task<ResponseModel> SearchAsync(
        SearchRequest request,
        bool isSearch3,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        long totalCount = 0;
        var defaultPageSize = (await Configuration.Value).GetValue<short>(SettingRegistry.SearchEngineDefaultPageSize);
        var maxAllowedPageSize = (await Configuration.Value).GetValue<short>(SettingRegistry.SearchEngineMaximumAllowedPageSize);

        var artistOffset = request.ArtistOffset ?? 0;
        if (artistOffset < 0) artistOffset = defaultPageSize;

        var artistCount = request.ArtistCount ?? defaultPageSize;
        if (artistCount > maxAllowedPageSize) artistCount = maxAllowedPageSize;

        var albumOffset = request.AlbumOffset ?? 0;
        if (albumOffset < 0) albumOffset = defaultPageSize;

        var albumCount = request.AlbumCount ?? defaultPageSize;
        if (albumCount > maxAllowedPageSize) albumCount = maxAllowedPageSize;

        var songOffset = request.SongOffset ?? 0;
        if (songOffset < 0) songOffset = defaultPageSize;

        var songCount = request.SongCount ?? defaultPageSize;
        if (songCount > maxAllowedPageSize) songCount = maxAllowedPageSize;

        // Handle total count requests for empty queries
        if (request.Query.Nullify() == null)
        {
            if (request.AlbumCount == 1)
            {
                totalCount = (await statisticsService.GetAlbumCountAsync(cancellationToken).ConfigureAwait(false)).Data?.DataAsLong ?? 0;
            }
            else if (request.ArtistCount == 1)
            {
                totalCount = (await statisticsService.GetArtistCountAsync(cancellationToken).ConfigureAwait(false)).Data?.DataAsLong ?? 0;
            }
            else if (request.SongCount == 1)
            {
                totalCount = (await statisticsService.GetSongCountAsync(cancellationToken).ConfigureAwait(false)).Data?.DataAsLong ?? 0;
            }
        }

        // Use SearchService instead of raw SQL
        var searchResult = await searchService.DoOpenSubsonicSearchAsync(
            authResponse.UserInfo.ApiKey,
            request.Query,
            artistOffset,
            artistCount,
            albumOffset,
            albumCount,
            songOffset,
            songCount,
            cancellationToken);

        if (!searchResult.IsSuccess)
        {
            Logger.Warning("[{ServiceName}] Search failed: {Message}", nameof(OpenSubsonicApiService),
                searchResult.Messages?.FirstOrDefault());
            return new ResponseModel
            {
                UserInfo = authResponse.UserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.AuthError)
            };
        }

        // Convert domain objects to OpenSubsonic search results
        var artists = searchResult.Data.Artists.Select(ToArtistSearchResult).ToArray();
        var albums = searchResult.Data.Albums.Select(ToAlbumSearchResult).ToArray();
        var songs = searchResult.Data.Songs.Select(ToSongSearchResult).ToArray();

        if (albums.Length == 0 && songs.Length == 0 && artists.Length == 0)
        {
            Logger.Information("! No search results found.");
        }

        return new ResponseModel
        {
            TotalCount = totalCount,
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = isSearch3
                    ? new SearchResult3(albums, songs, artists)
                    : new SearchResult2(albums, songs, artists),
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty :
                isSearch3 ? "searchResult3" : "searchResult2"
            }
        };
    }


    public async Task<ResponseModel> GetMusicDirectoryAsync(string apiId, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Directory? data = null;

        var apiKey = ApiKeyFromId(apiId);
        if (IsApiIdForArtist(apiId) && apiKey != null)
        {
            var artistInfo =
                await DatabaseArtistInfoForArtistApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
            if (artistInfo != null)
            {
                await using (var scopedContext =
                             await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var artistAlbums = await scopedContext
                        .Albums
                        .Include(x => x.Artist)
                        .Include(x => x.UserAlbums.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                        .Where(x => x.ArtistId == artistInfo.Id)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    data = new Directory(artistInfo.CoverArt,
                        null,
                        artistInfo.Name,
                        artistInfo.UserStarred.ToString(),
                        artistInfo.UserRating,
                        artistInfo.CalculatedRating,
                        artistInfo.PlayCount,
                        artistInfo.Played.ToString(),
                        artistAlbums.Select(x => x.ToApiChild(x.UserAlbums.FirstOrDefault())).ToArray());
                }
            }
        }
        else if (IsApiIdForAlbum(apiId) && apiKey != null)
        {
            var albumInfo =
                await DatabaseAlbumInfoForAlbumApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
            if (albumInfo != null)
            {
                var albumSongInfos =
                    await DatabaseSongInfosForAlbumApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
                if (albumSongInfos != null)
                {
                    await using (var scopedContext =
                                 await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var songIds = albumSongInfos.Select(x => x.Id).ToArray();
                        var albumSongs = await scopedContext
                            .Songs
                            .Include(x => x.Album).ThenInclude(x => x.Artist)
                            .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                            .Where(x => songIds.Contains(x.Id))
                            .ToArrayAsync(cancellationToken)
                            .ConfigureAwait(false);
                        data = new Directory(albumInfo.CoverArt,
                            albumInfo.CoverArt,
                            albumInfo.Name,
                            albumInfo.UserStarred.ToString(),
                            albumInfo.UserRating,
                            albumInfo.CalculatedRating,
                            albumInfo.PlayCount,
                            albumInfo.Played.ToString(),
                            albumSongs.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray());
                    }
                }
            }
        }

        data ??= new Directory(
            apiId.Nullify() ?? string.Empty,
            null,
            string.Empty,
            null,
            null,
            0,
            0,
            null,
            []);

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "directory"
            }
        };
    }


    public async Task<ResponseModel> GetIndexesAsync(bool isArtistIndex, string dataPropertyName, Guid? musicFolderId,
        long? ifModifiedSince, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        object? data;
        var libraryId = 0;
        var lastModified = string.Empty;
        if (musicFolderId.HasValue)
        {
            var libraryResult =
                await libraryService.ListAsync(new PagedRequest(), cancellationToken).ConfigureAwait(false);
            var library = libraryResult.Data.FirstOrDefault(x => x.ApiKey == musicFolderId.Value);
            libraryId = library?.Id ?? 0;
            lastModified = library?.LastUpdatedAt.ToString() ?? string.Empty;
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Build EF Core query for artist indexes
            var artistQuery = scopedContext.Artists
                .Include(a => a.UserArtists.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                .AsNoTracking()
                .Where(a => libraryId == 0 || a.LibraryId == libraryId)
                .OrderBy(a => a.SortOrder)
                .ThenBy(a => a.SortName);

            var artists = await artistQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

            var indexes = artists.Select(a =>
            {
                var userArtist = a.UserArtists.FirstOrDefault();
                return new DatabaseDirectoryInfo(
                    a.Id,
                    a.ApiKey,
                    a.SortName?.Length > 0 ? a.SortName.Substring(0, 1) : string.Empty,
                    a.Name,
                    $"artist_{a.ApiKey}",
                    a.CalculatedRating,
                    a.AlbumCount,
                    a.PlayedCount,
                    a.CreatedAt,
                    a.LastUpdatedAt,
                    a.LastPlayedAt,
                    a.Directory,
                    userArtist?.IsStarred == true ? userArtist.StarredAt : null,
                    userArtist?.Rating
                );
            }).ToArray();

            var configuration = await Configuration.Value;

            var artistIndexes = new List<ArtistIndex>();
            foreach (var grouped in indexes.GroupBy(x => x.Index))
            {
                var aa = new List<Artist>();
                foreach (var info in grouped)
                {
                    aa.Add(new Artist(info.CoverArt,
                        info.Name,
                        info.AlbumCount,
                        info.UserRatingValue,
                        info.CalculatedRating,
                        info.CoverArt,
                        configuration.GenerateImageUrl(info.CoverArt, ImageSize.Large),
                        info.UserStarred?.ToString()));
                }

                artistIndexes.Add(new ArtistIndex(grouped.Key, aa.Take(indexLimit).ToArray()));
            }

            if (!isArtistIndex)
            {
                data = new Indexes(
                    (await Configuration.Value).GetValue<string>(SettingRegistry.ProcessingIgnoredArticles) ??
                    string.Empty, lastModified,
                    [],
                    artistIndexes.ToArray(),
                    []);
            }
            else
            {
                data = new Artists(
                    (await Configuration.Value).GetValue<string>(SettingRegistry.ProcessingIgnoredArticles) ??
                    string.Empty, lastModified,
                    artistIndexes.ToArray());
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : dataPropertyName
            }
        };
    }

    /// <summary>
    ///     Returns all configured top-level music folders.
    /// </summary>
    public async Task<ResponseModel> GetMusicFolders(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        NamedInfo[] data = [];

        var libraryResult = await libraryService.ListAsync(new PagedRequest(), cancellationToken).ConfigureAwait(false);
        if (libraryResult.IsSuccess)
        {
            data = libraryResult.Data.Where(x => x.TypeValue == LibraryType.Storage)
                .Select(x => new NamedInfo(x.ToApiKey(), x.Name)).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "musicFolders",
                DataDetailPropertyName = "musicFolder"
            }
        };
    }

    public async Task<ResponseModel> GetArtistAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Artist? data = null;

        var apiKey = ApiKeyFromId(id);
        if (apiKey != null)
        {
            var artistInfo =
                await DatabaseArtistInfoForArtistApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken)
                    .ConfigureAwait(false);
            if (artistInfo != null)
            {
                var configuration = await Configuration.Value;
                data = new Artist(
                    id,
                    artistInfo.Name,
                    artistInfo.AlbumCount,
                    artistInfo.UserRating,
                    artistInfo.CalculatedRating,
                    artistInfo.CoverArt,
                    configuration.GenerateImageUrl(id, ImageSize.Large),
                    artistInfo.UserStarred?.ToString(),
                    await AlbumListForArtistApiKey(apiKey.Value, authResponse.UserInfo.Id, cancellationToken)
                        .ConfigureAwait(false));
            }
            else
            {
                Logger.Warning("[{MethodName}] invalid artist id requested.",
                    nameof(GetArtistAsync));
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = data?.AlbumCount ?? 0,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "artist"
            }
        };
    }

    /// <summary>
    ///     Toggles a star to a song, album, or artist.
    /// </summary>
    public async Task<ResponseModel> ToggleStarAsync(bool isStarred, string? id, string? albumId, string? artistId,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var result = false;
        var idValue = id ?? albumId ?? artistId;
        var apiKey = ApiKeyFromId(idValue);
        if (apiKey != null)
        {
            if (IsApiIdForArtist(idValue))
            {
                result = (await userService
                    .ToggleAristStarAsync(authResponse.UserInfo.Id, apiKey.Value, isStarred, cancellationToken)
                    .ConfigureAwait(false)).Data;
            }

            if (IsApiIdForAlbum(idValue))
            {
                result = (await userService
                    .ToggleAlbumStarAsync(authResponse.UserInfo.Id, apiKey.Value, isStarred, cancellationToken)
                    .ConfigureAwait(false)).Data;
            }

            if (IsApiIdForSong(idValue))
            {
                result = (await userService
                    .ToggleSongStarAsync(authResponse.UserInfo.Id, apiKey.Value, isStarred, cancellationToken)
                    .ConfigureAwait(false)).Data;
            }
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                result ? null : Error.InvalidApiKeyError)
        };
    }

    /// <summary>
    ///     Sets the rating for a music file.
    /// </summary>
    public async Task<ResponseModel> SetRatingAsync(string id, int rating, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        OperationResult<bool> result;

        if (IsApiIdForSong(id))
        {
            result = await userRatingService.SetSongRatingAsync(authResponse.UserInfo.Id, apiKey.Value, rating, cancellationToken);
        }
        else if (IsApiIdForAlbum(id))
        {
            result = await userRatingService.SetAlbumRatingAsync(authResponse.UserInfo.Id, apiKey.Value, rating, cancellationToken);
        }
        else if (IsApiIdForArtist(id))
        {
            result = await userRatingService.SetArtistRatingAsync(authResponse.UserInfo.Id, apiKey.Value, rating, cancellationToken);
        }
        else
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result.IsSuccess,
            ResponseData = await NewApiResponse(result.IsSuccess, string.Empty, string.Empty,
                result.IsSuccess ? null : Error.InvalidApiKeyError)
        };
    }

    public async Task<ResponseModel> GetTopSongsAsync(string artist, int? count, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Child[]? data;

        await artistSearchEngineService.InitializeAsync(await Configuration.Value, cancellationToken)
            .ConfigureAwait(false);

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var artistId = await scopedContext.Artists.Where(x => x.Name == artist).Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var topSongsResult = await artistSearchEngineService
                .DoArtistTopSongsSearchAsync(artist, artistId, count, cancellationToken).ConfigureAwait(false);
            var songIds = topSongsResult.Data.Where(x => x.Id != null).Select(x => x.Id).ToArray();
            var songs = await scopedContext
                .Songs.Include(x => x.Album).ThenInclude(x => x.Artist)
                .Include(x => x.UserSongs.Where(us => us.UserId == authResponse.UserInfo.Id))
                .Where(x => songIds.Contains(x.Id)).ToArrayAsync(cancellationToken).ConfigureAwait(false);
            data = (from s in songs
                    join tsr in topSongsResult.Data on s.Id equals tsr.Id
                    orderby tsr.SortOrder
                    select s
                ).Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "topSongs",
                DataDetailPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetSimilarSongsAsync(string id, int? count, bool isV2, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var maxResults = count.HasValue && count.Value > 0 ? count.Value : 50;
        var dataPropertyName = isV2 ? "similarSongs2" : "similarSongs";
        Child[] data = [];

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = authResponse.UserInfo,
                ResponseData = await DefaultApiResponse() with
                {
                    Data = data,
                    DataPropertyName = dataPropertyName,
                    DataDetailPropertyName = "song"
                }
            };
        }

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            int? artistId = null;
            Guid? songApiKey = null;

            if (IsApiIdForSong(id))
            {
                var song = await scopedContext.Songs
                    .Include(s => s.Album)
                    .ThenInclude(a => a.Artist)
                    .FirstOrDefaultAsync(s => s.ApiKey == apiKey.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (song?.Album?.ArtistId != null)
                {
                    artistId = song.Album.ArtistId;
                    songApiKey = song.ApiKey;
                }
            }
            else if (IsApiIdForArtist(id))
            {
                var artist = await scopedContext.Artists
                    .FirstOrDefaultAsync(a => a.ApiKey == apiKey.Value, cancellationToken)
                    .ConfigureAwait(false);

                if (artist != null)
                {
                    artistId = artist.Id;
                }
            }

            if (artistId == null)
            {
                return new ResponseModel
                {
                    UserInfo = authResponse.UserInfo,
                    ResponseData = await DefaultApiResponse() with
                    {
                        Data = data,
                        DataPropertyName = dataPropertyName,
                        DataDetailPropertyName = "song"
                    }
                };
            }

            var similarArtistIds = await scopedContext.ArtistRelation
                .Where(ar => ar.ArtistId == artistId.Value && ar.ArtistRelationType == SafeParser.ToNumber<int>(ArtistRelationType.Similar))
                .Select(ar => ar.RelatedArtistId)
                .Distinct()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (similarArtistIds.Length > 0)
            {
                var songs = await scopedContext.Songs
                    .Include(s => s.Album)
                    .ThenInclude(a => a.Artist)
                    .Include(s => s.UserSongs.Where(us => us.UserId == authResponse.UserInfo.Id))
                    .Where(s => similarArtistIds.Contains(s.Album.ArtistId))
                    .Where(s => songApiKey == null || s.ApiKey != songApiKey.Value)
                    .OrderBy(s => s.Album.Artist.SortName ?? s.Album.Artist.Name)
                    .ThenBy(s => s.Album.ReleaseDate)
                    .ThenBy(s => s.SongNumber)
                    .ThenBy(s => s.TitleSort ?? s.Title)
                    .Take(maxResults)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                data = songs.Select(s => s.ToApiChild(s.Album, s.UserSongs.FirstOrDefault())).ToArray();
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = dataPropertyName,
                DataDetailPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetStarred2Async(string? musicFolderId, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        ArtistID3[] artists;
        AlbumID3[] albums;
        Child[] songs;

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userStarredArtists = await scopedContext
                .UserArtists.Include(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            artists = userStarredArtists.Select(x => x.Artist.ToApiArtistID3(x)).ToArray();

            var userStarredAlbums = await scopedContext
                .UserAlbums.Include(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            albums = userStarredAlbums.Select(x => x.Album.ToArtistID3(x, null)).ToArray();

            var userStarredSongs = await scopedContext
                .UserSongs.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            songs = userStarredSongs.Select(x => x.Song.ToApiChild(x.Song.Album, x)).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = new StarredInfo2(artists, albums, songs),
                DataPropertyName = "starred2"
            }
        };
    }

    public async Task<ResponseModel> GetStarredAsync(string? musicFolderId, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Artist[] artists;
        Child[] albums;
        Child[] songs;

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userStarredArtists = await scopedContext
                .UserArtists.Include(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            artists = userStarredArtists.Select(x => x.Artist.ToApiArtist(x)).ToArray();

            var userStarredAlbums = await scopedContext
                .UserAlbums.Include(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            albums = userStarredAlbums.Select(x => x.Album.ToApiChild(x)).ToArray();

            var userStarredSongs = await scopedContext
                .UserSongs.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
                .Where(x => x.UserId == authResponse.UserInfo.Id && x.IsStarred)
                .OrderBy(x => x.Id)
                .Take(indexLimit)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            songs = userStarredSongs.Select(x => x.Song.ToApiChild(x.Song.Album, x)).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = new StarredInfo(artists, albums, songs),
                DataPropertyName = "starred"
            }
        };
    }

    public async Task<ResponseModel> GetSongsByGenreAsync(string genre, int? count, int? offset, string? musicFolderId,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var indexLimit = (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
        if (indexLimit == 0)
        {
            indexLimit = short.MaxValue;
        }

        long totalCount;
        Child[] songs;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Build EF Core query for songs by genre
            var songQuery = scopedContext.Songs
                .Include(x => x.Album).ThenInclude(x => x.Artist)
                .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                .AsNoTracking()
                .Where(s => (s.Genres != null && s.Genres.Contains(genre)) ||
                            (s.Album.Genres != null && s.Album.Genres.Contains(genre)));

            // Get total count
            totalCount = await songQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            // Apply pagination and get songs
            var takeSize = (count ?? indexLimit) < indexLimit ? (count ?? indexLimit) : indexLimit;
            var dbSongs = await songQuery
                .Skip(offset ?? 0)
                .Take(takeSize)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = dbSongs.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            TotalCount = totalCount,
            ResponseData = await DefaultApiResponse() with
            {
                Data = songs,
                DataPropertyName = "songsByGenre",
                DataDetailPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> GetBookmarksAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var data = new List<Bookmark>();

        var bookmarksResult = await userBookmarkService.GetBookmarksAsync(authResponse.UserInfo.Id, cancellationToken).ConfigureAwait(false);
        if (bookmarksResult.IsSuccess && bookmarksResult.Data != null)
        {
            data.AddRange(bookmarksResult.Data.Select(x => x.ToApiBookmark()));
        }

        var podcastBookmarksResult = await podcastPlaybackService.GetAllBookmarksAsync(authResponse.UserInfo.Id, cancellationToken).ConfigureAwait(false);
        if (podcastBookmarksResult.IsSuccess && podcastBookmarksResult.Data != null)
        {
            foreach (var bookmark in podcastBookmarksResult.Data)
            {
                var bookmarkEntry = new Child(
                    $"podcast:episode:{bookmark.PodcastEpisodeId}",
                    null,
                    false,
                    null,
                    null,
                    null,
                    null,
                    0,
                    null,
                    0,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                );
                data.Add(new Bookmark(
                    bookmark.PositionSeconds,
                    authResponse.UserInfo.UserName,
                    bookmark.Comment,
                    bookmark.CreatedAt.InUtc().ToDateTimeUtc().ToString("O"),
                    bookmark.UpdatedAt.InUtc().ToDateTimeUtc().ToString("O"),
                    [bookmarkEntry]
                ));
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data.ToArray(),
                DataPropertyName = "bookmarks",
                DataDetailPropertyName = "bookmark"
            }
        };
    }

    public async Task<ResponseModel> CreateBookmarkAsync(string id, int position, string? comment,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var podcastEpisodeId = ParsePodcastEpisodeIdFromApiId(id);
        if (podcastEpisodeId.HasValue)
        {
            var podcastSaveResult = await podcastPlaybackService.SaveBookmarkAsync(authResponse.UserInfo.Id, podcastEpisodeId.Value, position, comment, cancellationToken).ConfigureAwait(false);
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = podcastSaveResult.IsSuccess,
                ResponseData = await NewApiResponse(podcastSaveResult.IsSuccess, string.Empty, string.Empty,
                    podcastSaveResult.IsSuccess ? null : Error.InvalidApiKeyError)
            };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        var bookmarkResult = await userBookmarkService.CreateBookmarkAsync(authResponse.UserInfo.Id, apiKey.Value, position, comment, cancellationToken).ConfigureAwait(false);

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = bookmarkResult.IsSuccess,
            ResponseData = await NewApiResponse(bookmarkResult.IsSuccess, string.Empty, string.Empty,
                bookmarkResult.IsSuccess ? null : Error.InvalidApiKeyError)
        };
    }

    public async Task<ResponseModel> DeleteBookmarkAsync(
        string id,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        var podcastEpisodeId = ParsePodcastEpisodeIdFromApiId(id);
        if (podcastEpisodeId.HasValue)
        {
            var podcastDeleteResult = await podcastPlaybackService.DeleteBookmarkAsync(authResponse.UserInfo.Id, podcastEpisodeId.Value, cancellationToken).ConfigureAwait(false);
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = podcastDeleteResult.IsSuccess,
                ResponseData = await NewApiResponse(podcastDeleteResult.IsSuccess, string.Empty, string.Empty,
                    podcastDeleteResult.IsSuccess ? null : Error.InvalidApiKeyError)
            };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.InvalidApiKeyError)
            };
        }

        var deleteResult = await userBookmarkService.DeleteBookmarkAsync(authResponse.UserInfo.Id, apiKey.Value, cancellationToken).ConfigureAwait(false);

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = deleteResult.IsSuccess,
            ResponseData = await NewApiResponse(deleteResult.IsSuccess, string.Empty, string.Empty,
                deleteResult.IsSuccess ? null : Error.InvalidApiKeyError)
        };
    }

    public async Task<ResponseModel> GetArtistInfoAsync(
        string id,
        int? numberOfSimilarArtistsToReturn,
        bool isArtistInfo2,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        ArtistInfo? data = null;
        var apiKey = ApiKeyFromId(id);

        if (apiKey != null)
        {
            var artistResult = await artistService.GetArtistWithRelatedAsync(apiKey.Value, numberOfSimilarArtistsToReturn, ArtistRelationType.Similar, cancellationToken);

            if (artistResult.IsSuccess)
            {
                var (artist, similarArtists) = artistResult.Data;
                var configuration = await Configuration.Value;

                Artist[]? similarArtistModels = null;
                if (similarArtists.Any())
                {
                    similarArtistModels = similarArtists.Select(x => x.ToApiArtist()).ToArray();
                }

                // Construct Last.fm URL if LastFmId is present
                string? lastFmUrl = null;
                if (!string.IsNullOrWhiteSpace(artist.LastFmId))
                {
                    lastFmUrl = $"https://www.last.fm/music/{Uri.EscapeDataString(artist.LastFmId)}";
                }

                data = new ArtistInfo(artist.ToApiKey(),
                    artist.Name,
                    configuration.GenerateImageUrl(id, ImageSize.Thumbnail),
                    configuration.GenerateImageUrl(id, ImageSize.Medium),
                    configuration.GenerateImageUrl(id, ImageSize.Large),
                    artist.SongCount,
                    artist.AlbumCount,
                    artist.Biography,
                    artist.MusicBrainzId,
                    lastFmUrl,
                    similarArtistModels,
                    isArtistInfo2);
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,

            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : isArtistInfo2 ? "artistInfo2" : "artistInfo"
            }
        };
    }

    public async Task<ResponseModel> GetAlbumInfoAsync(
        string id,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        AlbumInfo? data = null;
        var apiKey = ApiKeyFromId(id);

        if (IsApiIdForSong(id))
        {
            // Some players send the first song to get an albums details. No idea why.
            var songApiKey = ApiKeyFromId(id);
            if (songApiKey != null)
            {
                var songInfo = await DatabaseSongIdsInfoForSongApiKey(songApiKey.Value, cancellationToken)
                    .ConfigureAwait(false);
                apiKey = songInfo?.AlbumApiKey ?? apiKey;
            }
        }

        if (apiKey != null)
        {
            var albumResult = await albumService.GetByApiKeyAsync(apiKey.Value, cancellationToken);
            if (albumResult.IsSuccess && albumResult.Data != null)
            {
                var album = albumResult.Data;
                var configuration = await Configuration.Value;

                data = new AlbumInfo(album.ToApiKey(),
                    album.Name,
                    configuration.GenerateImageUrl(id, ImageSize.Thumbnail),
                    configuration.GenerateImageUrl(id, ImageSize.Medium),
                    configuration.GenerateImageUrl(id, ImageSize.Large),
                    album.SongCount,
                    1,
                    album.Notes,
                    album.MusicBrainzId);
            }
        }

        return new ResponseModel
        {
            IsSuccess = data != null,
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = apiRequest.IsXmlRequest ? string.Empty : "albumInfo"
            }
        };
    }

    public async Task<ResponseModel> GetUserAsync(
        string username,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userProfileService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        User? data = null;
        var user = await userProfileService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user.IsSuccess)
        {
            data = user.Data!.ToApiUser();
        }

        return new ResponseModel
        {
            IsSuccess = data != null,
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "user"
            }
        };
    }

    public async Task<ResponseModel> GetRandomSongsAsync(
        int size,
        string? genre,
        int? fromYear,
        int? toYear,
        string? musicFolderId,
        ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Child[]? songs;

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var indexLimit =
                (await Configuration.Value).GetValue<short>(SettingRegistry.OpenSubsonicIndexesArtistLimit);
            if (indexLimit == 0)
            {
                indexLimit = short.MaxValue;
            }

            var takeSize = size < indexLimit ? size : indexLimit;
            var genreFilter = genre ?? string.Empty;
            var fromYearFilter = fromYear ?? 0;
            var toYearFilter = toYear ?? 9999;

            // Build EF Core query for random songs with filters
            var songQuery = scopedContext.Songs
                .Include(x => x.Album).ThenInclude(x => x.Artist)
                .Include(x => x.UserSongs.Where(ua => ua.UserId == authResponse.UserInfo.Id))
                .AsNoTracking()
                .Where(s =>
                    (string.IsNullOrEmpty(genreFilter) ||
                     (s.Genres != null && s.Genres.Contains(genreFilter)) ||
                     (s.Album.Genres != null && s.Album.Genres.Contains(genreFilter))) &&
                    s.Album.ReleaseDate.Year >= fromYearFilter &&
                    s.Album.ReleaseDate.Year <= toYearFilter);

            // Get random songs - first get all matching songs, then shuffle in memory
            var allDbSongs = await songQuery
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            // Shuffle and take the required number using Random.Shared for thread-safe randomization
            var dbSongs = allDbSongs.OrderBy(_ => Random.Shared.Next()).Take(takeSize).ToArray();

            songs = dbSongs.Select(x => x.ToApiChild(x.Album, x.UserSongs.FirstOrDefault())).ToArray();
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = songs,
                DataPropertyName = "randomSongs",
                DataDetailPropertyName = "song"
            }
        };
    }

    public async Task<ResponseModel> DeleteInternetRadioStationAsync(string id, ApiRequest apiRequest,
        CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userProfileService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null && int.TryParse(id, out var radioStationId))
        {
            var stationResult = await radioStationService.GetAsync(radioStationId, cancellationToken).ConfigureAwait(false);
            apiKey = stationResult.Data?.ApiKey;
        }
        if (apiKey != null)
        {
            var deleteResult = await radioStationService.DeleteByApiKeyAsync(apiKey.Value, authResponse.UserInfo.Id, cancellationToken);
            result = deleteResult.IsSuccess;
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    public async Task<ResponseModel> CreateInternetRadioStationAsync(string name, string streamUrl, string? homePageUrl,
        ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userProfileService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        Error? notAuthorizedError = null;
        var createResult = await radioStationService.CreateAsync(name, streamUrl, homePageUrl, cancellationToken);
        var result = createResult.IsSuccess;

        if (result)
        {
            Logger.Information("Radio station created.");
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    public async Task<ResponseModel> UpdateInternetRadioStationAsync(string id, string name, string streamUrl,
        string? homePageUrl, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? notAuthorizedError = null;
        var result = false;

        // Only users with admin privileges are allowed to call this method.
        var isUserAdmin = await userProfileService.IsUserAdminAsync(authResponse.UserInfo.UserName, cancellationToken)
            .ConfigureAwait(false);
        if (!isUserAdmin)
        {
            return new ResponseModel
            {
                UserInfo = UserInfo.BlankUserInfo,
                IsSuccess = false,
                ResponseData = await NewApiResponse(false, string.Empty, string.Empty, Error.UserNotAuthorizedError)
            };
        }

        var apiKey = ApiKeyFromId(id);
        if (apiKey == null && int.TryParse(id, out var radioStationId))
        {
            var stationResult = await radioStationService.GetAsync(radioStationId, cancellationToken).ConfigureAwait(false);
            apiKey = stationResult.Data?.ApiKey;
        }
        if (apiKey != null)
        {
            var updateResult = await radioStationService.UpdateByApiKeyAsync(apiKey.Value, name, streamUrl, homePageUrl, cancellationToken);
            result = updateResult.IsSuccess;
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            IsSuccess = result,
            ResponseData = await NewApiResponse(result, string.Empty, string.Empty,
                notAuthorizedError ?? (result ? null : Error.InvalidApiKeyError))
        };
    }

    public async Task<ResponseModel> GetInternetRadioStationsAsync(ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Error? error = null;
        var data = new List<InternetRadioStation>();
        try
        {
            var radioStationsResult = await radioStationService.GetAllAsync(cancellationToken);
            if (radioStationsResult.IsSuccess)
            {
                data = radioStationsResult.Data.Select(x => x.ToApiInternetRadioStation()).ToList();
            }
        }
        catch (Exception e)
        {
            error = Error.GenericError($"Failed to get Radio Stations");
            Logger.Error(e, "Failed to get Radio Stations Request [{ApiResult}]", apiRequest);
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Error = error,
                Data = data.ToArray(),
                DataPropertyName = "internetRadioStations",
                DataDetailPropertyName = apiRequest.IsXmlRequest ? string.Empty : "internetRadioStation"
            }
        };
    }

    public async Task<ResponseModel> GetLyricsListForSongIdAsync(string id, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        LyricsList[]? data = null;
        var apiKey = ApiKeyFromId(id);

        if (apiKey != null)
        {
            var songResult = await songService.GetSongWithPathInfoAsync(apiKey.Value, cancellationToken);
            if (songResult.IsSuccess)
            {
                var (song, libraryPath, artistDirectory) = songResult.Data;
                var lyricsResult = await lyricPlugin.GetLyricListAsync(
                    Path.Combine(libraryPath, artistDirectory).ToFileSystemDirectoryInfo(),
                    new FileSystemFileInfo
                    {
                        Name = song.FileName,
                        Size = song.FileSize
                    },
                    cancellationToken).ConfigureAwait(false);
                if (lyricsResult.IsSuccess)
                {
                    data = [lyricsResult.Data!];
                }
            }
        }

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "structuredLyrics",
                DataDetailPropertyName = string.Empty
            }
        };
    }

    public async Task<ResponseModel> GetLyricsForArtistAndTitleAsync(string? artist, string? title, ApiRequest apiRequest, CancellationToken cancellationToken)
    {
        var authResponse = await AuthenticateSubsonicApiAsync(apiRequest, cancellationToken);
        if (!authResponse.IsSuccess)
        {
            return authResponse with { UserInfo = UserInfo.BlankUserInfo };
        }

        Lyrics? data = null;

        if (artist.Nullify() != null && title.Nullify() != null)
        {
            var songResult = await songService.GetSongByArtistAndTitleAsync(artist!, title!, cancellationToken);
            if (songResult.IsSuccess)
            {
                var (song, libraryPath, artistDirectory) = songResult.Data;
                var lyricsResult = await lyricPlugin.GetLyricsAsync(
                    Path.Combine(libraryPath, artistDirectory).ToFileSystemDirectoryInfo(),
                    new FileSystemFileInfo
                    {
                        Name = song.FileName,
                        Size = song.FileSize
                    },
                    cancellationToken).ConfigureAwait(false);
                if (lyricsResult.IsSuccess)
                {
                    data = lyricsResult.Data;
                }
            }
        }

        data ??= new Lyrics
        {
            Value = string.Empty,
            Artist = artist ?? string.Empty,
            Title = title ?? string.Empty
        };

        return new ResponseModel
        {
            UserInfo = authResponse.UserInfo,
            ResponseData = await DefaultApiResponse() with
            {
                Data = data,
                DataPropertyName = "lyrics",
                DataDetailPropertyName = string.Empty
            }
        };
    }

    private static ArtistSearchResult ToArtistSearchResult(ArtistDataInfo artist)
    {
        return new ArtistSearchResult($"artist_{artist.ApiKey}", artist.Name, $"artist_{artist.ApiKey}", artist.AlbumCount);
    }

    private static AlbumSearchResult ToAlbumSearchResult(AlbumDataInfo album)
    {
        return new AlbumSearchResult
        {
            Id = $"album_{album.ApiKey}",
            Name = album.Name,
            CoverArt = $"album_{album.ApiKey}",
            SongCount = album.SongCount,
            Artist = album.ArtistName,
            ArtistId = $"artist_{album.ArtistApiKey}",
            CreatedAt = album.CreatedAt,
            DurationMs = album.Duration,
            Genres = ParseGenresFromTags(album.Tags)
        };
    }

    private static SongSearchResult ToSongSearchResult(SongDataInfo song)
    {
        return new SongSearchResult
        {
            Id = $"song_{song.ApiKey}",
            Parent = $"album_{song.AlbumApiKey}",
            Title = song.Title,
            Album = song.AlbumName,
            Artist = song.ArtistName,
            CoverArt = $"song_{song.ApiKey}",
            CreatedAt = song.CreatedAt,
            Duration = (int)(song.Duration / 1000), // Convert milliseconds to seconds
            Bitrate = 0, // Not available in SongDataInfo
            Track = song.SongNumber,
            Year = song.ReleaseDate.Year,
            Genres = ParseGenresFromTags(song.Tags),
            Size = (int)song.FileSize,
            ContentType = "audio/mpeg", // Default - not available in SongDataInfo
            Path = "unknown", // Not available in SongDataInfo
            Suffix = "mp3", // Default - not available in SongDataInfo
            AlbumId = $"album_{song.AlbumApiKey}",
            ArtistId = $"artist_{song.ArtistApiKey}"
        };
    }

    private static string[]? ParseGenresFromTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;

        // Tags are pipe-separated, genres might be in there
        // This is a simplified implementation - adjust based on actual tag format
        return tags.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToArray();
    }
}
