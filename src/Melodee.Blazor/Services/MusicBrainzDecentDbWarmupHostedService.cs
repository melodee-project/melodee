using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

namespace Melodee.Blazor.Services;

internal sealed class MusicBrainzDecentDbWarmupHostedService(
    IServiceScopeFactory scopeFactory,
    Serilog.ILogger logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            using var scope = scopeFactory.CreateScope();
            var warmupService = scope.ServiceProvider.GetRequiredService<MusicBrainzDecentDbWarmupService>();
            var result = await warmupService.WarmHotIndexesAsync(stoppingToken).ConfigureAwait(false);
            if (result.Succeeded || result.Skipped)
            {
                return;
            }

            logger.Warning(
                "MusicBrainz DecentDB startup warm-up did not complete: {Message}",
                result.Message);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            logger.Warning(
                ex,
                "MusicBrainz DecentDB startup warm-up failed. Search remains available; indexes will warm on demand.");
        }
    }
}
