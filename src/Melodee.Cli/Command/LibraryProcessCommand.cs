using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Models;
using Melodee.Common.Plugins.Processor.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

public class ProcessInboundCommand : CommandBase<LibraryProcessSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryProcessSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var melodeeConfigurationFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
            var melodeeConfiguration = await melodeeConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            string directoryInbound;
            string directoryStaging;
            Instant? lastScanAt = null;

            if (settings.IsPathBasedMode)
            {
                directoryInbound = settings.InboundPath!;
                directoryStaging = settings.StagingPath!;
            }
            else
            {
                var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

                var libraryToProcess = (await libraryService.ListAsync(new PagedRequest(), cancellationToken)).Data?.FirstOrDefault(x => string.Equals(x.Name, settings.LibraryName, StringComparison.OrdinalIgnoreCase));
                if (libraryToProcess == null)
                {
                    throw new Exception($"Library with name [{settings.LibraryName}] not found.");
                }

                directoryInbound = libraryToProcess.Path;
                directoryStaging = (await libraryService.GetStagingLibraryAsync(cancellationToken).ConfigureAwait(false)).Data!.Path;
                lastScanAt = settings.ForceMode ? null : libraryToProcess.LastScanAt;
            }

            var grid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(4))
                .AddColumn()
                .AddRow("[b]Mode[/]", settings.IsPathBasedMode ? "[blue]Path-based[/] (bypassing database)" : "[blue]Library-based[/]")
                .AddRow("[b]Copy Mode[/]", SafeParser.ToBoolean(melodeeConfiguration.Configuration[SettingRegistry.ProcessingDoDeleteOriginal]) ? "[dim]No[/]" : "[yellow]Yes[/]")
                .AddRow("[b]Force Mode[/]", SafeParser.ToBoolean(melodeeConfiguration.Configuration[SettingRegistry.ProcessingDoOverrideExistingMelodeeDataFiles]) ? "[yellow]Yes[/]" : "[dim]No[/]")
                .AddRow("[b]PreDiscovery Script[/]", $"{SafeParser.ToString(melodeeConfiguration.Configuration[SettingRegistry.ScriptingPreDiscoveryScript])}")
                .AddRow("[b]Inbound[/]", $"{directoryInbound.EscapeMarkup()}")
                .AddRow("[b]Staging[/]", $"{directoryStaging.EscapeMarkup()}");

            AnsiConsole.Write(
                new Panel(grid)
                    .Header("[yellow]Process Inbound Configuration[/]")
                    .RoundedBorder()
                    .BorderColor(Color.Blue));

            AnsiConsole.WriteLine();

            var processor = scope.ServiceProvider.GetRequiredService<DirectoryProcessorToStagingService>();
            var dirInfo = new DirectoryInfo(directoryInbound);
            if (!dirInfo.Exists)
            {
                throw new Exception($"Directory [{directoryInbound}] does not exist.");
            }

            var startTicks = Stopwatch.GetTimestamp();

            Log.Debug("\ud83d\udcc1 Processing directory [{Inbound}]", directoryInbound);

            await processor.InitializeAsync(null, settings.IsPathBasedMode ? directoryStaging : null, cancellationToken);

            var fileSystemDirectoryInfo = new FileSystemDirectoryInfo
            {
                Path = directoryInbound,
                Name = dirInfo.Name
            };

            OperationResult<DirectoryProcessorResult>? result = null;

            var totalDirectories = 0;
            var processedDirectories = 0;
            var currentActivity = "Initializing...";
            var lastProcessedAlbum = string.Empty;

            processor.OnProcessingStart += (_, count) =>
            {
                totalDirectories = count;
            };

            processor.OnDirectoryProcessed += (_, fsInfo) =>
            {
                Interlocked.Increment(ref processedDirectories);
                lastProcessedAlbum = fsInfo.Name;
            };

            processor.OnProcessingEvent += (_, message) =>
            {
                currentActivity = message.Length > 60 ? message[..57] + "..." : message;
            };

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var mainTask = ctx.AddTask("[yellow]Processing inbound directory[/]", autoStart: false);
                    mainTask.IsIndeterminate = true;
                    mainTask.StartTask();

                    var processingTask = processor.ProcessDirectoryAsync(fileSystemDirectoryInfo, lastScanAt, settings.ProcessLimit, cancellationToken);

                    while (!processingTask.IsCompleted)
                    {
                        if (totalDirectories > 0)
                        {
                            mainTask.IsIndeterminate = false;
                            mainTask.MaxValue = totalDirectories;
                            mainTask.Value = processedDirectories;
                            mainTask.Description = $"[yellow]Processing:[/] [dim]{currentActivity.EscapeMarkup()}[/]";
                        }

                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }

                    mainTask.Value = mainTask.MaxValue > 0 ? mainTask.MaxValue : 1;
                    mainTask.Description = "[green]Processing complete[/]";
                    mainTask.StopTask();

                    result = await processingTask;
                });

            var elapsed = Stopwatch.GetElapsedTime(startTicks);
            Log.Debug("ℹ️ Processed directory [{Inbound}] in [{ElapsedTime}]", directoryInbound, elapsed);

            AnsiConsole.WriteLine();

            if (result is { IsSuccess: true, Data: not null })
            {
                var resultData = result.Data;
                var statsGrid = new Grid()
                    .AddColumn(new GridColumn().NoWrap().PadRight(4))
                    .AddColumn();

                statsGrid
                    .AddRow("[b]Elapsed Time[/]", $"[cyan]{elapsed:hh\\:mm\\:ss}[/]")
                    .AddRow("[b]Directories Processed[/]", $"[green]{processedDirectories:N0}[/]")
                    .AddRow("[b]Albums Processed[/]", $"[green]{resultData.NumberOfAlbumsProcessed:N0}[/]")
                    .AddRow("[b]Valid Albums[/]", $"[green]{resultData.NumberOfValidAlbumsProcessed:N0}[/] ([cyan]{resultData.FormattedValidPercentageProcessed}[/])")
                    .AddRow("[b]New Artists[/]", $"[yellow]{resultData.NewArtistsCount:N0}[/]")
                    .AddRow("[b]New Albums[/]", $"[yellow]{resultData.NewAlbumsCount:N0}[/]")
                    .AddRow("[b]New Songs[/]", $"[yellow]{resultData.NewSongsCount:N0}[/]");

                AnsiConsole.Write(
                    new Panel(statsGrid)
                        .Header("[green]Process Results[/]")
                        .RoundedBorder()
                        .BorderColor(Color.Green));
            }

            if (settings.Verbose && result != null)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(result) ?? string.Empty))
                        .Header("[yellow]Detailed Results[/]")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Yellow));
            }

            return result?.IsSuccess == true ? 0 : 1;
        }
    }
}
