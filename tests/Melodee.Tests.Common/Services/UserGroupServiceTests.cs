using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Common.Services;

/// <summary>
/// Tests for UserGroupService covering CRUD operations and group membership management.
/// </summary>
public class UserGroupServiceTests : ServiceTestBase
{
    private UserGroupService CreateUserGroupService()
    {
        return new UserGroupService(Logger, CacheManager, MockFactory());
    }

    #region GetById Tests

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsUserGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var group = new UserGroup
            {
                Id = 100,
                Name = "Test Group",
                Description = "Test Description",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetByIdAsync(100);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("Test Group", result.Data.Name);
        Assert.Equal("Test Description", result.Data.Description);
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act
        var result = await service.GetByIdAsync(999);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetByIdAsync_WithZeroId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetByIdAsync(0));
    }

    [Fact]
    public async Task GetByIdAsync_WithNegativeId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetByIdAsync(-1));
    }

    [Fact]
    public async Task GetByIdAsync_IncludesMembers_WhenPresent()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 1,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group = new UserGroup
            {
                Id = 101,
                Name = "Test Group",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();

            var membership = new UserGroupMember
            {
                UserId = user.Id,
                UserGroupId = group.Id,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroupMembers.Add(membership);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetByIdAsync(101);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.Members);
        Assert.Single(result.Data.Members);
        Assert.Equal("testuser", result.Data.Members.First().User!.UserName);
    }

    #endregion

    #region GetByApiKey Tests

    [Fact]
    public async Task GetByApiKeyAsync_WithValidApiKey_ReturnsUserGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        var apiKey = Guid.NewGuid();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var group = new UserGroup
            {
                Id = 102,
                Name = "Test Group",
                ApiKey = apiKey,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetByApiKeyAsync(apiKey);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("Test Group", result.Data.Name);
        Assert.Equal(apiKey, result.Data.ApiKey);
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithInvalidApiKey_ReturnsError()
    {
        // Arrange
        var service = CreateUserGroupService();
        var invalidApiKey = Guid.NewGuid();

        // Act
        var result = await service.GetByApiKeyAsync(invalidApiKey);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Contains("Unknown user group", result.Messages?.FirstOrDefault() ?? string.Empty);
    }

    [Fact]
    public async Task GetByApiKeyAsync_WithEmptyGuid_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetByApiKeyAsync(Guid.Empty));
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public async Task GetAllAsync_ReturnsAllGroups_OrderedByName()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.UserGroups.RemoveRange(context.UserGroups);
            await context.SaveChangesAsync();

            var groups = new[]
            {
                new UserGroup { Id = 201, Name = "Zebra Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new UserGroup { Id = 202, Name = "Alpha Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new UserGroup { Id = 203, Name = "Beta Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() }
            };
            context.UserGroups.AddRange(groups);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetAllAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.Length);
        Assert.Equal("Alpha Group", result.Data[0].Name);
        Assert.Equal("Beta Group", result.Data[1].Name);
        Assert.Equal("Zebra Group", result.Data[2].Name);
    }

    [Fact]
    public async Task GetAllAsync_WithNoGroups_ReturnsEmptyArray()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.UserGroups.RemoveRange(context.UserGroups);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetAllAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    #endregion

    #region GetGroupsForUser Tests

    [Fact]
    public async Task GetGroupsForUserAsync_ReturnsUserGroups()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 2,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group1 = new UserGroup { Id = 301, Name = "Group 1", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var group2 = new UserGroup { Id = 302, Name = "Group 2", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };

            context.Users.Add(user);
            context.UserGroups.AddRange(group1, group2);
            await context.SaveChangesAsync();

            var memberships = new[]
            {
                new UserGroupMember { UserId = 2, UserGroupId = 301, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new UserGroupMember { UserId = 2, UserGroupId = 302, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() }
            };
            context.UserGroupMembers.AddRange(memberships);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetGroupsForUserAsync(2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Length);
        Assert.Contains(result.Data, g => g.Name == "Group 1");
        Assert.Contains(result.Data, g => g.Name == "Group 2");
    }

    [Fact]
    public async Task GetGroupsForUserAsync_WithNoMemberships_ReturnsEmptyArray()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 3,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.GetGroupsForUserAsync(3);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task GetGroupsForUserAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetGroupsForUserAsync(0));
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesUserGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        var newGroup = new UserGroup
        {
            Name = "New Group",
            Description = "New Description",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.CreateAsync(newGroup);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("New Group", result.Data.Name);
        Assert.True(result.Data.Id > 0);
        Assert.NotEqual(default(Instant), result.Data.CreatedAt);

        // Verify in database
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var savedGroup = await context.UserGroups.FirstOrDefaultAsync(g => g.Name == "New Group");
            Assert.NotNull(savedGroup);
            Assert.Equal("New Description", savedGroup.Description);
        }
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ReturnsError()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var existingGroup = new UserGroup
            {
                Id = 401,
                Name = "Existing Group",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroups.Add(existingGroup);
            await context.SaveChangesAsync();
        }

        var duplicateGroup = new UserGroup
        {
            Name = "Existing Group",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.CreateAsync(duplicateGroup);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.Error, result.Type);
        Assert.Contains("already exists", result.Messages?.FirstOrDefault() ?? string.Empty);
    }

    [Fact]
    public async Task CreateAsync_WithNullGroup_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task UpdateAsync_WithValidData_UpdatesUserGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var group = new UserGroup
            {
                Id = 501,
                Name = "Original Name",
                Description = "Original Description",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        var updatedGroup = new UserGroup
        {
            Id = 501,
            Name = "Updated Name",
            Description = "Updated Description",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.UpdateAsync(updatedGroup);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("Updated Name", result.Data.Name);
        Assert.Equal("Updated Description", result.Data.Description);
        Assert.NotNull(result.Data.LastUpdatedAt);

        // Verify in database
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var savedGroup = await context.UserGroups.FindAsync(501);
            Assert.NotNull(savedGroup);
            Assert.Equal("Updated Name", savedGroup.Name);
            Assert.Equal("Updated Description", savedGroup.Description);
        }
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentGroup_ReturnsNotFound()
    {
        // Arrange
        var service = CreateUserGroupService();
        var nonExistentGroup = new UserGroup
        {
            Id = 999,
            Name = "Non-existent Group",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.UpdateAsync(nonExistentGroup);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
        Assert.Contains("not found", result.Messages?.FirstOrDefault() ?? string.Empty);
    }

    [Fact]
    public async Task UpdateAsync_WithNullGroup_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpdateAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_WithZeroId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();
        var group = new UserGroup { Id = 0, Name = "Test", CreatedAt = SystemClock.Instance.GetCurrentInstant() };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(group));
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_WithValidId_DeletesUserGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var group = new UserGroup
            {
                Id = 601,
                Name = "Group to Delete",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.DeleteAsync(601);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        // Verify deletion
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var deletedGroup = await context.UserGroups.FindAsync(601);
            Assert.Null(deletedGroup);
        }
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act
        var result = await service.DeleteAsync(999);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
        Assert.Contains("not found", result.Messages?.FirstOrDefault() ?? string.Empty);
    }

    [Fact]
    public async Task DeleteAsync_CascadesDeleteMembers()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 4,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group = new UserGroup
            {
                Id = 602,
                Name = "Group with Members",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();

            var membership = new UserGroupMember
            {
                UserId = 4,
                UserGroupId = 602,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroupMembers.Add(membership);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.DeleteAsync(602);

        // Assert
        Assert.True(result.IsSuccess);

        // Verify cascade deletion
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var members = await context.UserGroupMembers.Where(m => m.UserGroupId == 602).ToListAsync();
            Assert.Empty(members);
        }
    }

    #endregion

    #region AddUserToGroup Tests

    [Fact]
    public async Task AddUserToGroupAsync_WithValidData_AddsUserToGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 5,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group = new UserGroup
            {
                Id = 701,
                Name = "Test Group",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.AddUserToGroupAsync(5, 701);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        // Verify membership
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var membership = await context.UserGroupMembers
                .FirstOrDefaultAsync(m => m.UserId == 5 && m.UserGroupId == 701);
            Assert.NotNull(membership);
            Assert.NotEqual(Guid.Empty, membership.ApiKey);
            Assert.NotEqual(default(Instant), membership.CreatedAt);
        }
    }

    [Fact]
    public async Task AddUserToGroupAsync_WithDuplicateMembership_ReturnsError()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 6,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group = new UserGroup
            {
                Id = 702,
                Name = "Test Group",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();

            var existingMembership = new UserGroupMember
            {
                UserId = 6,
                UserGroupId = 702,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroupMembers.Add(existingMembership);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.AddUserToGroupAsync(6, 702);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Equal(OperationResponseType.Error, result.Type);
        Assert.Contains("already a member", result.Messages?.FirstOrDefault() ?? string.Empty);
    }

    [Fact]
    public async Task AddUserToGroupAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddUserToGroupAsync(0, 1));
    }

    [Fact]
    public async Task AddUserToGroupAsync_WithInvalidGroupId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddUserToGroupAsync(1, 0));
    }

    #endregion

    #region RemoveUserFromGroup Tests

    [Fact]
    public async Task RemoveUserFromGroupAsync_WithValidData_RemovesUserFromGroup()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 7,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group = new UserGroup
            {
                Id = 801,
                Name = "Test Group",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();

            var membership = new UserGroupMember
            {
                UserId = 7,
                UserGroupId = 801,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroupMembers.Add(membership);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.RemoveUserFromGroupAsync(7, 801);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);

        // Verify removal
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var membership = await context.UserGroupMembers
                .FirstOrDefaultAsync(m => m.UserId == 7 && m.UserGroupId == 801);
            Assert.Null(membership);
        }
    }

    [Fact]
    public async Task RemoveUserFromGroupAsync_WithNonExistentMembership_ReturnsNotFound()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User
            {
                Id = 8,
                UserName = "testuser",
                UserNameNormalized = "TESTUSER",
                Email = "test@example.com",
                EmailNormalized = "TEST@EXAMPLE.COM",
                PublicKey = Guid.NewGuid().ToString(),
                PasswordEncrypted = string.Empty,
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            var group = new UserGroup
            {
                Id = 802,
                Name = "Test Group",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.Users.Add(user);
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        // Act
        var result = await service.RemoveUserFromGroupAsync(8, 802);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Equal(OperationResponseType.NotFound, result.Type);
        Assert.Contains("not a member", result.Messages?.FirstOrDefault() ?? string.Empty);
    }

    [Fact]
    public async Task RemoveUserFromGroupAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveUserFromGroupAsync(0, 1));
    }

    [Fact]
    public async Task RemoveUserFromGroupAsync_WithInvalidGroupId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.RemoveUserFromGroupAsync(1, 0));
    }

    #endregion

    #region Cache Invalidation Tests

    [Fact]
    public async Task CreateAsync_ClearsCacheCorrectly()
    {
        // Arrange
        var service = CreateUserGroupService();

        // Populate cache
        await service.GetAllAsync();

        // Act
        var newGroup = new UserGroup
        {
            Name = "Cache Test Group",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        await service.CreateAsync(newGroup);

        // Assert - GetAll should return updated data
        var result = await service.GetAllAsync();
        Assert.Contains(result.Data, g => g.Name == "Cache Test Group");
    }

    [Fact]
    public async Task UpdateAsync_ClearsCacheCorrectly()
    {
        // Arrange
        var service = CreateUserGroupService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var group = new UserGroup
            {
                Id = 901,
                Name = "Original",
                ApiKey = Guid.NewGuid(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.UserGroups.Add(group);
            await context.SaveChangesAsync();
        }

        // Populate cache
        await service.GetByIdAsync(901);

        // Act
        var updatedGroup = new UserGroup { Id = 901, Name = "Updated", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        await service.UpdateAsync(updatedGroup);

        // Assert - GetById should return updated data
        var result = await service.GetByIdAsync(901);
        Assert.Equal("Updated", result.Data!.Name);
    }

    #endregion
}
