using System.Text.RegularExpressions;
using Fastenshtein;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Extensions;
using Melodee.Common.Services.Models.ArtistDuplicate;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for detecting duplicate artists based on external IDs, name similarity, and album overlap.
/// </summary>
public sealed partial class ArtistDuplicateFinder
{
    private readonly ILogger _logger;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;
    private readonly IMelodeeConfigurationFactory _configurationFactory;

    private const double ExternalIdScoreSpotify = 0.95;
    private const double ExternalIdScoreMusicBrainz = 0.95;
    private const double ExternalIdScoreOther = 0.92;
    private const double ExternalIdMultipleBonus = 0.05;
    private const double NameExactMatchScore = 0.90;
    private const double NameFirstLastReversalScore = 0.92;
    private const double HighSimilarityThreshold = 0.85;
    private const double MultiSignalBonus = 0.05;
    private const double AlbumOverlapHighThreshold = 0.8;
    private const int StreamingBatchSize = 5000;

    private const string DefaultIgnoredArticles = "THE|A|AN|DJ|MC";

    private static readonly string[] CommonGenericAlbums =
    [
        "GREATESTHITS", "BESTOF", "THEBESTOF", "ANTHOLOGY", "COLLECTION",
        "COMPLETEWORKS", "LIVECONCERT", "UNPLUGGED", "LIVEALBUM", "COMPILATION"
    ];

    private static readonly Regex LastFirstCommaRegex = LastFirstCommaPattern();

    public ArtistDuplicateFinder(
        ILogger logger,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        IMelodeeConfigurationFactory configurationFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
        _configurationFactory = configurationFactory;
    }

    public async Task<IReadOnlyList<ArtistDuplicateGroup>> FindDuplicatesAsync(
        ArtistDuplicateSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("Starting artist duplicate detection with MinScore={MinScore}, Limit={Limit}, Source={Source}, ArtistId={ArtistId}",
            criteria.MinScore, criteria.Limit, criteria.Source, criteria.ArtistId);

        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var ignoredArticles = configuration.GetIgnoredArticles() ?? DefaultIgnoredArticles;

        _logger.Debug("Using ignored articles for normalization: {IgnoredArticles}", ignoredArticles);

        var (artists, candidatePairs) = await LoadArtistsAndBuildCandidatesAsync(criteria, ignoredArticles, cancellationToken);

        if (artists.Count == 0)
        {
            _logger.Information("No artists found matching criteria");
            return [];
        }

        _logger.Debug("Loaded {ArtistCount} artists for duplicate detection", artists.Count);
        _logger.Debug("Generated {PairCount} candidate pairs", candidatePairs.Count);

        var scoredPairs = ScorePairs(candidatePairs, artists, criteria, ignoredArticles);

        var groups = BuildGroups(scoredPairs, artists, criteria);

        _logger.Information("Found {GroupCount} duplicate groups", groups.Count);

        return groups;
    }

    private async Task<(List<ArtistReadModel> Artists, List<(int LeftId, int RightId)> Pairs)> LoadArtistsAndBuildCandidatesAsync(
        ArtistDuplicateSearchCriteria criteria,
        string ignoredArticles,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (criteria.ArtistId.HasValue)
        {
            var targetArtist = await context.Artists
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == criteria.ArtistId.Value, cancellationToken);

            if (targetArtist == null)
            {
                return ([], []);
            }
        }

        var artists = new List<ArtistReadModel>();
        var candidatePairs = new HashSet<(int, int)>();

        var externalIdBuckets = new Dictionary<(string Provider, string Value), List<int>>();
        var nameBuckets = new Dictionary<string, List<int>>();
        var sortedTokenBuckets = new Dictionary<string, List<int>>();

        var query = context.Artists
            .AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.ApiKey,
                a.Name,
                a.NameNormalized,
                a.SortName,
                a.AlbumCount,
                a.SongCount,
                a.SpotifyId,
                a.MusicBrainzId,
                a.DiscogsId,
                a.ItunesId,
                a.DeezerId,
                a.LastFmId,
                a.AmgId,
                a.WikiDataId,
                CreatedAtTicks = a.CreatedAt.ToUnixTimeTicks(),
                Albums = a.Albums.Select(album => new
                {
                    album.Id,
                    album.Name,
                    album.NameNormalized,
                    Year = (int?)album.ReleaseDate.Year
                }).ToList()
            });

        var batchCount = 0;
        await foreach (var a in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var artist = new ArtistReadModel(
                a.Id,
                a.ApiKey,
                a.Name,
                a.NameNormalized,
                a.SortName,
                a.AlbumCount,
                a.SongCount,
                a.SpotifyId,
                a.MusicBrainzId,
                a.DiscogsId,
                a.ItunesId,
                a.DeezerId,
                a.LastFmId,
                a.AmgId,
                a.WikiDataId,
                a.Albums.Select(album => new AlbumStub(
                    album.Id,
                    album.Name,
                    album.NameNormalized,
                    album.Year
                )).ToList(),
                a.CreatedAtTicks);

            if (!string.IsNullOrWhiteSpace(criteria.Source) && !artist.HasExternalIdForSource(criteria.Source))
            {
                continue;
            }

            artists.Add(artist);

            AddToBucketsAndGeneratePairs(artist, externalIdBuckets, nameBuckets, sortedTokenBuckets, candidatePairs, ignoredArticles);

            batchCount++;
            if (batchCount % StreamingBatchSize == 0)
            {
                _logger.Debug("Processed {Count} artists, {PairCount} candidate pairs so far", batchCount, candidatePairs.Count);
            }
        }

        var filteredPairs = candidatePairs.ToList();
        if (criteria.ArtistId.HasValue)
        {
            filteredPairs = filteredPairs.Where(p =>
                p.Item1 == criteria.ArtistId.Value ||
                p.Item2 == criteria.ArtistId.Value).ToList();
        }

        return (artists, filteredPairs);
    }

    private void AddToBucketsAndGeneratePairs(
        ArtistReadModel artist,
        Dictionary<(string Provider, string Value), List<int>> externalIdBuckets,
        Dictionary<string, List<int>> nameBuckets,
        Dictionary<string, List<int>> sortedTokenBuckets,
        HashSet<(int, int)> pairs,
        string ignoredArticles)
    {
        var externalIds = artist.GetExternalIds();
        foreach (var (provider, value) in externalIds)
        {
            var key = (provider.ToLowerInvariant(), value.ToUpperInvariant());
            if (!externalIdBuckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                externalIdBuckets[key] = bucket;
            }

            foreach (var existingId in bucket)
            {
                var left = Math.Min(existingId, artist.ArtistId);
                var right = Math.Max(existingId, artist.ArtistId);
                pairs.Add((left, right));
            }

            bucket.Add(artist.ArtistId);
        }

        var normalizedName = NormalizeName(artist.Name, ignoredArticles);
        if (!string.IsNullOrEmpty(normalizedName) && normalizedName.Length >= 3)
        {
            var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tokenCountBucket = tokens.Length switch
            {
                1 => "1",
                2 => "2",
                _ => "3+"
            };

            var trigram = normalizedName[..Math.Min(3, normalizedName.Length)];
            var bucketKey = $"{trigram}:{tokenCountBucket}";

            if (!nameBuckets.TryGetValue(bucketKey, out var nameBucket))
            {
                nameBucket = [];
                nameBuckets[bucketKey] = nameBucket;
            }

            if (nameBucket.Count < 1000)
            {
                foreach (var existingId in nameBucket)
                {
                    var left = Math.Min(existingId, artist.ArtistId);
                    var right = Math.Max(existingId, artist.ArtistId);
                    pairs.Add((left, right));
                }

                nameBucket.Add(artist.ArtistId);
            }

            if (tokens.Length == 2)
            {
                var sortedKey = string.Join(":", tokens.OrderBy(t => t));

                if (!sortedTokenBuckets.TryGetValue(sortedKey, out var sortedBucket))
                {
                    sortedBucket = [];
                    sortedTokenBuckets[sortedKey] = sortedBucket;
                }

                if (sortedBucket.Count < 100)
                {
                    foreach (var existingId in sortedBucket)
                    {
                        var left = Math.Min(existingId, artist.ArtistId);
                        var right = Math.Max(existingId, artist.ArtistId);
                        pairs.Add((left, right));
                    }

                    sortedBucket.Add(artist.ArtistId);
                }
            }
        }
    }

    private List<(int LeftId, int RightId, double Score, List<string> Reasons)> ScorePairs(
        List<(int LeftId, int RightId)> pairs,
        List<ArtistReadModel> artists,
        ArtistDuplicateSearchCriteria criteria,
        string ignoredArticles)
    {
        var artistIndex = artists.ToDictionary(a => a.ArtistId);
        var scoredPairs = new List<(int LeftId, int RightId, double Score, List<string> Reasons)>();

        foreach (var (leftId, rightId) in pairs)
        {
            if (!artistIndex.TryGetValue(leftId, out var leftArtist) ||
                !artistIndex.TryGetValue(rightId, out var rightArtist))
            {
                continue;
            }

            var (score, reasons) = ComputePairScore(leftArtist, rightArtist, ignoredArticles);

            var effectiveMinScore = criteria.IncludeLowConfidence ? 0.5 : criteria.MinScore;

            if (score >= effectiveMinScore)
            {
                scoredPairs.Add((leftId, rightId, score, reasons));
            }
        }

        return scoredPairs;
    }

    private (double Score, List<string> Reasons) ComputePairScore(ArtistReadModel left, ArtistReadModel right, string ignoredArticles)
    {
        var reasons = new List<string>();
        var externalIdScore = ComputeExternalIdScore(left, right, reasons);
        var nameScore = ComputeNameScore(left, right, reasons, ignoredArticles);
        var albumScore = ComputeAlbumOverlapScore(left, right, reasons);

        var baseScore = Math.Max(externalIdScore, Math.Max(nameScore, albumScore));
        var bonus = 0.0;

        var highSignals = new[] { externalIdScore, nameScore, albumScore }.Count(s => s >= 0.9);
        if (highSignals >= 2)
        {
            bonus += MultiSignalBonus;
        }

        if (reasons.Contains(ArtistDuplicateMatchReason.NameFirstLastReversal))
        {
            bonus += MultiSignalBonus;
        }

        if (albumScore >= AlbumOverlapHighThreshold && nameScore < 0.7)
        {
            bonus += MultiSignalBonus;
        }

        var finalScore = Math.Min(1.0, baseScore + bonus);

        return (finalScore, reasons);
    }

    private double ComputeExternalIdScore(ArtistReadModel left, ArtistReadModel right, List<string> reasons)
    {
        var leftIds = left.GetExternalIds();
        var rightIds = right.GetExternalIds();

        var sharedProviders = new List<string>();
        var maxScore = 0.0;

        foreach (var (provider, leftValue) in leftIds)
        {
            if (rightIds.TryGetValue(provider, out var rightValue) &&
                string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase))
            {
                sharedProviders.Add(provider);

                var providerScore = provider switch
                {
                    "spotify" => ExternalIdScoreSpotify,
                    "musicbrainz" => ExternalIdScoreMusicBrainz,
                    _ => ExternalIdScoreOther
                };

                maxScore = Math.Max(maxScore, providerScore);

                reasons.Add(provider switch
                {
                    "spotify" => ArtistDuplicateMatchReason.SharedSpotifyId,
                    "musicbrainz" => ArtistDuplicateMatchReason.SharedMusicBrainzId,
                    "discogs" => ArtistDuplicateMatchReason.SharedDiscogsId,
                    "itunes" => ArtistDuplicateMatchReason.SharedItunesId,
                    "deezer" => ArtistDuplicateMatchReason.SharedDeezerId,
                    "lastfm" => ArtistDuplicateMatchReason.SharedLastFmId,
                    "amg" => ArtistDuplicateMatchReason.SharedAmgId,
                    "wikidata" => ArtistDuplicateMatchReason.SharedWikiDataId,
                    _ => $"Shared{provider}Id"
                });
            }
        }

        if (sharedProviders.Count >= 2)
        {
            maxScore = Math.Min(1.0, maxScore + ExternalIdMultipleBonus);
            reasons.Add(ArtistDuplicateMatchReason.MultipleSharedExternalIds);
        }

        return maxScore;
    }

    private double ComputeNameScore(ArtistReadModel left, ArtistReadModel right, List<string> reasons, string ignoredArticles)
    {
        var leftNormalized = NormalizeName(left.Name, ignoredArticles);
        var rightNormalized = NormalizeName(right.Name, ignoredArticles);

        if (string.IsNullOrEmpty(leftNormalized) || string.IsNullOrEmpty(rightNormalized))
        {
            return 0.0;
        }

        if (string.Equals(leftNormalized, rightNormalized, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(ArtistDuplicateMatchReason.ExactNormalizedNameMatch);
            return NameExactMatchScore;
        }

        if (IsFirstLastReversal(left.Name, right.Name, left.SortName, right.SortName, ignoredArticles))
        {
            reasons.Add(ArtistDuplicateMatchReason.NameFirstLastReversal);
            return NameFirstLastReversalScore;
        }

        var tokenSimilarity = ComputeTokenSimilarity(leftNormalized, rightNormalized);
        var charSimilarity = ComputeCharacterSimilarity(leftNormalized, rightNormalized);

        var combinedScore = Math.Max(tokenSimilarity, charSimilarity);

        if (combinedScore >= HighSimilarityThreshold)
        {
            if (tokenSimilarity >= HighSimilarityThreshold)
            {
                reasons.Add(ArtistDuplicateMatchReason.HighTokenSimilarity);
            }

            if (charSimilarity >= HighSimilarityThreshold)
            {
                reasons.Add(ArtistDuplicateMatchReason.HighNameSimilarity);
            }
        }

        return combinedScore;
    }

    private double ComputeAlbumOverlapScore(ArtistReadModel left, ArtistReadModel right, List<string> reasons)
    {
        if (left.Albums.Count == 0 || right.Albums.Count == 0)
        {
            return 0.0;
        }

        var leftAlbumKeys = left.Albums
            .Where(a => !IsGenericAlbumTitle(a.TitleNormalized))
            .Select(a => NormalizeAlbumKey(a.TitleNormalized, a.Year))
            .ToHashSet();

        var rightAlbumKeys = right.Albums
            .Where(a => !IsGenericAlbumTitle(a.TitleNormalized))
            .Select(a => NormalizeAlbumKey(a.TitleNormalized, a.Year))
            .ToHashSet();

        if (leftAlbumKeys.Count == 0 || rightAlbumKeys.Count == 0)
        {
            return 0.0;
        }

        var intersection = leftAlbumKeys.Intersect(rightAlbumKeys).Count();
        var minCount = Math.Min(leftAlbumKeys.Count, rightAlbumKeys.Count);
        var overlapScore = (double)intersection / minCount;

        if (intersection >= 2)
        {
            reasons.Add(ArtistDuplicateMatchReason.SharedAlbums);
        }

        if (overlapScore >= AlbumOverlapHighThreshold)
        {
            reasons.Add(ArtistDuplicateMatchReason.HighAlbumOverlap);
        }

        return overlapScore * 0.85;
    }

    private List<ArtistDuplicateGroup> BuildGroups(
        List<(int LeftId, int RightId, double Score, List<string> Reasons)> scoredPairs,
        List<ArtistReadModel> artists,
        ArtistDuplicateSearchCriteria criteria)
    {
        var artistIndex = artists.ToDictionary(a => a.ArtistId);

        var adjacency = new Dictionary<int, HashSet<int>>();
        var pairScores = new Dictionary<(int, int), (double Score, List<string> Reasons)>();

        foreach (var (leftId, rightId, score, reasons) in scoredPairs)
        {
            if (!adjacency.TryGetValue(leftId, out var leftNeighbors))
            {
                leftNeighbors = [];
                adjacency[leftId] = leftNeighbors;
            }
            leftNeighbors.Add(rightId);

            if (!adjacency.TryGetValue(rightId, out var rightNeighbors))
            {
                rightNeighbors = [];
                adjacency[rightId] = rightNeighbors;
            }
            rightNeighbors.Add(leftId);

            var pairKey = (Math.Min(leftId, rightId), Math.Max(leftId, rightId));
            pairScores[pairKey] = (score, reasons);
        }

        var visited = new HashSet<int>();
        var components = new List<HashSet<int>>();

        foreach (var artistId in adjacency.Keys)
        {
            if (visited.Contains(artistId))
            {
                continue;
            }

            var component = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(artistId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }

                component.Add(current);

                if (adjacency.TryGetValue(current, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (!visited.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            if (component.Count > 1)
            {
                components.Add(component);
            }
        }

        var groups = new List<ArtistDuplicateGroup>();
        var groupIndex = 0;

        foreach (var component in components)
        {
            var componentArtists = component
                .Where(id => artistIndex.ContainsKey(id))
                .Select(id => artistIndex[id])
                .ToList();

            var candidates = componentArtists
                .Select(a => new ArtistDuplicateCandidate(
                    a.ArtistId,
                    a.ApiKey,
                    a.Name,
                    a.SortName,
                    a.GetExternalIds(),
                    a.AlbumCount,
                    a.SongCount))
                .ToList();

            var pairs = new List<ArtistDuplicatePair>();
            var maxScore = 0.0;

            foreach (var leftId in component)
            {
                foreach (var rightId in component)
                {
                    if (leftId >= rightId)
                    {
                        continue;
                    }

                    var pairKey = (leftId, rightId);
                    if (pairScores.TryGetValue(pairKey, out var pairData))
                    {
                        pairs.Add(new ArtistDuplicatePair(leftId, rightId, pairData.Score, pairData.Reasons));
                        maxScore = Math.Max(maxScore, pairData.Score);
                    }
                }
            }

            if (maxScore >= criteria.MinScore)
            {
                var suggestedPrimary = componentArtists
                    .OrderByDescending(a => a.ComputePrimaryScore())
                    .First()
                    .ArtistId;

                groupIndex++;
                groups.Add(new ArtistDuplicateGroup(
                    $"artist-dup-{groupIndex:D4}",
                    maxScore,
                    candidates,
                    pairs.OrderByDescending(p => p.Score).ToList(),
                    suggestedPrimary));
            }
        }

        var result = groups.OrderByDescending(g => g.MaxScore).ToList();

        if (criteria.Limit.HasValue && criteria.Limit.Value > 0)
        {
            result = result.Take(criteria.Limit.Value).ToList();
        }

        return result;
    }

    private static string NormalizeName(string name, string ignoredArticles)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var result = name.ToLowerInvariant().Trim();
        result = result.RemoveAccents() ?? result;
        result = result.StripLeadingArticles(ignoredArticles) ?? result;
        result = NonAlphanumericRegex().Replace(result, " ");
        result = WhitespaceCollapseRegex().Replace(result, " ").Trim();

        return result;
    }

    private static bool IsFirstLastReversal(string leftName, string rightName, string? leftSort, string? rightSort, string ignoredArticles)
    {
        var leftTokens = GetNameTokens(leftName, ignoredArticles);
        var rightTokens = GetNameTokens(rightName, ignoredArticles);

        if (leftTokens.Count != 2 || rightTokens.Count != 2)
        {
            return false;
        }

        var leftSorted = string.Join(" ", leftTokens.OrderBy(t => t));
        var rightSorted = string.Join(" ", rightTokens.OrderBy(t => t));

        if (!string.Equals(leftSorted, rightSorted, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasLastFirstPattern =
            (leftSort != null && LastFirstCommaRegex.IsMatch(leftSort)) ||
            (rightSort != null && LastFirstCommaRegex.IsMatch(rightSort)) ||
            LastFirstCommaRegex.IsMatch(leftName) ||
            LastFirstCommaRegex.IsMatch(rightName);

        return hasLastFirstPattern ||
               (leftTokens[0].Equals(rightTokens[1], StringComparison.OrdinalIgnoreCase) &&
                leftTokens[1].Equals(rightTokens[0], StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetNameTokens(string name, string ignoredArticles)
    {
        var normalized = NormalizeName(name, ignoredArticles);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static double ComputeTokenSimilarity(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0.0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();

        return (double)intersection / union;
    }

    private static double ComputeCharacterSimilarity(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return 0.0;
        }

        var maxLen = Math.Max(left.Length, right.Length);
        var distance = Levenshtein.Distance(left, right);

        return 1.0 - ((double)distance / maxLen);
    }

    private static bool IsGenericAlbumTitle(string normalizedTitle)
    {
        var cleanTitle = NonAlphanumericRegex().Replace(normalizedTitle.ToUpperInvariant(), "");
        return CommonGenericAlbums.Any(g => cleanTitle.Contains(g));
    }

    private static string NormalizeAlbumKey(string titleNormalized, int? year)
    {
        var cleanTitle = NonAlphanumericRegex().Replace(titleNormalized.ToUpperInvariant(), "");
        return year.HasValue ? $"{cleanTitle}:{year}" : cleanTitle;
    }

    [GeneratedRegex(@"^\w+,\s*\w+$")]
    private static partial Regex LastFirstCommaPattern();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceCollapseRegex();
}
