using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

public class RadioStationLogoCacheService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IHttpClientFactory httpClientFactory,
    IMelodeeConfigurationFactory configurationFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const int MaxLogoSizeBytes = 512 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/svg+xml"
    };

    public async Task<OperationResult<string?>> CacheLogoAsync(int radioStationId, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var station = await context.RadioStations.FirstOrDefaultAsync(s => s.Id == radioStationId, cancellationToken);
        if (station == null)
        {
            return new OperationResult<string?>
            {
                Data = null,
                Type = OperationResponseType.NotFound
            };
        }

        if (string.IsNullOrWhiteSpace(station.LogoUrl))
        {
            return new OperationResult<string?>
            {
                Data = null,
                Type = OperationResponseType.ValidationFailure
            };
        }

        try
        {
            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
            var logosPath = configuration.GetValue<string>(SettingRegistry.AssetsPath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "assets", "radio-logos");

            if (!Directory.Exists(logosPath))
            {
                Directory.CreateDirectory(logosPath);
            }

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Melodee/1.0");

            using var response = await httpClient.GetAsync(station.LogoUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("Failed to download logo for station {StationId}: HTTP {StatusCode}", radioStationId, (int)response.StatusCode);
                return new OperationResult<string?>
                {
                    Data = null,
                    Type = OperationResponseType.Error
                };
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
            {
                Logger.Warning("Invalid content type for logo {StationId}: {ContentType}", radioStationId, contentType);
                return new OperationResult<string?>
                {
                    Data = null,
                    Type = OperationResponseType.ValidationFailure
                };
            }

            using var httpContent = response.Content;
            var bytes = await httpContent.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length > MaxLogoSizeBytes)
            {
                Logger.Warning("Logo too large for station {StationId}: {Size} bytes (max: {Max})", radioStationId, bytes.Length, MaxLogoSizeBytes);
                return new OperationResult<string?>
                {
                    Data = null,
                    Type = OperationResponseType.ValidationFailure
                };
            }

            var extension = GetExtensionFromContentType(contentType);
            var fileName = $"{station.Id}_{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(logosPath, fileName);

            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

            station.LogoCacheKey = fileName;
            await context.SaveChangesAsync(cancellationToken);

            Logger.Information("Cached logo for station {StationId}: {FileName}", radioStationId, fileName);

            return new OperationResult<string?>
            {
                Data = fileName
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error caching logo for station {StationId}", radioStationId);
            return new OperationResult<string?>
            {
                Data = null,
                Type = OperationResponseType.Error
            };
        }
    }

    public async Task<OperationResult<bool>> DeleteCachedLogoAsync(int radioStationId, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var station = await context.RadioStations.FirstOrDefaultAsync(s => s.Id == radioStationId, cancellationToken);
        if (station == null)
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        if (string.IsNullOrWhiteSpace(station.LogoCacheKey))
        {
            return new OperationResult<bool> { Data = true };
        }

        try
        {
            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
            var logosPath = configuration.GetValue<string>(SettingRegistry.AssetsPath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "assets", "radio-logos");
            var filePath = Path.Combine(logosPath, station.LogoCacheKey);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            station.LogoCacheKey = null;
            await context.SaveChangesAsync(cancellationToken);

            Logger.Information("Deleted cached logo for station {StationId}", radioStationId);

            return new OperationResult<bool> { Data = true };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error deleting cached logo for station {StationId}", radioStationId);
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.Error
            };
        }
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => ".bin"
        };
    }
}
