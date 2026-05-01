using System.Collections.Concurrent;
using System.Globalization;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;
using Melodee.Common.Utility;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Album = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;
using Artist = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
/// Callback to persist artists and relations to the database. Returns a function to lookup artist by MusicBrainzArtistId.
/// </summary>
public delegate Task<Func<long, Artist?>> ArtistPersistCallback(
    IReadOnlyCollection<Artist> artists,
    IReadOnlyCollection<ArtistRelation> relations,
    ImportProgressCallback? progressCallback,
    CancellationToken cancellationToken);

public abstract class MusicBrainzRepositoryBase(ILogger logger, IMelodeeConfigurationFactory configurationFactory)
    : IMusicBrainzRepository
{
    public const int MaxIndexSize = 255;

    protected readonly ConcurrentBag<Album> LoadedMaterializedAlbums = [];
    protected readonly ConcurrentBag<ArtistRelation> LoadedMaterializedArtistRelations = [];

    protected readonly ConcurrentBag<Artist> LoadedMaterializedArtists = [];
    protected ArtistAlias[] LoadedArtistAliases = [];
    protected ArtistCreditName[] LoadedArtistCreditNames = [];
    protected ArtistCredit[] LoadedArtistCredits = [];

    protected Models.Artist[] LoadedArtists = [];
    protected LinkArtistToArtist[] LoadedLinkArtistToArtists = [];
    protected Link[] LoadedLinks = [];
    protected LinkType[] LoadedLinkTypes = [];
    protected ReleaseGroupMeta[] LoadedReleaseGroupMetas = [];
    protected ReleaseGroup[] LoadedReleaseGroups = [];

    protected Release[] LoadedReleases = [];
    protected ReleaseCountry[] LoadedReleasesCountries = [];
    protected ReleaseTag[] LoadedReleaseTags = [];
    protected Tag[] LoadedTags = [];

    protected ILogger Logger { get; } = logger;
    protected IMelodeeConfigurationFactory ConfigurationFactory { get; } = configurationFactory;

    public abstract Task<Album?> GetAlbumByMusicBrainzId(Guid musicBrainzId,
        CancellationToken cancellationToken = default);

    public abstract Task<PagedResult<ArtistSearchResult>> SearchArtist(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<bool>> ImportData(
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);

    public abstract Task<OperationResult<bool>> ImportData(
        MusicBrainzImportRequest request,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default);

    protected static T[] LoadDataFromFileAsync<T>(string file, Func<string[], T> constructor,
        CancellationToken cancellationToken = default) where T : notnull
    {
        if (!File.Exists(file))
        {
            return [];
        }

        var result = new ConcurrentBag<T>();
        Parallel.ForEach(File.ReadLines(file), lineFromFile =>
        {
            var parts = lineFromFile.Split('\t');
            result.Add(constructor(parts));
        });
        return result.ToArray();
    }

    /// <summary>
    /// Clears intermediate data arrays to free memory after they've been processed into materialized forms.
    /// Call this after processing artists to free memory before processing albums.
    /// </summary>
    protected void ClearArtistIntermediateData()
    {
        LoadedArtists = [];
        LoadedArtistAliases = [];
        LoadedLinkTypes = [];
        LoadedLinks = [];
        LoadedLinkArtistToArtists = [];
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Clears intermediate data arrays for album/release data to free memory.
    /// </summary>
    protected void ClearAlbumIntermediateData()
    {
        LoadedReleases = [];
        LoadedReleasesCountries = [];
        LoadedReleaseTags = [];
        LoadedTags = [];
        LoadedReleaseGroups = [];
        LoadedReleaseGroupMetas = [];
        LoadedArtistCredits = [];
        LoadedArtistCreditNames = [];
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    protected async Task<string> StoragePath(CancellationToken cancellationToken = default)
    {
        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var storagePath = configuration.GetValue<string>(SettingRegistry.SearchEngineMusicBrainzStoragePath);
        if (storagePath == null || !System.IO.Directory.Exists(storagePath))
        {
            throw new Exception(
                "MusicBrainz storage path is invalid [{SettingRegistry.SearchEngineMusicBrainzStoragePath}]");
        }

        return storagePath;
    }

    /// <summary>
    /// Clears all materialized data collections to free memory.
    /// </summary>
    protected void ClearAllMaterializedData()
    {
        LoadedMaterializedArtists.Clear();
        LoadedMaterializedArtistRelations.Clear();
        LoadedMaterializedAlbums.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Load and process MusicBrainz data files with memory-efficient phased processing.
    /// Artists are persisted via callback before album processing to minimize memory usage.
    /// </summary>
    /// <param name="artistPersistCallback">Callback to persist artists and get lookup function</param>
    /// <param name="progressCallback">Progress reporting callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected async Task LoadDataFromMusicBrainzFiles(
        ArtistPersistCallback? artistPersistCallback,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var storagePath = await StoragePath(cancellationToken).ConfigureAwait(false);

        // Phase 1: Load and process artist data (artists, aliases, links)
        progressCallback?.Invoke("Loading Artist Data", 0, 6, "Loading artist file...");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded artists"))
        {
            LoadedArtists = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/artist"), parts =>
                new Models.Artist
                {
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    MusicBrainzId = SafeParser.ToGuid(parts[1]) ?? Guid.Empty,
                    Name = parts[2].CleanString().TruncateLongString(MaxIndexSize)!,
                    NameNormalized = parts[2].CleanString().TruncateLongString(MaxIndexSize)!.ToNormalizedString() ??
                                     parts[2],
                    SortName = parts[3].CleanString(true).TruncateLongString(MaxIndexSize) ?? parts[2]
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Artist Data", 1, 6, $"Loaded {LoadedArtists.Length:N0} artists");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded artist_alias"))
        {
            LoadedArtistAliases = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/artist_alias"),
                parts => new ArtistAlias
                {
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    ArtistId = SafeParser.ToNumber<long>(parts[1]),
                    Name = parts[2],
                    Type = SafeParser.ToNumber<int>(parts[6]),
                    SortName = parts[7]
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Artist Data", 2, 6, $"Loaded {LoadedArtistAliases.Length:N0} artist aliases");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded link_type"))
        {
            LoadedLinkTypes = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/link_type"), parts =>
                new LinkType
                {
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    ParentId = SafeParser.ToNumber<long>(parts[1]),
                    ChildOrder = SafeParser.ToNumber<int>(parts[2]),
                    MusicBrainzId = SafeParser.ToGuid(parts[3]) ?? Guid.Empty,
                    EntityType0 = parts[4],
                    EntityType1 = parts[5],
                    Name = parts[6],
                    Description = parts[7],
                    LinkPhrase = parts[8],
                    ReverseLinkPhrase = parts[9],
                    HasDates = SafeParser.ToBoolean(parts[13]),
                    Entity0Cardinality = SafeParser.ToNumber<int>(parts[14]),
                    Entity1Cardinality = SafeParser.ToNumber<int>(parts[15])
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Artist Data", 3, 6, $"Loaded {LoadedLinkTypes.Length:N0} link types");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded link"))
        {
            LoadedLinks = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/link"), parts => new Link
            {
                Id = SafeParser.ToNumber<long>(parts[0]),
                LinkTypeId = SafeParser.ToNumber<long>(parts[1]),
                BeginDateYear = SafeParser.ToNumber<int?>(parts[2]),
                BeginDateMonth = SafeParser.ToNumber<int?>(parts[3]),
                BeginDateDay = SafeParser.ToNumber<int?>(parts[4]),
                EndDateYear = SafeParser.ToNumber<int?>(parts[5]),
                EndDateMonth = SafeParser.ToNumber<int?>(parts[6]),
                EndDateDay = SafeParser.ToNumber<int?>(parts[7]),
                IsEnded = SafeParser.ToBoolean(parts[10])
            }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Artist Data", 4, 6, $"Loaded {LoadedLinks.Length:N0} links");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded artist link"))
        {
            LoadedLinkArtistToArtists = LoadDataFromFileAsync(
                Path.Combine(storagePath, "staging/mbdump/l_artist_artist"), parts => new LinkArtistToArtist
                {
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    LinkId = SafeParser.ToNumber<long>(parts[1]),
                    Artist0 = SafeParser.ToNumber<long>(parts[2]),
                    Artist1 = SafeParser.ToNumber<long>(parts[3]),
                    LinkOrder = SafeParser.ToNumber<int>(parts[6]),
                    Artist0Credit = parts[7],
                    Artist1Credit = parts[8]
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Artist Data", 5, 6, $"Loaded {LoadedLinkArtistToArtists.Length:N0} artist links");

        // Materialize artists
        progressCallback?.Invoke("Processing Artists", 0, 2, "Building materialized artists...");
        var artistAliasDictionary =
            LoadedArtistAliases.GroupBy(x => x.ArtistId).ToDictionary(x => x.Key, x => x.ToArray());
        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: LoadedMaterializedArtists"))
        {
            Parallel.ForEach(LoadedArtists, artist =>
            {
                artistAliasDictionary.TryGetValue(artist.Id, out var aArtistAlias);
                LoadedMaterializedArtists.Add(new Artist
                {
                    MusicBrainzArtistId = artist.Id,
                    Name = artist.Name,
                    SortName = artist.SortName,
                    NameNormalized = artist.NameNormalized,
                    MusicBrainzIdRaw = artist.MusicBrainzId.ToString(),
                    AlternateNames = "".AddTags(aArtistAlias?.Select(x => x.Name.ToNormalizedString() ?? x.Name),
                        dontLowerCase: true)
                });
            });
        }
        progressCallback?.Invoke("Processing Artists", 1, 2, $"Created {LoadedMaterializedArtists.Count:N0} materialized artists");

        // Materialize artist relations (needs temporary dictionary)
        progressCallback?.Invoke("Processing Relations", 0, 1, "Building artist relations...");
        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: LoadedMaterializedArtistRelations"))
        {
            // Temporary dictionary for artist relation processing only
            var tempArtistDictionary = LoadedMaterializedArtists.ToDictionary(x => x.MusicBrainzArtistId, x => x);
            var loadedLinkDictionary = LoadedLinks.ToDictionary(x => x.Id, x => x);

            var artistLinks = LoadedLinkArtistToArtists.GroupBy(x => x.Artist0)
                .ToDictionary(x => x.Key, x => x.ToArray());
            var associatedArtistRelationType = SafeParser.ToNumber<int>(ArtistRelationType.Associated);
            Parallel.ForEach(artistLinks, artistLink =>
            {
                if (!tempArtistDictionary.TryGetValue(artistLink.Key, out var dbArtist))
                {
                    return;
                }

                foreach (var artistLinkRelation in artistLink.Value)
                {
                    if (!tempArtistDictionary.TryGetValue(artistLinkRelation.Artist1,
                            out var dbLinkedArtist))
                    {
                        continue;
                    }

                    loadedLinkDictionary.TryGetValue(artistLink.Key, out var link);
                    if (link != null)
                    {
                        LoadedMaterializedArtistRelations.Add(new ArtistRelation
                        {
                            ArtistId = dbArtist.Id,
                            RelatedArtistId = dbLinkedArtist.Id,
                            ArtistRelationType = associatedArtistRelationType,
                            SortOrder = artistLinkRelation.LinkOrder,
                            RelationStart = link.BeginDate,
                            RelationEnd = link.EndDate
                        });
                    }
                }
            });
            // tempArtistDictionary goes out of scope here
        }
        progressCallback?.Invoke("Processing Relations", 1, 1, $"Created {LoadedMaterializedArtistRelations.Count:N0} artist relations");

        // Convert to lists for efficient iteration (ConcurrentBag iteration is slow)
        // This is the ONLY copy we make - used for database import, then discarded
        var artistsList = LoadedMaterializedArtists.ToList();
        var relationsList = LoadedMaterializedArtistRelations.ToList();

        // Clear the ConcurrentBags immediately - we have the lists now
        LoadedMaterializedArtists.Clear();
        LoadedMaterializedArtistRelations.Clear();

        // Clear raw artist intermediate data after materialization to reduce peak memory use.
        Logger.Debug("MusicBrainzRepository: Clearing artist intermediate data to free memory...");
        ClearArtistIntermediateData();
        progressCallback?.Invoke("Memory Cleanup", 1, 1, "Freed artist intermediate data");

        // Force GC after clearing raw data
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Now persist artists to the database and get lookup function
        Func<long, Artist?>? artistLookup = null;
        if (artistPersistCallback != null)
        {
            Logger.Debug("MusicBrainzRepository: Persisting artists to database...");
            artistLookup = await artistPersistCallback(
                artistsList,
                relationsList,
                progressCallback,
                cancellationToken).ConfigureAwait(false);

            // Clear the lists - database has the data now, we have the lookup function
            Logger.Debug("MusicBrainzRepository: Clearing artist lists from memory...");
            artistsList.Clear();
            relationsList.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            progressCallback?.Invoke("Memory Cleanup", 1, 1, "Freed artist data after database import");
        }
        else
        {
            // Fallback: create in-memory dictionary (high memory usage)
            var loadedMaterializedArtistsDictionary = artistsList.ToDictionary(x => x.MusicBrainzArtistId, x => x);
            artistLookup = id => loadedMaterializedArtistsDictionary.GetValueOrDefault(id);
        }

        // Phase 2: Load and process release/album data
        progressCallback?.Invoke("Loading Album Data", 0, 8, "Loading artist credits...");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded artist_credit"))
        {
            LoadedArtistCredits = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/artist_credit"),
                parts => new ArtistCredit
                {
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    ArtistCount = SafeParser.ToNumber<int>(parts[2]),
                    Name = parts[1],
                    RefCount = SafeParser.ToNumber<int>(parts[3]),
                    Gid = SafeParser.ToGuid(parts[6]) ?? Guid.Empty
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 1, 8, $"Loaded {LoadedArtistCredits.Length:N0} artist credits");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded artist_credit_name"))
        {
            LoadedArtistCreditNames = LoadDataFromFileAsync(
                Path.Combine(storagePath, "staging/mbdump/artist_credit_name"), parts => new ArtistCreditName
                {
                    ArtistCreditId = SafeParser.ToNumber<long>(parts[0]),
                    Position = SafeParser.ToNumber<int>(parts[1]),
                    ArtistId = SafeParser.ToNumber<long>(parts[2]),
                    Name = parts[3]
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 2, 8, $"Loaded {LoadedArtistCreditNames.Length:N0} artist credit names");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded release"))
        {
            LoadedReleases = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/release"), parts =>
                new Release
                {
                    ArtistCreditId = SafeParser.ToNumber<long>(parts[3]),
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    MusicBrainzId = parts[1],
                    Name = parts[2].CleanString()!,
                    NameNormalized = parts[2].CleanString().TruncateLongString(MaxIndexSize).ToNormalizedString() ??
                                     parts[2],
                    SortName = parts[2].CleanString(true).TruncateLongString(MaxIndexSize) ?? parts[2],
                    ReleaseGroupId = SafeParser.ToNumber<long>(parts[4])
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 3, 8, $"Loaded {LoadedReleases.Length:N0} releases");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded release_country"))
        {
            LoadedReleasesCountries = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/release_country"),
                parts => new ReleaseCountry
                {
                    ReleaseId = SafeParser.ToNumber<long>(parts[0]),
                    CountryId = SafeParser.ToNumber<long>(parts[1]),
                    DateYear = SafeParser.ToNumber<int>(parts[2]),
                    DateMonth = SafeParser.ToNumber<int>(parts[3]),
                    DateDay = SafeParser.ToNumber<int>(parts[4])
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 4, 8, $"Loaded {LoadedReleasesCountries.Length:N0} release countries");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded tag"))
        {
            LoadedTags = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/tag"), parts => new Tag
            {
                Id = SafeParser.ToNumber<long>(parts[0]),
                Name = parts[1]
            }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 5, 8, $"Loaded {LoadedTags.Length:N0} tags");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded release_tag"))
        {
            LoadedReleaseTags = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/release_tag"), parts =>
                new ReleaseTag
                {
                    ReleaseId = SafeParser.ToNumber<long>(parts[0]),
                    TagId = SafeParser.ToNumber<long>(parts[1])
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 6, 8, $"Loaded {LoadedReleaseTags.Length:N0} release tags");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded release_group"))
        {
            LoadedReleaseGroups = LoadDataFromFileAsync(Path.Combine(storagePath, "staging/mbdump/release_group"),
                parts => new ReleaseGroup
                {
                    Id = SafeParser.ToNumber<long>(parts[0]),
                    MusicBrainzIdRaw = parts[1],
                    Name = parts[2],
                    ArtistCreditId = SafeParser.ToNumber<long>(parts[3]),
                    ReleaseType = SafeParser.ToNumber<int>(parts[4])
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 7, 8, $"Loaded {LoadedReleaseGroups.Length:N0} release groups");

        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: Loaded release_group_meta"))
        {
            LoadedReleaseGroupMetas = LoadDataFromFileAsync(
                Path.Combine(storagePath, "staging/mbdump/release_group_meta"), parts => new ReleaseGroupMeta
                {
                    ReleaseGroupId = SafeParser.ToNumber<long>(parts[0]),
                    DateYear = SafeParser.ToNumber<int>(parts[2]),
                    DateMonth = SafeParser.ToNumber<int>(parts[3]),
                    DateDay = SafeParser.ToNumber<int>(parts[4])
                }, cancellationToken);
        }
        progressCallback?.Invoke("Loading Album Data", 8, 8, $"Loaded {LoadedReleaseGroupMetas.Length:N0} release group metadata");

        // Materialize albums
        progressCallback?.Invoke("Processing Albums", 0, 1, $"Building {LoadedReleases.Length:N0} materialized albums...");
        using (Operation.At(LogEventLevel.Debug).Time("MusicBrainzRepository: LoadedMaterializedAlbums"))
        {
            var releaseCountriesDictionary = LoadedReleasesCountries.GroupBy(x => x.ReleaseId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var releaseGroupsDictionary =
                LoadedReleaseGroups.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.ToList());
            var releaseGroupsMetaDictionary = LoadedReleaseGroupMetas.GroupBy(x => x.ReleaseGroupId)
                .ToDictionary(x => x.Key, x => x.ToList());
            var artistCreditsDictionary =
                LoadedArtistCredits.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.ToList());
            var artistCreditsNamesDictionary = LoadedArtistCreditNames.GroupBy(x => x.ArtistCreditId)
                .ToDictionary(x => x.Key, x => x.ToList());

            Parallel.ForEach(LoadedReleases, release =>
            {
                releaseCountriesDictionary.TryGetValue(release.Id, out var releaseCountries);
                releaseGroupsDictionary.TryGetValue(release.ReleaseGroupId, out var releaseReleaseGroups);
                var releaseCountry = releaseCountries?.OrderBy(x => x.ReleaseDate).FirstOrDefault();
                var releaseGroup = releaseReleaseGroups?.FirstOrDefault();

                var releaseArtist = artistLookup(release.ArtistCreditId);

                if (releaseGroup != null && !(releaseCountry?.IsValid ?? false))
                {
                    releaseGroupsMetaDictionary.TryGetValue(release.ReleaseGroupId, out var releaseGroupsMeta);
                    var releaseGroupMeta = releaseGroupsMeta?.OrderBy(x => x.ReleaseDate).FirstOrDefault();
                    if (releaseGroupMeta?.IsValid ?? false)
                    {
                        releaseCountry = new ReleaseCountry
                        {
                            ReleaseId = release.Id,
                            DateDay = releaseGroupMeta.DateDay,
                            DateMonth = releaseGroupMeta.DateMonth,
                            DateYear = releaseGroupMeta.DateYear
                        };
                    }
                }

                string? contributorIds = null;

                artistCreditsDictionary.TryGetValue(release.ArtistCreditId, out var releaseArtistCredits);
                var artistCredit = releaseArtistCredits?.FirstOrDefault();
                if (artistCredit != null)
                {
                    artistCreditsNamesDictionary.TryGetValue(artistCredit.Id, out var releaseArtistCreditNames);
                    var artistCreditName = releaseArtistCreditNames?.OrderBy(x => x.Position).FirstOrDefault();
                    if (artistCreditName != null)
                    {
                        releaseArtist = artistLookup(artistCreditName.ArtistId);
                    }

                    var artistCreditNameArtistId = artistCreditName?.ArtistId ?? 0;
                    contributorIds = releaseArtistCreditNames == null
                        ? null
                        : "".AddTags(releaseArtistCreditNames
                            .Where(x => x.ArtistId != artistCreditNameArtistId)
                            .Select(x => x.ArtistId.ToString()));
                }

                if (releaseArtist != null && releaseGroup != null && (releaseCountry?.IsValid ?? false))
                {
                    if (release.Name.Nullify() != null)
                    {
                        LoadedMaterializedAlbums.Add(new Album
                        {
                            MusicBrainzArtistId = releaseArtist.MusicBrainzArtistId,
                            ContributorIds = contributorIds,
                            MusicBrainzIdRaw = release.MusicBrainzId,
                            Name = release.Name,
                            NameNormalized = release.NameNormalized ?? release.Name,
                            ReleaseDate = releaseCountry.ReleaseDate,
                            ReleaseGroupMusicBrainzIdRaw = releaseGroup.MusicBrainzIdRaw,
                            ReleaseType = releaseGroup.ReleaseType,
                            SortName = release.SortName ?? release.Name
                        });
                    }
                }
            });
        }
        progressCallback?.Invoke("Processing Albums", 1, 1, $"Created {LoadedMaterializedAlbums.Count:N0} materialized albums");

        // Clear album intermediate data to free memory before database import
        Logger.Debug("MusicBrainzRepository: Clearing album intermediate data to free memory...");
        ClearAlbumIntermediateData();
        progressCallback?.Invoke("Memory Cleanup", 1, 1, "Freed album intermediate data");
    }

    /// <summary>
    ///     This is because "1994-02-29" isn't a date.
    /// </summary>
    public static DateTime? ParseJackedUpMusicBrainzDate(string? dateRaw)
    {
        if (dateRaw == null)
        {
            return null;
        }

        if (DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, out var date))
        {
            return date;
        }

        return null;
    }
}
