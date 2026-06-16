using System.Diagnostics;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Services.Caching;
using Serilog;

namespace Melodee.Common.Services.Scanning;

/// <summary>
///     Aggregate performance metrics collected during one directory processing or library scan run.
/// </summary>
public sealed record DirectoryRunPerformanceSummary(
    long RuntimeMs,
    int DirectoriesProcessed,
    long PluginTimeMs,
    long AlbumProcessingTimeMs,
    long EnrichmentTimeMs,
    long CopyTimeMs,
    long ConversionTimeMs,
    int ConversionFilesProcessed,
    int ArtistSearchPersistenceRetries,
    int ArtistSearchPersistenceConflicts,
    int ArtistSearchPersistenceCorruptions,
    int ArtistSearchReadErrors,
    int ArtistSearchReadCorruptions,
    int AlbumsSkippedRevalidation,
    int AlbumsDeferredRevalidation,
    CacheStatistics ArtistSearchCache,
    CacheStatistics ForcedArtistSearchCache,
    CacheStatistics AlbumImageCache,
    IReadOnlyDictionary<string, ThrottleStatistics> ApiThrottleStatistics);

/// <summary>
///     Context for a single library processing run.
///     Holds per-run caches to avoid duplicate API calls within a processing session.
/// </summary>
public sealed class DirectoryRunContext : IDisposable
{
    private readonly Stopwatch _runStopwatch;
    private long _pluginTimeMs;
    private long _albumProcessingTimeMs;
    private long _enrichmentTimeMs;
    private long _copyTimeMs;
    private long _conversionTimeMs;
    private int _conversionFilesProcessed;
    private int _directoriesProcessed;
    private int _artistSearchPersistenceRetries;
    private int _artistSearchPersistenceConflicts;
    private int _artistSearchPersistenceCorruptions;
    private int _artistSearchReadErrors;
    private int _artistSearchReadCorruptions;
    private int _albumsSkippedRevalidation;
    private int _albumsDeferredRevalidation;

    /// <summary>
    ///     Per-run cache for artist search results.
    ///     Keyed by normalized artist identity (name + optional MBID/SpotifyId).
    /// </summary>
    public SingleFlightCache<ArtistQuery, ArtistSearchResult[]> ArtistSearchCache { get; }

    /// <summary>
    ///     Per-run cache for forced artist revalidation searches.
    ///     Kept separate so an inbound negative lookup does not block a later forced revalidation lookup.
    /// </summary>
    public SingleFlightCache<ArtistQuery, ArtistSearchResult[]> ForcedArtistSearchCache { get; }

    /// <summary>
    ///     Per-run cache for album image search results.
    ///     Keyed by normalized album identity (artist + album name + year).
    /// </summary>
    public SingleFlightCache<AlbumQuery, ImageSearchResult[]> AlbumImageCache { get; }

    /// <summary>
    ///     Global throttler for external API calls.
    /// </summary>
    public ExternalApiThrottler ApiThrottler { get; }

    public DirectoryRunContext(
        int artistCacheSize = 500,
        int albumImageCacheSize = 500,
        TimeSpan? negativeCacheTtl = null)
    {
        _runStopwatch = Stopwatch.StartNew();

        var negTtl = negativeCacheTtl ?? TimeSpan.FromMinutes(2);

        ArtistSearchCache = new SingleFlightCache<ArtistQuery, ArtistSearchResult[]>(
            NormalizeArtistKey,
            maxSize: artistCacheSize,
            positiveTtl: TimeSpan.FromHours(24),
            negativeTtl: negTtl,
            cacheName: "ArtistRunCache");

        ForcedArtistSearchCache = new SingleFlightCache<ArtistQuery, ArtistSearchResult[]>(
            NormalizeArtistKey,
            maxSize: artistCacheSize,
            positiveTtl: TimeSpan.FromHours(24),
            negativeTtl: negTtl,
            cacheName: "ForcedArtistRunCache");

        AlbumImageCache = new SingleFlightCache<AlbumQuery, ImageSearchResult[]>(
            NormalizeAlbumImageKey,
            maxSize: albumImageCacheSize,
            positiveTtl: TimeSpan.FromHours(24),
            negativeTtl: negTtl,
            cacheName: "AlbumImageRunCache");

        ApiThrottler = new ExternalApiThrottler();
    }

    /// <summary>
    ///     Normalizes artist query to a cache key.
    ///     Uses name + MBID + SpotifyId for unique identification.
    /// </summary>
    public static string NormalizeArtistKey(ArtistQuery query)
    {
        var name = (query.NameNormalized ?? query.Name ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        var mbid = query.MusicBrainzId ?? string.Empty;
        var spotifyId = query.SpotifyId ?? string.Empty;

        return $"ARTIST:{name}|MBID:{mbid}|SPOTIFY:{spotifyId}";
    }

    /// <summary>
    ///     Normalizes album query to a cache key.
    ///     Uses artist + album name + year for unique identification.
    /// </summary>
    public static string NormalizeAlbumImageKey(AlbumQuery query)
    {
        var artist = (query.Artist ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        var name = (query.Name ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        var year = query.Year.ToString();

        return $"ALBUM:{artist}|{name}|{year}";
    }

    /// <summary>
    ///     Records time spent in plugin processing.
    /// </summary>
    public void AddPluginTime(long milliseconds)
    {
        Interlocked.Add(ref _pluginTimeMs, milliseconds);
    }

    /// <summary>
    ///     Records time spent in album processing.
    /// </summary>
    public void AddAlbumProcessingTime(long milliseconds)
    {
        Interlocked.Add(ref _albumProcessingTimeMs, milliseconds);
    }

    /// <summary>
    ///     Records time spent in external enrichment (API calls).
    /// </summary>
    public void AddEnrichmentTime(long milliseconds)
    {
        Interlocked.Add(ref _enrichmentTimeMs, milliseconds);
    }

    /// <summary>
    ///     Records time spent copying files.
    /// </summary>
    public void AddCopyTime(long milliseconds)
    {
        Interlocked.Add(ref _copyTimeMs, milliseconds);
    }

    /// <summary>
    ///     Records time spent converting one source file.
    /// </summary>
    public void AddConversionTime(long milliseconds)
    {
        Interlocked.Add(ref _conversionTimeMs, milliseconds);
        Interlocked.Increment(ref _conversionFilesProcessed);
    }

    /// <summary>
    ///     Increments the count of processed directories.
    /// </summary>
    public void IncrementDirectoriesProcessed()
    {
        Interlocked.Increment(ref _directoriesProcessed);
    }

    /// <summary>
    ///     Records that an artist search cache write was retried.
    /// </summary>
    public void RecordArtistSearchPersistenceRetry()
    {
        Interlocked.Increment(ref _artistSearchPersistenceRetries);
    }

    /// <summary>
    ///     Records that DecentDB reported a transient artist search persistence conflict.
    /// </summary>
    public void RecordArtistSearchPersistenceConflict()
    {
        Interlocked.Increment(ref _artistSearchPersistenceConflicts);
    }

    /// <summary>
    ///     Records that DecentDB reported a non-retryable error during artist search persistence.
    /// </summary>
    public void RecordArtistSearchPersistenceCorruption()
    {
        Interlocked.Increment(ref _artistSearchPersistenceCorruptions);
    }

    /// <summary>
    ///     Records an artist search database read error.
    /// </summary>
    public void RecordArtistSearchReadError()
    {
        Interlocked.Increment(ref _artistSearchReadErrors);
    }

    /// <summary>
    ///     Records an artist search database open/read failure that should not be retried.
    /// </summary>
    public void RecordArtistSearchReadCorruption()
    {
        Interlocked.Increment(ref _artistSearchReadCorruptions);
    }

    /// <summary>
    ///     Records a staging album skipped because it could not usefully be revalidated.
    /// </summary>
    public void RecordAlbumSkippedRevalidation()
    {
        Interlocked.Increment(ref _albumsSkippedRevalidation);
    }

    /// <summary>
    ///     Records a staging album deferred by the persistent revalidation backoff policy.
    /// </summary>
    public void RecordAlbumDeferredRevalidation()
    {
        Interlocked.Increment(ref _albumsDeferredRevalidation);
    }

    /// <summary>
    ///     Returns a snapshot of the run counters.
    /// </summary>
    public DirectoryRunPerformanceSummary GetPerformanceSummary()
    {
        return new DirectoryRunPerformanceSummary(
            RuntimeMs: _runStopwatch.ElapsedMilliseconds,
            DirectoriesProcessed: _directoriesProcessed,
            PluginTimeMs: _pluginTimeMs,
            AlbumProcessingTimeMs: _albumProcessingTimeMs,
            EnrichmentTimeMs: _enrichmentTimeMs,
            CopyTimeMs: _copyTimeMs,
            ConversionTimeMs: _conversionTimeMs,
            ConversionFilesProcessed: _conversionFilesProcessed,
            ArtistSearchPersistenceRetries: _artistSearchPersistenceRetries,
            ArtistSearchPersistenceConflicts: _artistSearchPersistenceConflicts,
            ArtistSearchPersistenceCorruptions: _artistSearchPersistenceCorruptions,
            ArtistSearchReadErrors: _artistSearchReadErrors,
            ArtistSearchReadCorruptions: _artistSearchReadCorruptions,
            AlbumsSkippedRevalidation: _albumsSkippedRevalidation,
            AlbumsDeferredRevalidation: _albumsDeferredRevalidation,
            ArtistSearchCache: ArtistSearchCache.GetStatistics(),
            ForcedArtistSearchCache: ForcedArtistSearchCache.GetStatistics(),
            AlbumImageCache: AlbumImageCache.GetStatistics(),
            ApiThrottleStatistics: ApiThrottler.GetStatistics());
    }

    /// <summary>
    ///     Logs summary statistics for this run.
    /// </summary>
    public void LogSummary()
    {
        var summary = GetPerformanceSummary();

        Log.Information(
            "[DirectoryRunContext] Run completed in {TotalMs}ms | " +
            "Directories: {DirCount} | Plugin: {PluginMs}ms | Album: {AlbumMs}ms | " +
            "Enrichment: {EnrichMs}ms | Conversion: {ConversionMs}ms ({ConversionFiles} files) | Copy: {CopyMs}ms",
            summary.RuntimeMs,
            summary.DirectoriesProcessed,
            summary.PluginTimeMs,
            summary.AlbumProcessingTimeMs,
            summary.EnrichmentTimeMs,
            summary.ConversionTimeMs,
            summary.ConversionFilesProcessed,
            summary.CopyTimeMs);

        Log.Information(
            "[DirectoryRunContext] Artist cache: {Entries} entries, {Hits} hits, {Misses} misses, {Coalesced} coalesced ({HitRate:P1} hit rate)",
            summary.ArtistSearchCache.TotalEntries,
            summary.ArtistSearchCache.Hits,
            summary.ArtistSearchCache.Misses,
            summary.ArtistSearchCache.CoalescedRequests,
            summary.ArtistSearchCache.HitRate);

        Log.Information(
            "[DirectoryRunContext] Forced artist cache: {Entries} entries, {Hits} hits, {Misses} misses, {Coalesced} coalesced ({HitRate:P1} hit rate)",
            summary.ForcedArtistSearchCache.TotalEntries,
            summary.ForcedArtistSearchCache.Hits,
            summary.ForcedArtistSearchCache.Misses,
            summary.ForcedArtistSearchCache.CoalescedRequests,
            summary.ForcedArtistSearchCache.HitRate);

        Log.Information(
            "[DirectoryRunContext] Album image cache: {Entries} entries, {Hits} hits, {Misses} misses, {Coalesced} coalesced ({HitRate:P1} hit rate)",
            summary.AlbumImageCache.TotalEntries,
            summary.AlbumImageCache.Hits,
            summary.AlbumImageCache.Misses,
            summary.AlbumImageCache.CoalescedRequests,
            summary.AlbumImageCache.HitRate);

        if (summary.ArtistSearchPersistenceConflicts > 0 ||
            summary.ArtistSearchPersistenceRetries > 0 ||
            summary.ArtistSearchPersistenceCorruptions > 0 ||
            summary.ArtistSearchReadErrors > 0 ||
            summary.ArtistSearchReadCorruptions > 0 ||
            summary.AlbumsSkippedRevalidation > 0 ||
            summary.AlbumsDeferredRevalidation > 0)
        {
            Log.Information(
                "[DirectoryRunContext] Artist search: {ReadErrors} read errors, {ReadFailures} open/read failures | " +
                "Artist persistence: {Conflicts} conflicts, {Retries} retries, {NonRetryableErrors} non-retryable errors | " +
                "Revalidation skipped: {Skipped}, deferred: {Deferred}",
                summary.ArtistSearchReadErrors,
                summary.ArtistSearchReadCorruptions,
                summary.ArtistSearchPersistenceConflicts,
                summary.ArtistSearchPersistenceRetries,
                summary.ArtistSearchPersistenceCorruptions,
                summary.AlbumsSkippedRevalidation,
                summary.AlbumsDeferredRevalidation);
        }

        foreach (var (provider, stats) in summary.ApiThrottleStatistics)
        {
            Log.Information(
                "[DirectoryRunContext] {Provider} throttle: {Total} total requests, {Throttled} rate-limited",
                provider,
                stats.TotalRequests,
                stats.ThrottledRequests);
        }
    }

    public void Dispose()
    {
        LogSummary();
        ArtistSearchCache.Dispose();
        ForcedArtistSearchCache.Dispose();
        AlbumImageCache.Dispose();
        ApiThrottler.Dispose();
    }
}
