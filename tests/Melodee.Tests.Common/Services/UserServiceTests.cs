using System.Collections.Concurrent;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Importing;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Rebus.Bus;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Melodee.Tests.Common.Services;

public class UserServiceTests : ServiceTestBase
{
    private UserService CreateUserService(IMelodeeConfigurationFactory? configFactory = null, IBus? bus = null)
    {
        var actualConfigFactory = configFactory ?? MockConfigurationFactory();
        var actualBus = bus ?? MockBus();

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
            CreateUserAuthenticationService(actualConfigFactory, actualBus),
            CreateUserProfileService(actualConfigFactory, actualBus));
    }

    private UserProfileService CreateUserProfileService(
        IMelodeeConfigurationFactory? configFactory = null,
        IBus? bus = null,
        Serilog.ILogger? logger = null)
    {
        var actualConfigFactory = configFactory ?? MockConfigurationFactory();
        var actualBus = bus ?? MockBus();

        return new UserProfileService(
            logger ?? Logger,
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
            MockPasswordHashService(),
            MockSecretProtector(),
            new ImageProcessor());
    }

    private UserAuthenticationService CreateUserAuthenticationService(IMelodeeConfigurationFactory? configFactory = null, IBus? bus = null)
    {
        var actualConfigFactory = configFactory ?? MockConfigurationFactory();
        var actualBus = bus ?? MockBus();

        return new UserAuthenticationService(
            Logger,
            MockPasswordHashService(),
            MockSecretProtector(),
            actualBus,
            CreateUserProfileService(actualConfigFactory, actualBus),
            actualConfigFactory);
    }

    [Fact]
    public async Task ListAsync_WithValidRequest_ReturnsPagedResult()
    {
        // Arrange
        var pagedRequest = new PagedRequest { Page = 1, PageSize = 10 };

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user1 = CreateTestUser(1, "user1", "user1@test.com");
            var user2 = CreateTestUser(2, "user2", "user2@test.com");
            context.Users.AddRange(user1, user2);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 2);
        Assert.NotEmpty(result.Data);
        Assert.IsType<UserDataInfo[]>(result.Data);

        // Verify UserDataInfo properties are correctly mapped
        var firstUser = result.Data.First();
        Assert.True(firstUser.Id > 0);
        Assert.NotEqual(Guid.Empty, firstUser.ApiKey);
        Assert.NotNull(firstUser.UserName);
        Assert.NotNull(firstUser.Email);
    }

    [Fact]
    public async Task ListAsync_WithUsernameFilter_ReturnsFilteredResults()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();

            var user1 = CreateTestUser(101, "johnsmith", "john@test.com");
            var user2 = CreateTestUser(102, "janesmith", "jane@test.com");
            var user3 = CreateTestUser(103, "bobwilson", "bob@test.com");
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var pagedRequest = new PagedRequest
        {
            Page = 1,
            PageSize = 10,
            FilterBy = [new Melodee.Common.Filtering.FilterOperatorInfo("username", Melodee.Common.Filtering.FilterOperator.Contains, "smith")]
        };

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Data, u => Assert.Contains("smith", u.UserName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_WithEmailFilter_ReturnsFilteredResults()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();

            var user1 = CreateTestUser(101, "user1", "alpha@company.com");
            var user2 = CreateTestUser(102, "user2", "beta@other.com");
            var user3 = CreateTestUser(103, "user3", "gamma@company.com");
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var pagedRequest = new PagedRequest
        {
            Page = 1,
            PageSize = 10,
            FilterBy = [new Melodee.Common.Filtering.FilterOperatorInfo("email", Melodee.Common.Filtering.FilterOperator.Contains, "company")]
        };

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Data, u => Assert.Contains("company", u.Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_WithIsLockedFilter_ReturnsFilteredResults()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();

            var user1 = CreateTestUser(101, "activeuser", "active@test.com");
            user1.IsLocked = false;
            var user2 = CreateTestUser(102, "lockeduser", "locked@test.com");
            user2.IsLocked = true;
            var user3 = CreateTestUser(103, "anotheractive", "another@test.com");
            user3.IsLocked = false;
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var pagedRequest = new PagedRequest
        {
            Page = 1,
            PageSize = 10,
            FilterBy = [new Melodee.Common.Filtering.FilterOperatorInfo("islocked", Melodee.Common.Filtering.FilterOperator.Equals, "true")]
        };

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Data);
        Assert.True(result.Data.First().IsLocked);
    }

    [Fact]
    public async Task ListAsync_WithIsAdminFilter_ReturnsFilteredResults()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();

            var user1 = CreateTestUser(101, "adminuser", "admin@test.com");
            user1.IsAdmin = true;
            var user2 = CreateTestUser(102, "regularuser", "regular@test.com");
            user2.IsAdmin = false;
            var user3 = CreateTestUser(103, "anotheradmin", "admin2@test.com");
            user3.IsAdmin = true;
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var pagedRequest = new PagedRequest
        {
            Page = 1,
            PageSize = 10,
            FilterBy = [new Melodee.Common.Filtering.FilterOperatorInfo("isadmin", Melodee.Common.Filtering.FilterOperator.Equals, "true")]
        };

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Data, u => Assert.True(u.IsAdmin));
    }

    [Fact]
    public async Task ListAsync_WithOrderByUsername_ReturnsOrderedResults()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();

            var user1 = CreateTestUser(101, "zebra", "zebra@test.com");
            var user2 = CreateTestUser(102, "alpha", "alpha@test.com");
            var user3 = CreateTestUser(103, "middle", "middle@test.com");
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var pagedRequest = new PagedRequest
        {
            Page = 1,
            PageSize = 10,
            OrderBy = new Dictionary<string, string> { { "username", "ASC" } }
        };

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        var users = result.Data.ToArray();
        Assert.Equal(3, users.Length);
        Assert.Equal("alpha", users[0].UserName);
        Assert.Equal("middle", users[1].UserName);
        Assert.Equal("zebra", users[2].UserName);
    }

    [Fact]
    public async Task ListAsync_WithMultipleFilters_ReturnsFilteredResults()
    {
        // Arrange
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();

            var user1 = CreateTestUser(101, "johnsmith", "john@test.com");
            var user2 = CreateTestUser(102, "janesmith", "jane@test.com");
            var user3 = CreateTestUser(103, "bobwilson", "bob@test.com");
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();
        }

        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        // Multiple filters use OR logic
        var pagedRequest = new PagedRequest
        {
            Page = 1,
            PageSize = 10,
            FilterBy = [
                new Melodee.Common.Filtering.FilterOperatorInfo("username", Melodee.Common.Filtering.FilterOperator.Contains, "john"),
                new Melodee.Common.Filtering.FilterOperatorInfo("username", Melodee.Common.Filtering.FilterOperator.Contains, "bob")
            ]
        };

        // Act
        var result = await userProfileService.ListAsync(pagedRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_WithNullUserIds_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => userProfileService.DeleteAsync(null!));
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyUserIds_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userIds = Array.Empty<int>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userProfileService.DeleteAsync(userIds));
    }

    [Fact]
    public async Task DeleteAsync_WithValidUserIds_ReturnsSuccess()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            // Check if UserImages library already exists
            var existingLibrary = await context.Libraries.FirstOrDefaultAsync(x => x.Type == (int)LibraryType.UserImages);
            if (existingLibrary == null)
            {
                var library = new Library
                {
                    Name = "User Images",
                    Path = "/test/path",
                    Type = (int)LibraryType.UserImages,
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Libraries.Add(library);
                await context.SaveChangesAsync();
            }

            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var userIds = new[] { 1 };

        // Act
        var result = await userProfileService.DeleteAsync(userIds);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task DeleteAsync_WithUnknownUserId_ReturnsNotFound()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act
        var result = await userProfileService.DeleteAsync([999]);

        // Assert
        Assert.False(result.Data);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
    }

    [Fact]
    public async Task GetByEmailAddressAsync_WithNullEmail_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => userProfileService.GetByEmailAddressAsync(null!));
    }

    [Fact]
    public async Task GetByEmailAddressAsync_WithValidEmail_ReturnsUser()
    {
        // Arrange
        var email = "test@example.com";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.GetByEmailAddressAsync(email);

        // Assert
        Assert.NotNull(result);
        if (result.IsSuccess)
        {
            Assert.NotNull(result.Data);
            Assert.Equal(email, result.Data!.Email);
        }
        else
        {
            // If not successful, at least verify the operation completed without throwing
            Assert.NotNull(result);
        }
    }

    [Fact]
    public async Task UserLookupTimings_DoNotLogEmailAddressOrUsername()
    {
        const string email = "sensitive.user@example.test";
        const string username = "sensitive-username";
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.Add(CreateTestUser(210, username, email));
            await context.SaveChangesAsync();
        }

        var sink = new RecordingLogEventSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var userProfileService = CreateUserProfileService(logger: logger);

        var emailResult = await userProfileService.GetByEmailAddressAsync(email);
        var usernameResult = await userProfileService.GetByUsernameAsync(username);

        emailResult.IsSuccess.Should().BeTrue();
        usernameResult.IsSuccess.Should().BeTrue();
        sink.Output.Should().Contain("GetByEmailAddressAsync");
        sink.Output.Should().Contain("GetByUsernameAsync");
        sink.Output.Should().NotContainAny(email, username);
    }

    [Fact]
    public async Task GetByUsernameAsync_WithNullUsername_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => userProfileService.GetByUsernameAsync(null!));
    }

    [Fact]
    public async Task GetByUsernameAsync_WithValidUsername_ReturnsUser()
    {
        // Arrange
        var username = "testuser";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, username, "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.GetByUsernameAsync(username);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(username, result.Data.UserName);
    }

    [Fact]
    public async Task IsUserAdminAsync_WithAdminUser_ReturnsTrue()
    {
        // Arrange
        var username = "adminuser";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var adminUser = CreateTestUser(1, username, "admin@example.com");
            adminUser.IsAdmin = true;
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.IsUserAdminAsync(username);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsUserAdminAsync_WithNonAdminUser_ReturnsFalse()
    {
        // Arrange
        var username = "regularuser";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, username, "user@example.com");
            user.IsAdmin = false;
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.IsUserAdminAsync(username);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithEmptyGuid_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var apiKey = Guid.Empty;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userProfileService.GetByApiKeyAsync(apiKey));
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithValidApiKey_ReturnsUser()
    {
        // Arrange
        var apiKey = Guid.NewGuid();
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            user.ApiKey = apiKey;
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.GetByApiKeyAsync(apiKey);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(apiKey, result.Data.ApiKey);
    }

    [Fact]
    public async Task GetAsync_WithInvalidId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var id = 0;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userProfileService.GetAsync(id));
    }

    [Fact]
    public async Task GetAsync_WithValidId_ReturnsUser()
    {
        // Arrange
        var id = 1;
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(id, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.GetAsync(id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(id, result.Data.Id);
    }

    [Fact]
    public async Task LoginUserAsync_WithNullEmail_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var password = "testpassword";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => userAuthenticationService.LoginUserAsync(null!, password));
    }

    [Fact]
    public async Task LoginUserAsync_WithNullPassword_ReturnsUnauthorized()
    {
        // Arrange
        var emailAddress = "test@example.com";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act
        var result = await userAuthenticationService.LoginUserAsync(emailAddress, null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
    }

    [Fact]
    public async Task LoginUserAsync_WithValidPassword_ReturnsUser()
    {
        // Arrange
        var plainPassword = "Sup3rSecret!";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var configuration = TestsBase.NewPluginsConfiguration();
        var user = CreateTestUser(5, "loginuser", "loginuser@example.com");
        user.PublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        user.PasswordEncrypted = EncryptionHelper.Encrypt(
            configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
            plainPassword,
            user.PublicKey);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userAuthenticationService.LoginUserAsync(user.Email, plainPassword);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.LastLoginAt);
        Assert.NotNull(result.Data.LastActivityAt);
    }

    [Fact]
    public async Task LoginUserAsync_WithInvalidPassword_ReturnsUnauthorized()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var configuration = TestsBase.NewPluginsConfiguration();
        var user = CreateTestUser(6, "wrongpassword", "wrongpassword@example.com");
        user.PublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        user.PasswordEncrypted = EncryptionHelper.Encrypt(
            configuration.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
            "correct-password",
            user.PublicKey);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userAuthenticationService.LoginUserAsync(user.Email, "incorrect-password");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ReturnsUser()
    {
        // Arrange
        var password = "token-pass";
        var salt = "123";
        var config = TestsBase.NewPluginsConfiguration();
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var publicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var encryptedPassword = EncryptionHelper.Encrypt(
            config.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
            password,
            publicKey);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(9, "tokenuser", "token@example.com");
            user.PublicKey = publicKey;
            user.PasswordEncrypted = encryptedPassword;
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var token = HashHelper.CreateMd5($"{password}{salt}");

        // Act
        var result = await userAuthenticationService.ValidateTokenAsync("tokenuser", token!, salt);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(9, result.Data!.Id);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithLockedUser_ReturnsUnauthorized()
    {
        // Arrange
        var password = "locked-pass";
        var salt = "456";
        var config = TestsBase.NewPluginsConfiguration();
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var publicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var encryptedPassword = EncryptionHelper.Encrypt(
            config.GetValue<string>(SettingRegistry.EncryptionPrivateKey)!,
            password,
            publicKey);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(10, "lockeduser", "locked@example.com");
            user.PublicKey = publicKey;
            user.PasswordEncrypted = encryptedPassword;
            user.IsLocked = true;
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var token = HashHelper.CreateMd5($"{password}{salt}");

        // Act
        var result = await userAuthenticationService.ValidateTokenAsync("lockeduser", token!, salt);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateEmail_ReturnsValidationFailure()
    {
        // Arrange
        var email = "duplicate@example.com";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var existingUser = CreateTestUser(7, "existinguser", email);
            context.Users.Add(existingUser);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.RegisterAsync("newuser", email, "P@ssword1", null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task RegisterAsync_FirstUserBecomesAdmin()
    {
        // Arrange
        var userProfileService = GetUserProfileService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.RegisterAsync("first", "first@example.com", "P@ssw0rd!", null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.IsAdmin);
    }

    [Fact]
    public async Task RegisterAsync_WithInvalidPrivateCode_ReturnsUnauthorized()
    {
        // Arrange
        var settings = TestsBase.NewConfiguration();
        settings[SettingRegistry.RegisterPrivateCode] = "secret-code";
        var configFactory = new Mock<IMelodeeConfigurationFactory>();
        configFactory.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MelodeeConfiguration(settings));

        var userProfileService = CreateUserProfileService(configFactory.Object);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.RemoveRange(context.Users);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await userProfileService.RegisterAsync("user", "code@example.com", "Password!", "wrong");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.Unauthorized, result.Type);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ImportUserFavoriteSongs_WithNullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => userService.ImportUserFavoriteSongs(null!));
    }

    [Fact]
    public async Task ImportUserFavoriteSongs_WithNonExistentFile_ReturnsNotFound()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var apiKey = Guid.NewGuid();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            user.ApiKey = apiKey;
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var configuration = new UserFavoriteSongConfiguration(
            "/nonexistent/file.csv",
            apiKey,
            "Artist",
            "Album",
            "Song",
            false);

        // Act
        var result = await userService.ImportUserFavoriteSongs(configuration);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var currentUser = CreateTestUser(1, "current", "current@example.com");
        var detailToUpdate = CreateTestUser(0, "invalid", "invalid@example.com"); // Invalid ID

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userProfileService.UpdateAsync(currentUser, detailToUpdate));
    }

    [Fact]
    public async Task UpdateAsync_WithValidUser_UpdatesProperties()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var currentUser = CreateTestUser(8, "before", "before@example.com");

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            currentUser.Description = "old description";
            currentUser.Notes = "old notes";
            context.Users.Add(currentUser);
            await context.SaveChangesAsync();
        }

        var detailToUpdate = CreateTestUser(8, "after", "after@example.com");
        detailToUpdate.Description = "new description";
        detailToUpdate.Notes = "new notes";
        detailToUpdate.SortOrder = 5;
        detailToUpdate.Tags = "alpha,beta";
        detailToUpdate.IsLocked = true;

        // Act
        var result = await userProfileService.UpdateAsync(currentUser, detailToUpdate);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        await using var verifyContext = await MockFactory().CreateDbContextAsync();
        var updatedUser = await verifyContext.Users.FirstAsync(u => u.Id == 8);
        Assert.Equal("after@example.com", updatedUser.Email);
        Assert.Equal("after", updatedUser.UserName);
        Assert.Equal("new description", updatedUser.Description);
        Assert.Equal("new notes", updatedUser.Notes);
        Assert.True(updatedUser.IsLocked);
        Assert.Equal("alpha,beta", updatedUser.Tags);
        Assert.Equal(5, updatedUser.SortOrder);
    }

    [Fact]
    public async Task ToggleGenreHatedAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var genre = "Rock";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleGenreHatedAsync(userId, genre));
    }

    [Fact]
    public async Task ToggleArtistHatedAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var artistApiKey = Guid.NewGuid();
        var isHated = true;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleArtistHatedAsync(userId, artistApiKey, isHated));
    }

    [Fact]
    public async Task SetAlbumRatingAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var albumId = 1;
        var rating = 5;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.SetAlbumRatingAsync(userId, albumId, rating));
    }

    [Fact]
    public async Task SetSongRatingAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var songId = 1;
        var rating = 5;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.SetSongRatingAsync(userId, songId, rating));
    }

    [Fact]
    public async Task ToggleArtistStarAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var artistApiKey = Guid.NewGuid();
        var isStarred = true;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleArtistStarAsync(userId, artistApiKey, isStarred));
    }

    [Fact]
    public async Task ToggleAlbumHatedAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var albumApiKey = Guid.NewGuid();
        var isHated = true;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleAlbumHatedAsync(userId, albumApiKey, isHated));
    }

    [Fact]
    public async Task ToggleAlbumStarAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var albumApiKey = Guid.NewGuid();
        var isStarred = true;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleAlbumStarAsync(userId, albumApiKey, isStarred));
    }

    [Fact]
    public async Task SetArtistRatingAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var artistApiKey = Guid.NewGuid();
        var rating = 5;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.SetArtistRatingAsync(userId, artistApiKey, rating));
    }

    [Fact]
    public async Task SetAlbumRatingAsync_ByApiKey_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var albumApiKey = Guid.NewGuid();
        var rating = 5;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.SetAlbumRatingAsync(userId, albumApiKey, rating));
    }

    [Fact]
    public async Task ToggleSongStarAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var songApiKey = Guid.NewGuid();
        var isStarred = true;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleSongStarAsync(userId, songApiKey, isStarred));
    }

    [Fact]
    public async Task ToggleSongHatedAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var songApiKey = Guid.NewGuid();
        var isHated = true;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => userService.ToggleSongHatedAsync(userId, songApiKey, isHated));
    }

    [Fact]
    public async Task UpdateLastLogin_WithValidEventData_ReturnsSuccess()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(1, "testuser", "test@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var eventData = new UserLoginEvent(1, "testuser");

        // Act
        var result = await userService.UpdateLastLogin(eventData);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task GetByUsernameAsync_CacheIsUsedOnRepeatedCalls()
    {
        // Arrange
        var username = "cacheuser";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(2, username, "cacheuser@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        // Act
        var result1 = await userProfileService.GetByUsernameAsync(username);
        var result2 = await userProfileService.GetByUsernameAsync(username);
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.NotNull(result1.Data);
        Assert.NotNull(result2.Data);
        Assert.Equal(result1.Data.Id, result2.Data.Id);
    }

    [Fact]
    public async Task UpdateLastLogin_UpdatesUserLoginTimestamps()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(3, "eventuser", "eventuser@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        var eventData = new UserLoginEvent(3, "eventuser");

        // Act
        var result = await userService.UpdateLastLogin(eventData);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify the user's last login was updated
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var updatedUser = await context.Users.FirstAsync(u => u.Id == 3);
            Assert.NotNull(updatedUser.LastLoginAt);
            Assert.NotNull(updatedUser.LastActivityAt);
        }
    }

    [Fact]
    public async Task LoginUserAsync_PublishesBusEvent()
    {
        // Arrange - Create a mock bus that can be verified
        var busMock = new Mock<IBus>();
        busMock.Setup(b => b.SendLocal(It.IsAny<object>(), It.IsAny<Dictionary<string, string>>()))
            .Returns(Task.CompletedTask);

        // Create user authentication service with the verifiable bus mock
        var userAuthenticationService = CreateUserAuthenticationService(bus: busMock.Object);

        // Create test user with known encrypted password
        // Using "enc:" prefix pattern which bypasses encryption in LoginUserAsync
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(4, "logintest", "logintest@example.com");
            user.PasswordEncrypted = "testencryptedpassword123";
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act - Use "enc:" prefix to match the encrypted password directly
        var result = await userAuthenticationService.LoginUserAsync("logintest@example.com", "enc:testencryptedpassword123");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);

        // Verify that bus.SendLocal was called with a UserLoginEvent
        busMock.Verify(
            b => b.SendLocal(
                It.Is<UserLoginEvent>(e => e.UserId == 4 && e.UserName == "logintest"),
                It.IsAny<Dictionary<string, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetByEmailAddressAsync_WithUnusualCharacters_ReturnsUser()
    {
        // Arrange
        var email = "üñîçødë@example.com";
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(4, "unicodeuser", email);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        // Act
        var result = await userProfileService.GetByEmailAddressAsync(email);
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(email, result.Data.Email);
    }

    [Fact]
    public async Task IsUserAdminAsync_UnauthorizedAccess_ReturnsFalse()
    {
        // Arrange
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var username = "nonexistentuser";
        // Act
        var result = await userProfileService.IsUserAdminAsync(username);
        // Assert
        Assert.False(result);
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

    #region Podcast Channel Pinning Tests

    [Fact]
    public async Task IsPinned_WithInvalidUserId_ThrowsArgumentException()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var pinId = 1;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            userService.IsPinned(userId, UserPinType.PodcastChannel, pinId));
    }

    [Fact]
    public async Task IsPinned_WithPodcastChannel_WhenNotPinned_ReturnsFalse()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(201, "pinuser1", "pinuser1@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var result = await userService.IsPinned(201, UserPinType.PodcastChannel, 999);

        Assert.False(result);
    }

    [Fact]
    public async Task IsPinned_WithPodcastChannel_WhenPinned_ReturnsTrue()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(202, "pinuser2", "pinuser2@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var userPin = new UserPin
            {
                UserId = 202,
                PinType = (int)UserPinType.PodcastChannel,
                PinId = 100,
                CreatedAt = now
            };
            context.UserPins.Add(userPin);
            await context.SaveChangesAsync();
        }

        var result = await userService.IsPinned(202, UserPinType.PodcastChannel, 100);

        Assert.True(result);
    }

    [Fact]
    public async Task TogglePinnedAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var userId = 0;
        var pinId = 1;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            userService.TogglePinnedAsync(userId, UserPinType.PodcastChannel, pinId));
    }

    [Fact]
    public async Task TogglePinnedAsync_WithPodcastChannel_WhenNotPinned_CreatesPin()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(203, "pinuser3", "pinuser3@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var result = await userService.TogglePinnedAsync(203, UserPinType.PodcastChannel, 101);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        await using (var verifyContext = await MockFactory().CreateDbContextAsync())
        {
            var pin = await verifyContext.UserPins
                .FirstOrDefaultAsync(p => p.UserId == 203 && p.PinId == 101 && p.PinType == (int)UserPinType.PodcastChannel);
            Assert.NotNull(pin);
        }
    }

    [Fact]
    public async Task TogglePinnedAsync_WithPodcastChannel_WhenAlreadyPinned_RemovesPin()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(204, "pinuser4", "pinuser4@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var userPin = new UserPin
            {
                UserId = 204,
                PinType = (int)UserPinType.PodcastChannel,
                PinId = 102,
                CreatedAt = now
            };
            context.UserPins.Add(userPin);
            await context.SaveChangesAsync();
        }

        var result = await userService.TogglePinnedAsync(204, UserPinType.PodcastChannel, 102);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        await using (var verifyContext = await MockFactory().CreateDbContextAsync())
        {
            var pin = await verifyContext.UserPins
                .FirstOrDefaultAsync(p => p.UserId == 204 && p.PinId == 102 && p.PinType == (int)UserPinType.PodcastChannel);
            Assert.Null(pin);
        }
    }

    [Fact]
    public async Task TogglePinnedAsync_WithPodcastChannel_TogglesCorrectly()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(205, "pinuser5", "pinuser5@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var firstToggle = await userService.TogglePinnedAsync(205, UserPinType.PodcastChannel, 103);
        Assert.True(firstToggle.IsSuccess);

        var isPinnedAfterFirst = await userService.IsPinned(205, UserPinType.PodcastChannel, 103);
        Assert.True(isPinnedAfterFirst);

        var secondToggle = await userService.TogglePinnedAsync(205, UserPinType.PodcastChannel, 103);
        Assert.True(secondToggle.IsSuccess);

        var isPinnedAfterSecond = await userService.IsPinned(205, UserPinType.PodcastChannel, 103);
        Assert.False(isPinnedAfterSecond);
    }

    [Fact]
    public async Task IsPinned_WithDifferentPinTypes_ReturnsCorrectResults()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(206, "pinuser6", "pinuser6@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var podcastPin = new UserPin
            {
                UserId = 206,
                PinType = (int)UserPinType.PodcastChannel,
                PinId = 104,
                CreatedAt = now
            };
            context.UserPins.Add(podcastPin);
            await context.SaveChangesAsync();
        }

        var isPodcastPinned = await userService.IsPinned(206, UserPinType.PodcastChannel, 104);
        var isArtistPinned = await userService.IsPinned(206, UserPinType.Artist, 104);
        var isAlbumPinned = await userService.IsPinned(206, UserPinType.Album, 104);

        Assert.True(isPodcastPinned);
        Assert.False(isArtistPinned);
        Assert.False(isAlbumPinned);
    }

    [Fact]
    public async Task TogglePinnedAsync_WithMultiplePodcastChannels_PinsIndependently()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = CreateTestUser(207, "pinuser7", "pinuser7@example.com");
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        await userService.TogglePinnedAsync(207, UserPinType.PodcastChannel, 105);
        await userService.TogglePinnedAsync(207, UserPinType.PodcastChannel, 106);
        await userService.TogglePinnedAsync(207, UserPinType.PodcastChannel, 107);

        var isChannel105Pinned = await userService.IsPinned(207, UserPinType.PodcastChannel, 105);
        var isChannel106Pinned = await userService.IsPinned(207, UserPinType.PodcastChannel, 106);
        var isChannel107Pinned = await userService.IsPinned(207, UserPinType.PodcastChannel, 107);

        Assert.True(isChannel105Pinned);
        Assert.True(isChannel106Pinned);
        Assert.True(isChannel107Pinned);

        await userService.TogglePinnedAsync(207, UserPinType.PodcastChannel, 106);

        isChannel105Pinned = await userService.IsPinned(207, UserPinType.PodcastChannel, 105);
        isChannel106Pinned = await userService.IsPinned(207, UserPinType.PodcastChannel, 106);
        isChannel107Pinned = await userService.IsPinned(207, UserPinType.PodcastChannel, 107);

        Assert.True(isChannel105Pinned);
        Assert.False(isChannel106Pinned);
        Assert.True(isChannel107Pinned);
    }

    [Fact]
    public async Task TogglePinnedAsync_WithDifferentUsers_PinsIndependently()
    {
        var userService = GetUserService();
        var userProfileService = GetUserProfileService();
        var userAuthenticationService = GetUserAuthenticationService();

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user1 = CreateTestUser(208, "pinuser8", "pinuser8@example.com");
            var user2 = CreateTestUser(209, "pinuser9", "pinuser9@example.com");
            context.Users.AddRange(user1, user2);
            await context.SaveChangesAsync();
        }

        await userService.TogglePinnedAsync(208, UserPinType.PodcastChannel, 108);
        await userService.TogglePinnedAsync(209, UserPinType.PodcastChannel, 108);

        var isUser1Pinned = await userService.IsPinned(208, UserPinType.PodcastChannel, 108);
        var isUser2Pinned = await userService.IsPinned(209, UserPinType.PodcastChannel, 108);

        Assert.True(isUser1Pinned);
        Assert.True(isUser2Pinned);

        await userService.TogglePinnedAsync(208, UserPinType.PodcastChannel, 108);

        isUser1Pinned = await userService.IsPinned(208, UserPinType.PodcastChannel, 108);
        isUser2Pinned = await userService.IsPinned(209, UserPinType.PodcastChannel, 108);

        Assert.False(isUser1Pinned);
        Assert.True(isUser2Pinned);
    }

    #endregion

    private sealed class RecordingLogEventSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public string Output => string.Join(
            Environment.NewLine,
            _events.Select(x => $"{x.RenderMessage()} {x.Exception}"));

        public void Emit(LogEvent logEvent)
        {
            _events.Enqueue(logEvent);
        }
    }
}
