using System.Diagnostics;
using System.Web;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Search;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;

namespace Melodee.Common.Services;

public sealed class SearchService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    UserProfileService userProfileService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    PodcastService podcastService,
    IMusicBrainzRepository musicBrainzRepository,
    IBus bus)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<OperationResult<SearchResult>> DoSearchAsync(Guid userApiKey,
        string? userAgent,
        string? searchTerm,
        short albumPage,
        short artistPage,
        short songPage,
        short pageSize,
        SearchInclude include,
        Guid? filterByArtistId = null,
        CancellationToken cancellationToken = default)
    {
        var totalArtists = 0;
        var totalAlbums = 0;
        var totalSongs = 0;
        var totalPlaylists = 0;
        var totalMusicBrainzArtists = 0;
        var totalPodcastChannels = 0;
        var totalPodcastEpisodes = 0;

        List<ArtistDataInfo> artists = [];
        List<AlbumDataInfo> albums = [];
        List<SongDataInfo> songs = [];
        List<PlaylistDataInfo> playlists = [];
        List<ArtistDataInfo> musicBrainzArtists = [];
        List<PodcastChannelDataInfo> podcastChannels = [];
        List<PodcastEpisodeDataInfo> podcastEpisodes = [];

        if (searchTerm.Nullify() == null)
        {
            return new OperationResult<SearchResult>(OperationResponseType.ValidationFailure, "No Search Term Provided")
            {
                Data = new SearchResult([], 0, [], 0, [], 0, [], 0, [], 0, [], 0, [], 0)
            };
        }

        var user = await userProfileService.GetByApiKeyAsync(userApiKey, cancellationToken).ConfigureAwait(false);

        var startTicks = Stopwatch.GetTimestamp();

        var searchTermNormalized = searchTerm?.ToNormalizedString() ?? searchTerm ?? string.Empty;

        // If page size is 0, return empty results immediately
        if (pageSize == 0)
        {
            return new OperationResult<SearchResult>
            {
                Data = new SearchResult([], 0, [], 0, [], 0, [], 0, [], 0, [], 0, [], 0)
            };
        }

        if (include.HasFlag(SearchInclude.Artists))
        {
            var artistResult = await artistService.ListAsync(new PagedRequest
            {
                Page = artistPage,
                PageSize = pageSize,
                FilterBy =
                [
                    new FilterOperatorInfo(nameof(ArtistDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(ArtistDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ]
            }, cancellationToken);
            totalArtists = artistResult.TotalCount;
            artists = artistResult.Data.OrderBy(x => x.Name).ToList();
        }

        if (include.HasFlag(SearchInclude.Albums))
        {
            var albumFilters = new List<FilterOperatorInfo>();

            // Add search term filters (these should be OR'd together)
            albumFilters.Add(new FilterOperatorInfo(nameof(AlbumDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator));
            albumFilters.Add(new FilterOperatorInfo(nameof(AlbumDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator));

            // Add artist filter separately (this should be AND'd with the search results)
            if (filterByArtistId.HasValue)
            {
                albumFilters.Add(new FilterOperatorInfo(nameof(AlbumDataInfo.ArtistApiKey), FilterOperator.Equals, filterByArtistId.Value, FilterOperatorInfo.AndJoinOperator));
            }

            var albumResult = await albumService.ListAsync(new PagedRequest
            {
                Page = albumPage,
                PageSize = pageSize,
                FilterBy = albumFilters.ToArray()
            }, cancellationToken);
            totalAlbums = albumResult.TotalCount;
            albums = albumResult.Data.OrderBy(x => x.Name).ToList();

            if (include.HasFlag(SearchInclude.Contributors))
            {
                var contributorAlbumsResult = await albumService.ListForContributorsAsync(new PagedRequest
                {
                    Page = albumPage,
                    PageSize = pageSize
                }, HttpUtility.UrlDecode(searchTerm) ?? Guid.NewGuid().ToString(), cancellationToken);
                if (contributorAlbumsResult.TotalCount > 0)
                {
                    albums = albums.Union(contributorAlbumsResult.Data).Distinct().ToList();
                }
            }
        }

        if (include.HasFlag(SearchInclude.Songs))
        {
            if (filterByArtistId.HasValue)
            {
                var artist = await artistService.GetByApiKeyAsync(filterByArtistId.Value, cancellationToken);
                if (!artist.IsSuccess || artist.Data == null)
                {
                    return new OperationResult<SearchResult>(OperationResponseType.NotFound, "Artist not found")
                    {
                        Data = new SearchResult([], 0, [], 0, [], 0, [], 0, [], 0, [], 0, [], 0)
                    };
                }
            }
            var songFilters = new List<FilterOperatorInfo>
            {
                new FilterOperatorInfo(nameof(SongDataInfo.TitleNormalized), FilterOperator.Contains, searchTermNormalized)
            };
            if (filterByArtistId.HasValue)
            {
                songFilters.Add(new FilterOperatorInfo(nameof(SongDataInfo.ArtistApiKey), FilterOperator.Equals, filterByArtistId.Value, FilterOperatorInfo.AndJoinOperator));
            }
            var songResult = await songService.ListAsync(new PagedRequest
            {
                Page = songPage,
                PageSize = pageSize,
                FilterBy = songFilters.ToArray()
            }, user.Data!.Id, cancellationToken);
            totalSongs = songResult.TotalCount;
            songs = songResult.Data.OrderBy(x => x.ArtistName).ThenBy(x => x.AlbumName).ToList();

            if (include.HasFlag(SearchInclude.Contributors))
            {
                var contributorSongResult = await songService.ListForContributorsAsync(new PagedRequest
                {
                    Page = songPage,
                    PageSize = pageSize
                }, HttpUtility.UrlDecode(searchTerm) ?? Guid.NewGuid().ToString(), cancellationToken);
                if (contributorSongResult.TotalCount > 0)
                {
                    songs = songs.Union(contributorSongResult.Data).Distinct().ToList();
                }
            }
        }

        if (include.HasFlag(SearchInclude.MusicBrainz))
        {
            var searchResult = await musicBrainzRepository.SearchArtist(new ArtistQuery
            {
                Name = searchTerm ?? string.Empty
            }, artistPage * pageSize, cancellationToken);
            totalMusicBrainzArtists = searchResult.TotalCount;
            musicBrainzArtists = searchResult.Data
                .Where(x => x.MusicBrainzId != null)
                .Select(x => ArtistDataInfo.BlankArtistDataInfo with
                {
                    ApiKey = x.MusicBrainzId!.Value,
                    Name = x.Name,
                    NameNormalized = x.Name.ToNormalizedString() ?? x.Name
                })
                .ToList();
        }

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var podcastEnabled = configuration.GetValue<bool>(SettingRegistry.PodcastEnabled);

        if (include.HasFlag(SearchInclude.PodcastChannels) && user.Data!.HasPodcastRole && podcastEnabled)
        {
            var podcastChannelRequest = new PagedRequest
            {
                Page = 1,
                PageSize = pageSize,
                FilterBy = [new FilterOperatorInfo(nameof(PodcastChannelDataInfo.TitleNormalized), FilterOperator.Contains, searchTermNormalized)]
            };
            var podcastChannelResult = await podcastService.ListChannelsAsync(podcastChannelRequest, user.Data.Id, cancellationToken);
            totalPodcastChannels = podcastChannelResult.TotalCount;
            podcastChannels = podcastChannelResult.Data.ToList();
        }

        if (include.HasFlag(SearchInclude.PodcastEpisodes) && user.Data!.HasPodcastRole && podcastEnabled)
        {
            var podcastEpisodeRequest = new PagedRequest
            {
                Page = 1,
                PageSize = pageSize,
                FilterBy = [new FilterOperatorInfo(nameof(PodcastEpisodeDataInfo.TitleNormalized), FilterOperator.Contains, searchTermNormalized)]
            };
            var podcastEpisodeResult = await podcastService.ListEpisodesAsync(podcastEpisodeRequest, user.Data.Id, cancellationToken);
            totalPodcastEpisodes = podcastEpisodeResult.TotalCount;
            podcastEpisodes = podcastEpisodeResult.Data.ToList();
        }

        var elapsedTime = Stopwatch.GetElapsedTime(startTicks);

        await bus.SendLocal(new SearchHistoryEvent
        {
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
            ByUserApiKey = userApiKey,
            ByUserAgent = userAgent,
            SearchQuery = searchTerm?.ToBase64(),
            FoundArtistsCount = artists.Count,
            FoundAlbumsCount = albums.Count,
            FoundSongsCount = songs.Count,
            FoundOtherItems = musicBrainzArtists.Count,
            SearchDurationInMs = elapsedTime.TotalMilliseconds
        }).ConfigureAwait(false);
        return new OperationResult<SearchResult>
        {
            Data = new SearchResult(artists.ToArray(),
                totalArtists,
                albums.ToArray(),
                totalAlbums,
                songs.ToArray(),
                totalSongs,
                playlists.ToArray(),
                totalPlaylists,
                musicBrainzArtists.ToArray(),
                totalMusicBrainzArtists,
                podcastChannels.ToArray(),
                totalPodcastChannels,
                podcastEpisodes.ToArray(),
                totalPodcastEpisodes)
        };
    }

    public async Task<OperationResult<OpenSubsonicSearchResult>> DoOpenSubsonicSearchAsync(
        Guid userApiKey,
        string? searchQuery,
        int artistOffset = 0,
        int artistCount = 20,
        int albumOffset = 0,
        int albumCount = 20,
        int songOffset = 0,
        int songCount = 20,
        CancellationToken cancellationToken = default)
    {
        var user = await userProfileService.GetByApiKeyAsync(userApiKey, cancellationToken).ConfigureAwait(false);
        if (!user.IsSuccess || user.Data == null)
        {
            return new OperationResult<OpenSubsonicSearchResult>("User not found")
            {
                Type = OperationResponseType.NotFound,
                Data = new OpenSubsonicSearchResult([], [], [])
            };
        }

        var searchTermNormalized = searchQuery?.ToNormalizedString() ?? searchQuery ?? string.Empty;
        var artistResults = new List<ArtistDataInfo>();
        var albumResults = new List<AlbumDataInfo>();
        var songResults = new List<SongDataInfo>();

        if (searchQuery.Nullify() == null)
        {
            searchTermNormalized = string.Empty;
        }

        // Search artists
        if (artistCount > 0)
        {
            var artistRequest = new PagedRequest
            {
                Page = (artistOffset / artistCount) + 1,
                PageSize = (short)artistCount
            };

            if (searchQuery.Nullify() != null)
            {
                artistRequest.FilterBy =
                [
                    new FilterOperatorInfo(nameof(ArtistDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(ArtistDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ];
            }

            var artistResult = await artistService.ListAsync(artistRequest, cancellationToken).ConfigureAwait(false);
            artistResults = artistResult.Data.OrderBy(x => x.Name).ToList();
        }

        // Search albums
        if (albumCount > 0)
        {
            var albumRequest = new PagedRequest
            {
                Page = (albumOffset / albumCount) + 1,
                PageSize = (short)albumCount
            };

            if (searchQuery.Nullify() != null)
            {
                albumRequest.FilterBy =
                [
                    new FilterOperatorInfo(nameof(AlbumDataInfo.NameNormalized), FilterOperator.Contains, searchTermNormalized),
                    new FilterOperatorInfo(nameof(AlbumDataInfo.AlternateNames), FilterOperator.Contains, searchTermNormalized, FilterOperatorInfo.OrJoinOperator)
                ];
            }

            var albumResult = await albumService.ListAsync(albumRequest, cancellationToken).ConfigureAwait(false);
            albumResults = albumResult.Data.OrderBy(x => x.Name).ToList();
        }

        // Search songs
        if (songCount > 0)
        {
            var songRequest = new PagedRequest
            {
                Page = (songOffset / songCount) + 1,
                PageSize = (short)songCount
            };

            if (searchQuery.Nullify() != null)
            {
                songRequest.FilterBy =
                [
                    new FilterOperatorInfo(nameof(SongDataInfo.TitleNormalized), FilterOperator.Contains, searchTermNormalized)
                ];
            }

            var songResult = await songService.ListAsync(songRequest, user.Data.Id, cancellationToken).ConfigureAwait(false);
            songResults = songResult.Data.OrderBy(x => x.ArtistName).ThenBy(x => x.AlbumName).ToList();
        }

        return new OperationResult<OpenSubsonicSearchResult>
        {
            Data = new OpenSubsonicSearchResult(artistResults.ToArray(), albumResults.ToArray(), songResults.ToArray())
        };
    }
}

public record OpenSubsonicSearchResult(
    ArtistDataInfo[] Artists,
    AlbumDataInfo[] Albums,
    SongDataInfo[] Songs);
