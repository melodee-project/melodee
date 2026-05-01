using Melodee.Cli.CommandSettings;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

/// <summary>
///     This runs the library purge command that erases everything in a library
/// </summary>
public class LibraryPurgeCommand : CommandBase<LibrarySettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LibrarySettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var libraries = await libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken).ConfigureAwait(false);
            var library = libraries.Data.FirstOrDefault(x => x.Name.ToNormalizedString() == settings.LibraryName?.ToNormalizedString());
            if (library == null)
            {
                AnsiConsole.Write(
                    new Panel("Invalid library name.")
                        .Header("Purge Failed")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Red));
                return 0;
            }

            var result = await libraryService.PurgeLibraryAsync(library.Id, cancellationToken);

            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();

            if (!result.IsSuccess && settings.Verbose)
            {
                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(result) ?? string.Empty))
                        .Header("Failed")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Yellow));
                return 0;
            }

            if (settings.Verbose)
            {
                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(result) ?? string.Empty))
                        .Header("Successful")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Green));
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Successful[/]");
            }

            return 1;
        }
    }

}
