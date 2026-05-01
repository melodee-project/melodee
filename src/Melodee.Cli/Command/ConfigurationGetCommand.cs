using Melodee.Cli.CommandSettings;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Get a specific configuration setting value.
/// </summary>
public class ConfigurationGetCommand : CommandBase<ConfigurationGetSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ConfigurationGetSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var settingService = scope.ServiceProvider.GetRequiredService<SettingService>();

        var result = await settingService.GetAsync(settings.Key, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            if (settings.ReturnRaw)
            {
                return 1;
            }
            AnsiConsole.MarkupLine($"[red]Setting not found:[/] {settings.Key.EscapeMarkup()}");
            return 1;
        }

        var setting = result.Data;

        if (settings.ReturnRaw)
        {
            Console.WriteLine(setting.Value ?? string.Empty);
            return 0;
        }

        var panel = new Panel(
            new Rows(
                new Markup($"[bold]Key:[/] {setting.Key.EscapeMarkup()}"),
                new Markup($"[bold]Value:[/] [green]{(setting.Value ?? "<empty>").EscapeMarkup()}[/]"),
                new Markup($"[bold]Comment:[/] [grey]{(setting.Comment ?? "<none>").EscapeMarkup()}[/]")
            ))
        {
            Header = new PanelHeader($"[blue]Configuration Setting[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);

        return 0;
    }
}
