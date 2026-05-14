using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Imaging;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

public class ShowMpegInfoCommand : CommandBase<ShowMpegInfoSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ShowMpegInfoSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var imageProcessor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
            var config = await configFactory.GetConfigurationAsync(cancellationToken);

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

            Trace.WriteLine($"\ud83d\udcdc Processing File [{settings.Filename}]");

            var tags = await AudioTagManager.ReadAllTagsAsync(fileInfo.FullName, cancellationToken);

            AnsiConsole.Write(
                new Panel(new JsonText(serializer.Serialize(tags) ?? string.Empty))
                    .Header("MPEG Info")
                    .Collapse()
                    .RoundedBorder()
                    .BorderColor(tags.IsValid() ? Color.Green : Color.Red));

            return tags.IsValid() ? 0 : 1;
        }
    }
}
