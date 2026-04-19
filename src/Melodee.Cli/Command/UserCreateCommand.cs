using Melodee.Cli.CommandSettings;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Create a new user in the database.
/// </summary>
public class UserCreateCommand : CommandBase<UserCreateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, UserCreateSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var userProfileService = scope.ServiceProvider.GetRequiredService<UserProfileService>();

        var existingUser = await userProfileService.GetByUsernameAsync(settings.Username, cancellationToken);
        if (existingUser.IsSuccess && existingUser.Data != null)
        {
            if (!settings.Force)
            {
                AnsiConsole.MarkupLine($"[red]User already exists with username:[/] {settings.Username.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[grey]Use --force to delete and recreate the user.[/]");
                return 1;
            }

            var deleteResult = await userProfileService.DeleteAsync([existingUser.Data.Id], cancellationToken);
            if (!deleteResult.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete existing user:[/] {string.Join(", ", deleteResult.Messages ?? [])}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[yellow]Deleted existing user:[/] {settings.Username.EscapeMarkup()}");
        }

        var existingEmail = await userProfileService.GetByEmailAddressAsync(settings.Email, cancellationToken);
        if (existingEmail.IsSuccess && existingEmail.Data != null)
        {
            if (!settings.Force)
            {
                AnsiConsole.MarkupLine($"[red]User already exists with email:[/] {settings.Email.EscapeMarkup()}");
                AnsiConsole.MarkupLine("[grey]Use --force to delete and recreate the user.[/]");
                return 1;
            }

            var deleteResult = await userProfileService.DeleteAsync([existingEmail.Data.Id], cancellationToken);
            if (!deleteResult.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Failed to delete existing user:[/] {string.Join(", ", deleteResult.Messages ?? [])}");
                return 1;
            }

            AnsiConsole.MarkupLine($"[yellow]Deleted existing user with email:[/] {settings.Email.EscapeMarkup()}");
        }

        var result = await userProfileService.RegisterAsync(
            settings.Username,
            settings.Email,
            settings.Password,
            null,
            cancellationToken);

        if (!result.IsSuccess || result.Data == null)
        {
            AnsiConsole.MarkupLine($"[red]Failed to create user:[/] {string.Join(", ", result.Messages ?? [])}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓ User created successfully![/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold]Username:[/] {result.Data.UserName.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  [bold]Email:[/] {result.Data.Email.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"  [bold]ID:[/] {result.Data.Id}");
        AnsiConsole.MarkupLine($"  [bold]Admin:[/] {(result.Data.IsAdmin ? "[green]Yes[/]" : "[grey]No[/]")}");

        return 0;
    }
}
