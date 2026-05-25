using Melodee.Cli.Command;
using Melodee.Cli.CommandSettings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        ATL.Settings.OutputStacktracesToConsole = false;

        // Support "mcli help [command path]" as an alias for "--help" / "<command> --help".
        // (Spectre.Console.Cli supports --help, but users commonly expect a help subcommand.)
        if (args.Length > 0 && string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            var noAnsi = args.Skip(1).Any(static a => string.Equals(a, "--no-ansi", StringComparison.OrdinalIgnoreCase));
            var forwarded = args.Skip(1)
                .Where(static a => !string.Equals(a, "--no-ansi", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            args = forwarded.Length == 0
                ? noAnsi ? ["--help", "--no-ansi"] : ["--help"]
                : noAnsi ? [.. forwarded, "--help", "--no-ansi"] : [.. forwarded, "--help"];
        }

        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("mcli");

            config.AddBranch<AlbumSettings>("album", add =>
            {
                add.SetDescription("Album data management and statistics");
                add.AddCommand<AlbumDeleteCommand>("delete")
                    .WithAlias("rm")
                    .WithDescription("Delete an album from the database.");
                add.AddCommand<AlbumImageIssuesCommand>("image-issues")
                    .WithAlias("img")
                    .WithDescription("Find albums with missing, invalid, or misnumbered images.");
                add.AddCommand<AlbumListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("List albums in the database.");
                add.AddCommand<AlbumSearchCommand>("search")
                    .WithAlias("s")
                    .WithDescription("Search for albums by name.");
                add.AddCommand<AlbumStatsCommand>("stats")
                    .WithDescription("Show album statistics grouped by status.");
            });
            config.AddBranch<ArtistSettings>("artist", add =>
            {
                add.SetDescription("Artist data management and statistics");
                add.AddCommand<ArtistDeleteCommand>("delete")
                    .WithAlias("rm")
                    .WithDescription("Delete an artist from the database.");
                add.AddCommand<ArtistFindDuplicatesCommand>("find-duplicates")
                    .WithAlias("fd")
                    .WithDescription("Find potential duplicate artists based on external IDs, name similarity, and album overlap.");
                add.AddCommand<ArtistListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("List artists in the database.");
                add.AddCommand<ArtistSearchCommand>("search")
                    .WithAlias("s")
                    .WithDescription("Search for artists by name.");
                add.AddCommand<ArtistStatsCommand>("stats")
                    .WithDescription("Show artist statistics including missing images and potential duplicates.");
            });
            config.AddBranch<BackupExportSettings>("backup", add =>
            {
                add.SetDescription("Backup and restore system configuration");
                add.AddCommand<BackupExportCommand>("export")
                    .WithDescription("Export system configuration to JSON. This includes all settings and libraries. Use --redact-secrets to mask sensitive values.");
            });
            config.AddBranch<ConfigurationSettings>("configuration", add =>
            {
                add.SetDescription("Manage Melodee configuration settings");
                add.AddCommand<ConfigurationGetCommand>("get")
                    .WithDescription("Get a specific configuration setting value.");
                add.AddCommand<ConfigurationListCommand>("list")
                    .WithDescription("List all configuration settings.");
                add.AddCommand<ConfigurationSetCommand>("set")
                    .WithDescription("Modify Melodee configuration.");
            });

            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription("Run environment and configuration diagnostics to validate Melodee is ready to run.");

            config.AddCommand<SearchCommand>("search")
                .WithAlias("s")
                .WithDescription("Search for artists, albums, songs, and playlists. Works in both local and remote mode.");

            config.AddBranch<ShowMpegInfoSettings>("file", add =>
            {
                add.SetDescription("File analysis and inspection tools");
                add.AddCommand<ShowMpegInfoCommand>("mpeg")
                    .WithDescription("Load given file and show MPEG info and if Melodee thinks this is a valid MPEG file.");
            });
            config.AddBranch<ImportSetting>("import", add =>
            {
                add.SetDescription("Import data from external sources");
                add.AddCommand<ImportUserFavoriteCommand>("user-favorite-songs")
                    .WithDescription("Import user favorite songs from a given CSV file.");
            });
            config.AddBranch<JobSettings>("job", add =>
            {
                add.SetDescription("Run jobs synchronously from the command line");
                add.AddCommand<JobRunArtistSearchEngineDatabaseHousekeepingJobCommand>("artistsearchengine-refresh")
                    .WithDescription("Run artist search engine refresh job. This updates the local database of artists albums from search engines.");
                add.AddCommand<JobListCommand>("list")
                    .WithDescription("List all known background jobs with their execution history and statistics.");
                add.AddCommand<JobRunMusicBrainzUpdateDatabaseJobCommand>("musicbrainz-update")
                    .WithDescription("Run MusicBrainz update database job. This downloads MusicBrainz data dump and creates local database for Melodee when scanning metadata.");
                add.AddCommand<JobRunCommand>("run")
                    .WithDescription("Run a specific job by name synchronously (waits for completion).");
            });
            config.AddBranch("library", add =>
            {
                add.SetDescription("Library management and operations");
                add.AddCommand<LibraryAlbumStatusReportCommand>("album-report")
                    .WithAlias("ar")
                    .WithDescription("Show report of albums found for library.");
                add.AddCommand<LibraryCleanCommand>("clean")
                    .WithAlias("c")
                    .WithDescription("Clean library and delete any folders without media files. CAUTION: Destructive!");
                add.AddCommand<AlbumFindDuplicateDirsCommand>("find-duplicate-dirs")
                    .WithAlias("fdd")
                    .WithDescription("Find duplicate album directories and optionally resolve using metadata searches.");
                add.AddCommand<LibraryListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("List all libraries with their details.");
                add.AddCommand<LibraryMoveOkCommand>("move-ok")
                    .WithAlias("m")
                    .WithDescription("Move 'Ok' status albums into the given library.");
                add.AddCommand<ProcessInboundCommand>("process")
                    .WithAlias("p")
                    .WithDescription("Process media in given library into staging library.");
                add.AddCommand<LibraryPurgeCommand>("purge")
                    .WithDescription("Purge library, deleting artists, albums, album songs and resetting library stats. CAUTION: Destructive!");
                add.AddCommand<LibraryRebuildCommand>("rebuild")
                    .WithAlias("r")
                    .WithDescription("Rebuild melodee metadata albums in the given library.");
                add.AddCommand<LibraryScanCommand>("scan")
                    .WithAlias("s")
                    .WithDescription("Full library scan workflow: process inbound → revalidate staging → move to storage → insert into database.");
                add.AddCommand<LibraryStatsCommand>("stats")
                    .WithAlias("ss")
                    .WithDescription("Show statistics for given library and library directory.");
                add.AddCommand<LibraryValidateCommand>("validate")
                    .WithAlias("v")
                    .WithDescription("Validate library integrity: check DB records match disk files and vice versa.");
            });
            config.AddBranch<ParseSettings>("parser", add =>
            {
                add.SetDescription("Parse and analyze media metadata files");
                add.AddCommand<ParseCommand>("parse")
                    .WithDescription("Parse a given media file (CUE, NFO, SFV, etc.) and show results.");
            });
            config.AddBranch<ShowTagsSettings>("tags", add =>
            {
                add.SetDescription("Display and manage media file tags");
                add.AddCommand<ShowTagsCommand>("show")
                    .WithDescription("Load given media file and show all known ID3 tags.");
            });
            config.AddBranch<SystemSettings>("system", add =>
            {
                add.SetDescription("System information and diagnostics");
                add.AddCommand<SystemInfoCommand>("info")
                    .WithDescription("Get system information (version, name, description). Works in both local and remote mode.");
            });
            config.AddBranch<UserSettings>("user", add =>
            {
                add.SetDescription("User account management");
                add.AddCommand<UserCreateCommand>("create")
                    .WithDescription("Create a new user account.");
                add.AddCommand<UserDeleteCommand>("delete")
                    .WithAlias("rm")
                    .WithDescription("Delete a user account.");
                add.AddCommand<UserListCommand>("list")
                    .WithAlias("ls")
                    .WithDescription("List all users. Works in both local and remote mode.");
                add.AddCommand<UserMeCommand>("me")
                    .WithDescription("Get information about the current authenticated user. Works in both local and remote mode.");
            });
            config.AddBranch<ValidateSettings>("validate", add =>
            {
                add.SetDescription("Validate media files and metadata");
                add.SetDefaultCommand<ValidateCommand>();
                add.AddCommand<ValidateCommand>("album")
                    .WithDescription("Validate a metadata album data file (melodee.json).");
            });
        });


        var doShowVersion = args.Length < 4 || (args.Length > 3 && args[2] == "--verbose" && args[3]?.ToUpper() != "FALSE");
        if (doShowVersion)
        {
            var version = typeof(Program).Assembly.GetName().Version;
            AnsiConsole.MarkupLine($":musical_note: [bold cyan]Melodee Command Line Interface[/] [grey]v{version}[/]");
            AnsiConsole.WriteLine();

            if (args.Length == 0)
            {
                var panel = new Panel(
                    "[yellow]Environment Variables:[/]\n" +
                    "  [cyan]MELODEE_APPSETTINGS_PATH[/] - Path to custom appsettings.json file\n" +
                    "  [cyan]ASPNETCORE_ENVIRONMENT[/]   - Environment name (Development, Production, etc.)\n\n" +
                    "[yellow]Examples:[/]\n" +
                    "  [grey]mcli library list[/]\n" +
                    "  [grey]mcli library album-report --library \"Staging\"[/]\n" +
                    "  [grey]mcli library album-report -l \"Staging\" --full[/]\n" +
                    "  [grey]mcli library stats --library \"Staging\"[/]\n" +
                    "  [grey]MELODEE_APPSETTINGS_PATH=\"/path/to/appsettings.json\" mcli library scan -l \"Storage\"[/]")
                {
                    Header = new PanelHeader("[bold]Quick Start[/]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(foreground: Color.Grey)
                };
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }
        }

        return app.Run(args);
    }
}
