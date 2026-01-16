using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

public sealed class BackupExportCommand : CommandBase<BackupExportSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, BackupExportSettings settings, CancellationToken cancellationToken)
    {
        var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<Serilog.ILogger>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var cacheManager = scope.ServiceProvider.GetRequiredService<ICacheManager>();
        var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();

        var exportService = new SystemExportService(logger, cacheManager, configFactory, dbFactory);
        var result = await exportService.ExportAsync(settings.RedactSecrets, cancellationToken);

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Error: {result.ErrorMessage}[/]");
            return 1;
        }

        if (settings.WriteToStdout || settings.ReturnRaw)
        {
            Console.WriteLine(result.Json);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            var outputDir = Path.GetDirectoryName(settings.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await File.WriteAllTextAsync(settings.OutputPath, result.Json!, cancellationToken);
            AnsiConsole.MarkupLine($"[green]Export written to:[/] {settings.OutputPath}");
            AnsiConsole.MarkupLine($"[grey]Settings: {result.SettingsCount}, Libraries: {result.LibrariesCount}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[yellow]No output specified. Use --output <path> or --stdout.[/]");
        return 1;
    }
}
