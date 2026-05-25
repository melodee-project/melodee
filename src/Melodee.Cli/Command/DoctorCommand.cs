using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using DecentDB.AdoNet;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Doctor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

public sealed class DoctorCommand : CommandBase<DoctorSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();

        var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<Serilog.ILogger>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var mbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MusicBrainzDbContext>>();
        var aseFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ArtistSearchEngineServiceDbContext>>();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var configurationFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
        var cacheManager = scope.ServiceProvider.GetRequiredService<ICacheManager>();

        var cliDoctorService = new CliDoctorService(
            logger,
            dbFactory,
            mbFactory,
            aseFactory,
            libraryService,
            configurationFactory,
            cacheManager,
            Configuration());

        var results = await cliDoctorService.RunAllChecksAsync(settings.WriteTest, cancellationToken);

        if (settings.ReturnRaw)
        {
            var obj = new
            {
                success = results.IssuesCount == 0,
                durationSeconds = startedAt.Elapsed.TotalSeconds,
                checks = results.Checks.Select(c => new
                {
                    name = c.Name,
                    success = c.Success,
                    details = c.Details,
                    durationMs = (int)c.Duration.TotalMilliseconds
                }),
                libraryPaths = results.LibraryPaths.Select(p => new
                {
                    name = p.Name,
                    type = p.Type,
                    path = p.Path,
                    exists = p.Exists,
                    writable = p.Writable,
                    details = p.Details
                }),
                configurableServices = results.ConfigurableServices.Select(s => new
                {
                    category = s.Category,
                    name = s.Name,
                    settingKey = s.SettingKey,
                    enabled = s.Enabled
                }),
                overlaps = results.Overlaps
            };

            Console.WriteLine(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            return results.IssuesCount == 0 ? 0 : 1;
        }

        RenderSummary(results, startedAt.Elapsed, settings.WriteTest);

        return results.IssuesCount == 0 ? 0 : 1;
    }

    private static void RenderSummary(CliDoctorCheckResults results, TimeSpan elapsed, bool writeTest)
    {
        var header = new Panel(new Markup($"[bold cyan]mcli doctor[/] completed in [grey]{elapsed:c}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Grey)
        };
        AnsiConsole.Write(header);
        AnsiConsole.WriteLine();

        var table = new Table().RoundedBorder();
        table.AddColumn("Check");
        table.AddColumn("Status");
        table.AddColumn("Details");
        table.AddColumn(new TableColumn("Duration").RightAligned());

        foreach (var c in results.Checks)
        {
            table.AddRow(
                c.Name.EscapeMarkup(),
                c.Success ? "[green]OK[/]" : "[red]FAIL[/]",
                c.Details.EscapeMarkup(),
                $"{c.Duration.TotalMilliseconds:0}ms");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (results.LibraryPaths.Count != 0)
        {
            var libTable = new Table().RoundedBorder();
            libTable.AddColumn("Library");
            libTable.AddColumn("Type");
            libTable.AddColumn("Exists");
            if (writeTest)
            {
                libTable.AddColumn("Writable");
            }
            libTable.AddColumn("Path");
            libTable.AddColumn("Details");

            foreach (var l in results.LibraryPaths.OrderBy(l => l.Type).ThenBy(l => l.Name))
            {
                var existsText = l.Exists ? "[green]✓[/]" : "[red]✗[/]";
                var writableText = l.Writable ? "[green]✓[/]" : "[red]✗[/]";

                if (writeTest)
                {
                    libTable.AddRow(
                        l.Name.EscapeMarkup(),
                        l.Type.EscapeMarkup(),
                        existsText,
                        writableText,
                        $"[dim]{l.Path.EscapeMarkup()}[/]",
                        l.Details.EscapeMarkup());
                }
                else
                {
                    libTable.AddRow(
                        l.Name.EscapeMarkup(),
                        l.Type.EscapeMarkup(),
                        existsText,
                        $"[dim]{l.Path.EscapeMarkup()}[/]",
                        l.Details.EscapeMarkup());
                }
            }

            AnsiConsole.Write(new Panel(libTable)
            {
                Header = new PanelHeader("[bold]Library Paths[/]", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: Color.Grey)
            });
            AnsiConsole.WriteLine();

            if (results.Overlaps.Count > 0)
            {
                var overlapText = string.Join("\n", results.Overlaps.Select(o => $"[red]![/] {o}"));
                AnsiConsole.Write(new Panel(new Markup(overlapText))
                {
                    Header = new PanelHeader("[bold]Path Overlaps[/]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(foreground: Color.Yellow)
                });
                AnsiConsole.WriteLine();
            }
        }

        if (results.ConfigurableServices.Count != 0)
        {
            var serviceTable = new Table().RoundedBorder();
            serviceTable.AddColumn("Category");
            serviceTable.AddColumn("Service");
            serviceTable.AddColumn("Status");
            serviceTable.AddColumn(new TableColumn("Setting Key").Centered());

            foreach (var s in results.ConfigurableServices.OrderBy(s => s.Category).ThenBy(s => s.Name))
            {
                var statusText = s.Enabled ? "[green]Enabled[/]" : "[dim]Disabled[/]";
                serviceTable.AddRow(
                    s.Category.EscapeMarkup(),
                    s.Name.EscapeMarkup(),
                    statusText,
                    $"[dim]{s.SettingKey.EscapeMarkup()}[/]");
            }

            AnsiConsole.Write(new Panel(serviceTable)
            {
                Header = new PanelHeader("[bold]Configurable Services[/]", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(foreground: Color.Grey)
            });
            AnsiConsole.WriteLine();
        }

        if (results.IssuesCount > 0)
        {
            var failed = results.Checks.Where(c => !c.Success).Select(c => c.Name).ToList();
            AnsiConsole.MarkupLine($"[red]Doctor found issues:[/] {string.Join(", ", failed.Select(x => x.EscapeMarkup()))}");
            AnsiConsole.MarkupLine("[grey]Tip:[/] verify MELODEE_APPSETTINGS_PATH, connection strings, and library path mounts/permissions.");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]All checks passed.[/]");
        }
    }
}

public sealed class CliDoctorService : DoctorServiceBase
{
    private static readonly string[] RequiredMusicBrainzTables =
    [
        "Artist",
        "Album",
        "ArtistAlias"
    ];

    private static readonly string[] RequiredArtistSearchTables =
    [
        "Artists",
        "Albums"
    ];

    private readonly Serilog.ILogger _logger;
    private readonly IDbContextFactory<MelodeeDbContext> _dbContextFactory;
    private readonly IDbContextFactory<MusicBrainzDbContext> _musicBrainzDbContextFactory;
    private readonly IDbContextFactory<ArtistSearchEngineServiceDbContext> _artistSearchEngineDbContextFactory;
    private readonly IConfigurationRoot _configuration;

    public CliDoctorService(
        Serilog.ILogger logger,
        IDbContextFactory<MelodeeDbContext> dbContextFactory,
        IDbContextFactory<MusicBrainzDbContext> musicBrainzDbContextFactory,
        IDbContextFactory<ArtistSearchEngineServiceDbContext> artistSearchEngineDbContextFactory,
        LibraryService libraryService,
        IMelodeeConfigurationFactory configurationFactory,
        ICacheManager cacheManager,
        IConfigurationRoot configuration) : base(dbContextFactory, libraryService, configurationFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _musicBrainzDbContextFactory = musicBrainzDbContextFactory;
        _artistSearchEngineDbContextFactory = artistSearchEngineDbContextFactory;
        _configuration = configuration;
    }

    public async Task<CliDoctorCheckResults> RunAllChecksAsync(bool writeTest = false, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var checks = new List<DoctorCheckResult>();
        var libraryPaths = new List<LibraryPathResult>();
        var configurableServices = new List<ConfigurableServiceResult>();
        var overlaps = new List<string>();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns([
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn()
            ])
            .StartAsync(async progress =>
            {
                await RunCheckAsync(progress, checks, "Configuration", async () =>
                {
                    var result = await RunConfigurationCheckAsync(cancellationToken);
                    var configPathInfo = GetConfigurationPathInfo();
                    return new DoctorCheckResult(
                        result.Name,
                        result.Success,
                        result.Success ? $"{result.Details}; {configPathInfo}" : $"{result.Details}; {configPathInfo}",
                        result.Duration);
                });

                await RunCheckAsync(progress, checks, "Database: PostgreSQL", async () => await RunDatabaseCheckAsync(cancellationToken));

                await RunCheckAsync(progress, checks, "Database: MusicBrainz (DecentDB)", async () =>
                {
                    var checkSw = Stopwatch.StartNew();
                    var cs = GetConnectionString("MusicBrainzConnection");
                    var fileInfo = DescribeFileDatabasePath(cs);
                    var probe = await ProbeDecentDbDatabaseAsync(
                            cs,
                            RequiredMusicBrainzTables,
                            [
                                """SELECT "Id" FROM "Artist" LIMIT 1""",
                                """SELECT "Id" FROM "Album" LIMIT 1"""
                            ],
                            requireRowsForReadQueries: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var details = probe.Success
                        ? $"{probe.Details}; {fileInfo}"
                        : $"{probe.Details}; {fileInfo}";
                    return new DoctorCheckResult("Database: MusicBrainz (DecentDB)", probe.Success, details, checkSw.Elapsed);
                });

                await RunCheckAsync(progress, checks, "Database: ArtistSearchEngine (DecentDB)", async () =>
                {
                    var checkSw = Stopwatch.StartNew();
                    var cs = GetConnectionString("ArtistSearchEngineConnection");
                    var fileInfo = DescribeFileDatabasePath(cs);
                    var dataSource = GetDataSourceFromConnectionString(cs);
                    if (!HasNonEmptyFileBackedDatabase(cs) &&
                        !string.IsNullOrWhiteSpace(dataSource) &&
                        Directory.Exists(Path.GetDirectoryName(dataSource)))
                    {
                        _logger.Information("[{JobName}] Creating new empty artist search engine database at [{Path}]", nameof(DoctorCommand), dataSource);
                        await using var db = await _artistSearchEngineDbContextFactory.CreateDbContextAsync(cancellationToken);
                        await db.Database.EnsureCreatedAsync(cancellationToken);
                        fileInfo = DescribeFileDatabasePath(cs);
                    }

                    if (!HasNonEmptyFileBackedDatabase(cs))
                    {
                        return new DoctorCheckResult(
                            "Database: ArtistSearchEngine (DecentDB)",
                            false,
                            $"Artist search engine database is empty or not initialized; {fileInfo}",
                            checkSw.Elapsed);
                    }

                    var probe = await ProbeDecentDbDatabaseAsync(
                            cs,
                            RequiredArtistSearchTables,
                            [
                                """SELECT "Id" FROM "Artists" LIMIT 1""",
                                """SELECT "Id" FROM "Albums" LIMIT 1"""
                            ],
                            requireRowsForReadQueries: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var details = probe.Success
                        ? $"{probe.Details}; {fileInfo}"
                        : $"{probe.Details}; {fileInfo}";
                    return new DoctorCheckResult("Database: ArtistSearchEngine (DecentDB)", probe.Success, details, checkSw.Elapsed);
                });

                await RunCheckAsync(progress, checks, "Library Paths", async () =>
                {
                    var (check, paths, pathOverlaps) = await RunLibraryPathCheckAsync(writeTest, cancellationToken);
                    libraryPaths.AddRange(paths);
                    overlaps.AddRange(pathOverlaps);
                    return check;
                });

                await RunCheckAsync(progress, checks, "Configurable Services", async () =>
                {
                    var (check, services) = await RunConfigurableServicesCheckAsync(cancellationToken);
                    configurableServices.AddRange(services);
                    return check;
                });
            });

        sw.Stop();

        return new CliDoctorCheckResults
        {
            Checks = checks,
            LibraryPaths = libraryPaths,
            ConfigurableServices = configurableServices,
            Overlaps = overlaps,
            Duration = sw.Elapsed
        };
    }

    private static async Task RunCheckAsync(ProgressContext progress, List<DoctorCheckResult> results, string name, Func<Task<DoctorCheckResult>> action)
    {
        var task = progress.AddTask($"{name}...", maxValue: 1);
        var result = await action();
        task.Increment(1);

        var icon = result.Success ? "[green]✓[/]" : "[red]✗[/]";
        task.Description = $"{icon} {name}";

        results.Add(result);
    }

    private static string GetConfigurationPathInfo()
    {
        var appSettingsPath = Environment.GetEnvironmentVariable("MELODEE_APPSETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(appSettingsPath))
        {
            return File.Exists(appSettingsPath)
                ? $"MELODEE_APPSETTINGS_PATH={appSettingsPath}"
                : $"MELODEE_APPSETTINGS_PATH={appSettingsPath} (missing)";
        }

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var basePath = Directory.GetCurrentDirectory();
        var defaultFile = Path.Combine(basePath, "appsettings.json");
        var envFile = Path.Combine(basePath, $"appsettings.{env}.json");

        var defaultExists = File.Exists(defaultFile);
        var envExists = File.Exists(envFile);

        return $"appsettings.json={defaultExists}; appsettings.{env}.json={envExists}; cwd={basePath}";
    }

    private string GetConnectionString(string name)
    {
        return _configuration.GetConnectionString(name) ?? string.Empty;
    }

    private static string DescribeFileDatabasePath(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var dataSource = builder.ContainsKey("Data Source") ? builder["Data Source"]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                return "DataSource=(empty)";
            }

            var exists = File.Exists(dataSource);
            return $"DataSource={dataSource}; Exists={exists}";
        }
        catch
        {
            return "DataSource=(unparseable)";
        }
    }

    private static bool HasNonEmptyFileBackedDatabase(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var dataSource = builder.ContainsKey("Data Source") ? builder["Data Source"]?.ToString() : null;
            return !string.IsNullOrWhiteSpace(dataSource)
                   && File.Exists(dataSource)
                   && new FileInfo(dataSource).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetDataSourceFromConnectionString(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return builder.ContainsKey("Data Source") ? builder["Data Source"]?.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<DecentDbDoctorProbeResult> ProbeDecentDbDatabaseAsync(
        string connectionString,
        IReadOnlyCollection<string> requiredTables,
        IReadOnlyCollection<string> readQueries,
        bool requireRowsForReadQueries,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dataSource = GetDataSourceFromConnectionString(connectionString);
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                return DecentDbDoctorProbeResult.Fail("Data Source is empty or invalid");
            }

            if (!File.Exists(dataSource))
            {
                return DecentDbDoctorProbeResult.Fail($"Database file does not exist: {dataSource}");
            }

            var databaseFile = new FileInfo(dataSource);
            if (databaseFile.Length == 0)
            {
                return DecentDbDoctorProbeResult.Fail($"Database file is empty: {dataSource}");
            }

            await using var connection = new DecentDBConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var tables = ParseTableNames(connection.ListTablesJson());
            var missingTables = requiredTables
                .Where(required => !tables.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (missingTables.Length > 0)
            {
                return DecentDbDoctorProbeResult.Fail($"Missing required table(s): {string.Join(", ", missingTables)}");
            }

            foreach (var table in requiredTables)
            {
                var ddl = connection.GetTableDdl(table);
                if (string.IsNullOrWhiteSpace(ddl))
                {
                    return DecentDbDoctorProbeResult.Fail($"Required table [{table}] has no DDL from schema introspection");
                }
            }

            foreach (var query in readQueries)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = query;
                var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (requireRowsForReadQueries && (scalar is null || scalar == DBNull.Value))
                {
                    return DecentDbDoctorProbeResult.Fail($"Read query returned no rows: {query}");
                }
            }

            return DecentDbDoctorProbeResult.Ok($"OK; opened; tables={tables.Length}; readQueries={readQueries.Count}");
        }
        catch (Exception ex)
        {
            return DecentDbDoctorProbeResult.Fail($"Unable to open/query DecentDB: {ex.Message}");
        }
    }

    private static string[] ParseTableNames(string tablesJson)
    {
        if (string.IsNullOrWhiteSpace(tablesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(tablesJson) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

public sealed class CliDoctorCheckResults
{
    public List<DoctorCheckResult> Checks { get; init; } = new();
    public List<LibraryPathResult> LibraryPaths { get; init; } = new();
    public List<ConfigurableServiceResult> ConfigurableServices { get; init; } = new();
    public List<string> Overlaps { get; init; } = new();
    public TimeSpan Duration { get; init; }

    public int IssuesCount => Checks.Count(c => !c.Success);
}

public sealed record DecentDbDoctorProbeResult(bool Success, string Details)
{
    public static DecentDbDoctorProbeResult Ok(string details) => new(true, details);

    public static DecentDbDoctorProbeResult Fail(string details) => new(false, details);
}
