using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Conversion.Image;
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

public class ShowTagsCommand : CommandBase<ShowTagsSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ShowTagsSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var imageProcessor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
            var config = await configFactory.GetConfigurationAsync();

            var imageValidator = new ImageValidator(imageProcessor, config);
            var imageConvertor = new ImageConvertor(imageProcessor, config);

            var fileInfo = new FileInfo(settings.Filename);
            if (!fileInfo.Exists)
            {
                throw new Exception($"Media file [{settings.Filename}] does not exist.");
            }

            if (fileInfo.Directory == null)
            {
                throw new Exception($"Media file directory [{settings.Filename}] does not exist.");
            }

            Log.Debug("\ud83d\udcdc Processing File [{MediaFilename}]", settings.Filename);

            var isValid = false;

            var metaTag = new AtlMetaTag(new MetaTagsProcessor(config, serializer), imageProcessor, imageConvertor, imageValidator, config);
            var tagResult = await metaTag.ProcessFileAsync(fileInfo.Directory!.ToDirectorySystemInfo(), FileSystemInfoExtensions.ToFileSystemInfo(fileInfo));

            if (settings.OnlyTags.Nullify() != null)
            {
                var onlyTags = new Dictionary<string, object?>();
                foreach (var tag in settings.OnlyTags!.Split(','))
                {
                    var t = tag.ToNormalizedString() ?? string.Empty;
                    var value = tagResult.Data?.Tags?.FirstOrDefault(x => x.IdentifierDescription.ToNormalizedString()?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)?.Value;
                    onlyTags.TryAdd(t, value);
                }

                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(onlyTags) ?? string.Empty))
                        .Header("Only Tags")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Green));
            }
            else if (settings.Verbose)
            {
                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(tagResult) ?? string.Empty))
                        .Header("File Info")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Green));
            }

            return isValid ? 0 : 1;
        }
    }
}
