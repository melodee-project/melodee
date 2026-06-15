using System.Data.Common;
using System.Diagnostics;
using DecentDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
///     Warms the indexed DecentDB query shapes used by the MusicBrainz search provider.
/// </summary>
public sealed class MusicBrainzDecentDbWarmupService(
    ILogger logger,
    IDbContextFactory<MusicBrainzDbContext> dbContextFactory)
{
    /// <summary>
    ///     Warms the configured MusicBrainz DecentDB database.
    /// </summary>
    public Task<MusicBrainzDecentDbWarmupResult> WarmHotIndexesAsync(
        CancellationToken cancellationToken = default)
    {
        return WarmHotIndexesAsync(null, cancellationToken);
    }

    /// <summary>
    ///     Warms the MusicBrainz DecentDB database at the supplied path, or the configured database when omitted.
    /// </summary>
    public async Task<MusicBrainzDecentDbWarmupResult> WarmHotIndexesAsync(
        string? databasePath,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var measurements = new List<MusicBrainzDecentDbWarmupMeasurement>();

        try
        {
            await using var context = await CreateContextAsync(databasePath, cancellationToken).ConfigureAwait(false);
            var canConnect = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!canConnect)
            {
                return MusicBrainzDecentDbWarmupResult.CreateSkipped(
                    measurements,
                    Stopwatch.GetElapsedTime(started),
                    "MusicBrainz DecentDB database is not reachable.");
            }

            var sample = await ResolveSampleAsync(context, measurements, cancellationToken).ConfigureAwait(false);
            if (sample is null)
            {
                return MusicBrainzDecentDbWarmupResult.CreateSkipped(
                    measurements,
                    Stopwatch.GetElapsedTime(started),
                    "MusicBrainz DecentDB database has no materialized artists.");
            }

            await MeasureAsync(
                    measurements,
                    "exact-normalized-name",
                    async () => (await context.Artists
                        .AsNoTracking()
                        .Where(artist => artist.NameNormalized == sample.NameNormalized)
                        .OrderBy(artist => artist.SortName)
                        .Select(artist => artist.Id)
                        .Take(10)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false)).Length)
                .ConfigureAwait(false);

            await MeasureAsync(
                    measurements,
                    "exact-musicbrainz-id-raw",
                    async () => (await context.Artists
                        .AsNoTracking()
                        .Where(artist => artist.MusicBrainzIdRaw == sample.MusicBrainzIdRaw)
                        .OrderBy(artist => artist.Id)
                        .Select(artist => artist.Id)
                        .Take(1)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false)).Length)
                .ConfigureAwait(false);

            await MeasureAsync(
                    measurements,
                    "aliases-by-artist-id",
                    async () => (await context.ArtistAliases
                        .AsNoTracking()
                        .Where(alias => alias.MusicBrainzArtistId == sample.MusicBrainzArtistId)
                        .Select(alias => alias.NameNormalized)
                        .Take(25)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false)).Length)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(sample.AliasNormalized))
            {
                await MeasureAsync(
                        measurements,
                        "exact-normalized-alias",
                        async () => (await context.ArtistAliases
                            .AsNoTracking()
                            .Where(alias => alias.NameNormalized == sample.AliasNormalized)
                            .OrderBy(alias => alias.MusicBrainzArtistId)
                            .Select(alias => alias.MusicBrainzArtistId)
                            .Take(10)
                            .ToArrayAsync(cancellationToken)
                            .ConfigureAwait(false)).Length)
                    .ConfigureAwait(false);
            }

            await MeasureAsync(
                    measurements,
                    "albums-by-artist-id",
                    async () => (await context.Albums
                        .AsNoTracking()
                        .Where(album => album.MusicBrainzArtistId == sample.MusicBrainzArtistId)
                        .OrderBy(album => album.ReleaseDate)
                        .ThenBy(album => album.SortName)
                        .Select(album => album.Id)
                        .Take(25)
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false)).Length)
                .ConfigureAwait(false);

            var result = MusicBrainzDecentDbWarmupResult.CreateCompleted(
                measurements,
                Stopwatch.GetElapsedTime(started));

            logger.Information(
                "MusicBrainz DecentDB warm-up completed in {ElapsedMilliseconds:F1} ms. Warmed {QueryCount} query shapes.",
                result.Elapsed.TotalMilliseconds,
                result.WarmedQueryCount);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = MusicBrainzDecentDbWarmupResult.CreateFailed(
                measurements,
                Stopwatch.GetElapsedTime(started),
                ex.Message);

            logger.Warning(
                ex,
                "MusicBrainz DecentDB warm-up failed after {ElapsedMilliseconds:F1} ms. Search remains available; indexes will warm on demand.",
                result.Elapsed.TotalMilliseconds);

            return result;
        }
    }

    private async Task<MusicBrainzDbContext> CreateContextAsync(
        string? databasePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var baseContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connectionString = baseContext.Database.GetConnectionString()
                               ?? throw new InvalidOperationException("MusicBrainzDbContext has no connection string configured.");
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };
        builder["Data Source"] = databasePath;

        var options = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB(builder.ConnectionString, optionsBuilder => optionsBuilder.UseNodaTime())
            .Options;

        return new MusicBrainzDbContext(options);
    }

    private static async Task<WarmupArtistSample?> ResolveSampleAsync(
        MusicBrainzDbContext context,
        List<MusicBrainzDecentDbWarmupMeasurement> measurements,
        CancellationToken cancellationToken)
    {
        var sampleStarted = Stopwatch.GetTimestamp();
        var artist = await context.Artists
            .AsNoTracking()
            .OrderBy(artist => artist.Id)
            .Select(artist => new
            {
                artist.Id,
                artist.MusicBrainzArtistId,
                artist.NameNormalized,
                artist.MusicBrainzIdRaw
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var sampleElapsed = Stopwatch.GetElapsedTime(sampleStarted);
        measurements.Add(new MusicBrainzDecentDbWarmupMeasurement(
            "sample-artist-row",
            artist is null ? 0 : 1,
            sampleElapsed));

        if (artist is null)
        {
            return null;
        }

        var aliasStarted = Stopwatch.GetTimestamp();
        var alias = await context.ArtistAliases
            .AsNoTracking()
            .Where(alias => alias.MusicBrainzArtistId == artist.MusicBrainzArtistId)
            .OrderBy(alias => alias.NameNormalized)
            .Select(alias => alias.NameNormalized)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var aliasElapsed = Stopwatch.GetElapsedTime(aliasStarted);
        measurements.Add(new MusicBrainzDecentDbWarmupMeasurement(
            "sample-alias-by-artist-id",
            string.IsNullOrWhiteSpace(alias) ? 0 : 1,
            aliasElapsed));

        return new WarmupArtistSample(
            artist.MusicBrainzArtistId,
            artist.NameNormalized,
            artist.MusicBrainzIdRaw,
            alias);
    }

    private static async Task MeasureAsync(
        List<MusicBrainzDecentDbWarmupMeasurement> measurements,
        string name,
        Func<Task<int>> query)
    {
        var started = Stopwatch.GetTimestamp();
        var rowCount = await query().ConfigureAwait(false);
        measurements.Add(new MusicBrainzDecentDbWarmupMeasurement(
            name,
            rowCount,
            Stopwatch.GetElapsedTime(started)));
    }

    private sealed record WarmupArtistSample(
        long MusicBrainzArtistId,
        string NameNormalized,
        string MusicBrainzIdRaw,
        string? AliasNormalized);
}

/// <summary>
///     Result of a MusicBrainz DecentDB warm-up run.
/// </summary>
public sealed record MusicBrainzDecentDbWarmupResult(
    bool Succeeded,
    bool Skipped,
    string? Message,
    TimeSpan Elapsed,
    IReadOnlyList<MusicBrainzDecentDbWarmupMeasurement> Measurements)
{
    /// <summary>
    ///     Number of indexed query shapes executed during warm-up.
    /// </summary>
    public int WarmedQueryCount => Measurements.Count(measurement =>
        !measurement.Name.StartsWith("sample-", StringComparison.Ordinal));

    internal static MusicBrainzDecentDbWarmupResult CreateCompleted(
        IReadOnlyList<MusicBrainzDecentDbWarmupMeasurement> measurements,
        TimeSpan elapsed)
    {
        return new MusicBrainzDecentDbWarmupResult(true, false, null, elapsed, measurements);
    }

    internal static MusicBrainzDecentDbWarmupResult CreateSkipped(
        IReadOnlyList<MusicBrainzDecentDbWarmupMeasurement> measurements,
        TimeSpan elapsed,
        string message)
    {
        return new MusicBrainzDecentDbWarmupResult(false, true, message, elapsed, measurements);
    }

    internal static MusicBrainzDecentDbWarmupResult CreateFailed(
        IReadOnlyList<MusicBrainzDecentDbWarmupMeasurement> measurements,
        TimeSpan elapsed,
        string message)
    {
        return new MusicBrainzDecentDbWarmupResult(false, false, message, elapsed, measurements);
    }
}

/// <summary>
///     Timing for a single MusicBrainz DecentDB warm-up query.
/// </summary>
public sealed record MusicBrainzDecentDbWarmupMeasurement(
    string Name,
    int RowCount,
    TimeSpan Elapsed);
