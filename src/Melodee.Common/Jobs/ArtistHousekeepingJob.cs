using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Services;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;
using Serilog;

namespace Melodee.Common.Jobs;

/// <summary>
///     Performs housekeeping tasks for artists that are missing images.
/// </summary>
/// <remarks>
///     <para>
///         This job runs periodically to find artists in the database that have no images assigned.
///         For each artist found, it searches external image services (configured via ArtistImageSearchEngineService)
///         and downloads the first valid image found.
///     </para>
///     <para>
///         Processing flow:
///         <list type="number">
///             <item>Queries artists with ImageCount of 0 or null that are not locked and have ReadyToProcess status</item>
///             <item>For each artist, searches for images using the artist name and their albums as search context</item>
///             <item>Downloads the first valid image to the artist's file system directory</item>
///             <item>Validates the downloaded image using ImageValidator rules (dimensions, format, etc.)</item>
///             <item>Updates the artist record with new image count and UpdatedImages status</item>
///             <item>Clears the artist cache to ensure the new image is served</item>
///         </list>
///     </para>
///     <para>
///         Configuration settings used:
///         <list type="bullet">
///             <item>DefaultsBatchSize: Maximum number of artists to process per job run</item>
///             <item>ImagingMaximumNumberOfArtistImages: Maximum images allowed per artist</item>
///         </list>
///     </para>
/// </remarks>
public class ArtistHousekeepingJob(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    ArtistService artistService,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ArtistImageSearchEngineService imageSearchEngine,
    IHttpClientFactory httpClientFactory,
    IImageProcessor imageProcessor) : JobBase(logger, configurationFactory)
{
    public override async Task Execute(IJobExecutionContext context)
    {
        var configuration = await ConfigurationFactory.GetConfigurationAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var batchSize = configuration.GetValue<int>(SettingRegistry.DefaultsBatchSize);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var httpClient = httpClientFactory.CreateClient();
        var imageValidator = new ImageValidator(imageProcessor, configuration);
        await using (var scopedContext = await contextFactory.CreateDbContextAsync(context.CancellationToken))
        {
            var readyToProcessStatus = SafeParser.ToNumber<int>(MetaDataModelStatus.ReadyToProcess);
            var artists = await scopedContext.Artists
                .Include(x => x.Library)
                .Include(x => x.Albums)
                .Where(x => !x.Library.IsLocked && !x.IsLocked && (x.ImageCount == null || x.ImageCount == 0) &&
                            x.MetaDataStatus == readyToProcessStatus)
                .OrderBy(x => x.Id)
                .Take(batchSize)
                .ToArrayAsync(context.CancellationToken)
                .ConfigureAwait(false);

            if (artists.Length == 0)
            {
                return;
            }

            var maxNumberOfImagesAllowed =
                configuration.GetValue<short>(SettingRegistry.ImagingMaximumNumberOfArtistImages);
            if (maxNumberOfImagesAllowed == 0)
            {
                maxNumberOfImagesAllowed = short.MaxValue;
            }

            Logger.Debug("[{JobName}] found [{Count}] artists without images.", nameof(ArtistHousekeepingJob),
                artists.Length);

            var updatedArtists = new List<Artist>();

            foreach (var artist in artists)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var artistFileDirectory = artist.ToFileSystemDirectoryInfo();
                var albumImageSearchResult = await imageSearchEngine.DoSearchAsync(
                        artist.ToArtistQuery(artist.Albums.Select(x => x.ToKeyValue()).ToArray()),
                        1,
                        context.CancellationToken)
                    .ConfigureAwait(false);
                if (!albumImageSearchResult.IsSuccess || albumImageSearchResult.Data.Length == 0)
                {
                    continue;
                }

                var imageFileName = artistFileDirectory.GetNextFileNameForType(Artist.ImageType).Item1;
                if (await httpClient.DownloadFileAsync(
                        albumImageSearchResult.Data.First().MediaUrl,
                        imageFileName,
                        async (_, newFileInfo, _) =>
                            (await imageValidator
                                .ValidateImage(newFileInfo, PictureIdentifier.Artist, context.CancellationToken)
                                .ConfigureAwait(false)).Data.IsValid,
                        context.CancellationToken).ConfigureAwait(false))
                {
                    artist.LastUpdatedAt = now;
                    artist.ImageCount = 1;
                    artist.MetaDataStatus = SafeParser.ToNumber<int>(MetaDataModelStatus.UpdatedImages);
                    await artistService.ClearCacheAsync(artist, context.CancellationToken);
                    updatedArtists.Add(artist);
                    Logger.Information("[{JobName}] Updated artist image for artist [{ArtistName}]",
                        nameof(ArtistHousekeepingJob), artist.Name);
                }
            }

            if (updatedArtists.Count > 0)
            {
                await scopedContext.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
            }
        }
    }
}
