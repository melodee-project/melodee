using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

/// <summary>
///     Validates a given album or all albums for an artist
/// </summary>
public class ValidateCommand : CommandBase<ValidateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ValidateSettings settings, CancellationToken cancellationToken)
    {
        var isValid = false;

        using (var scope = CreateServiceProvider().CreateScope())
        {
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();

            var config = await configFactory.GetConfigurationAsync();

            // Handle artist validation
            if (settings.ArtistApiKey != null)
            {
                var artistApiKey = SafeParser.ToGuid(settings.ArtistApiKey);
                if (artistApiKey == null)
                {
                    Log.Logger.Error("Invalid artist ApiKey: {ApiKey}", settings.ArtistApiKey);
                    return 1;
                }

                var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();
                var validationResult = await artistService.ValidateArtistAlbumsAsync(artistApiKey.Value, cancellationToken).ConfigureAwait(false);

                if (!validationResult.IsSuccess || validationResult.Data == null)
                {
                    AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(string.Join(", ", validationResult.Messages ?? []))}[/]");
                    return 1;
                }

                var result = validationResult.Data;
                isValid = result.IsValid;

                if (settings.Json == true)
                {
                    AnsiConsole.WriteLine(serializer.Serialize(result) ?? string.Empty);
                    return isValid ? 0 : 1;
                }

                // Display summary table
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Property")
                    .AddColumn("Value");

                summaryTable.AddRow("Artist", result.ArtistName);
                summaryTable.AddRow("Total Albums", result.TotalAlbums.ToString());
                summaryTable.AddRow("Valid Albums", $"[green]{result.ValidAlbums}[/]");
                summaryTable.AddRow("Invalid Albums", result.InvalidAlbums > 0 ? $"[red]{result.InvalidAlbums}[/]" : "0");
                summaryTable.AddRow("Status", result.IsValid ? "[green]✓ All Valid[/]" : "[red]✗ Issues Found[/]");

                AnsiConsole.Write(new Panel(summaryTable).Header("Artist Albums Validation Summary").BorderColor(result.IsValid ? Color.Green : Color.Red));

                // Display album details table
                var albumTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Album")
                    .AddColumn("ApiKey")
                    .AddColumn("Year")
                    .AddColumn("Status")
                    .AddColumn("Directory")
                    .AddColumn("Cover")
                    .AddColumn("Issues");

                foreach (var albumDetail in result.AlbumResults)
                {
                    var statusColor = albumDetail.IsValid ? "green" : "red";
                    var dirColor = albumDetail.DirectoryExists ? "green" : "red";
                    var coverColor = albumDetail.HasCoverImage ? "green" : "yellow";
                    var issueCount = albumDetail.Messages.Count(m => m.Severity == Common.Models.Validation.ValidationResultMessageSeverity.Critical);

                    albumTable.AddRow(
                        albumDetail.AlbumName.Length > 40 ? albumDetail.AlbumName[..37] + "..." : albumDetail.AlbumName,
                        albumDetail.AlbumApiKey.ToString(),
                        albumDetail.ReleaseYear?.ToString() ?? "—",
                        $"[{statusColor}]{(albumDetail.IsValid ? "✓" : "✗")}[/]",
                        $"[{dirColor}]{(albumDetail.DirectoryExists ? "✓" : "✗")}[/]",
                        $"[{coverColor}]{(albumDetail.HasCoverImage ? "✓" : "✗")}[/]",
                        issueCount > 0 ? $"[red]{issueCount}[/]" : "[green]0[/]"
                    );
                }

                AnsiConsole.Write(new Panel(albumTable).Header("Album Details").BorderColor(Color.Blue));

                // Show detailed issues for invalid albums
                var invalidAlbums = result.AlbumResults.Where(a => !a.IsValid || a.Messages.Any()).ToList();
                if (invalidAlbums.Any())
                {
                    AnsiConsole.MarkupLine("\n[yellow]Issues Found:[/]");
                    foreach (var invalidAlbum in invalidAlbums)
                    {
                        if (invalidAlbum.Messages.Any())
                        {
                            AnsiConsole.MarkupLine($"\n[bold]{Markup.Escape(invalidAlbum.AlbumName)}[/] ({invalidAlbum.ReleaseYear?.ToString() ?? "?"}):");
                            foreach (var msg in invalidAlbum.Messages)
                            {
                                var icon = msg.Severity == Common.Models.Validation.ValidationResultMessageSeverity.Critical ? "[red]✗[/]" : "[yellow]⚠[/]";
                                AnsiConsole.MarkupLine($"  {icon} {Markup.Escape(msg.Message)}");
                            }
                        }
                    }
                }

                return isValid ? 0 : 1;
            }

            // Handle single album validation
            var albumValidator = new AlbumValidator(config);

            Album? album = null;
            if (settings is { LibraryName: not null, Id: not null })
            {
                var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

                var libraryListResult = await libraryService.ListAsync(new PagedRequest()).ConfigureAwait(false);
                var library = libraryListResult.Data.FirstOrDefault(x => x.Name == settings.LibraryName);
                if (library == null)
                {
                    Log.Logger.Error("Could not find library named {LibraryName}", settings.LibraryName);
                    return 0;
                }

                var albumDiscoveryService = scope.ServiceProvider.GetRequiredService<AlbumDiscoveryService>();
                await albumDiscoveryService.InitializeAsync();
                album = await albumDiscoveryService.AlbumByUniqueIdAsync(library.ToFileSystemDirectoryInfo(), settings.Id.Value);
            }
            else if (settings.ApiKey != null)
            {
                var albumService = scope.ServiceProvider.GetRequiredService<AlbumService>();
                var albumResult = await albumService.GetByApiKeyAsync(SafeParser.ToGuid(settings.ApiKey)!.Value).ConfigureAwait(false);
                if (albumResult.IsSuccess)
                {
                    album = await Album.DeserializeAndInitializeAlbumAsync(serializer, Path.Combine(albumResult.Data!.Directory, "melodee.json")).ConfigureAwait(false);
                }
            }
            else if (settings.PathToMelodeeDataFile != null)
            {
                album = await Album.DeserializeAndInitializeAlbumAsync(serializer, Path.Combine(settings.PathToMelodeeDataFile)).ConfigureAwait(false);
            }

            if (album != null)
            {
                var validationResult = albumValidator.ValidateAlbum(album);
                isValid = validationResult.IsSuccess;

                if (settings.Json == true)
                {
                    AnsiConsole.WriteLine(serializer.Serialize(validationResult) ?? string.Empty);
                    return isValid ? 0 : 1;
                }

                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(validationResult) ?? string.Empty))
                        .Header("Validation Result")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Red));
            }

            return isValid ? 0 : 1;
        }
    }
}
