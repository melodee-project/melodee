using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
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
    public override async Task<int> ExecuteAsync(CommandContext context, ShowMpegInfoSettings settings)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
            var config = await configFactory.GetConfigurationAsync();

            var imageValidator = new ImageValidator(config);
            var imageConvertor = new ImageConvertor(config);

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

            var tags = await AudioTagManager.ReadAllTagsAsync(fileInfo.FullName, CancellationToken.None);

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
