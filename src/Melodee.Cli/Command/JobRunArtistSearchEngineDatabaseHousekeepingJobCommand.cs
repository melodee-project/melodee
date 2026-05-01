using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Jobs;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Services.SearchEngines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

public class JobRunArtistSearchEngineDatabaseHousekeepingJobCommand : CommandBase<JobSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, JobSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var job = new ArtistSearchEngineRepositoryHousekeepingJob
            (
                scope.ServiceProvider.GetRequiredService<ILogger>(),
                scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>(),
                scope.ServiceProvider.GetRequiredService<ArtistSearchEngineService>(),
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<ArtistSearchEngineServiceDbContext>>()
            );
            var jc = new MelodeeJobExecutionContext(cancellationToken);
            if (settings.BatchSize != null)
            {
                jc.Put(JobMapNameRegistry.BatchSize, settings.BatchSize);
            }

            await job.Execute(jc);
            return 1;
        }
    }
}
