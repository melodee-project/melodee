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
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public sealed class UserSocialLoginService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    UserProfileService userProfileService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:user:apikey:{0}";
    private const string CacheKeyDetailByEmailAddressKeyTemplate = "urn:user:emailaddress:{0}";
    private const string CacheKeyDetailByUsernameTemplate = "urn:user:username:{0}";
    private const string CacheKeyDetailTemplate = "urn:user:{0}";

    private readonly IMelodeeConfigurationFactory _configurationFactory = configurationFactory;
    private readonly UserProfileService _userProfileService = userProfileService;

    public async Task<MelodeeModels.OperationResult<User?>> GetUserBySocialLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(subject, nameof(subject));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogin = await scopedContext.UserSocialLogins
            .Include(sl => sl.User)
            .ThenInclude(u => u.Pins)
            .AsNoTracking()
            .FirstOrDefaultAsync(sl => sl.Provider == provider && sl.Subject == subject, cancellationToken)
            .ConfigureAwait(false);

        if (socialLogin == null)
        {
            return new MelodeeModels.OperationResult<User?>("Social login not found")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<User?>
        {
            Data = socialLogin.User
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> LinkSocialLoginAsync(
        int userId,
        string provider,
        string subject,
        string? email,
        string? displayName,
        string? hostedDomain,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(subject, nameof(subject));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existingLink = await scopedContext.UserSocialLogins
            .FirstOrDefaultAsync(sl => sl.Provider == provider && sl.Subject == subject, cancellationToken)
            .ConfigureAwait(false);

        if (existingLink != null)
        {
            if (existingLink.UserId == userId)
            {
                existingLink.LastLoginAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new MelodeeModels.OperationResult<bool> { Data = true };
            }

            return new MelodeeModels.OperationResult<bool>("This social account is already linked to another user")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var socialLogin = new UserSocialLogin
        {
            UserId = userId,
            Provider = provider,
            Subject = subject,
            Email = email,
            DisplayName = displayName,
            HostedDomain = hostedDomain,
            LastLoginAt = now,
            CreatedAt = now
        };

        scopedContext.UserSocialLogins.Add(socialLogin);
        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    public async Task<MelodeeModels.OperationResult<bool>> UnlinkSocialLoginAsync(
        int userId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogin = await scopedContext.UserSocialLogins
            .FirstOrDefaultAsync(sl => sl.UserId == userId && sl.Provider == provider, cancellationToken)
            .ConfigureAwait(false);

        if (socialLogin == null)
        {
            return new MelodeeModels.OperationResult<bool>("Social login not found")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        scopedContext.UserSocialLogins.Remove(socialLogin);
        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    public async Task<MelodeeModels.OperationResult<UserSocialLogin[]>> GetUserSocialLoginsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogins = await scopedContext.UserSocialLogins
            .Where(sl => sl.UserId == userId)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<UserSocialLogin[]> { Data = socialLogins };
    }

    public async Task<MelodeeModels.OperationResult<MelodeeModels.LinkedProviderInfo[]>> GetLinkedProvidersAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var socialLogins = await scopedContext.UserSocialLogins
            .Where(sl => sl.UserId == userId)
            .Select(sl => new MelodeeModels.LinkedProviderInfo
            {
                Provider = sl.Provider,
                Email = sl.Email,
                LinkedAt = sl.CreatedAt.ToDateTimeUtc()
            })
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<MelodeeModels.LinkedProviderInfo[]> { Data = socialLogins };
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateSocialLoginLastLoginAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(subject, nameof(subject));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var updated = await scopedContext.UserSocialLogins
            .Where(sl => sl.Provider == provider && sl.Subject == subject)
            .ExecuteUpdateAsync(s => s.SetProperty(sl => sl.LastLoginAt, now), cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<bool> { Data = updated > 0 };
    }

    public async Task<MelodeeModels.OperationResult<User?>> CreateUserFromGoogleAsync(
        string googleSubject,
        string email,
        string displayName,
        string? hostedDomain,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(googleSubject, nameof(googleSubject));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));
        Guard.Against.NullOrWhiteSpace(displayName, nameof(displayName));

        var existingUser = await _userProfileService.GetByEmailAddressAsync(email, cancellationToken).ConfigureAwait(false);
        if (existingUser.IsSuccess && existingUser.Data != null)
        {
            return new MelodeeModels.OperationResult<User?>("User with this email already exists. Please log in with password and link your Google account.")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        var baseUsername = email.Split('@')[0].Replace(".", "_").Replace("+", "_");
        var username = baseUsername;

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var usernameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant();
        var counter = 1;
        while (await scopedContext.Users.AnyAsync(u => u.UserNameNormalized == usernameNormalized, cancellationToken).ConfigureAwait(false))
        {
            username = $"{baseUsername}{counter}";
            usernameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant();
            counter++;
        }

        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var usersPublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        var randomPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var newUser = new User
        {
            UserName = username,
            UserNameNormalized = usernameNormalized,
            Email = email,
            EmailNormalized = email.ToNormalizedString() ?? email.ToUpperInvariant(),
            PublicKey = usersPublicKey,
            PasswordEncrypted = EncryptionHelper.Encrypt(
                configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                randomPassword,
                usersPublicKey),
            CreatedAt = now,
            LastActivityAt = now,
            LastLoginAt = now
        };

        scopedContext.Users.Add(newUser);

        if (await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) < 1)
        {
            return new MelodeeModels.OperationResult<User?>("Failed to create user")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Error
            };
        }

        var dbUserCount = await scopedContext.Users.CountAsync(cancellationToken).ConfigureAwait(false);
        if (dbUserCount == 1)
        {
            await scopedContext.Users
                .Where(x => x.Id == newUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsAdmin, true), cancellationToken)
                .ConfigureAwait(false);
            newUser.IsAdmin = true;
        }

        var socialLogin = new UserSocialLogin
        {
            UserId = newUser.Id,
            Provider = "Google",
            Subject = googleSubject,
            Email = email,
            DisplayName = displayName,
            HostedDomain = hostedDomain,
            LastLoginAt = now,
            CreatedAt = now
        };

        scopedContext.UserSocialLogins.Add(socialLogin);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearUserCache(newUser);

        return new MelodeeModels.OperationResult<User?> { Data = newUser };
    }

    private void ClearUserCache(User user)
    {
        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(user.Id));
        CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(user.ApiKey));
        CacheManager.Remove(CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(user.EmailNormalized));
        CacheManager.Remove(CacheKeyDetailByUsernameTemplate.FormatSmart(user.UserNameNormalized));
    }
}
