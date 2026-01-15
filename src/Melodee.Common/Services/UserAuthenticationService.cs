using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

/// <summary>
/// Service for user authentication operations.
/// </summary>
public sealed class UserAuthenticationService(
    ILogger logger,
    IPasswordHashService passwordHashService,
    IOpenSubsonicSecretProtector openSubsonicSecretProtector,
    IBus bus,
    UserProfileService userProfileService,
    IMelodeeConfigurationFactory configurationFactory)
{
    private readonly ILogger _logger = logger;
    private readonly IPasswordHashService _passwordHashService = passwordHashService;
    private readonly IOpenSubsonicSecretProtector _openSubsonicSecretProtector = openSubsonicSecretProtector;
    private readonly IBus _bus = bus;
    private readonly UserProfileService _userProfileService = userProfileService;
    private readonly IMelodeeConfigurationFactory _configurationFactory = configurationFactory;

    /// <summary>
    /// Logs a user in using their username and password.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> LoginUserByUsernameAsync(
        string userName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return new MelodeeModels.OperationResult<User?>
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        var passwordValue = password!;
        var userResult = await _userProfileService.GetByUsernameAsync(userName, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return new MelodeeModels.OperationResult<User?>
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        return await CompleteLoginAsync(userResult.Data, passwordValue, userName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a user in using their email address and password.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> LoginUserAsync(
        string emailAddress,
        string? password,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(emailAddress, nameof(emailAddress));

        if (string.IsNullOrWhiteSpace(password))
        {
            return new MelodeeModels.OperationResult<User?>
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        var passwordValue = password!;
        var userResult = await _userProfileService.GetByEmailAddressAsync(emailAddress, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return new MelodeeModels.OperationResult<User?>("User not found")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return await CompleteLoginAsync(userResult.Data, passwordValue, emailAddress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates credentials and applies login side effects for an identified user.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> CompleteLoginAsync(
        User user,
        string password,
        string identifier,
        CancellationToken cancellationToken)
    {
        var authenticated = false;
        var shouldMigrate = false;

        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            authenticated = _passwordHashService.Verify(password, user.PasswordHash);
        }
        else
        {
            var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken);
            if (password.StartsWith("enc:", StringComparison.Ordinal))
            {
                authenticated = password[4..] == user.PasswordEncrypted;
            }
            else
            {
                authenticated = user.PasswordEncrypted == EncryptionHelper.Encrypt(
                    configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                    password,
                    user.PublicKey);
            }

            if (authenticated)
            {
                shouldMigrate = true;
            }
        }

        if (!authenticated)
        {
            Log.Warning("[{ServiceName}] LoginUserAsync [{Identifier}] failed", nameof(UserAuthenticationService), identifier);
            return new MelodeeModels.OperationResult<User?>
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        await _bus.SendLocal(new UserLoginEvent(user.Id, user.UserName)).ConfigureAwait(false);

        if (shouldMigrate)
        {
            await using var scopedContext = await _userProfileService.GetContextFactory().CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbUser = await scopedContext.Users.FirstAsync(x => x.Id == user.Id, cancellationToken).ConfigureAwait(false);
            dbUser.PasswordHash = _passwordHashService.Hash(password);
            dbUser.PasswordHashAlgorithm = "bcrypt";
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            user.PasswordHash = dbUser.PasswordHash;
            user.PasswordHashAlgorithm = dbUser.PasswordHashAlgorithm;
            Log.Information("[{ServiceName}] Migrated user [{EmailAddress}] to BCrypt password hashing", nameof(UserAuthenticationService),
                user.Email ?? identifier);
        }

        user.LastActivityAt = now;
        user.LastLoginAt = now;

        return new MelodeeModels.OperationResult<User?>
        {
            Data = user
        };
    }

    /// <summary>
    /// Validates a user token for OpenSubsonic API authentication.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<User?>> ValidateTokenAsync(
        string username,
        string token,
        string salt,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(username, nameof(username));
        Guard.Against.NullOrWhiteSpace(token, nameof(token));
        Guard.Against.NullOrWhiteSpace(salt, nameof(salt));

        var userResult = await _userProfileService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (!userResult.IsSuccess || userResult.Data == null)
        {
            return new MelodeeModels.OperationResult<User?>("User not found")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var user = userResult.Data;
        if (user.IsLocked)
        {
            return new MelodeeModels.OperationResult<User?>("User is locked")
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        var configuration = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var usersPassword = string.Empty;
        var shouldMigrateToSecret = false;

        if (!string.IsNullOrEmpty(user.OpenSubsonicSecretProtected))
        {
            usersPassword = _openSubsonicSecretProtector.Unprotect(user.OpenSubsonicSecretProtected);
        }
        else
        {
            usersPassword = EncryptionHelper.Decrypt(
                configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                user.PasswordEncrypted,
                user.PublicKey);
            shouldMigrateToSecret = true;
        }

        // NOTE: MD5 is required here by the OpenSubsonic API specification for token-based authentication.
        // The token is computed as MD5(password + salt) per the OpenSubsonic/Subsonic protocol.
        // This cannot be changed without breaking API compatibility with all Subsonic clients.
        // See: http://www.subsonic.org/pages/api.jsp#authentication
        // lgtm[cs/weak-crypto] MD5 mandated by OpenSubsonic API specification - cannot change
        var expectedToken = HashHelper.CreateMd5($"{usersPassword}{salt}");
        var isAuthenticated = string.Equals(expectedToken, token, StringComparison.OrdinalIgnoreCase);

        if (!isAuthenticated)
        {
            Log.Warning("[{ServiceName}] ValidateTokenAsync [{Username}] failed token validation", nameof(UserAuthenticationService), username);
            return new MelodeeModels.OperationResult<User?>
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.Unauthorized
            };
        }

        if (shouldMigrateToSecret)
        {
            await using var scopedContext = await _userProfileService.GetContextFactory().CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var dbUser = await scopedContext.Users.FirstAsync(x => x.Id == user.Id, cancellationToken).ConfigureAwait(false);
            var newSecret = GenerateOpenSubsonicSecret();
            dbUser.OpenSubsonicSecretProtected = _openSubsonicSecretProtector.Protect(newSecret);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            user.OpenSubsonicSecretProtected = dbUser.OpenSubsonicSecretProtected;
            Log.Information("[{ServiceName}] Migrated user [{Username}] to OpenSubsonic secret protection", nameof(UserAuthenticationService), username);
        }

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await _bus.SendLocal(new UserLoginEvent(user.Id, user.UserName)).ConfigureAwait(false);

        user.LastActivityAt = now;
        user.LastLoginAt = now;
        return userResult;
    }

    /// <summary>
    /// Generate a salt for password hashing.
    /// </summary>
    public string GenerateSalt(int saltLength = 16, int logRounds = 10)
        => GenerateSaltStatic(saltLength, logRounds);

    /// <summary>
    /// Generate a salt for password hashing (static version).
    /// </summary>
    public static string GenerateSaltStatic(int saltLength = 16, int logRounds = 10)
    {
        var randomBytes = new byte[saltLength];
        RandomNumberGenerator.Create().GetBytes(randomBytes);

        var rs = new StringBuilder(randomBytes.Length * 2 + 8);

        rs.Append("$2a$");
        if (logRounds < 10)
        {
            rs.Append('0');
        }

        rs.Append(logRounds);
        rs.Append('$');
        rs.Append(Encoding.UTF8.GetString(randomBytes).ToBase64());

        return rs.ToString();
    }

    /// <summary>
    /// Generate a secure OpenSubsonic secret.
    /// </summary>
    public string GenerateOpenSubsonicSecret()
        => GenerateOpenSubsonicSecretStatic();

    /// <summary>
    /// Generate a secure OpenSubsonic secret (static version).
    /// </summary>
    public static string GenerateOpenSubsonicSecretStatic()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
