using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

namespace Melodee.Benchmarks;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Melodee Benchmarks");
            Console.WriteLine("==================");
            Console.WriteLine();
            Console.WriteLine("Available benchmark categories:");
            Console.WriteLine("  streaming    - API streaming performance benchmarks");
            Console.WriteLine("  database     - Database query performance benchmarks");
            Console.WriteLine("  cache        - Cache performance benchmarks");
            Console.WriteLine("  collection   - Collection operation benchmarks");
            Console.WriteLine("  musicbrainz  - MusicBrainz import performance benchmarks");
            Console.WriteLine("  musicbrainz-query-probe  - Manual DecentDB MusicBrainz query probe");
            Console.WriteLine("  musicbrainz-import-probe - Manual DecentDB MusicBrainz import probe");
            Console.WriteLine("  all          - Run all benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet run -c Release --project benchmarks/Melodee.Benchmarks [category] [-- BenchmarkDotNet args]");
            Console.WriteLine("Example: dotnet run -c Release --project benchmarks/Melodee.Benchmarks streaming");
            Console.WriteLine("Example: dotnet run -c Release --project benchmarks/Melodee.Benchmarks all -- --exporters json,github,csv --artifacts benchmarks/artifacts");
            Console.WriteLine("Example: dotnet run -c Release --project benchmarks/Melodee.Benchmarks musicbrainz-query-probe -- --db /path/musicbrainz.ddb --output query-probe.json");
            Console.WriteLine("Example: dotnet run -c Release --project benchmarks/Melodee.Benchmarks musicbrainz-import-probe -- --storage /path/musicbrainz --db /tmp/musicbrainz.ddb --output import-probe.json --clean");
            return 0;
        }

        var category = args[0].ToLower();

        // Parse BenchmarkDotNet arguments (everything after the first argument)
        var benchmarkArgs = args.Skip(1).ToArray();
        if (benchmarkArgs.Length > 0 && benchmarkArgs[0] == "--")
        {
            benchmarkArgs = benchmarkArgs.Skip(1).ToArray();
        }

        if (category == "musicbrainz-query-probe")
        {
            return await MusicBrainzQueryProbe.RunAsync(benchmarkArgs);
        }

        if (category == "musicbrainz-import-probe")
        {
            return await MusicBrainzImportProbe.RunAsync(benchmarkArgs);
        }

        var config = CreateConfig(benchmarkArgs);

        switch (category)
        {
            case "streaming":
                BenchmarkRunner.Run<StreamingBenchmarks>(config);
                break;
            case "database":
                BenchmarkRunner.Run<DatabaseQueryBenchmarks>(config);
                break;
            case "cache":
                BenchmarkRunner.Run<CacheBenchmarks>(config);
                break;
            case "collection":
                BenchmarkRunner.Run<CollectionOperationBenchmarks>(config);
                break;
            case "musicbrainz":
                BenchmarkRunner.Run<MusicBrainzImportBenchmarks>(config);
                break;
            case "all":
                BenchmarkRunner.Run<StreamingBenchmarks>(config);
                BenchmarkRunner.Run<DatabaseQueryBenchmarks>(config);
                BenchmarkRunner.Run<CacheBenchmarks>(config);
                BenchmarkRunner.Run<CollectionOperationBenchmarks>(config);
                BenchmarkRunner.Run<MusicBrainzImportBenchmarks>(config);
                break;
            default:
                Console.WriteLine($"Unknown benchmark category: {category}");
                Console.WriteLine("Available categories: streaming, database, cache, collection, musicbrainz, musicbrainz-query-probe, musicbrainz-import-probe, all");
                break;
        }

        return 0;
    }

    private static IConfig CreateConfig(string[] args)
    {
        var config = DefaultConfig.Instance;

        // Parse custom arguments
        string? artifactsPath = null;
        string[]? exporters = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--artifacts" && i + 1 < args.Length)
            {
                artifactsPath = args[i + 1];
                i++; // Skip next argument since we consumed it
            }
            else if (args[i] == "--exporters" && i + 1 < args.Length)
            {
                exporters = args[i + 1].Split(',');
                i++; // Skip next argument since we consumed it
            }
        }

        // Apply custom configuration
        if (!string.IsNullOrEmpty(artifactsPath))
        {
            config = config.WithArtifactsPath(artifactsPath);
        }

        if (exporters != null && exporters.Length > 0)
        {
            var exporterList = new List<IExporter>();

            foreach (var exporter in exporters)
            {
                switch (exporter.Trim().ToLower())
                {
                    case "json":
                        exporterList.Add(JsonExporter.Full);
                        break;
                    case "csv":
                        exporterList.Add(CsvExporter.Default);
                        break;
                    case "github":
                        exporterList.Add(MarkdownExporter.GitHub);
                        break;
                    case "html":
                        exporterList.Add(HtmlExporter.Default);
                        break;
                    default:
                        Console.WriteLine($"Warning: Unknown exporter '{exporter}'. Available: json, csv, github, html");
                        break;
                }
            }

            if (exporterList.Count > 0)
            {
                config = config.AddExporter(exporterList.ToArray());
            }
        }

        return config;
    }
}
