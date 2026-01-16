using System.Net;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.Extensions;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Common.Models.Streaming;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Serilog;

namespace Melodee.Blazor.Controllers.OpenSubsonic;

[ApiController]
[Route("[controller]")]
public class PodcastController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IMelodeeConfigurationFactory configurationFactory,
    PodcastService podcastService,
    LibraryService libraryService,
    OpenSubsonicApiService openSubsonicApiService) : ControllerBase(etagRepository, serializer, configurationFactory)
{
    [HttpGet]
    [HttpPost]
    [Route("/rest/getPodcasts.view")]
    [Route("/rest/getPodcasts")]
    public async Task<IActionResult> GetPodcastsAsync(
        string? id = null,
        bool includeEpisodes = true,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        try
        {
            List<PodcastChannel> channels;

            if (id.Nullify() != null)
            {
                var channelId = ParsePodcastChannelId(id!);
                if (channelId == null)
                {
                    return BadRequest(await CreateResponseAsync(Error.GenericError("Invalid channel id")).ConfigureAwait(false));
                }

                var channelResult = await podcastService.GetChannelAsync(channelId.Value, auth.UserInfo.Id, cancellationToken).ConfigureAwait(false);
                if (!channelResult.IsSuccess || channelResult.Data == null)
                {
                    return NotFound(await CreateResponseAsync(Error.DataNotFoundError).ConfigureAwait(false));
                }

                channels = [channelResult.Data];
            }
            else
            {
                var result = await podcastService.ListChannelsAsync(auth.UserInfo.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                channels = result.Data?.ToList() ?? [];
            }

            var podcasts = new List<PodcastChannelResponse>();

            foreach (var channel in channels)
            {
                var podcast = new PodcastChannelResponse
                {
                    Id = $"podcast:channel:{channel.Id}",
                    Title = channel.Title,
                    Description = channel.Description,
                    Url = channel.FeedUrl,
                    CoverArt = channel.CoverArtLocalPath.Nullify() != null ? $"podcast:channel:{channel.Id}" : null,
                    OriginalImageUrl = channel.ImageUrl
                };

                if (includeEpisodes)
                {
                    var episodesResult = await podcastService.ListEpisodesAsync(channel.Id, auth.UserInfo.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
                    podcast.Episode = episodesResult.Data?.Select(e => e.ToPodcastEpisodeResponse()).ToList() ?? [];
                }

                podcasts.Add(podcast);
            }

            var response = new PodcastsResponse
            {
                Podcasts = new PodcastsContainer { Channel = podcasts }
            };

            return await MakeResult(Task.FromResult(await CreateResponseAsync(response).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in GetPodcasts", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/getNewestPodcasts.view")]
    [Route("/rest/getNewestPodcasts")]
    public async Task<IActionResult> GetNewestPodcastsAsync(
        int count = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        try
        {
            var result = await podcastService.GetNewestEpisodesAsync(auth.UserInfo.Id, count, offset, cancellationToken).ConfigureAwait(false);

            var episodes = result.Data?.Select(e => e.ToPodcastEpisodeResponse()).ToList() ?? [];

            var response = new NewestPodcastsResponse
            {
                NewestPodcasts = new NewestPodcastsContainer { Episode = episodes }
            };

            return await MakeResult(Task.FromResult(await CreateResponseAsync(response).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in GetNewestPodcasts", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/refreshPodcasts.view")]
    [Route("/rest/refreshPodcasts")]
    public async Task<IActionResult> RefreshPodcastsAsync(CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        try
        {
            var result = await podcastService.ListChannelsAsync(auth.UserInfo.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            var channels = result.Data?.ToList() ?? [];

            await using var context = await podcastService.CreateDbContextAsync(cancellationToken);
            foreach (var channel in channels)
            {
                channel.NextSyncAt = null;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return await MakeResult(Task.FromResult(await CreateResponseAsync(new StatusResponse { Status = "ok" }).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in RefreshPodcasts", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/createPodcastChannel.view")]
    [Route("/rest/createPodcastChannel")]
    public async Task<IActionResult> CreatePodcastChannelAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        if (url.Nullify() == null)
        {
            return BadRequest(await CreateResponseAsync(Error.RequiredParameterMissingError).ConfigureAwait(false));
        }

        try
        {
            var result = await podcastService.CreateChannelAsync(auth.UserInfo.Id, url, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return BadRequest(await CreateResponseAsync(Error.GenericError(result.Messages?.FirstOrDefault() ?? "Unknown error")).ConfigureAwait(false));
            }

            var channel = result.Data!;
            var response = new PodcastChannelResponse
            {
                Id = $"podcast:channel:{channel.Id}",
                Title = channel.Title,
                Description = channel.Description,
                Url = channel.FeedUrl
            };

            return await MakeResult(Task.FromResult(await CreateResponseAsync(response).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in CreatePodcastChannel", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/deletePodcastChannel.view")]
    [Route("/rest/deletePodcastChannel")]
    public async Task<IActionResult> DeletePodcastChannelAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        var channelId = ParsePodcastChannelId(id);
        if (channelId == null)
        {
            return BadRequest(await CreateResponseAsync(Error.GenericError("Invalid channel id")).ConfigureAwait(false));
        }

        try
        {
            // Hard delete for podcasts - they can be easily re-added from RSS feed
            // Soft delete would block re-adding the same feed URL due to unique constraint
            var result = await podcastService.DeleteChannelAsync(channelId.Value, auth.UserInfo.Id, softDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return NotFound(await CreateResponseAsync(Error.GenericError(result.Messages?.FirstOrDefault() ?? "Unknown error")).ConfigureAwait(false));
            }

            return await MakeResult(Task.FromResult(await CreateResponseAsync(new StatusResponse { Status = "ok" }).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in DeletePodcastChannel", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/deletePodcastEpisode.view")]
    [Route("/rest/deletePodcastEpisode")]
    public async Task<IActionResult> DeletePodcastEpisodeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        var episodeId = ParsePodcastEpisodeId(id);
        if (episodeId == null)
        {
            return BadRequest(await CreateResponseAsync(Error.GenericError("Invalid episode id")).ConfigureAwait(false));
        }

        try
        {
            var result = await podcastService.DeleteEpisodeAsync(episodeId.Value, auth.UserInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return NotFound(await CreateResponseAsync(Error.GenericError(result.Messages?.FirstOrDefault() ?? "Unknown error")).ConfigureAwait(false));
            }

            return await MakeResult(Task.FromResult(await CreateResponseAsync(new StatusResponse { Status = "ok" }).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in DeletePodcastEpisode", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/downloadPodcastEpisode.view")]
    [Route("/rest/downloadPodcastEpisode")]
    public async Task<IActionResult> DownloadPodcastEpisodeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);
        if (!auth.IsSuccess)
        {
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        if (!(auth.UserInfo.Roles?.Contains("HasPodcastRole") ?? false))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        var episodeId = ParsePodcastEpisodeId(id);
        if (episodeId == null)
        {
            return BadRequest(await CreateResponseAsync(Error.GenericError("Invalid episode id")).ConfigureAwait(false));
        }

        try
        {
            var result = await podcastService.QueueDownloadAsync(episodeId.Value, auth.UserInfo.Id, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return BadRequest(await CreateResponseAsync(Error.GenericError(result.Messages?.FirstOrDefault() ?? "Unknown error")).ConfigureAwait(false));
            }

            return await MakeResult(Task.FromResult(await CreateResponseAsync(new StatusResponse { Status = "ok" }).ConfigureAwait(false))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in DownloadPodcastEpisode", nameof(PodcastController));
            return await MakeResult(Task.FromResult(await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false))).ConfigureAwait(false);
        }
    }

    [HttpGet]
    [HttpPost]
    [Route("/rest/streamPodcastEpisode.view")]
    [Route("/rest/streamPodcastEpisode")]
    public async Task<IActionResult> StreamPodcastEpisodeAsync(
        string id,
        string? format = null,
        string? filename = null,
        CancellationToken cancellationToken = default)
    {
        var auth = await AuthenticateAsync(cancellationToken);

        Log.Debug("[StreamPodcastEpisode] Auth result - IsSuccess: {IsSuccess}, UserId: {UserId}, Roles: {Roles}",
            auth.IsSuccess,
            auth.UserInfo?.Id ?? 0,
            string.Join(", ", auth.UserInfo?.Roles ?? []));

        if (!auth.IsSuccess)
        {
            Log.Warning("[StreamPodcastEpisode] Authentication failed for episode ID: {EpisodeId}", id);
            return await AuthFailedAsync().ConfigureAwait(false);
        }

        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.PodcastEnabled))
        {
            return await NotSupportedAsync().ConfigureAwait(false);
        }

        // Only check for HasStreamRole if authentication was required (not localhost/cookie auth)
        // When authentication is bypassed (localhost or Blazor cookie auth), skip role check
        var userRoles = auth.UserInfo!.Roles ?? [];
        if (ApiRequest.RequiresAuthentication && !userRoles.Contains("HasStreamRole"))
        {
            Log.Warning("[StreamPodcastEpisode] User {UserId} does not have HasStreamRole. Roles: {Roles}",
                auth.UserInfo.Id,
                string.Join(", ", userRoles));
            return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.UserNotAuthorizedError).ConfigureAwait(false));
        }

        Log.Debug("[StreamPodcastEpisode] Role check passed. RequiresAuth: {RequiresAuth}, HasStreamRole: {HasStreamRole}",
            ApiRequest.RequiresAuthentication,
            userRoles.Contains("HasStreamRole"));

        var episodeId = ParsePodcastEpisodeId(id);
        Log.Debug("[StreamPodcastEpisode] Parsing episode ID: {RawId} -> {ParsedId}", id, episodeId);

        if (episodeId == null)
        {
            return BadRequest(await CreateResponseAsync(Error.GenericError("Invalid episode id")).ConfigureAwait(false));
        }

        try
        {
            // Use streaming-specific method that doesn't require user filtering
            // Authentication is already handled at endpoint level (localhost/cookie auth)
            Log.Debug("[StreamPodcastEpisode] Fetching episode {EpisodeId} for streaming", episodeId.Value);
            var episodeResult = await podcastService.GetEpisodeForStreamingAsync(episodeId.Value, cancellationToken).ConfigureAwait(false);

            Log.Debug("[StreamPodcastEpisode] Episode fetch result - IsSuccess: {IsSuccess}, HasData: {HasData}, Messages: {Messages}",
                episodeResult.IsSuccess,
                episodeResult.Data != null,
                string.Join(", ", episodeResult.Messages ?? []));

            if (!episodeResult.IsSuccess || episodeResult.Data == null)
            {
                return NotFound(await CreateResponseAsync(Error.DataNotFoundError).ConfigureAwait(false));
            }

            var episode = episodeResult.Data;

            Log.Debug("[StreamPodcastEpisode] Episode {EpisodeId}: DownloadStatus={DownloadStatus}, LocalPath={LocalPath}",
                episode.Id, episode.DownloadStatus, episode.LocalPath ?? "null");

            if (episode.DownloadStatus != PodcastEpisodeDownloadStatus.Downloaded || episode.LocalPath.Nullify() == null)
            {
                Log.Warning("[StreamPodcastEpisode] Episode {EpisodeId} not ready for streaming: DownloadStatus={DownloadStatus}, HasLocalPath={HasLocalPath}",
                    episode.Id, episode.DownloadStatus, episode.LocalPath != null);
                return BadRequest(await CreateResponseAsync(Error.GenericError("Episode not downloaded")).ConfigureAwait(false));
            }

            var podcastLibraryResult = await libraryService.GetPodcastLibraryAsync(cancellationToken).ConfigureAwait(false);
            var podcastLibrary = podcastLibraryResult.Data;
            if (podcastLibrary == null)
            {
                return NotFound(await CreateResponseAsync(Error.GenericError("Podcast library not configured")).ConfigureAwait(false));
            }

            var filePath = Path.Combine(podcastLibrary.Path, episode.LocalPath ?? string.Empty);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(await CreateResponseAsync(Error.GenericError("Episode file not found")).ConfigureAwait(false));
            }

            var fileInfo = new FileInfo(filePath);
            var rangeHeader = Request.Headers["Range"].FirstOrDefault();

            StreamingDescriptor descriptor;

            if (rangeHeader.Nullify() != null)
            {
                var range = RangeParser.ParseRange(rangeHeader!, fileInfo.Length);
                if (range == null || !range.IsValidForFileSize(fileInfo.Length))
                {
                    Response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                    return new EmptyResult();
                }

                descriptor = new StreamingDescriptor
                {
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    ContentType = episode.MimeType ?? "audio/mpeg",
                    ResponseHeaders = new Dictionary<string, StringValues>(),
                    Range = range,
                    FileName = episode.Title
                };
            }
            else
            {
                descriptor = new StreamingDescriptor
                {
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    ContentType = episode.MimeType ?? "audio/mpeg",
                    ResponseHeaders = new Dictionary<string, StringValues>(),
                    FileName = episode.Title
                };
            }

            var statusCode = descriptor.Range != null ? 206 : 200;

            foreach (var header in RangeParser.CreateResponseHeaders(descriptor, statusCode))
            {
                Response.Headers[header.Key] = header.Value;
            }

            Response.StatusCode = statusCode;

            if (descriptor.Range != null)
            {
                var fileStream = new FileStream(descriptor.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);

                fileStream.Seek(descriptor.Range.Start, SeekOrigin.Begin);
                var rangeStream = new BoundedStream(fileStream, descriptor.Range.GetContentLength(descriptor.FileSize));

                return new FileStreamResult(rangeStream, descriptor.ContentType)
                {
                    EnableRangeProcessing = true,
                    FileDownloadName = descriptor.FileName
                };
            }

            return new PhysicalFileResult(descriptor.FilePath, descriptor.ContentType)
            {
                EnableRangeProcessing = true,
                FileDownloadName = descriptor.FileName
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Controller}] Error in StreamPodcastEpisode", nameof(PodcastController));
            return StatusCode((int)HttpStatusCode.InternalServerError, await CreateResponseAsync(Error.GenericError("Internal server error")).ConfigureAwait(false));
        }
    }

    private static int? ParsePodcastChannelId(string id)
    {
        if (id.StartsWith("podcast:channel:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = id["podcast:channel:".Length..];
            if (int.TryParse(idPart, out var channelId))
            {
                return channelId;
            }
        }

        return null;
    }

    private static int? ParsePodcastEpisodeId(string id)
    {
        if (id.StartsWith("podcast:episode:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = id["podcast:episode:".Length..];
            if (int.TryParse(idPart, out var episodeId))
            {
                return episodeId;
            }
        }

        return null;
    }

    private record StatusResponse
    {
        public required string Status { get; init; }
    }

    private async Task<ResponseModel> CreateResponseAsync(object data)
    {
        var isError = data is Error;
        var dataPropertyName = "podcasts";
        if (isError)
        {
            dataPropertyName = "error";
        }
        else if (data is StatusResponse)
        {
            dataPropertyName = "status";
        }
        else if (data.GetType().Name.Contains("Channel"))
        {
            dataPropertyName = "podcastChannel";
        }
        else if (data.GetType().Name.Contains("Episode"))
        {
            dataPropertyName = "podcastEpisode";
        }

        return new ResponseModel
        {
            UserInfo = UserInfo.BlankUserInfo,
            ResponseData = await openSubsonicApiService.NewApiResponse(
                !isError,
                dataPropertyName,
                string.Empty,
                isError ? (Error)data : null,
                isError ? null : data).ConfigureAwait(false)
        };
    }

    private async Task<ResponseModel> AuthenticateAsync(CancellationToken cancellationToken)
    {
        return await openSubsonicApiService.AuthenticateSubsonicApiAsync(ApiRequest, cancellationToken);
    }

    private async Task<IActionResult> AuthFailedAsync()
    {
        return StatusCode((int)HttpStatusCode.Forbidden, await CreateResponseAsync(Error.AuthError).ConfigureAwait(false));
    }

    private async Task<IActionResult> NotSupportedAsync()
    {
        return StatusCode((int)HttpStatusCode.BadRequest, await CreateResponseAsync(Error.GenericError("Podcasts are currently disabled.")).ConfigureAwait(false));
    }
}

