using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Jobs;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

public class JobRunMusicBrainzUpdateDatabaseJobCommand : CommandBase<JobSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, JobSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var job = new MusicBrainzUpdateDatabaseJob
            (
                scope.ServiceProvider.GetRequiredService<ILogger>(),
                scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>(),
                scope.ServiceProvider.GetRequiredService<SettingService>(),
                scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<MusicBrainzDbContext>>(),
                scope.ServiceProvider.GetRequiredService<IMusicBrainzRepository>()
            );
            await job.Execute(new MelodeeJobExecutionContext(cancellationToken));
            return 1;
        }
    }
}
