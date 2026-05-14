using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
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
/// Tests for password reset functionality in UserService.
/// Covers token generation, validation, expiry, and consumption.
/// </summary>
public class UserServicePasswordResetTests : ServiceTestBase
{
    private UserService CreateUserService(IMelodeeConfigurationFactory? configFactory = null, IBus? bus = null)
    {
        var passwordHashService = new Mock<IPasswordHashService>().Object;
        var secretProtector = new Mock<ISecretProtector>().Object;
        var actualConfigFactory = configFactory ?? MockConfigurationFactory();
        var actualBus = bus ?? MockBus();

        var userProfileService = new UserProfileService(
            Logger,
            CacheManager,
            MockFactory(),
            actualConfigFactory,
            GetLibraryService(),
            GetArtistService(),
            GetAlbumService(),
            GetSongService(),
            GetPlaylistService(),
            GetPodcastService(),
            actualBus,
            passwordHashService,
            secretProtector,
            new ImageProcessor());
        var userAuthenticationService = new UserAuthenticationService(
            Logger,
            passwordHashService,
            secretProtector,
            actualBus,
            userProfileService,
            actualConfigFactory);

        return new UserService(
            Logger,
            CacheManager,
            MockFactory(),
            actualConfigFactory,
            GetLibraryService(),
            GetArtistService(),
            GetAlbumService(),
            GetSongService(),
            GetPlaylistService(),
            GetPodcastService(),
            actualBus,
            userAuthenticationService,
            userProfileService);
    }

    private User CreateTestUserForReset(string email)
    {
        var password = "TestPassword123!";
        var publicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var username = email.Split('@')[0];
        var config = TestsBase.NewPluginsConfiguration();
        var encryptedPassword = EncryptionHelper.Encrypt(
            config.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
            password,
            publicKey);

        return new User
        {
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
    public async Task GeneratePasswordResetTokenAsync_WithValidEmail_ReturnsToken()
    {
        // Arrange
        var email = "testreset@example.com";
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = CreateUserService();

        // Act
        var result = await userService.GeneratePasswordResetTokenAsync(email);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data);

        // Verify token format (Base64URL encoded)
        Assert.DoesNotContain("+", result.Data);
        Assert.DoesNotContain("/", result.Data);
        Assert.DoesNotContain("=", result.Data);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_UsesConfigurableTokenExpiry()
    {
        // Arrange
        var email = "expiry@example.com";
        var customExpiryMinutes = 120; // 2 hours

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Mock configuration with custom expiry
        var mockConfig = new Mock<IMelodeeConfiguration>();
        mockConfig.Setup(c => c.GetValue<int?>(SettingRegistry.SecurityPasswordResetTokenExpiryMinutes))
            .Returns(customExpiryMinutes);

        var mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConfig.Object);

        var userService = CreateUserService(mockConfigFactory.Object);

        // Act
        var result = await userService.GeneratePasswordResetTokenAsync(email);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify the token was set with correct expiry
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.NotNull(user);
            Assert.NotNull(user.PasswordResetToken);
            Assert.NotNull(user.PasswordResetTokenExpiresAt);

            // Verify expiry is approximately 2 hours from now (within 1 minute tolerance)
            var expectedExpiry = SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromMinutes(customExpiryMinutes));
            var actualExpiry = user.PasswordResetTokenExpiresAt.Value;
            var difference = Math.Abs((expectedExpiry - actualExpiry).TotalMinutes);
            Assert.True(difference < 1, $"Token expiry should be {customExpiryMinutes} minutes, but difference is {difference} minutes");
        }
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_WithNonexistentEmail_ReturnsNotFound()
    {
        // Arrange
        var userService = CreateUserService();

        // Act
        var result = await userService.GeneratePasswordResetTokenAsync("nonexistent@example.com");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_WithLockedUser_ReturnsAccessDenied()
    {
        // Arrange
        var email = "locked@example.com";
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            user.IsLocked = true;
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = CreateUserService();

        // Act
        var result = await userService.GeneratePasswordResetTokenAsync(email);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.AccessDenied, result.Type);
    }

    [Fact]
    public async Task ValidatePasswordResetTokenAsync_WithValidToken_ReturnsUser()
    {
        // Arrange
        var email = "validate@example.com";
        string? token = null;

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = CreateUserService();
        var generateResult = await userService.GeneratePasswordResetTokenAsync(email);
        token = generateResult.Data;

        // Act
        var result = await userService.ValidatePasswordResetTokenAsync(token!);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(email, result.Data.Email);
    }

    [Fact]
    public async Task ValidatePasswordResetTokenAsync_WithInvalidToken_ReturnsNotFound()
    {
        // Arrange
        var userService = CreateUserService();

        // Act
        var result = await userService.ValidatePasswordResetTokenAsync("invalid-token-12345");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
    }

    [Fact]
    public async Task ValidatePasswordResetTokenAsync_WithExpiredToken_ReturnsValidationFailure()
    {
        // Arrange
        var email = "expired@example.com";
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            user.PasswordResetToken = "expired-token";
            user.PasswordResetTokenExpiresAt = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromHours(1));
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = CreateUserService();

        // Act
        var result = await userService.ValidatePasswordResetTokenAsync("expired-token");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.Contains("expired", result.Messages?.FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPasswordWithTokenAsync_WithValidToken_ResetsPassword()
    {
        // Arrange
        var email = "reset@example.com";
        var newPassword = "NewPassword456!";
        string? token = null;

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = CreateUserService();
        var generateResult = await userService.GeneratePasswordResetTokenAsync(email);
        token = generateResult.Data;

        // Act
        var result = await userService.ResetPasswordWithTokenAsync(token!, newPassword);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        // Verify password was changed and token was cleared
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.NotNull(user);
            Assert.Null(user.PasswordResetToken);
            Assert.Null(user.PasswordResetTokenExpiresAt);

            // Verify new password works by decrypting
            var config = TestsBase.NewPluginsConfiguration();
            var decryptedPassword = EncryptionHelper.Decrypt(
                config.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                user.PasswordEncrypted,
                user.PublicKey);
            Assert.Equal(newPassword, decryptedPassword);
        }
    }

    [Fact]
    public async Task ResetPasswordWithTokenAsync_TokenCannotBeReused()
    {
        // Arrange
        var email = "reuse@example.com";
        var newPassword1 = "FirstPassword123!";
        var newPassword2 = "SecondPassword456!";
        string? token = null;

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUserForReset(email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = CreateUserService();
        var generateResult = await userService.GeneratePasswordResetTokenAsync(email);
        token = generateResult.Data;

        // Act - First reset should succeed
        var firstResult = await userService.ResetPasswordWithTokenAsync(token!, newPassword1);
        Assert.True(firstResult.IsSuccess);

        // Act - Second reset with same token should fail
        var secondResult = await userService.ResetPasswordWithTokenAsync(token!, newPassword2);

        // Assert
        Assert.False(secondResult.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, secondResult.Type);

        // Verify password is still the first one
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.NotNull(user);
            var config = TestsBase.NewPluginsConfiguration();
            var decryptedPassword = EncryptionHelper.Decrypt(
                config.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
                user.PasswordEncrypted,
                user.PublicKey);
            Assert.Equal(newPassword1, decryptedPassword);
        }
    }

    [Fact]
    public async Task ResetPasswordWithTokenAsync_WithInvalidToken_ReturnsNotFound()
    {
        // Arrange
        var userService = CreateUserService();

        // Act
        var result = await userService.ResetPasswordWithTokenAsync("invalid-token", "NewPassword123!");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
    }
}
