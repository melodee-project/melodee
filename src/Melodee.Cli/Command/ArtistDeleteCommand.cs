using Melodee.Cli.CommandSettings;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Delete an artist from the database.
/// </summary>
public class ArtistDeleteCommand : CommandBase<ArtistDeleteSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ArtistDeleteSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();

        var artistResult = await artistService.GetAsync(settings.ArtistId, cancellationToken);
        if (!artistResult.IsSuccess || artistResult.Data == null)
        {
            AnsiConsole.MarkupLine($"[red]Artist not found with ID:[/] {settings.ArtistId}");
            return 1;
        }

        var artist = artistResult.Data;
        var artistDirectoryInfo = artist.ToFileSystemDirectoryInfo();
        var artistDirectoryPath = artistDirectoryInfo.Path;

        AnsiConsole.MarkupLine($"[bold]Artist:[/] {artist.Name.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Albums:[/] {artist.AlbumCount}");
        AnsiConsole.MarkupLine($"[bold]Songs:[/] {artist.SongCount}");
        AnsiConsole.MarkupLine($"[bold]Directory:[/] {artistDirectoryPath.EscapeMarkup()}");
        AnsiConsole.WriteLine();

        if (artist.IsLocked)
        {
            AnsiConsole.MarkupLine("[red]Cannot delete a locked artist. Unlock the artist first.[/]");
            return 1;
        }

        if (!settings.SkipConfirmation)
        {
            var deleteFiles = !settings.KeepFiles;
            var confirmMessage = deleteFiles
                ? $"[red]Delete artist '{artist.Name.EscapeMarkup()}' and ALL files on disk?[/]"
                : $"[yellow]Delete artist '{artist.Name.EscapeMarkup()}' from database (keeping files)?[/]";

            if (!AnsiConsole.Confirm(confirmMessage, false))
            {
                AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
                return 0;
            }
        }

        string? backupPath = null;
        if (settings.KeepFiles && Directory.Exists(artistDirectoryPath))
        {
            backupPath = Path.Combine(Path.GetTempPath(), $"melodee_artist_backup_{artist.Id}_{Guid.NewGuid():N}");
            try
            {
                Directory.Move(artistDirectoryPath, backupPath);
                AnsiConsole.MarkupLine($"[grey]Backed up artist directory to: {backupPath.EscapeMarkup()}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to backup directory: {ex.Message.EscapeMarkup()}[/]");
                return 1;
            }
        }

        try
        {
            var deleteResult = await artistService.DeleteAsync([settings.ArtistId], cancellationToken);

            if (!deleteResult.IsSuccess || !deleteResult.Data)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete artist:[/] {string.Join(", ", deleteResult.Messages ?? [])}");

                if (backupPath != null && Directory.Exists(backupPath))
                {
                    try
                    {
                        Directory.Move(backupPath, artistDirectoryPath);
                        AnsiConsole.MarkupLine("[grey]Restored artist directory from backup.[/]");
                    }
                    catch
                    {
                        AnsiConsole.MarkupLine($"[yellow]Backup directory remains at: {backupPath.EscapeMarkup()}[/]");
                    }
                }
                return 1;
            }

            if (backupPath != null && Directory.Exists(backupPath))
            {
                try
                {
                    Directory.Move(backupPath, artistDirectoryPath);
                    AnsiConsole.MarkupLine($"[green]✓ Artist deleted from database. Files preserved at:[/] {artistDirectoryPath.EscapeMarkup()}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Artist deleted but failed to restore files: {ex.Message.EscapeMarkup()}[/]");
                    AnsiConsole.MarkupLine($"[yellow]Files are at: {backupPath.EscapeMarkup()}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ Artist '{artist.Name.EscapeMarkup()}' deleted successfully.[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error deleting artist: {ex.Message.EscapeMarkup()}[/]");

            if (backupPath != null && Directory.Exists(backupPath))
            {
                try
                {
                    Directory.Move(backupPath, artistDirectoryPath);
                    AnsiConsole.MarkupLine("[grey]Restored artist directory from backup.[/]");
                }
                catch
                {
                    AnsiConsole.MarkupLine($"[yellow]Backup directory remains at: {backupPath.EscapeMarkup()}[/]");
                }
            }

            return 1;
        }
    }
}
