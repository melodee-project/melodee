using Melodee.Cli.CommandSettings;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     List users in the database.
/// </summary>
public class UserListCommand : CommandBase<UserListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, UserListSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var userProfileService = scope.ServiceProvider.GetRequiredService<UserProfileService>();

        var pagedRequest = new PagedRequest
        {
            PageSize = (short)settings.Limit,
            OrderBy = new Dictionary<string, string> { { "UserName", "ASC" } }
        };

        var result = await userProfileService.ListAsync(pagedRequest, cancellationToken);

        if (!result.IsSuccess || result.Data == null || !result.Data.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No users found.[/]");
            return 0;
        }

        var users = result.Data.ToList();

        if (settings.ReturnRaw)
        {
            var jsonOutput = users.Select(u => new
            {
                u.Id,
                u.ApiKey,
                u.UserName,
                u.Email,
                u.IsAdmin,
                u.IsLocked,
                CreatedAt = u.CreatedAt.ToDateTimeUtc(),
                LastLoginAt = u.LastLoginAt?.ToDateTimeUtc(),
                LastActivityAt = u.LastActivityAt?.ToDateTimeUtc()
            });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("[bold]ID[/]").RightAligned());
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

            var statusDisplay = user.IsLocked
                ? "[red]🔒 Locked[/]"
                : "[green]✓[/]";

            var lastLoginDisplay = user.LastLoginAt.HasValue
                ? user.LastLoginAt.Value.ToDateTimeUtc().ToString(Iso8601DateFormat)
                : "[grey]Never[/]";

            table.AddRow(
                user.Id.ToString(),
                user.UserName.EscapeMarkup(),
                user.Email.EscapeMarkup(),
                adminDisplay,
                lastLoginDisplay,
                statusDisplay
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Showing {users.Count} of {result.TotalCount:N0} users[/]");

        return 0;
    }
}
