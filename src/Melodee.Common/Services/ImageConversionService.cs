using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Imaging;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class ImageConversionService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    IImageProcessor imageProcessor
)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<MelodeeModels.OperationResult<bool>> ConvertImageAsync(FileInfo imageFileInfo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
            if (configuration == null)
            {
                return new MelodeeModels.OperationResult<bool>(MelodeeModels.OperationResponseType.Error, "Configuration is not available")
                {
                    Data = false
                };
            }

            var imageConvertor = new ImageConvertor(imageProcessor, configuration);
            var convertResult = await imageConvertor.ProcessFileAsync(imageFileInfo.ToDirectorySystemInfo(),
                imageFileInfo.ToFileSystemInfo(), cancellationToken);
            return new MelodeeModels.OperationResult<bool>
            {
                Data = convertResult.IsSuccess
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error converting image {ImageFile}", imageFileInfo.FullName);
            return new MelodeeModels.OperationResult<bool>(MelodeeModels.OperationResponseType.Error, ex.Message)
            {
                Data = false
            };
        }
    }
}
