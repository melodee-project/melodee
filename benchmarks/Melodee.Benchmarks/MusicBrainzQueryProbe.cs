using System.Diagnostics;
using System.Text.Json;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Benchmarks;

internal static class MusicBrainzQueryProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = MusicBrainzProbeOptions.Parse(args);
            var databasePath = options.RequireString("db");
            var outputPath = options.GetString("output");

            if (!File.Exists(databasePath))
            {
                throw new FileNotFoundException("MusicBrainz DecentDB database was not found.", databasePath);
            }

            var report = await RunProbeAsync(databasePath, options, CancellationToken.None)
                .ConfigureAwait(false);
            await WriteReportAsync(report, outputPath, CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<MusicBrainzQueryProbeReport> RunProbeAsync(
        string databasePath,
        MusicBrainzProbeOptions options,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await using var sampleContext = CreateContext(databasePath);
        var sample = await ResolveSampleAsync(sampleContext, options, cancellationToken)
            .ConfigureAwait(false);
        var indexes = GetIndexDiagnostics(sampleContext);

        var measurements = new List<QueryProbeMeasurement>();
        foreach (var pass in new[] { "cold", "warm" })
        {
            await using var context = CreateContext(databasePath);
            measurements.Add(await MeasureCanConnectAsync(context, pass, cancellationToken)
                .ConfigureAwait(false));
            measurements.Add(await MeasureQueryAsync(
                    context,
                    pass,
                    "ordered-first-row-existence",
                    sample.FirstArtistId.ToString(),
                    context.Artists
                        .AsNoTracking()
                        .OrderBy(a => a.Id)
                        .Select(a => a.Id)
                        .Take(1),
                    cancellationToken)
                .ConfigureAwait(false));
            measurements.Add(await MeasureQueryAsync(
                    context,
                    pass,
                    "exact-normalized-name",
                    sample.NameNormalized,
                    context.Artists
                        .AsNoTracking()
                        .Where(a => a.NameNormalized == sample.NameNormalized)
                        .OrderBy(a => a.SortName)
                        .Take(10),
                    cancellationToken)
                .ConfigureAwait(false));

            if (!string.IsNullOrWhiteSpace(sample.AliasNormalized))
            {
                measurements.Add(await MeasureQueryAsync(
                        context,
                        pass,
                        "exact-normalized-alias",
                        sample.AliasNormalized,
                        context.ArtistAliases
                            .AsNoTracking()
                            .Where(a => a.NameNormalized == sample.AliasNormalized)
                            .OrderBy(a => a.MusicBrainzArtistId)
                            .Take(10),
                        cancellationToken)
                    .ConfigureAwait(false));
            }

            measurements.Add(await MeasureQueryAsync(
                    context,
                    pass,
                    "exact-musicbrainz-id-raw",
                    sample.MusicBrainzIdRaw,
                    context.Artists
                        .AsNoTracking()
                        .Where(a => a.MusicBrainzIdRaw == sample.MusicBrainzIdRaw)
                        .OrderBy(a => a.Id)
                        .Take(1),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return new MusicBrainzQueryProbeReport(
            "musicbrainz-query-probe",
            databasePath,
            sampleContext.Database.ProviderName ?? "unknown",
            startedAt,
            DateTimeOffset.UtcNow,
            sample,
            indexes,
            measurements);
    }

    private static MusicBrainzDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={databasePath}")
            .Options;

        return new MusicBrainzDbContext(options);
    }

    private static async Task<QueryProbeSample> ResolveSampleAsync(
        MusicBrainzDbContext context,
        MusicBrainzProbeOptions options,
        CancellationToken cancellationToken)
    {
        var requestedName = options.GetString("name");
        var requestedAlias = options.GetString("alias");
        var requestedMbid = options.GetString("mbid");

        var artist = !string.IsNullOrWhiteSpace(requestedMbid)
            ? await context.Artists
                .AsNoTracking()
                .Where(a => a.MusicBrainzIdRaw == requestedMbid)
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        artist ??= !string.IsNullOrWhiteSpace(requestedName)
            ? await context.Artists
                .AsNoTracking()
                .Where(a => a.NameNormalized == requestedName)
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        artist ??= await context.Artists
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (artist is null)
        {
            throw new InvalidOperationException("The MusicBrainz database has no materialized artists.");
        }

        var alias = requestedAlias;
        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = await context.ArtistAliases
                .AsNoTracking()
                .Where(a => a.MusicBrainzArtistId == artist.MusicBrainzArtistId)
                .OrderBy(a => a.NameNormalized)
                .Select(a => a.NameNormalized)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            alias = await context.ArtistAliases
                .AsNoTracking()
                .OrderBy(a => a.MusicBrainzArtistId)
                .ThenBy(a => a.NameNormalized)
                .Select(a => a.NameNormalized)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return new QueryProbeSample(
            artist.Id,
            SanitizeSampleValue(artist.NameNormalized) ?? string.Empty,
            SanitizeSampleValue(alias),
            SanitizeSampleValue(artist.MusicBrainzIdRaw) ?? string.Empty);
    }

    private static IReadOnlyList<QueryProbeIndexDiagnostic> GetIndexDiagnostics(MusicBrainzDbContext context)
    {
        var entityTypes = new[]
        {
            typeof(Artist),
            typeof(ArtistAliasLookup),
            typeof(Album)
        };

        return entityTypes
            .SelectMany(entityType => context.Model.FindEntityType(entityType)?.GetIndexes() ?? [])
            .Select(index => new QueryProbeIndexDiagnostic(
                index.DeclaringEntityType.GetTableName() ?? index.DeclaringEntityType.DisplayName(),
                index.GetDatabaseName() ?? "(unnamed)",
                index.IsUnique,
                index.Properties.Select(property => property.Name).ToArray()))
            .OrderBy(index => index.Table)
            .ThenBy(index => index.Name)
            .ToArray();
    }

    private static async Task<QueryProbeMeasurement> MeasureCanConnectAsync(
        MusicBrainzDbContext context,
        string pass,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var canConnect = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started);

        return new QueryProbeMeasurement(
            "database-can-connect",
            pass,
            null,
            canConnect ? 1 : 0,
            elapsed.TotalMilliseconds,
            null,
            "No SQL plan is available for Database.CanConnectAsync().");
    }

    private static async Task<QueryProbeMeasurement> MeasureQueryAsync<T>(
        MusicBrainzDbContext context,
        string pass,
        string name,
        string? sampleValue,
        IQueryable<T> query,
        CancellationToken cancellationToken)
    {
        var sql = TryGetSql(query);
        var started = Stopwatch.GetTimestamp();
        var rows = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started);

        return new QueryProbeMeasurement(
            name,
            pass,
            SanitizeSampleValue(sampleValue),
            rows.Length,
            elapsed.TotalMilliseconds,
            sql,
            "Plan output is provider-dependent; compare elapsed time, row count, SQL shape, and configured index diagnostics.");
    }

    private static string? TryGetSql<T>(IQueryable<T> query)
    {
        try
        {
            return query.ToQueryString();
        }
        catch (Exception ex)
        {
            return $"SQL unavailable: {ex.Message}";
        }
    }

    private static string? SanitizeSampleValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= 160 ? value : string.Concat(value.AsSpan(0, 160), "...");
    }

    private static async Task WriteReportAsync(
        MusicBrainzQueryProbeReport report,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine(json);
            return;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Wrote MusicBrainz query probe report to {outputPath}");
    }

    private sealed record MusicBrainzQueryProbeReport(
        string ProbeName,
        string DatabasePath,
        string ProviderName,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset FinishedAtUtc,
        QueryProbeSample Sample,
        IReadOnlyList<QueryProbeIndexDiagnostic> Indexes,
        IReadOnlyList<QueryProbeMeasurement> Measurements);

    private sealed record QueryProbeSample(
        long FirstArtistId,
        string NameNormalized,
        string? AliasNormalized,
        string MusicBrainzIdRaw);

    private sealed record QueryProbeIndexDiagnostic(
        string Table,
        string Name,
        bool IsUnique,
        IReadOnlyList<string> Properties);

    private sealed record QueryProbeMeasurement(
        string Name,
        string Pass,
        string? SampleValue,
        int RowCount,
        double ElapsedMilliseconds,
        string? Sql,
        string? PlanDiagnostics);
}
