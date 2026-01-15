using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Rebus.Bus;

namespace Melodee.Tests.Common.Services;

/// <summary>
/// Tests for UserAuthenticationService.
/// </summary>
public class UserAuthenticationServiceTests : ServiceTestBase
{
    private UserAuthenticationService CreateUserAuthenticationService(
        UserProfileService? userProfileService = null,
        IMelodeeConfigurationFactory? configFactory = null,
        IBus? bus = null,
        IPasswordHashService? passwordHashService = null,
        IOpenSubsonicSecretProtector? openSubsonicSecretProtector = null)
    {
        return new UserAuthenticationService(
            Logger,
            passwordHashService ?? new Mock<IPasswordHashService>().Object,
            openSubsonicSecretProtector ?? new Mock<IOpenSubsonicSecretProtector>().Object,
            bus ?? MockBus(),
            userProfileService ?? GetUserProfileService(),
            configFactory ?? MockConfigurationFactory());
    }

    [Fact]
    public async Task LoginUserByUsernameAsync_WithValidCredentials_ReturnsUser()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword";
        var email = "test@example.com";

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserWithPassword(1, username, email, password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userProfileService = GetUserProfileService();
        var authService = CreateUserAuthenticationService(userProfileService: userProfileService);

        // Act
        var result = await authService.LoginUserByUsernameAsync(username, password);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(username, result.Data.UserName);
        Assert.Equal(email, result.Data.Email);
    }

    [Fact]
    public async Task LoginUserByUsernameAsync_WithInvalidPassword_ReturnsUnauthorized()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword";
        var wrongPassword = "wrongpassword";
        var email = "test@example.com";

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserWithPassword(1, username, email, password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userProfileService = GetUserProfileService();
        var authService = CreateUserAuthenticationService(userProfileService: userProfileService);

        // Act
        var result = await authService.LoginUserByUsernameAsync(username, wrongPassword);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
    }

    [Fact]
    public async Task LoginUserByUsernameAsync_WithNullPassword_ReturnsUnauthorized()
    {
        // Arrange
        var username = "testuser";
        var authService = CreateUserAuthenticationService();

        // Act
        var result = await authService.LoginUserByUsernameAsync(username, null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
    }

    [Fact]
    public async Task LoginUserAsync_WithValidCredentials_ReturnsUser()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword";
        var email = "test@example.com";

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserWithPassword(1, username, email, password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userProfileService = GetUserProfileService();
        var authService = CreateUserAuthenticationService(userProfileService: userProfileService);

        // Act
        var result = await authService.LoginUserAsync(email, password);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(username, result.Data.UserName);
        Assert.Equal(email, result.Data.Email);
    }

    [Fact]
    public async Task LoginUserAsync_WithInvalidEmail_ReturnsNotFound()
    {
        // Arrange
        var email = "nonexistent@example.com";
        var password = "testpassword";
        var authService = CreateUserAuthenticationService();

        // Act
        var result = await authService.LoginUserAsync(email, password);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
    }

    [Fact]
    public async Task GenerateSalt_WithDefaultParameters_ReturnsValidSalt()
    {
        // Arrange
        var authService = CreateUserAuthenticationService();

        // Act
        var salt = UserAuthenticationService.GenerateSaltStatic();

        // Assert
        Assert.NotNull(salt);
        Assert.NotEmpty(salt);
        Assert.StartsWith("$2a$", salt);
        Assert.Contains("$", salt);
    }

    [Fact]
    public async Task GenerateSalt_WithCustomParameters_ReturnsValidSalt()
    {
        // Arrange
        var authService = CreateUserAuthenticationService();
        var saltLength = 32;
        var logRounds = 12;

        // Act
        var salt = UserAuthenticationService.GenerateSaltStatic(saltLength, logRounds);

        // Assert
        Assert.NotNull(salt);
        Assert.NotEmpty(salt);
        Assert.StartsWith("$2a$", salt);
        Assert.Contains("$12$", salt);
    }

    [Fact]
    public async Task GenerateOpenSubsonicSecret_ReturnsValidSecret()
    {
        // Arrange & Act
        var secret = UserAuthenticationService.GenerateOpenSubsonicSecretStatic();

        // Assert
        Assert.NotNull(secret);
        Assert.NotEmpty(secret);
        Assert.DoesNotContain("+", secret);
        Assert.DoesNotContain("/", secret);
        Assert.DoesNotContain("=", secret);
        Assert.InRange(secret.Length, 40, 50); // Base64URL encoded 32 bytes
    }

    private User CreateTestUserWithPassword(int id, string username, string email, string password)
    {
        var publicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var config = TestsBase.NewPluginsConfiguration();
        var encryptedPassword = EncryptionHelper.Encrypt(
            config.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
            password,
            publicKey);

        return new User
        {
            Id = id,
            UserName = username,
            UserNameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant(),
            Email = email,
            EmailNormalized = email.ToNormalizedString() ?? email.ToUpperInvariant(),
            PublicKey = publicKey,
            PasswordEncrypted = encryptedPassword,
            IsAdmin = false,
            IsLocked = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ApiKey = Guid.NewGuid()
        };
    }

    [Fact]
    public async Task CompleteLoginAsync_WithValidUserAndPassword_ReturnsUserWithUpdatedTimestamps()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword";
        var email = "test@example.com";

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserWithPassword(1, username, email, password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userProfileService = GetUserProfileService();
        var userResult = await userProfileService.GetByUsernameAsync(username);
        var retrievedUser = userResult.Data;

        var authService = CreateUserAuthenticationService(userProfileService: userProfileService);

        // Act
        var result = await authService.CompleteLoginAsync(retrievedUser!, password, username, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(username, result.Data.UserName);
        Assert.NotEqual(default, result.Data.LastActivityAt);
        Assert.NotEqual(default, result.Data.LastLoginAt);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ReturnsUser()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword";
        var email = "test@example.com";
        var salt = "testSalt";

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserWithPassword(1, username, email, password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userProfileService = GetUserProfileService();
        var userResult = await userProfileService.GetByUsernameAsync(username);
        var retrievedUser = userResult.Data;

        // Generate expected token (MD5 of password + salt)
        var expectedToken = HashHelper.CreateMd5($"{password}{salt}");

        var authService = CreateUserAuthenticationService(userProfileService: userProfileService);

        // Act
        var result = await authService.ValidateTokenAsync(username, expectedToken!, salt);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(username, result.Data.UserName);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        var username = "testuser";
        var password = "testpassword";
        var email = "test@example.com";
        var salt = "testSalt";
        var invalidToken = "invalidToken";

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserWithPassword(1, username, email, password);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userProfileService = GetUserProfileService();
        var authService = CreateUserAuthenticationService(userProfileService: userProfileService);

        // Act
        var result = await authService.ValidateTokenAsync(username, invalidToken, salt);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
    }
}
