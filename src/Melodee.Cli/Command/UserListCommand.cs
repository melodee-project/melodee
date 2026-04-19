using System.Text.Json;
using Melodee.Cli.Client;
using Melodee.Cli.CommandSettings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
/// List users in the database.
/// Works in both local and remote mode.
/// </summary>
public class UserListCommand : CommandBase<UserListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, UserListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateMelodeeClient(settings);
            var users = await client.GetAdminUsersAsync(cancellationToken);

            if (!users.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No users found.[/]");
                return 0;
            }

            if (settings.ReturnRaw)
            {
                var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return 0;
            }

            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn(new TableColumn("[bold]ID[/]"));
            table.AddColumn(new TableColumn("[bold]Username[/]"));
            table.AddColumn(new TableColumn("[bold]Email[/]"));
            table.AddColumn(new TableColumn("[bold]Admin[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Last Login[/]"));
            table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

            foreach (var user in users)
            {
                var adminDisplay = user.IsAdmin
                    ? "[green]Yes[/]"
                    : "[grey]No[/]";

                var statusDisplay = user.IsEnabled
                    ? "[green]✓[/]"
                    : "[red]🔒 Locked[/]";

                var lastLoginDisplay = !string.IsNullOrWhiteSpace(user.LastLoginAt)
                    ? DateTime.Parse(user.LastLoginAt).ToString(Iso8601DateFormat)
                    : "[grey]Never[/]";

                table.AddRow(
                    user.Id.ToString()[..8] + "...", // Show first 8 chars of GUID
                    user.Username.EscapeMarkup(),
                    user.Email?.EscapeMarkup() ?? "[grey]N/A[/]",
                    adminDisplay,
                    lastLoginDisplay,
                    statusDisplay
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]Showing {users.Count:N0} users[/]");

            return 0;
        }
        catch (MelodeeRemoteException ex)
        {
            Console.Error.WriteLine($"ERROR ({ex.HttpStatusCode ?? 0} {ex.Message})");
            return ex.GetExitCode();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 15; // Unexpected error
        }
    }
}
