using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
///     Maintains the local Artist Search Engine cache by refreshing stale artist album data from external sources.
/// </summary>
/// <remarks>
///     <para>
///         The Artist Search Engine Repository is a local database that caches artist and album information
///         from external services (MusicBrainz, Spotify, etc.). This job ensures the cached data stays current
///         by periodically refreshing records that haven't been updated within the configured refresh interval.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Queries artists from the search engine cache that are older than the refresh threshold</item>
///             <item>For each stale artist, re-fetches their album information from external APIs</item>
///             <item>Updates the local cache with fresh data and sets the LastRefreshed timestamp</item>
///         </list>
///     </para>
///     <para>
///         This job processes artists in batches to avoid rate limiting issues with external APIs.
///         Locked artists (IsLocked=true) are skipped to preserve manually curated data.
///     </para>
///     <para>
///         Configuration settings used:
///         <list type="bullet">
///             <item>SearchEngineArtistSearchDatabaseRefreshInDays: How old records must be before refresh (0 disables)</item>
///             <item>DefaultsBatchSize: Maximum artists to refresh per job run</item>
///         </list>
///     </para>
/// </remarks>
public class ArtistSearchEngineRepositoryHousekeepingJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    ArtistSearchEngineService artistSearchEngineService,
    IDbContextFactory<ArtistSearchEngineServiceDbContext> artistSearchEngineServiceDbContextFactory)
    : JobBase(logger, configurationFactory)
{
    public override async Task Execute(IJobExecutionContext context)
    {
        var startTimeStamp = Stopwatch.GetTimestamp();
        Logger.Information("[{JobName}] Starting job.", nameof(ArtistSearchEngineRepositoryHousekeepingJob));

        var configuration = await ConfigurationFactory.GetConfigurationAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var refreshInDays =
            configuration.GetValue<int?>(SettingRegistry.SearchEngineArtistSearchDatabaseRefreshInDays) ?? 0;
        if (refreshInDays == 0)
        {
            Logger.Warning(
                "[{JobName}] Skipped refreshing Artist Search Engine Repository. No refresh interval configured.",
                nameof(ArtistSearchEngineRepositoryHousekeepingJob));
            return;
        }

        var batchSize = SafeParser.ToNumber<int?>(context.Get(JobMapNameRegistry.BatchSize)) ??
                        configuration.GetValue<int?>(SettingRegistry.DefaultsBatchSize) ?? 50;

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var refreshOlderThan = now.Minus(Duration.FromDays(refreshInDays));
        var refreshOtherThanDateTime = refreshOlderThan.ToDateTimeUtc();

        Logger.Information("[{JobName}] Refreshing Artist Search Engine Repository older than [{RefreshOlderThan}]",
            nameof(ArtistSearchEngineRepositoryHousekeepingJob), refreshOlderThan);

        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                         .CreateDbContextAsync(context.CancellationToken).ConfigureAwait(false))
        {
            // Refresh a batch of albums for Artists, this is to prevent rate limiting issues. 
            var artistsToRefresh = await scopedContext
                .Artists
                .Where(x => (x.IsLocked == null || x.IsLocked == false) &&
                            (x.LastRefreshed == null || x.LastRefreshed <= refreshOtherThanDateTime))
                .OrderByDescending(x => x.LastRefreshed)
                .Take(batchSize)
                .ToListAsync(context.CancellationToken)
                .ConfigureAwait(false);

            if (artistsToRefresh.Count > 0)
            {
                Logger.Information("[{JobName}] Refreshing [{Count}] artists.",
                    nameof(ArtistSearchEngineRepositoryHousekeepingJob), artistsToRefresh.Count);
                await artistSearchEngineService.InitializeAsync(configuration, context.CancellationToken)
                    .ConfigureAwait(false);
                await artistSearchEngineService
                    .RefreshArtistAlbums(artistsToRefresh.ToArray(), context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                Logger.Information("[{JobName}] No artists need refreshing.",
                    nameof(ArtistSearchEngineRepositoryHousekeepingJob));
            }

            Logger.Information("[{JobName}] Completed job in [{Elapsed}].",
                nameof(ArtistSearchEngineRepositoryHousekeepingJob), Stopwatch.GetElapsedTime(startTimeStamp));
        }
    }
}
