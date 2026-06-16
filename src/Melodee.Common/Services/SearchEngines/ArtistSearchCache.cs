using System.Collections.Concurrent;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;

namespace Melodee.Common.Services.SearchEngines;

/// <summary>
///     Cache for artist search results to avoid redundant API calls
/// </summary>
public sealed class ArtistSearchCache : ICacheInvalidatable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _negativeResultTtl = TimeSpan.FromHours(2);
    private readonly TimeSpan _positiveResultTtl = TimeSpan.FromHours(24);
    private readonly int _maxCacheSize = 10000;

    private sealed class CacheEntry
    {
        public required bool Found { get; init; }
        public required DateTime Expiry { get; init; }
        public int? ArtistId { get; init; }
    }

    /// <summary>
    ///     Check if an artist search has been attempted recently
    /// </summary>
    public bool TryGetCachedResult(ArtistQuery query, out bool wasFound, out int? artistId)
    {
        CleanExpiredEntries();

        var key = GenerateKey(query);
        if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
        {
            wasFound = entry.Found;
            artistId = entry.ArtistId;
            return true;
        }

        wasFound = false;
        artistId = null;
        return false;
    }

    /// <summary>
    ///     Cache a positive search result
    /// </summary>
    public void CachePositiveResult(ArtistQuery query, int artistId)
    {
        var key = GenerateKey(query);
        var entry = new CacheEntry
        {
            Found = true,
            Expiry = DateTime.UtcNow.Add(_positiveResultTtl),
            ArtistId = artistId
        };

        _cache.AddOrUpdate(key, entry, (_, _) => entry);
        CleanExpiredEntries();
    }

    /// <summary>
    ///     Cache a negative search result (artist not found)
    /// </summary>
    public void CacheNegativeResult(ArtistQuery query)
    {
        var key = GenerateKey(query);
        var entry = new CacheEntry
        {
            Found = false,
            Expiry = DateTime.UtcNow.Add(_negativeResultTtl),
            ArtistId = null
        };

        _cache.AddOrUpdate(key, entry, (_, _) => entry);
        CleanExpiredEntries();
    }

    /// <summary>
    ///     Clear all cached entries
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    ///     Get cache statistics
    /// </summary>
    public (int totalEntries, int positiveResults, int negativeResults) GetStatistics()
    {
        var entries = _cache.Values.Where(e => e.Expiry > DateTime.UtcNow).ToList();
        return (
            entries.Count,
            entries.Count(e => e.Found),
            entries.Count(e => !e.Found)
        );
    }

    private string GenerateKey(ArtistQuery query)
    {
        // Create a stable key from normalized artist identity so equivalent names share cache entries.
        var normalizedName = UnicodeNormalizer.NormalizeForSearch(query.NameNormalized ?? query.Name);
        return $"{normalizedName}|{query.MusicBrainzId}|{query.SpotifyId}".ToUpperInvariant();
    }

    private void CleanExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.Expiry <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        // If still over max size, remove oldest entries
        if (_cache.Count > _maxCacheSize)
        {
            var keysToRemove = _cache
                .OrderBy(kvp => kvp.Value.Expiry)
                .Take(_cache.Count - _maxCacheSize)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    /// <inheritdoc />
    public void InvalidateByEntityType(string entityTypeName)
    {
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        Clear();
    }
}
