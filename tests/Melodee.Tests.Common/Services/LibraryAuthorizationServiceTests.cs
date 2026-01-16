using Melodee.Common.Data.Models;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Common.Services;

public class LibraryAuthorizationServiceTests : ServiceTestBase
{
    private LibraryAuthorizationService CreateLibraryAuthorizationService()
    {
        return new LibraryAuthorizationService(Logger, CacheManager, MockFactory());
    }

    [Fact]
    public async Task CanUserAccessLibraryAsync_WithUnrestrictedLibrary_ReturnsTrue()
    {
        var service = CreateLibraryAuthorizationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User { Id = 1, UserName = "testuser", UserNameNormalized = "TESTUSER", Email = "test@example.com", EmailNormalized = "TEST@EXAMPLE.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var library = new Library { Id = 100, Name = "Public Library", Path = "/music/public", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            context.Users.Add(user);
            context.Libraries.Add(library);
            await context.SaveChangesAsync();
        }

        var result = await service.CanUserAccessLibraryAsync(1, 100);
        Assert.True(result);
    }

    [Fact]
    public async Task CanUserAccessLibraryAsync_WithUserInAllowedGroup_ReturnsTrue()
    {
        var service = CreateLibraryAuthorizationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User { Id = 2, UserName = "testuser", UserNameNormalized = "TESTUSER", Email = "test@example.com", EmailNormalized = "TEST@EXAMPLE.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var group = new UserGroup { Id = 201, Name = "Allowed Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var library = new Library { Id = 101, Name = "Restricted Library", Path = "/music/restricted", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };

            context.Users.Add(user);
            context.UserGroups.Add(group);
            context.Libraries.Add(library);
            await context.SaveChangesAsync();

            context.UserGroupMembers.Add(new UserGroupMember { UserId = 2, UserGroupId = 201, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            context.LibraryAccessControls.Add(new LibraryAccessControl { LibraryId = 101, UserGroupId = 201, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            await context.SaveChangesAsync();
        }

        var result = await service.CanUserAccessLibraryAsync(2, 101);
        Assert.True(result);
    }

    [Fact]
    public async Task CanUserAccessLibraryAsync_WithUserNotInAllowedGroup_ReturnsFalse()
    {
        var service = CreateLibraryAuthorizationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User { Id = 4, UserName = "testuser", UserNameNormalized = "TESTUSER", Email = "test@example.com", EmailNormalized = "TEST@EXAMPLE.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var group = new UserGroup { Id = 401, Name = "Restricted Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var library = new Library { Id = 103, Name = "Restricted Library", Path = "/music/restricted", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };

            context.Users.Add(user);
            context.UserGroups.Add(group);
            context.Libraries.Add(library);
            await context.SaveChangesAsync();

            context.LibraryAccessControls.Add(new LibraryAccessControl { LibraryId = 103, UserGroupId = 401, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            await context.SaveChangesAsync();
        }

        var result = await service.CanUserAccessLibraryAsync(4, 103);
        Assert.False(result);
    }

    [Fact]
    public async Task CanUserAccessLibraryAsync_WithInvalidUserId_ThrowsArgumentException()
    {
        var service = CreateLibraryAuthorizationService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CanUserAccessLibraryAsync(0, 1));
    }

    [Fact]
    public async Task GetAccessibleLibraryIdsForUserAsync_WithAllUnrestrictedLibraries_ReturnsAllLibraries()
    {
        var service = CreateLibraryAuthorizationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User { Id = 7, UserName = "testuser", UserNameNormalized = "TESTUSER", Email = "test@example.com", EmailNormalized = "TEST@EXAMPLE.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var libraries = new[]
            {
                new Library { Id = 701, Name = "Lib1", Path = "/lib1", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new Library { Id = 702, Name = "Lib2", Path = "/lib2", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new Library { Id = 703, Name = "Lib3", Path = "/lib3", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() }
            };
            context.Users.Add(user);
            context.Libraries.AddRange(libraries);
            await context.SaveChangesAsync();
        }

        var result = await service.GetAccessibleLibraryIdsForUserAsync(7);
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Contains(701, result);
        Assert.Contains(702, result);
        Assert.Contains(703, result);
    }

    [Fact]
    public async Task GetAccessibleLibraryIdsForUserAsync_WithMixedRestrictions_ReturnsCorrectLibraries()
    {
        var service = CreateLibraryAuthorizationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = new User { Id = 8, UserName = "testuser", UserNameNormalized = "TESTUSER", Email = "test@example.com", EmailNormalized = "TEST@EXAMPLE.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var group = new UserGroup { Id = 801, Name = "Test Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var unrestrictedLib = new Library { Id = 801, Name = "Public", Path = "/public", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var restrictedAccessibleLib = new Library { Id = 802, Name = "Restricted Accessible", Path = "/restricted-ok", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var restrictedInaccessibleLib = new Library { Id = 803, Name = "Restricted Blocked", Path = "/restricted-no", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };

            context.Users.Add(user);
            context.UserGroups.Add(group);
            context.Libraries.AddRange(unrestrictedLib, restrictedAccessibleLib, restrictedInaccessibleLib);
            await context.SaveChangesAsync();

            context.UserGroupMembers.Add(new UserGroupMember { UserId = 8, UserGroupId = 801, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            context.LibraryAccessControls.Add(new LibraryAccessControl { LibraryId = 802, UserGroupId = 801, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() });

            var otherGroup = new UserGroup { Id = 802, Name = "Other Group", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            context.UserGroups.Add(otherGroup);
            context.LibraryAccessControls.Add(new LibraryAccessControl { LibraryId = 803, UserGroupId = 802, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() });
            await context.SaveChangesAsync();
        }

        var result = await service.GetAccessibleLibraryIdsForUserAsync(8);
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Contains(801, result);
        Assert.Contains(802, result);
        Assert.DoesNotContain(803, result);
    }

    [Fact]
    public async Task AcceptanceTest_TwoLibrariesTwoGroupsTwoUsers_EnforcesCorrectAccess()
    {
        var service = CreateLibraryAuthorizationService();
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user1 = new User { Id = 201, UserName = "user1", UserNameNormalized = "USER1", Email = "user1@test.com", EmailNormalized = "USER1@TEST.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var user2 = new User { Id = 202, UserName = "user2", UserNameNormalized = "USER2", Email = "user2@test.com", EmailNormalized = "USER2@TEST.COM", PublicKey = Guid.NewGuid().ToString(), PasswordEncrypted = string.Empty, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var groupA = new UserGroup { Id = 2001, Name = "Group A", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var groupB = new UserGroup { Id = 2002, Name = "Group B", ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var libraryA = new Library { Id = 2001, Name = "Library A", Path = "/lib-a", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            var libraryB = new Library { Id = 2002, Name = "Library B", Path = "/lib-b", Type = (int)Melodee.Common.Enums.LibraryType.Storage, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() };

            context.Users.AddRange(user1, user2);
            context.UserGroups.AddRange(groupA, groupB);
            context.Libraries.AddRange(libraryA, libraryB);
            await context.SaveChangesAsync();

            context.UserGroupMembers.AddRange(
                new UserGroupMember { UserId = 201, UserGroupId = 2001, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new UserGroupMember { UserId = 202, UserGroupId = 2002, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() }
            );

            context.LibraryAccessControls.AddRange(
                new LibraryAccessControl { LibraryId = 2001, UserGroupId = 2001, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() },
                new LibraryAccessControl { LibraryId = 2002, UserGroupId = 2002, ApiKey = Guid.NewGuid(), CreatedAt = SystemClock.Instance.GetCurrentInstant() }
            );
            await context.SaveChangesAsync();
        }

        Assert.True(await service.CanUserAccessLibraryAsync(201, 2001));
        Assert.False(await service.CanUserAccessLibraryAsync(201, 2002));
        Assert.False(await service.CanUserAccessLibraryAsync(202, 2001));
        Assert.True(await service.CanUserAccessLibraryAsync(202, 2002));

        var user1Libraries = await service.GetAccessibleLibraryIdsForUserAsync(201);
        var user2Libraries = await service.GetAccessibleLibraryIdsForUserAsync(202);

        Assert.Single(user1Libraries);
        Assert.Contains(2001, user1Libraries);
        Assert.Single(user2Libraries);
        Assert.Contains(2002, user2Libraries);
    }
}
