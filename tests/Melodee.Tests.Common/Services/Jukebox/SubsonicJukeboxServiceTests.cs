using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Jukebox;
using Melodee.Common.Services.Playback;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.Jukebox;

public class SubsonicJukeboxServiceTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;
    private readonly Mock<IDbContextFactory<MelodeeDbContext>> _contextFactoryMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configurationFactoryMock;

    public SubsonicJukeboxServiceTests()
    {
        _loggerMock = new Mock<ILogger>();
        _cacheManagerMock = new Mock<ICacheManager>();
        _contextFactoryMock = new Mock<IDbContextFactory<MelodeeDbContext>>();
        _configurationFactoryMock = new Mock<IMelodeeConfigurationFactory>();

        var configMock = new Mock<IMelodeeConfiguration>();
        configMock.Setup(x => x.GetValue<bool>(SettingRegistry.JukeboxEnabled)).Returns(false);
        _configurationFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configMock.Object);
    }

    private SubsonicJukeboxService CreateService()
    {
        var playbackBackendServiceMock = new Mock<IPlaybackBackendService>();

        // PartyQueueService and PartyPlaybackService are not used when jukebox is disabled,
        // so we pass null. These are sealed classes that cannot be mocked.
        return new SubsonicJukeboxService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _contextFactoryMock.Object,
            _configurationFactoryMock.Object,
            null!,
            null!,
            playbackBackendServiceMock.Object);
    }

    [Fact]
    public async Task GetStatusAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.GetStatusAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task GetPlaylistAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.GetPlaylistAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task SetGainAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.SetGainAsync(1, 0.8);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task StartAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.StartAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task StopAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.StopAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task SkipAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.SkipAsync(1, 1, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task AddAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.AddAsync(1, new[] { "song1-id" });

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task ClearAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.ClearAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task RemoveAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.RemoveAsync(1, 0);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }

    [Fact]
    public async Task ShuffleAsync_WhenJukeboxDisabled_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.ShuffleAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationResponseType.BadRequest, result.Type);
    }
}
