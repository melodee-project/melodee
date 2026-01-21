using System.Security.Cryptography;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Extensions;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using ILogger = Serilog.ILogger;

namespace Melodee.Blazor.Services;

public class StartupMelodeeConfigurationService(
    ILogger logger,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    LibraryService libraryService,
    SettingService settingService,
    IConfiguration configuration)
    : IStartupMelodeeConfigurationService
{
    public async Task UpdateConfigurationFromEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        await using (var scopedContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var storagePath = configuration.GetValue<string>("MELODEE_STORAGE_PATH");
            if (storagePath.Nullify() != null)
            {
                var storageLibrary = await libraryService.GetStorageLibrariesAsync(cancellationToken).ConfigureAwait(false);
                if (storageLibrary.IsSuccess)
                {
                    storageLibrary.Data.First().Path = storagePath!;
                    var result = await libraryService.UpdateAsync(storageLibrary.Data.First(), cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        logger.Information("Storage path updated to {Path}", storagePath);
                    }
                }
            }

            var inboundPath = configuration.GetValue<string>("MELODEE_INBOUND_PATH");
            if (inboundPath.Nullify() != null)
            {
                var inboundLibrary = await libraryService.GetInboundLibraryAsync(cancellationToken).ConfigureAwait(false);
                if (inboundLibrary.IsSuccess)
                {
                    inboundLibrary.Data.Path = inboundPath!;
                    var result = await libraryService.UpdateAsync(inboundLibrary.Data, cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        logger.Information("Inbound path updated to {Path}", inboundPath);
                    }
                }
            }

            var stagingPath = configuration.GetValue<string>("MELODEE_STAGING_PATH");
            if (stagingPath.Nullify() != null)
            {
                var stagingLibrary = await libraryService.GetStagingLibraryAsync(cancellationToken).ConfigureAwait(false);
                if (stagingLibrary.IsSuccess)
                {
                    stagingLibrary.Data.Path = stagingPath!;
                    var result = await libraryService.UpdateAsync(stagingLibrary.Data, cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        logger.Information("Staging path updated to {Path}", stagingPath);
                    }
                }
            }

            var userImagePath = configuration.GetValue<string>("MELODEE_USER_IMAGES_PATH");
            if (userImagePath.Nullify() != null)
            {
                var userImageLibrary = await libraryService.GetUserImagesLibraryAsync(cancellationToken).ConfigureAwait(false);
                if (userImageLibrary.IsSuccess)
                {
                    userImageLibrary.Data.Path = userImagePath!;
                    var result = await libraryService.UpdateAsync(userImageLibrary.Data, cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        logger.Information("User image path updated to {Path}", userImagePath);
                    }
                }
            }

            var playlistPath = configuration.GetValue<string>("MELODEE_PLAYLISTS_PATH");
            if (playlistPath.Nullify() != null)
            {
                var playlistLibrary = await libraryService.GetPlaylistLibraryAsync(cancellationToken).ConfigureAwait(false);
                if (playlistLibrary.IsSuccess)
                {
                    playlistLibrary.Data.Path = playlistPath!;
                    var result = await libraryService.UpdateAsync(playlistLibrary.Data, cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        logger.Information("Playlist path updated to {Path}", playlistPath);
                    }
                }
            }

            var templatesPath = configuration.GetValue<string>("MELODEE_TEMPLATES_PATH");
            if (templatesPath.Nullify() != null)
            {
                var templatesLibrary = await libraryService.GetTemplatesLibraryAsync(cancellationToken).ConfigureAwait(false);
                if (templatesLibrary.IsSuccess)
                {
                    templatesLibrary.Data.Path = templatesPath!;
                    var result = await libraryService.UpdateAsync(templatesLibrary.Data, cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        logger.Information("Templates path updated to {Path}", templatesPath);
                    }
                }
            }

            await scopedContext.SaveChangesAsync(cancellationToken);
        }

        await EnsureSecretKeyConfiguredAsync(cancellationToken);
    }

    private async Task EnsureSecretKeyConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var secretKey = await settingService.GetValueAsync<string>(SettingRegistry.SecuritySecretKey, null, cancellationToken);
        var isValid = !string.IsNullOrWhiteSpace(secretKey.Data) && secretKey.Data.Length >= 32;

        if (!isValid)
        {
            logger.Warning(
                "The setting {SettingKey} is missing or invalid (blank, null, or less than 32 characters). A new secure random value will be generated and saved to the database.",
                SettingRegistry.SecuritySecretKey);

            var newSecretKey = GenerateSecureKey();
            await settingService.SetAsync(SettingRegistry.SecuritySecretKey, newSecretKey, cancellationToken);

            logger.Information("A new secure {SettingKey} has been generated and saved to the database.", SettingRegistry.SecuritySecretKey);
        }
    }

    private static string GenerateSecureKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes);
    }
}
