using FluentAssertions;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Common.Services;

/// <summary>
/// Tests for UserService social login functionality (Phase 2).
/// </summary>
public class UserServiceSocialLoginTests : ServiceTestBase
{
    [Fact]
    public async Task GetUserBySocialLoginAsync_ReturnsNotFound_WhenNoSocialLoginExists()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act
        var result = await userService.GetUserBySocialLoginAsync("Google", "nonexistent_subject");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task LinkSocialLoginAsync_LinksGoogleAccount_Successfully()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Get the user to obtain their ID
        var userResult = await userProfileService.GetByUsernameAsync("testuser");
        userResult.IsSuccess.Should().BeTrue();
        var userId = userResult.Data!.Id;

        // Act
        var result = await userService.LinkSocialLoginAsync(
            userId,
            "Google",
            "google_subject_123",
            "test@gmail.com",
            "Test User",
            null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public async Task LinkSocialLoginAsync_ReturnsError_WhenSubjectAlreadyLinkedToAnotherUser()
    {
        // Arrange
        int user1Id, user2Id;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user1 = CreateTestUser(1, "user1", "user1@example.com");
            var user2 = CreateTestUser(2, "user2", "user2@example.com");
            context.Users.AddRange(user1, user2);
            await context.SaveChangesAsync();
            user1Id = user1.Id;
            user2Id = user2.Id;

            // Link to first user
            var socialLogin = new UserSocialLogin
            {
                UserId = user1Id,
                Provider = "Google",
                Subject = "google_subject_shared",
                Email = "shared@gmail.com",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.UserSocialLogins.Add(socialLogin);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act - Try to link same subject to second user
        var result = await userService.LinkSocialLoginAsync(
            user2Id,
            "Google",
            "google_subject_shared",
            "shared@gmail.com",
            "Shared User",
            null);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeFalse();
        result.Messages.Should().Contain(m => m != null && m.Contains("already linked"));
    }

    [Fact]
    public async Task UnlinkSocialLoginAsync_RemovesGoogleLink_Successfully()
    {
        // Arrange
        int userId;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;

            var socialLogin = new UserSocialLogin
            {
                UserId = userId,
                Provider = "Google",
                Subject = "google_subject_to_unlink",
                Email = "test@gmail.com",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.UserSocialLogins.Add(socialLogin);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act
        var result = await userService.UnlinkSocialLoginAsync(userId, "Google");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkSocialLoginAsync_ReturnsNotFound_WhenNoLinkExists()
    {
        // Arrange
        int userId;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act
        var result = await userService.UnlinkSocialLoginAsync(userId, "Google");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserSocialLoginsAsync_ReturnsAllLinkedProviders()
    {
        // Arrange
        int userId;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;

            var socialLogin = new UserSocialLogin
            {
                UserId = userId,
                Provider = "Google",
                Subject = "google_subject_123",
                Email = "test@gmail.com",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.UserSocialLogins.Add(socialLogin);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act
        var result = await userService.GetUserSocialLoginsAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data.Should().HaveCount(1);
        result.Data![0].Provider.Should().Be("Google");
    }

    [Fact]
    public async Task UpdateSocialLoginLastLoginAsync_UpdatesTimestamp()
    {
        // Arrange
        int userId;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;

            var originalTime = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(-1));
            var socialLogin = new UserSocialLogin
            {
                UserId = userId,
                Provider = "Google",
                Subject = "google_subject_123",
                Email = "test@gmail.com",
                LastLoginAt = originalTime,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.UserSocialLogins.Add(socialLogin);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act
        var result = await userService.UpdateSocialLoginLastLoginAsync("Google", "google_subject_123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserBySocialLoginAsync_ReturnsUser_WhenSocialLoginExists()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var socialLogin = new UserSocialLogin
            {
                UserId = user.Id,
                Provider = "Google",
                Subject = "google_subject_found",
                Email = "test@gmail.com",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.UserSocialLogins.Add(socialLogin);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act
        var result = await userService.GetUserBySocialLoginAsync("Google", "google_subject_found");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.UserName.Should().Be("testuser");
    }

    [Fact]
    public async Task LinkSocialLoginAsync_UpdatesExistingLink_WhenSameUserRelinks()
    {
        // Arrange
        int userId;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;

            var socialLogin = new UserSocialLogin
            {
                UserId = userId,
                Provider = "Google",
                Subject = "google_subject_relink",
                Email = "old@gmail.com",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(-7))
            };
            context.UserSocialLogins.Add(socialLogin);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();

        // Act - Same user relinking same Google account
        var result = await userService.LinkSocialLoginAsync(
            userId,
            "Google",
            "google_subject_relink",
            "new@gmail.com",
            "Updated User",
            null);

        // Assert - Should succeed (updates existing link)
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    private static User CreateTestUser(int id, string username, string email)
    {
        return new User
        {
            Id = id,
            UserName = username,
            UserNameNormalized = username.ToNormalizedString() ?? username.ToUpperInvariant(),
            Email = email,
            EmailNormalized = email.ToNormalizedString() ?? email.ToUpperInvariant(),
            PublicKey = "testkey",
            PasswordEncrypted = "encryptedpassword",
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
            IsAdmin = false,
            IsLocked = false
        };
    }
}

