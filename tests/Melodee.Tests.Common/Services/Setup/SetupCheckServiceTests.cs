using FluentAssertions;
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

namespace Melodee.Tests.Common.Services.Setup;

public class SetupCheckServiceTests : IDisposable
{
    private readonly DbContextOptions<MelodeeDbContext> _dbContextOptions;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;

    public SetupCheckServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"SetupCheckTest_{Guid.NewGuid()}")
            .Options;
        _loggerMock = new Mock<ILogger>();
        _cacheManagerMock = new Mock<ICacheManager>();
    }

    public void Dispose()
    {
        using var context = new MelodeeDbContext(_dbContextOptions);
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task SetupCheckAsync_InvalidBaseUrl_ReturnsBlockingItem()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            await SeedLibrariesAsync(tempRoot);
            var config = CreateConfiguration(new Dictionary<string, object?>
            {
                [SettingRegistry.SystemBaseUrl] = "not-a-url",
                [SettingRegistry.SystemSiteName] = "Melodee",
                [SettingRegistry.SecuritySecretKey] = new string('a', 32)
            });

            var service = CreateService(config);

            var status = await service.SetupCheckAsync();

            status.BlockingItems.Should().Contain(item => item.Id == $"setting-{SettingRegistry.SystemBaseUrl}");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task SetupCheckAsync_ShortSecretKey_ReturnsBlockingItem()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            await SeedLibrariesAsync(tempRoot);
            var config = CreateConfiguration(new Dictionary<string, object?>
            {
                [SettingRegistry.SystemBaseUrl] = "https://example.com",
                [SettingRegistry.SystemSiteName] = "Melodee",
                [SettingRegistry.SecuritySecretKey] = "short-key"
            });

            var service = CreateService(config);

            var status = await service.SetupCheckAsync();

            status.BlockingItems.Should().Contain(item => item.Id == $"setting-{SettingRegistry.SecuritySecretKey}");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task SetupCheckAsync_RelativeLibraryPath_ReturnsBlockingItem()
    {
        await using var context = CreateContext();
        context.Libraries.AddRange(
            CreateLibrary(LibraryType.Inbound, "relative/inbound"),
            CreateLibrary(LibraryType.Staging, "relative/staging"),
            CreateLibrary(LibraryType.Storage, "relative/storage"));
        await context.SaveChangesAsync();

        var config = CreateConfiguration(new Dictionary<string, object?>
        {
            [SettingRegistry.SystemBaseUrl] = "https://example.com",
            [SettingRegistry.SystemSiteName] = "Melodee",
            [SettingRegistry.SecuritySecretKey] = new string('a', 32)
        });

        var service = CreateService(config);

        var status = await service.SetupCheckAsync();

        status.BlockingItems.Should().Contain(item => item.Id.StartsWith("library-relative-", StringComparison.OrdinalIgnoreCase));
    }

    private SetupCheckService CreateService(IMelodeeConfiguration configuration)
    {
        var configFactory = new Mock<IMelodeeConfigurationFactory>();
        configFactory.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configuration);

        var service = new SetupCheckService(
            CreateContextFactory(),
            CreateLibraryService(configFactory.Object),
            configFactory.Object);

        return service;
    }

    private IMelodeeConfiguration CreateConfiguration(Dictionary<string, object?> values)
    {
        return new MelodeeConfiguration(values);
    }

    private MelodeeDbContext CreateContext() => new(_dbContextOptions);

    private IDbContextFactory<MelodeeDbContext> CreateContextFactory()
    {
        var factory = new Mock<IDbContextFactory<MelodeeDbContext>>();
        factory.Setup(x => x.CreateDbContext())
            .Returns(CreateContext);
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(CreateContext()));
        return factory.Object;
    }

    private LibraryService CreateLibraryService(IMelodeeConfigurationFactory configurationFactory)
    {
        return new LibraryService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            CreateContextFactory(),
            configurationFactory,
            null!,
            null!);
    }

    private static Library CreateLibrary(LibraryType type, string path)
    {
        return new Library
        {
            Name = type.ToString(),
            Path = path,
            Type = (int)type,
            SortOrder = (int)type,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
    }

    private async Task SeedLibrariesAsync(string root)
    {
        var inbound = Path.Combine(root, "inbound");
        var staging = Path.Combine(root, "staging");
        var storage = Path.Combine(root, "storage");
        Directory.CreateDirectory(inbound);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(storage);

        await using var context = CreateContext();
        context.Libraries.AddRange(
            CreateLibrary(LibraryType.Inbound, inbound),
            CreateLibrary(LibraryType.Staging, staging),
            CreateLibrary(LibraryType.Storage, storage));
        await context.SaveChangesAsync();
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"melodee-setup-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
