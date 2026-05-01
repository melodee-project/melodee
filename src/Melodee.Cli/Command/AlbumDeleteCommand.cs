using Melodee.Cli.CommandSettings;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Delete an album from the database.
/// </summary>
public class AlbumDeleteCommand : CommandBase<AlbumDeleteSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AlbumDeleteSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var albumService = scope.ServiceProvider.GetRequiredService<AlbumService>();

        var albumResult = await albumService.GetAsync(settings.AlbumId, cancellationToken);
        if (!albumResult.IsSuccess || albumResult.Data == null)
        {
            AnsiConsole.MarkupLine($"[red]Album not found with ID:[/] {settings.AlbumId}");
            return 1;
        }

        var album = albumResult.Data;
        var albumDirectoryInfo = album.ToFileSystemDirectoryInfo();
        var albumDirectoryPath = albumDirectoryInfo.Path;

        AnsiConsole.MarkupLine($"[bold]Album:[/] {album.Name.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Artist:[/] {album.Artist.Name.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Songs:[/] {album.SongCount}");
        AnsiConsole.MarkupLine($"[bold]Directory:[/] {albumDirectoryPath.EscapeMarkup()}");
        AnsiConsole.WriteLine();

        if (album.IsLocked)
        {
            AnsiConsole.MarkupLine("[red]Cannot delete a locked album. Unlock the album first.[/]");
            return 1;
        }

        if (!settings.SkipConfirmation)
        {
            var deleteFiles = !settings.KeepFiles;
            var confirmMessage = deleteFiles
                ? $"[red]Delete album '{album.Name.EscapeMarkup()}' and ALL files on disk?[/]"
                : $"[yellow]Delete album '{album.Name.EscapeMarkup()}' from database (keeping files)?[/]";

            if (!AnsiConsole.Confirm(confirmMessage, false))
            {
                AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
                return 0;
            }
        }

        string? backupPath = null;
        if (settings.KeepFiles && Directory.Exists(albumDirectoryPath))
        {
            backupPath = Path.Combine(Path.GetTempPath(), $"melodee_album_backup_{album.Id}_{Guid.NewGuid():N}");
            try
            {
                Directory.Move(albumDirectoryPath, backupPath);
                AnsiConsole.MarkupLine($"[grey]Backed up album directory to: {backupPath.EscapeMarkup()}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to backup directory: {ex.Message.EscapeMarkup()}[/]");
                return 1;
            }
        }

        try
        {
            var deleteResult = await albumService.DeleteAsync([settings.AlbumId], cancellationToken);

            if (!deleteResult.IsSuccess || !deleteResult.Data)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete album:[/] {string.Join(", ", deleteResult.Messages ?? [])}");

                if (backupPath != null && Directory.Exists(backupPath))
                {
                    try
                    {
                        Directory.Move(backupPath, albumDirectoryPath);
                        AnsiConsole.MarkupLine("[grey]Restored album directory from backup.[/]");
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
                    Directory.Move(backupPath, albumDirectoryPath);
                    AnsiConsole.MarkupLine($"[green]✓ Album deleted from database. Files preserved at:[/] {albumDirectoryPath.EscapeMarkup()}");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Album deleted but failed to restore files: {ex.Message.EscapeMarkup()}[/]");
                    AnsiConsole.MarkupLine($"[yellow]Files are at: {backupPath.EscapeMarkup()}[/]");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓ Album '{album.Name.EscapeMarkup()}' deleted successfully.[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error deleting album: {ex.Message.EscapeMarkup()}[/]");

            if (backupPath != null && Directory.Exists(backupPath))
            {
                try
                {
                    Directory.Move(backupPath, albumDirectoryPath);
                    AnsiConsole.MarkupLine("[grey]Restored album directory from backup.[/]");
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
