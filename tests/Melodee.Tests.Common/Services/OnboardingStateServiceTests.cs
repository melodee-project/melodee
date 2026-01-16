using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Setup;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Common.Services;

public class OnboardingStateServiceTests : IDisposable
{
    private readonly DbContextOptions<MelodeeDbContext> _dbContextOptions;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;
    private readonly Mock<ISetupCheckService> _setupCheckServiceMock;

    public OnboardingStateServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"OnboardingStateTest_{Guid.NewGuid()}")
            .Options;

        _loggerMock = new Mock<ILogger>();
        _cacheManagerMock = new Mock<ICacheManager>();
        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();
        _setupCheckServiceMock = new Mock<ISetupCheckService>();
    }

    public void Dispose()
    {
        using var context = new MelodeeDbContext(_dbContextOptions);
        context.Database.EnsureDeleted();
    }

    private MelodeeDbContext CreateContext() => new(_dbContextOptions);
    private IDbContextFactory<MelodeeDbContext> CreateContextFactory()
    {
        var factory = new Mock<IDbContextFactory<MelodeeDbContext>>();
        factory.Setup(x => x.CreateDbContext(It.IsAny<CancellationToken>()))
            .Returns(CreateContext);
        return factory.Object;
    }

    [Fact]
    public async Task IsOnboardingRequiredAsync_NoCompletionMarker_ReturnsTrue()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemOnboardingCompletedAt))
            .Returns((string?)null);
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupStatus { IsReady = true });

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        var result = await service.IsOnboardingRequiredAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsOnboardingRequiredAsync_CompletionMarkerSetAndReady_ReturnsFalse()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemOnboardingCompletedAt))
            .Returns(DateTimeOffset.UtcNow.ToString("O"));
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupStatus { IsReady = true });

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        var result = await service.IsOnboardingRequiredAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsOnboardingRequiredAsync_CompletionMarkerSetButNotReady_ReturnsTrue()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemOnboardingCompletedAt))
            .Returns(DateTimeOffset.UtcNow.ToString("O"));
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupStatus
            {
                IsReady = false,
                BlockingItems = new List<SetupItem>
                {
                    new() { Name = "Missing baseUrl", Severity = SetupItemSeverity.Blocking }
                }
            });

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        var result = await service.IsOnboardingRequiredAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetSetupStatusAsync_ReturnsCachedStatus()
    {
        var expectedStatus = new SetupStatus { IsReady = true };
        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedStatus);

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        var result1 = await service.GetSetupStatusAsync();
        var result2 = await service.GetSetupStatusAsync();

        result1.Should().BeSameAs(result2);
        _setupCheckServiceMock.Verify(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshSetupStatusAsync_ClearsCache()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemOnboardingCompletedAt))
            .Returns((string?)null);
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        var status1 = new SetupStatus { IsReady = false };
        var status2 = new SetupStatus { IsReady = true };
        _setupCheckServiceMock.SetupSequence(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status1)
            .ReturnsAsync(status2);

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        var result1 = await service.GetSetupStatusAsync();
        await service.RefreshSetupStatusAsync();
        var result2 = await service.GetSetupStatusAsync();

        result1.IsReady.Should().BeFalse();
        result2.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task MarkOnboardingCompletedAsync_SetsCompletionMarker()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemOnboardingCompletedAt))
            .Returns((string?)null);
        config.Setup(x => x.GetValue<int>(It.IsAny<string>())).Returns(10);
        config.Setup(x => x.DefaultPageSize()).Returns(25);
        config.Setup(x => x.DefaultPageSizeOptions()).Returns(new[] { 10, 20, 30 });
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupStatus { IsReady = true });

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        await service.MarkOnboardingCompletedAsync();

        _setupCheckServiceMock.Verify(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task GetBlockingItemsAsync_ReturnsOnlyBlockingItems()
    {
        var blockingItems = new List<SetupItem>
        {
            new() { Name = "Missing baseUrl", Severity = SetupItemSeverity.Blocking },
            new() { Name = "Missing library paths", Severity = SetupItemSeverity.Blocking },
            new() { Name = "Optional: Low disk space", Severity = SetupItemSeverity.Recommended }
        };

        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupStatus
            {
                IsReady = false,
                Items = blockingItems,
                BlockingItems = blockingItems.Where(x => x.Severity == SetupItemSeverity.Blocking).ToList()
            });

        var service = new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory(),
            _loggerMock.Object,
            _cacheManagerMock.Object);

        var result = await service.GetBlockingItemsAsync();

        result.Count.Should().Be(2);
        result.Should().AllSatisfy(x => x.Severity.Should().Be(SetupItemSeverity.Blocking));
    }
}
