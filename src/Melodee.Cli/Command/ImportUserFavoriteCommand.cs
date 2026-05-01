using Melodee.Cli.CommandSettings;
using Melodee.Common.Models.Importing;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

public class ImportUserFavoriteCommand : CommandBase<ImportUserFavorite>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ImportUserFavorite settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var result = await userService.ImportUserFavoriteSongs(new UserFavoriteSongConfiguration(
                    settings.CsvFileName,
                    Guid.Parse(settings.UserApiKey),
                    settings.Artist,
                    settings.Album,
                    settings.Song,
                    settings.IsPretend), cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess ? 1 : 0;
        }
    }
}
