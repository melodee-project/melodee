using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public sealed class UserPasswordResetService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private readonly IMelodeeConfigurationFactory _configurationFactory = configurationFactory;

    public async Task<MelodeeModels.OperationResult<string?>> GeneratePasswordResetTokenAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        var emailNormalized = email.ToNormalizedString() ?? email.ToLowerInvariant();

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.EmailNormalized == emailNormalized, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<string?>("User not found")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        if (user.IsLocked)
        {
            return new MelodeeModels.OperationResult<string?>("User is locked")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.AccessDenied
            };
        }

        var tokenBytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var expiryMinutes = configuration.GetValue<int?>(SettingRegistry.SecurityPasswordResetTokenExpiryMinutes) ?? 60;

        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiresAt = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(expiryMinutes));
        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(user);

        return new MelodeeModels.OperationResult<string?>
        {
            Data = token
        };
    }

    public async Task<MelodeeModels.OperationResult<User?>> ValidatePasswordResetTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(token, nameof(token));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.PasswordResetToken == token, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<User?>("Invalid token")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        if (user.PasswordResetTokenExpiresAt == null ||
            user.PasswordResetTokenExpiresAt < SystemClock.Instance.GetCurrentInstant())
        {
            return new MelodeeModels.OperationResult<User?>("Token has expired")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        return new MelodeeModels.OperationResult<User?>
        {
            Data = user
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ResetPasswordWithTokenAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(token, nameof(token));
        Guard.Against.NullOrWhiteSpace(newPassword, nameof(newPassword));

        var validationResult = await ValidatePasswordResetTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsSuccess || validationResult.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>(validationResult.Messages ?? ["Invalid or expired token"])
            {
                Data = false,
                Type = validationResult.Type
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstAsync(u => u.Id == validationResult.Data.Id, cancellationToken)
            .ConfigureAwait(false);

        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var encryptionKey = configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey);
        user.PasswordEncrypted = EncryptionHelper.Encrypt(encryptionKey!, newPassword, user.PublicKey);

        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;

        if (user.EmailConfirmedDate == null)
        {
            user.EmailConfirmedDate = SystemClock.Instance.GetCurrentInstant();
        }

        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(user);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    private void ClearUserCache(User user)
    {
        CacheManager.Remove($"urn:user:{user.Id}");
        CacheManager.Remove($"urn:user:apikey:{user.ApiKey}");
        CacheManager.Remove($"urn:user:emailaddress:{user.EmailNormalized}");
        CacheManager.Remove($"urn:user:username:{user.UserNameNormalized}");
    }
}
