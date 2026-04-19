using Melodee.Cli.CommandSettings;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Delete a user from the database.
/// </summary>
public class UserDeleteCommand : CommandBase<UserDeleteSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, UserDeleteSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var userProfileService = scope.ServiceProvider.GetRequiredService<UserProfileService>();

        var userResult = await userProfileService.GetAsync(settings.UserId, cancellationToken);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            AnsiConsole.MarkupLine($"[red]User not found with ID:[/] {settings.UserId}");
            return 1;
        }

        var user = userResult.Data;

        AnsiConsole.MarkupLine($"[bold]User:[/] {user.UserName.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Email:[/] {user.Email.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Admin:[/] {(user.IsAdmin ? "Yes" : "No")}");
        AnsiConsole.WriteLine();

        if (user.IsAdmin)
        {
            AnsiConsole.MarkupLine("[yellow]Warning: This user is an administrator.[/]");
        }

        if (!settings.SkipConfirmation)
        {
            if (!AnsiConsole.Confirm($"[red]Delete user '{user.UserName.EscapeMarkup()}'?[/]", false))
            {
                AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
                return 0;
            }
        }

        var deleteResult = await userProfileService.DeleteAsync([settings.UserId], cancellationToken);

        if (!deleteResult.IsSuccess || !deleteResult.Data)
        {
            AnsiConsole.MarkupLine($"[red]Failed to delete user:[/] {string.Join(", ", deleteResult.Messages ?? [])}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓ User '{user.UserName.EscapeMarkup()}' deleted successfully.[/]");

        return 0;
    }
}
