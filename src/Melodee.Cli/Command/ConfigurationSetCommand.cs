using Melodee.Cli.CommandSettings;
using Melodee.Common.Data.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Modifies configuration values.
/// </summary>
public class ConfigurationSetCommand : CommandBase<ConfigurationSetSetting>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ConfigurationSetSetting settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var settingService = scope.ServiceProvider.GetRequiredService<SettingService>();

            var config = await settingService.GetAsync(settings.Key, cancellationToken).ConfigureAwait(false);
            if (!config.IsSuccess || config.Data == null)
            {
                AnsiConsole.MarkupLine(
                    ":cross_mark: Configuration key [bold red]{0}[/] not found. Creating new entry.",
                    settings.Key);

                var addResult = await settingService.AddAsync(new Setting
                {
                    Key = settings.Key,
                    Value = settings.Value,
                    CreatedAt = default
                }, cancellationToken).ConfigureAwait(false);
                if (addResult.IsSuccess)
                {
                    AnsiConsole.MarkupLine(":party_popper: Configuration created.");
                    return 0;
                }

                AnsiConsole.MarkupLine(":cross_mark: Failed to create configuration: {0}", addResult.Messages?.FirstOrDefault() ?? string.Empty);
                return 1;
            }

            if (settings.Remove)
            {
                config.Data.Value = string.Empty;
            }
            else
            {
                config.Data.Value = settings.Value;
            }

            var result = await settingService.UpdateAsync(config.Data, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                AnsiConsole.MarkupLine(":party_popper: Configuration updated.");
                return 0;
            }

            return 1;
        }
    }
}
