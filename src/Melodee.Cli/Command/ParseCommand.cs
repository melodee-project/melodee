using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Directory;
using Melodee.Common.Plugins.MetaData.Directory.Nfo;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

public class ParseCommand : CommandBase<ParseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ParseSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
            var config = await configFactory.GetConfigurationAsync();
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();

            var imageValidator = new ImageValidator(config);
            var imageConvertor = new ImageConvertor(config);
            var albumValidator = new AlbumValidator(config);

            var fileInfo = new FileInfo(settings.Filename);
            if (!fileInfo.Exists)
            {
                throw new Exception($"Parse File [{settings.Filename}] does not exist.");
            }

            if (fileInfo.Directory == null)
            {
                throw new Exception($"Parse Directory [{settings.Filename}] does not exist.");
            }

            var startTicks = Stopwatch.GetTimestamp();
            Log.Debug("\ud83d\udcdc Parsing File [{NfoFilename}]", settings.Filename);

            var isValid = false;

            var cue = new CueSheet(
                serializer,
                [
                    new AtlMetaTag(new MetaTagsProcessor(config, serializer), imageConvertor, imageValidator, config)
                ], albumValidator, config);
            if (cue.DoesHandleFile(fileInfo.Directory.ToDirectorySystemInfo(), fileInfo.ToFileSystemInfo()))
            {
                try
                {
                    var parseCueResult = await CueSheet.ParseFileAsync(fileInfo.FullName, config.Configuration);
                    if (!parseCueResult?.IsValid ?? false)
                    {
                        if (settings.Verbose)
                        {
                            AnsiConsole.Write(
                                new Panel(new JsonText(serializer.Serialize(parseCueResult)!))
                                    .Header("Parse Result")
                                    .Collapse()
                                    .RoundedBorder()
                                    .BorderColor(Color.Yellow));
                        }

                        isValid = false;
                    }
                    else
                    {
                        var cueResult = await cue.ProcessDirectoryAsync(fileInfo.Directory.ToDirectorySystemInfo());

                        Log.Debug("ℹ️  Processed CUE File [{NfoFilename}] in [{ElapsedTime}]", settings.Filename, Stopwatch.GetElapsedTime(startTicks));

                        if (settings.Verbose)
                        {
                            AnsiConsole.Write(
                                new Panel(new JsonText(serializer.Serialize(cueResult)!))
                                    .Header("Parse Result")
                                    .Collapse()
                                    .RoundedBorder()
                                    .BorderColor(Color.Yellow));
                        }

                        isValid = cueResult.IsSuccess;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e);
                }
            }


            var sfv = new SimpleFileVerification(serializer,
            [
                new AtlMetaTag(new MetaTagsProcessor(config, serializer), imageConvertor, imageValidator, config)
            ], new AlbumValidator(config), config);
            if (sfv.DoesHandleFile(fileInfo.Directory.ToDirectorySystemInfo(), fileInfo.ToFileSystemInfo()))
            {
                try
                {
                    var svfResult = await sfv.ProcessDirectoryAsync(fileInfo.Directory.ToDirectorySystemInfo());

                    Log.Debug("ℹ️  Processed SFV File [{NfoFilename}] in [{ElapsedTime}]", settings.Filename, Stopwatch.GetElapsedTime(startTicks));

                    if (settings.Verbose)
                    {
                        AnsiConsole.Write(
                            new Panel(new JsonText(serializer.Serialize(svfResult)!))
                                .Header("Parse Result")
                                .Collapse()
                                .RoundedBorder()
                                .BorderColor(Color.Yellow));
                    }

                    isValid = svfResult.IsSuccess;
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e);
                }
            }

            var m3u = new M3UPlaylist(serializer,
            [
                new AtlMetaTag(new MetaTagsProcessor(config, serializer), imageConvertor, imageValidator, config)
            ], albumValidator, config);
            if (m3u.DoesHandleFile(fileInfo.Directory.ToDirectorySystemInfo(), fileInfo.ToFileSystemInfo()))
            {
                try
                {
                    var svfResult = await m3u.ProcessDirectoryAsync(fileInfo.Directory.ToDirectorySystemInfo());

                    Log.Debug("ℹ️ Processed M3U File [{NfoFilename}] in [{ElapsedTime}]", settings.Filename, Stopwatch.GetElapsedTime(startTicks));

                    if (settings.Verbose)
                    {
                        AnsiConsole.Write(
                            new Panel(new JsonText(serializer.Serialize(svfResult)!))
                                .Header("Parse Result")
                                .Collapse()
                                .RoundedBorder()
                                .BorderColor(Color.Yellow));
                    }

                    isValid = svfResult.IsSuccess;
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e);
                }
            }

            var nfo = new Nfo(serializer, albumValidator, config);
            if (nfo.DoesHandleFile(fileInfo.Directory.ToDirectorySystemInfo(), fileInfo.ToFileSystemInfo()))
            {
                try
                {
                    var nfoParserResult = await nfo.AlbumForNfoFileAsync(fileInfo, fileInfo.Directory.ToDirectorySystemInfo());

                    Log.Debug("ℹ️ Processed Nfo File [{NfoFilename}] in [{ElapsedTime}]", settings.Filename, Stopwatch.GetElapsedTime(startTicks));

                    if (settings.Verbose)
                    {
                        AnsiConsole.Write(
                            new Panel(new JsonText(serializer.Serialize(nfoParserResult)!))
                                .Header("Parse Result")
                                .Collapse()
                                .RoundedBorder()
                                .BorderColor(Color.Yellow));
                    }

                    isValid = nfoParserResult?.IsValid ?? false;
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e);
                }
            }

            return isValid ? 0 : 1;
        }
    }
}
