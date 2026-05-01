using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic.Responses.Jukebox;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Playback;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services.Jukebox;

/// <summary>
/// Interface for Subsonic jukebox control operations.
/// </summary>
public interface ISubsonicJukeboxService
{
    /// <summary>
    /// Gets the jukebox status.
    /// </summary>
    Task<OperationResult<JukeboxStatusResponse>> GetStatusAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the jukebox playlist.
    /// </summary>
    Task<OperationResult<JukeboxGetResponse>> GetPlaylistAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the jukebox gain (volume).
    /// </summary>
    Task<OperationResult<bool>> SetGainAsync(int userId, double gain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts playback.
    /// </summary>
    Task<OperationResult<bool>> StartAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops playback.
    /// </summary>
    Task<OperationResult<bool>> StopAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips to a specific index or offset.
    /// </summary>
    Task<OperationResult<bool>> SkipAsync(int userId, int? index, int? offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds songs to the jukebox playlist.
    /// </summary>
    Task<OperationResult<bool>> AddAsync(int userId, IEnumerable<string> songIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the jukebox playlist.
    /// </summary>
    Task<OperationResult<bool>> ClearAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a song from the jukebox playlist by index.
    /// </summary>
    Task<OperationResult<bool>> RemoveAsync(int userId, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuffles the jukebox playlist.
    /// </summary>
    Task<OperationResult<bool>> ShuffleAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for Subsonic jukebox control operations.
/// Maps Subsonic jukeboxControl API to party session/queue operations.
/// </summary>
public sealed class SubsonicJukeboxService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    PartyQueueService partyQueueService,
    PartyPlaybackService partyPlaybackService,
    IPlaybackBackendService playbackBackendService)
    : ServiceBase(logger, cacheManager, contextFactory), ISubsonicJukeboxService
{
    // Use a fixed GUID for the system session
    private static readonly Guid JukeboxSessionApiKey = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

    public async Task<OperationResult<JukeboxStatusResponse>> GetStatusAsync(int userId, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<JukeboxStatusResponse>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = default!
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<JukeboxStatusResponse>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = default!
            };
        }

        var session = sessionResult.Data;
        var queueResult = await partyQueueService.GetQueueAsync(session.ApiKey, cancellationToken).ConfigureAwait(false);
        var playbackResult = await partyPlaybackService.GetPlaybackStateAsync(session.ApiKey, cancellationToken).ConfigureAwait(false);

        var playlist = await BuildJukeboxPlaylistAsync(session.ApiKey, cancellationToken).ConfigureAwait(false);

        var status = new JukeboxStatusResponse(
            CurrentIndex: playbackResult.Data?.CurrentQueueItemApiKey != null
                ? FindCurrentIndex(playlist.Entries, playbackResult.Data.CurrentQueueItemApiKey.Value)
                : 0,
            Playing: playbackResult.Data?.IsPlaying ?? false,
            Position: playbackResult.Data?.PositionSeconds ?? 0,
            Gain: playbackResult.Data?.Volume ?? 0.8,
            MaxVolume: 100,
            Playlist: playlist);

        return new OperationResult<JukeboxStatusResponse> { Data = status };
    }

    public async Task<OperationResult<JukeboxGetResponse>> GetPlaylistAsync(int userId, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<JukeboxGetResponse>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = default!
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<JukeboxGetResponse>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = default!
            };
        }

        var playlist = await BuildJukeboxPlaylistAsync(sessionResult.Data.ApiKey, cancellationToken).ConfigureAwait(false);

        return new OperationResult<JukeboxGetResponse>
        {
            Data = new JukeboxGetResponse(playlist)
        };
    }

    public async Task<OperationResult<bool>> SetGainAsync(int userId, double gain, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var volume01 = Math.Clamp(gain, 0.0, 1.0);
        var playbackResult = await partyPlaybackService.UpdateFromHeartbeatAsync(
            sessionResult.Data.ApiKey,
            null,
            0,
            false,
            volume01,
            userId,
            cancellationToken
        ).ConfigureAwait(false);

        if (!playbackResult.IsSuccess)
        {
            return new OperationResult<bool>(playbackResult.Messages)
            {
                Type = playbackResult.Type,
                Data = false
            };
        }

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> StartAsync(int userId, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var session = sessionResult.Data;

        // Get current playback state to find the current song
        var playbackState = await partyPlaybackService.GetPlaybackStateAsync(session.ApiKey, cancellationToken).ConfigureAwait(false);

        // Get the current queue item to find the song to play
        Guid? songApiKey = null;
        if (playbackState.Data?.CurrentQueueItemApiKey != null)
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var queueItem = await scopedContext.PartyQueueItems
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ApiKey == playbackState.Data.CurrentQueueItemApiKey, cancellationToken)
                .ConfigureAwait(false);
            songApiKey = queueItem?.SongApiKey;
        }
        else
        {
            // No current item set, try to get the first item in the queue
            var queueResult = await partyQueueService.GetQueueAsync(session.ApiKey, cancellationToken).ConfigureAwait(false);
            if (queueResult.IsSuccess && queueResult.Data.Items.Any())
            {
                var firstItem = queueResult.Data.Items.First();
                songApiKey = firstItem.SongApiKey;

                // Set the first item as the current item
                await partyPlaybackService.SetCurrentItemAsync(session.ApiKey, firstItem.ApiKey, cancellationToken).ConfigureAwait(false);
            }
        }

        if (songApiKey == null)
        {
            return new OperationResult<bool>("No song in queue to play")
            {
                Type = OperationResponseType.ValidationFailure,
                Data = false
            };
        }

        // Update the playback intent in the party system
        var playbackResult = await partyPlaybackService.UpdateIntentAsync(
            session.ApiKey,
            PlaybackIntent.Play,
            null,
            userId,
            session.PlaybackRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!playbackResult.IsSuccess)
        {
            return new OperationResult<bool>(playbackResult.Messages)
            {
                Type = playbackResult.Type,
                Data = false
            };
        }

        // Actually play the song on the backend
        var startPosition = playbackState.Data?.PositionSeconds ?? 0;
        var backendResult = await playbackBackendService.PlaySongAsync(songApiKey.Value, startPosition, cancellationToken).ConfigureAwait(false);

        if (!backendResult.IsSuccess)
        {
            Logger.Warning("[SubsonicJukeboxService] Failed to play song on backend: {Messages}", string.Join(", ", backendResult.Messages ?? []));
            return new OperationResult<bool>(backendResult.Messages)
            {
                Type = backendResult.Type,
                Data = false
            };
        }

        Logger.Information("[SubsonicJukeboxService] Started playback of song {SongApiKey} at position {Position}s", songApiKey, startPosition);
        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> StopAsync(int userId, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var playbackResult = await partyPlaybackService.UpdateIntentAsync(
            sessionResult.Data.ApiKey,
            PlaybackIntent.Pause,
            null,
            userId,
            sessionResult.Data.PlaybackRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!playbackResult.IsSuccess)
        {
            return new OperationResult<bool>(playbackResult.Messages)
            {
                Type = playbackResult.Type,
                Data = false
            };
        }

        // Actually pause on the backend
        var backendResult = await playbackBackendService.PauseAsync(cancellationToken).ConfigureAwait(false);

        if (!backendResult.IsSuccess)
        {
            Logger.Warning("[SubsonicJukeboxService] Failed to pause on backend: {Messages}", string.Join(", ", backendResult.Messages ?? []));
            return new OperationResult<bool>(backendResult.Messages)
            {
                Type = backendResult.Type,
                Data = false
            };
        }

        Logger.Information("[SubsonicJukeboxService] Paused playback");
        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> SkipAsync(int userId, int? index, int? offset, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var playbackResult = await partyPlaybackService.UpdateIntentAsync(
            sessionResult.Data.ApiKey,
            PlaybackIntent.Skip,
            null,
            userId,
            sessionResult.Data.PlaybackRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!playbackResult.IsSuccess)
        {
            return new OperationResult<bool>(playbackResult.Messages)
            {
                Type = playbackResult.Type,
                Data = false
            };
        }

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> AddAsync(int userId, IEnumerable<string> songIds, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var songApiKeys = songIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToList();

        if (!songApiKeys.Any())
        {
            return new OperationResult<bool>("No valid song IDs provided")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var queueResult = await partyQueueService.GetQueueAsync(sessionResult.Data.ApiKey, cancellationToken).ConfigureAwait(false);
        var addResult = await partyQueueService.AddItemsAsync(
            sessionResult.Data.ApiKey,
            songApiKeys,
            userId,
            "subsonic",
            queueResult.Data.Revision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!addResult.IsSuccess)
        {
            return new OperationResult<bool>(addResult.Messages)
            {
                Type = addResult.Type,
                Data = false
            };
        }

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> ClearAsync(int userId, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var queueResult = await partyQueueService.GetQueueAsync(sessionResult.Data.ApiKey, cancellationToken).ConfigureAwait(false);
        var clearResult = await partyQueueService.ClearAsync(
            sessionResult.Data.ApiKey,
            userId,
            queueResult.Data.Revision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!clearResult.IsSuccess)
        {
            return new OperationResult<bool>(clearResult.Messages)
            {
                Type = clearResult.Type,
                Data = false
            };
        }

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> RemoveAsync(int userId, int index, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var queueResult = await partyQueueService.GetQueueAsync(sessionResult.Data.ApiKey, cancellationToken).ConfigureAwait(false);
        var items = queueResult.Data.Items.ToList();

        if (index < 0 || index >= items.Count)
        {
            return new OperationResult<bool>("Invalid index")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var itemToRemove = items[index];
        var removeResult = await partyQueueService.RemoveItemAsync(
            sessionResult.Data.ApiKey,
            itemToRemove.ApiKey,
            userId,
            queueResult.Data.Revision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!removeResult.IsSuccess)
        {
            return new OperationResult<bool>(removeResult.Messages)
            {
                Type = removeResult.Type,
                Data = false
            };
        }

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> ShuffleAsync(int userId, CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (!configuration.GetValue<bool>(SettingRegistry.JukeboxEnabled))
        {
            return new OperationResult<bool>("Jukebox is not enabled")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        var sessionResult = await GetOrCreateJukeboxSessionAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return new OperationResult<bool>("Failed to get jukebox session")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.QueueItems)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionResult.Data.ApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null || !session.QueueItems.Any())
        {
            return new OperationResult<bool> { Data = true };
        }

        var items = session.QueueItems.ToList();
        var random = new Random();
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i].SortOrder, items[j].SortOrder) = (items[j].SortOrder, items[i].SortOrder);
        }

        var newSortOrder = 0;
        foreach (var item in items)
        {
            item.SortOrder = newSortOrder++;
        }

        session.QueueRevision++;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        CacheManager.Remove($"urn:party:queue:{sessionResult.Data.ApiKey}");

        return new OperationResult<bool> { Data = true };
    }

    private async Task<OperationResult<PartySession>> GetOrCreateJukeboxSessionAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.PlaybackState)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == JukeboxSessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session != null)
        {
            // Ensure user is a participant
            if (!session.Participants.Any(p => p.UserId == userId))
            {
                var participant = new PartySessionParticipant
                {
                    PartySessionId = session.Id,
                    UserId = userId,
                    Role = PartyRole.DJ,
                    JoinedAt = SystemClock.Instance.GetCurrentInstant(),
                    IsBanned = false
                };
                scopedContext.PartySessionParticipants.Add(participant);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                Logger.Debug("[SubsonicJukeboxService] Added user {UserId} as participant to jukebox session", userId);
            }
            return new OperationResult<PartySession> { Data = session };
        }

        session = new PartySession
        {
            ApiKey = JukeboxSessionApiKey,
            Name = "Subsonic Jukebox",
            OwnerUserId = userId,
            Status = PartySessionStatus.Active,
            QueueRevision = 0,
            PlaybackRevision = 0,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        scopedContext.PartySessions.Add(session);

        var playbackState = new PartyPlaybackState
        {
            PartySessionId = session.Id,
            PositionSeconds = 0,
            IsPlaying = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        scopedContext.PartyPlaybackStates.Add(playbackState);
        session.PlaybackState = playbackState;

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Add the owner as a participant with Owner role
        var ownerParticipant = new PartySessionParticipant
        {
            PartySessionId = session.Id,
            UserId = userId,
            Role = PartyRole.Owner,
            JoinedAt = SystemClock.Instance.GetCurrentInstant(),
            IsBanned = false
        };
        scopedContext.PartySessionParticipants.Add(ownerParticipant);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information("[SubsonicJukeboxService] Created jukebox session for user {UserId}", userId);

        return new OperationResult<PartySession> { Data = session };
    }

    private async Task<JukeboxPlaylistResponse> BuildJukeboxPlaylistAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        var queueResult = await partyQueueService.GetQueueAsync(sessionApiKey, cancellationToken).ConfigureAwait(false);
        var items = queueResult.Data.Items.ToList();

        if (!items.Any())
        {
            return new JukeboxPlaylistResponse(
                Entries: [],
                Username: "system",
                Comment: null,
                IsPublic: false,
                SongCount: 0,
                Duration: 0);
        }

        // Fetch song metadata
        var songApiKeys = items.Select(x => x.SongApiKey).Distinct().ToList();

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var songs = await scopedContext.Songs
            .AsNoTracking()
            .Include(x => x.Album)
            .ThenInclude(x => x.Artist)
            .Where(x => songApiKeys.Contains(x.ApiKey))
            .ToDictionaryAsync(x => x.ApiKey, cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<JukeboxEntryResponse>();
        int totalDuration = 0;

        foreach (var item in items.OrderBy(x => x.SortOrder))
        {
            if (songs.TryGetValue(item.SongApiKey, out var song))
            {
                // song.Duration is in milliseconds, convert to seconds for the API response
                var durationSeconds = (int)(song.Duration / 1000);
                var entry = new JukeboxEntryResponse(
                    Id: song.ApiKey.ToString(),
                    Parent: song.Album?.ApiKey.ToString() ?? Guid.Empty.ToString(),
                    Title: song.Title,
                    Artist: song.Album?.Artist?.Name ?? "Unknown Artist",
                    Album: song.Album?.Name ?? "Unknown Album",
                    Year: song.Album?.ReleaseDate.Year ?? 0,
                    Genre: null,
                    CoverArt: song.Album?.ApiKey.ToString(),
                    Duration: durationSeconds,
                    BitRate: song.BitRate,
                    Path: $"/song/{song.ApiKey}",
                    TranscodedContentType: null,
                    TranscodedSuffix: null,
                    IsDir: false,
                    IsVideo: false,
                    Type: "music");

                entries.Add(entry);
                totalDuration += durationSeconds;
            }
            else
            {
                // Fallback if song not found
                entries.Add(new JukeboxEntryResponse(
                    Id: item.SongApiKey.ToString(),
                    Parent: item.SongApiKey.ToString(),
                    Title: $"Song {item.SongApiKey:N}",
                    Artist: "Unknown",
                    Album: "Unknown",
                    Year: 0,
                    Genre: null,
                    CoverArt: null,
                    Duration: 0,
                    BitRate: 0,
                    Path: "",
                    TranscodedContentType: null,
                    TranscodedSuffix: null,
                    IsDir: false,
                    IsVideo: false,
                    Type: "music"));
            }
        }

        return new JukeboxPlaylistResponse(
            Entries: entries,
            Username: "system",
            Comment: null,
            IsPublic: false,
            SongCount: entries.Count,
            Duration: totalDuration);
    }

    private static int FindCurrentIndex(List<JukeboxEntryResponse> entries, Guid currentItemApiKey)
    {
        var index = entries.FindIndex(e => Guid.TryParse(e.Id, out var id) && id == currentItemApiKey);
        return index >= 0 ? index : 0;
    }
}
