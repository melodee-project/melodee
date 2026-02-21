using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

namespace Melodee.Benchmarks;

/// <summary>
/// BenchmarkDotNet benchmarks for MusicBrainz import performance.
/// Measures import time, memory allocation, and throughput for different data sizes.
///
/// Run with: dotnet run -c Release --project benchmarks/Melodee.Benchmarks/Melodee.Benchmarks.csproj musicbrainz
/// </summary>
[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class MusicBrainzImportBenchmarks
{
    private readonly ILogger _logger;
    private string _testDataPath = null!;
    private string _dbPath = null!;

    public MusicBrainzImportBenchmarks()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Error)
            .CreateLogger();
    }

    [Params(100, 500, 1000)]
    public int ArtistCount { get; set; }

    [Params(3, 5)]
    public int AlbumsPerArtist { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), $"mb-bench-{Guid.NewGuid():N}");
        _dbPath = Path.Combine(Path.GetTempPath(), $"mb-bench-db-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_testDataPath);
        Directory.CreateDirectory(_dbPath);

        var mbDumpPath = Path.Combine(_testDataPath, "staging", "mbdump");
        MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, ArtistCount, AlbumsPerArtist, seed: 42);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, true);
            }
            if (Directory.Exists(_dbPath))
            {
                Directory.Delete(_dbPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ImportWithDefaultSettings()
    {
        var dbFile = Path.Combine(_dbPath, $"mb-default-{Guid.NewGuid():N}.ddb");

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        await importer.ImportAsync(context, _testDataPath, null, CancellationToken.None);

        var count = await context.Artists.CountAsync();

        try
        {
            File.Delete(dbFile);
        }
        catch { }

        return count;
    }

    [Benchmark]
    public async Task<int> ImportWithLargerDataset()
    {
        var dbFile = Path.Combine(_dbPath, $"mb-large-{Guid.NewGuid():N}.ddb");

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        await importer.ImportAsync(context, _testDataPath, null, CancellationToken.None);

        var count = await context.Artists.CountAsync();

        try
        {
            File.Delete(dbFile);
        }
        catch { }

        return count;
    }

    [Benchmark]
    public async Task<int> ImportWithProgressTracking()
    {
        var dbFile = Path.Combine(_dbPath, $"mb-progress-{Guid.NewGuid():N}.ddb");

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        await importer.ImportAsync(context, _testDataPath,
            (phase, current, total, msg) => { }, CancellationToken.None);

        var count = await context.Artists.CountAsync();

        try
        {
            File.Delete(dbFile);
        }
        catch { }

        return count;
    }
}

/// <summary>
/// Synthetic test data generator for MusicBrainz benchmarks.
/// Creates TSV files in the same format as MusicBrainz dumps.
/// </summary>
public static class MusicBrainzTestDataGenerator
{
    private static readonly string[] FirstNames =
    [
        "John", "Paul", "George", "Ringo", "David", "Robert", "James", "Michael",
        "William", "Richard", "Thomas", "Charles", "Daniel", "Matthew", "Anthony"
    ];

    private static readonly string[] LastNames =
    [
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
        "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson"
    ];

    private static readonly string[] AlbumTitles =
    [
        "First Album", "Debut", "The Beginning", "Origins", "Genesis", "Breakthrough",
        "Rising", "Evolution", "Revolution", "Metamorphosis", "Transcendence", "Echoes"
    ];

    public static void GenerateTestData(string outputPath, int artistCount, int albumsPerArtist, int seed = 42)
    {
        Directory.CreateDirectory(outputPath);
        var random = new Random(seed);

        // Generate artists
        var artists = new List<(long Id, Guid MbId, string Name, string SortName)>();
        for (var i = 1; i <= artistCount; i++)
        {
            var name = GenerateName(random);
            artists.Add((i, Guid.NewGuid(), name, name));
        }

        // Generate release groups and releases
        var releaseGroups = new List<(long Id, Guid MbId, long ArtistCreditId, int Type)>();
        var releases = new List<(long Id, Guid MbId, string Name, long ArtistCreditId, long ReleaseGroupId)>();
        var releaseMetas = new List<(long ReleaseGroupId, int Year, int Month, int Day)>();

        var rgId = 1L;
        var relId = 1L;

        foreach (var artist in artists)
        {
            var albumCount = random.Next(1, albumsPerArtist * 2);
            for (var j = 0; j < albumCount; j++)
            {
                var year = random.Next(1960, 2025);
                releaseGroups.Add((rgId, Guid.NewGuid(), artist.Id, random.Next(1, 12)));
                releaseMetas.Add((rgId, year, random.Next(1, 13), random.Next(1, 29)));

                var name = AlbumTitles[random.Next(AlbumTitles.Length)];
                releases.Add((relId++, Guid.NewGuid(), name, artist.Id, rgId));
                rgId++;
            }
        }

        // Write files
        WriteLines(Path.Combine(outputPath, "artist"),
            artists.Select(a => $"{a.Id}\t{a.MbId}\t{a.Name}\t{a.SortName}\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N"));

        WriteLines(Path.Combine(outputPath, "artist_alias"),
            artists.SelectMany(a => Enumerable.Range(0, random.Next(0, 3))
                .Select((_, idx) => $"{a.Id * 10 + idx}\t{a.Id}\t{a.Name.ToUpperInvariant()}\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N")));

        WriteLines(Path.Combine(outputPath, "artist_credit"),
            artists.Select(a => $"{a.Id}\tCredit {a.Id}\t1\t\\N\t\\N"));

        WriteLines(Path.Combine(outputPath, "artist_credit_name"),
            artists.Select(a => $"{a.Id}\t0\t{a.Id}\t{a.Name}\t"));

        WriteLines(Path.Combine(outputPath, "release_group"),
            releaseGroups.Select(rg => $"{rg.Id}\t{rg.MbId}\tRelease Group {rg.Id}\t{rg.ArtistCreditId}\t{rg.Type}\t\\N\t\\N"));

        WriteLines(Path.Combine(outputPath, "release_group_meta"),
            releaseMetas.Select(rm => $"{rm.ReleaseGroupId}\t1\t{rm.Year}\t{rm.Month}\t{rm.Day}"));

        WriteLines(Path.Combine(outputPath, "release"),
            releases.Select(r => $"{r.Id}\t{r.MbId}\t{r.Name}\t{r.ArtistCreditId}\t{r.ReleaseGroupId}\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N\t\\N"));

        WriteLines(Path.Combine(outputPath, "release_country"),
            releaseMetas.Where(_ => random.Next(2) == 0)
                .Select((rm, idx) => $"{idx + 1}\t222\t{rm.Year}\t{rm.Month}\t{rm.Day}"));

        WriteLines(Path.Combine(outputPath, "link"),
            Enumerable.Range(1, artistCount / 10)
                .Select(i => $"{i}\t1\t{random.Next(1960, 2020)}\t{random.Next(1, 13)}\t{random.Next(1, 29)}\t0\t0\t0\t\\N\t\\N"));

        WriteLines(Path.Combine(outputPath, "l_artist_artist"),
            Enumerable.Range(1, artistCount / 10)
                .Select(i => $"{i}\t{i}\t{random.Next(1, artistCount)}\t{random.Next(1, artistCount)}\t0\t\\N\t0\t\\N\t\\N"));
    }

    private static string GenerateName(Random random)
    {
        return random.Next(2) == 0
            ? $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}"
            : $"The {LastNames[random.Next(LastNames.Length)]}s";
    }

    private static void WriteLines(string path, IEnumerable<string> lines)
    {
        File.WriteAllLines(path, lines);
    }
}
