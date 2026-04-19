using System.Text.RegularExpressions;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     List all configuration settings.
/// </summary>
public class ConfigurationListCommand : CommandBase<ConfigurationListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ConfigurationListSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<SettingService>();

        var result = await settingService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to retrieve settings.[/]");
            return 1;
        }

        var allSettings = result.Data.OrderBy(s => s.Key).ToList();

        if (!string.IsNullOrWhiteSpace(settings.Filter))
        {
            var pattern = "^" + Regex.Escape(settings.Filter).Replace("\\*", ".*") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
            allSettings = allSettings.Where(s => regex.IsMatch(s.Key)).ToList();
        }

        if (allSettings.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No settings found matching the filter.[/]");
            return 0;
        }

        if (settings.ReturnRaw)
        {
            var jsonOutput = allSettings.Select(s => new
            {
                s.Key,
                s.Value,
                s.Comment
            });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var groupedSettings = allSettings
            .GroupBy(s => s.Key.Contains('.') ? s.Key[..s.Key.IndexOf('.')] : "general")
            .OrderBy(g => g.Key);

        foreach (var group in groupedSettings)
        {
            var groupTable = new Table();
            groupTable.Border = TableBorder.Rounded;
            groupTable.Title = new TableTitle($"[bold blue]{group.Key}[/]");
            groupTable.AddColumn(new TableColumn("[bold]Key[/]").Width(50));
            groupTable.AddColumn(new TableColumn("[bold]Value[/]"));

            foreach (var setting in group.OrderBy(s => s.Key))
            {
                var displayKey = setting.Key;
                var displayValue = setting.Value ?? "[grey]<empty>[/]";

                if (displayValue.Length > 80)
                {
                    displayValue = displayValue[..77] + "...";
                }

                var valueColor = string.IsNullOrEmpty(setting.Value) ? "grey" : "green";
                groupTable.AddRow(
                    displayKey.EscapeMarkup(),
                    $"[{valueColor}]{displayValue.EscapeMarkup()}[/]"
                );
            }

            AnsiConsole.Write(groupTable);
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[grey]Total settings: {allSettings.Count}[/]");

        return 0;
    }
}
