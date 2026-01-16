using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using NodaTime;

namespace Melodee.Tests.Common.Services;

public class UserDeviceProfileServiceTests : ServiceTestBase
{
    #region GetEffectiveProfileAsync Tests

    [Fact]
    public async Task GetEffectiveProfileAsync_WithNoProfiles_ReturnsGlobalDefault()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        // Act
        var profile = await service.GetEffectiveProfileAsync(user.Id, null);

        // Assert
        Assert.NotNull(profile);
        Assert.True(profile.DirectPlay);
        Assert.Equal("Global Default - Direct Play", profile.Name);
    }

    [Fact]
    public async Task GetEffectiveProfileAsync_WithUserDefault_ReturnsUserDefault()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();
        
        var userDefault = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "User Default - MP3 128k",
            IsDefaultProfile = true,
            DirectPlay = false,
            TargetCodec = "mp3",
            MaxBitrate = 128,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        await service.CreateAsync(userDefault);

        // Act
        var profile = await service.GetEffectiveProfileAsync(user.Id, null);

        // Assert
        Assert.NotNull(profile);
        Assert.False(profile.DirectPlay);
        Assert.Equal("User Default - MP3 128k", profile.Name);
        Assert.Equal("mp3", profile.TargetCodec);
        Assert.Equal(128, profile.MaxBitrate);
    }

    [Fact]
    public async Task GetEffectiveProfileAsync_WithPlayerProfile_ReturnsPlayerProfile()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();
        var player = await CreateTestPlayerAsync(user.Id, "MobileClient");

        var userDefault = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "User Default - MP3 128k",
            IsDefaultProfile = true,
            DirectPlay = false,
            TargetCodec = "mp3",
            MaxBitrate = 128,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var playerProfile = new UserDeviceProfile
        {
            UserId = user.Id,
            PlayerId = player.Id,
            Name = "Mobile - Opus 96k",
            IsDefaultProfile = false,
            DirectPlay = false,
            TargetCodec = "opus",
            MaxBitrate = 96,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        await service.CreateAsync(userDefault);
        await service.CreateAsync(playerProfile);

        // Act
        var profile = await service.GetEffectiveProfileAsync(user.Id, player.Id);

        // Assert
        Assert.NotNull(profile);
        Assert.False(profile.DirectPlay);
        Assert.Equal("Mobile - Opus 96k", profile.Name);
        Assert.Equal("opus", profile.TargetCodec);
        Assert.Equal(96, profile.MaxBitrate);
    }

    [Fact]
    public async Task GetEffectiveProfileAsync_Precedence_PlayerOverUserDefault()
    {
        // Arrange - This test verifies the precedence: player > user default > global default
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();
        var mobilePlayer = await CreateTestPlayerAsync(user.Id, "Mobile");
        var desktopPlayer = await CreateTestPlayerAsync(user.Id, "Desktop");

        // User default: MP3 192k
        var userDefault = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "User Default - MP3 192k",
            IsDefaultProfile = true,
            DirectPlay = false,
            TargetCodec = "mp3",
            MaxBitrate = 192,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Mobile player: Opus 96k (override)
        var mobileProfile = new UserDeviceProfile
        {
            UserId = user.Id,
            PlayerId = mobilePlayer.Id,
            Name = "Mobile - Opus 96k",
            IsDefaultProfile = false,
            DirectPlay = false,
            TargetCodec = "opus",
            MaxBitrate = 96,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Desktop player: Direct Play (override)
        var desktopProfile = new UserDeviceProfile
        {
            UserId = user.Id,
            PlayerId = desktopPlayer.Id,
            Name = "Desktop - Lossless",
            IsDefaultProfile = false,
            DirectPlay = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        await service.CreateAsync(userDefault);
        await service.CreateAsync(mobileProfile);
        await service.CreateAsync(desktopProfile);

        // Act & Assert - Mobile gets its override
        var mobileEffective = await service.GetEffectiveProfileAsync(user.Id, mobilePlayer.Id);
        Assert.Equal("opus", mobileEffective.TargetCodec);
        Assert.Equal(96, mobileEffective.MaxBitrate);

        // Act & Assert - Desktop gets its override
        var desktopEffective = await service.GetEffectiveProfileAsync(user.Id, desktopPlayer.Id);
        Assert.True(desktopEffective.DirectPlay);

        // Act & Assert - Unknown player gets user default
        var unknownEffective = await service.GetEffectiveProfileAsync(user.Id, null);
        Assert.Equal("mp3", unknownEffective.TargetCodec);
        Assert.Equal(192, unknownEffective.MaxBitrate);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithValidDirectPlayProfile_Succeeds()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Desktop Lossless",
            DirectPlay = true,
            IsDefaultProfile = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.CreateAsync(profile);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Id > 0);
    }

    [Fact]
    public async Task CreateAsync_WithValidTranscodingProfile_Succeeds()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Mobile MP3",
            DirectPlay = false,
            TargetCodec = "mp3",
            MaxBitrate = 192,
            ResampleRate = 44100,
            IsDefaultProfile = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.CreateAsync(profile);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("mp3", result.Data.TargetCodec);
        Assert.Equal(192, result.Data.MaxBitrate);
    }

    [Fact]
    public async Task CreateAsync_DirectPlayWithCodec_ReturnsValidationError()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Invalid Profile",
            DirectPlay = true,
            TargetCodec = "mp3", // Invalid - DirectPlay shouldn't have codec
            MaxBitrate = 192,
            IsDefaultProfile = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.CreateAsync(profile);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
    }

    [Fact]
    public async Task CreateAsync_TranscodingWithoutCodec_ReturnsValidationError()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Invalid Profile",
            DirectPlay = false,
            MaxBitrate = 192,
            IsDefaultProfile = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        var result = await service.CreateAsync(profile);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
    }

    [Fact]
    public async Task CreateAsync_MultipleDefaults_OnlyOneIsDefault()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile1 = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Default 1",
            DirectPlay = true,
            IsDefaultProfile = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var profile2 = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Default 2",
            DirectPlay = false,
            TargetCodec = "mp3",
            MaxBitrate = 128,
            IsDefaultProfile = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        // Act
        await service.CreateAsync(profile1);
        await service.CreateAsync(profile2);

        // Assert - Get the user default
        var effectiveDefault = await service.GetDefaultByUserAsync(user.Id);
        Assert.True(effectiveDefault.IsSuccess);
        Assert.Equal("Default 2", effectiveDefault.Data!.Name);

        // Verify only one is marked as default
        var allProfiles = await service.ListByUserAsync(user.Id, new PagedRequest { Page = 1, PageSize = 10 });
        var defaults = allProfiles.Data.Where(p => p.IsDefaultProfile).ToList();
        Assert.Single(defaults);
        Assert.Equal("Default 2", defaults[0].Name);
    }

    #endregion

    #region UpdateAsync and DeleteAsync Tests

    [Fact]
    public async Task UpdateAsync_ExistingProfile_UpdatesSuccessfully()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "Original",
            DirectPlay = true,
            IsDefaultProfile = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var created = await service.CreateAsync(profile);

        // Act - Update to transcoding profile
        created.Data!.Name = "Updated";
        created.Data.DirectPlay = false;
        created.Data.TargetCodec = "opus";
        created.Data.MaxBitrate = 128;

        var result = await service.UpdateAsync(created.Data);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("Updated", result.Data!.Name);
        Assert.False(result.Data.DirectPlay);
        Assert.Equal("opus", result.Data.TargetCodec);
    }

    [Fact]
    public async Task DeleteAsync_ExistingProfile_DeletesSuccessfully()
    {
        // Arrange
        var service = GetUserDeviceProfileService();
        var user = await CreateTestUserAsync();

        var profile = new UserDeviceProfile
        {
            UserId = user.Id,
            Name = "To Delete",
            DirectPlay = true,
            IsDefaultProfile = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var created = await service.CreateAsync(profile);

        // Act
        var result = await service.DeleteAsync(created.Data!.Id);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);

        // Verify it's gone
        var fetched = await service.GetByIdAsync(created.Data.Id);
        Assert.False(fetched.IsSuccess);
    }

    #endregion

    #region Helper Methods

    private async Task<User> CreateTestUserAsync()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        var username = $"testuser_{Guid.NewGuid():N}";
        var email = $"test_{Guid.NewGuid():N}@example.com";
        var publicKey = "test_public_key";
        
        var user = new User
        {
            ApiKey = Guid.NewGuid(),
            UserName = username,
            UserNameNormalized = username.ToUpperInvariant(),
            Email = email,
            EmailNormalized = email.ToUpperInvariant(),
            PublicKey = publicKey,
            PasswordEncrypted = "encrypted_password",
            IsAdmin = false,
            IsLocked = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<Player> CreateTestPlayerAsync(int userId, string clientName)
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        var player = new Player
        {
            UserId = userId,
            Name = clientName,
            Client = clientName,
            LastSeenAt = SystemClock.Instance.GetCurrentInstant(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        context.Players.Add(player);
        await context.SaveChangesAsync();
        return player;
    }

    #endregion
}
