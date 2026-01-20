using Melodee.Common.Data.Models;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Common.Services;

public class RadioStationUserPreferenceServiceTests : ServiceTestBase
{
    private readonly RadioStationUserPreferenceService _service;
    private readonly RadioStation _testStation;
    private readonly User _testUser;

    public RadioStationUserPreferenceServiceTests()
    {
        _service = GetRadioStationUserPreferenceService();
        _testStation = CreateTestRadioStation();
        _testUser = CreateTestUser();
    }

    private RadioStation CreateTestRadioStation()
    {
        return new RadioStation
        {
            Name = "Test Station",
            StreamUrl = "https://test.com/stream",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    private User CreateTestUser()
    {
        return new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = "publickey",
            PasswordEncrypted = "password",
            IsAdmin = false,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    [Fact]
    public async Task UpdatePreferenceAsync_CreatesNewPreference_WhenNotExists()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        context.RadioStations.Add(_testStation);
        context.Users.Add(_testUser);
        await context.SaveChangesAsync();

        var result = await _service.UpdatePreferenceAsync(
            _testUser.Id,
            _testStation.Id,
            isFavorite: true,
            isHidden: false,
            sortOrder: 5);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsFavorite);
        Assert.False(result.Data.IsHidden);
        Assert.Equal(5, result.Data.SortOrder);
    }

    [Fact]
    public async Task UpdatePreferenceAsync_UpdatesExistingPreference_WhenExists()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        context.RadioStations.Add(_testStation);
        context.Users.Add(_testUser);
        await context.SaveChangesAsync();

        var existingPreference = new RadioStationUserPreference
        {
            UserId = _testUser.Id,
            RadioStationId = _testStation.Id,
            IsFavorite = false,
            IsHidden = false,
            SortOrder = 100,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.RadioStationUserPreferences.Add(existingPreference);
        await context.SaveChangesAsync();

        var result = await _service.UpdatePreferenceAsync(
            _testUser.Id,
            _testStation.Id,
            isFavorite: true,
            isHidden: null,
            sortOrder: null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsFavorite);
        Assert.False(result.Data.IsHidden);
        Assert.Equal(100, result.Data.SortOrder);
    }

    [Fact]
    public async Task UpdatePreferenceAsync_CreatesWithDefaults_WhenNoValuesProvided()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        context.RadioStations.Add(_testStation);
        context.Users.Add(_testUser);
        await context.SaveChangesAsync();

        var result = await _service.UpdatePreferenceAsync(
            _testUser.Id,
            _testStation.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsFavorite);
        Assert.False(result.Data.IsHidden);
        Assert.Equal(1000, result.Data.SortOrder);
    }

    [Fact]
    public async Task GetPreferenceAsync_ReturnsNull_WhenNotExists()
    {
        var result = await _service.GetPreferenceAsync(999, 999);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetPreferenceAsync_ReturnsPreference_WhenExists()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        context.RadioStations.Add(_testStation);
        context.Users.Add(_testUser);
        await context.SaveChangesAsync();

        var preference = new RadioStationUserPreference
        {
            UserId = _testUser.Id,
            RadioStationId = _testStation.Id,
            IsFavorite = true,
            IsHidden = true,
            SortOrder = 50,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.RadioStationUserPreferences.Add(preference);
        await context.SaveChangesAsync();

        var result = await _service.GetPreferenceAsync(_testUser.Id, _testStation.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsFavorite);
        Assert.True(result.Data.IsHidden);
        Assert.Equal(50, result.Data.SortOrder);
    }

    [Fact]
    public async Task GetUserPreferencesAsync_ReturnsAllUserPreferences()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        var station1 = new RadioStation { Name = "Station 1", StreamUrl = "https://test1.com/stream", CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        var station2 = new RadioStation { Name = "Station 2", StreamUrl = "https://test2.com/stream", CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        context.RadioStations.AddRange(station1, station2);
        context.Users.Add(_testUser);
        await context.SaveChangesAsync();

        var pref1 = new RadioStationUserPreference
        {
            UserId = _testUser.Id,
            RadioStationId = station1.Id,
            IsFavorite = true,
            IsHidden = false,
            SortOrder = 10,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        var pref2 = new RadioStationUserPreference
        {
            UserId = _testUser.Id,
            RadioStationId = station2.Id,
            IsFavorite = false,
            IsHidden = true,
            SortOrder = 20,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.RadioStationUserPreferences.AddRange(pref1, pref2);
        await context.SaveChangesAsync();

        var result = await _service.GetUserPreferencesAsync(_testUser.Id);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Length);
    }

    [Fact]
    public async Task DeletePreferenceAsync_ReturnsFalse_WhenNotExists()
    {
        var result = await _service.DeletePreferenceAsync(999, 999);

        Assert.False(result.IsSuccess);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task DeletePreferenceAsync_ReturnsTrue_WhenDeleted()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        context.RadioStations.Add(_testStation);
        context.Users.Add(_testUser);
        await context.SaveChangesAsync();

        var preference = new RadioStationUserPreference
        {
            UserId = _testUser.Id,
            RadioStationId = _testStation.Id,
            IsFavorite = true,
            IsHidden = false,
            SortOrder = 100,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.RadioStationUserPreferences.Add(preference);
        await context.SaveChangesAsync();

        var result = await _service.DeletePreferenceAsync(_testUser.Id, _testStation.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }
}
