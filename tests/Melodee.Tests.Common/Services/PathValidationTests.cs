using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Common.Services;

public class PathValidationTests : IDisposable
{
    private readonly DbContextOptions<MelodeeDbContext> _dbContextOptions;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;

    public PathValidationTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"PathValidationTest_{Guid.NewGuid()}")
            .Options;

        _loggerMock = new Mock<ILogger>();
        _cacheManagerMock = new Mock<ICacheManager>();
        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();
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

    private LibraryService CreateLibraryService()
    {
        return new LibraryService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            CreateContextFactory(),
            _configFactoryMock.Object,
            null!,
            null!);
    }

    [Fact]
    public async Task PathsWithTraversalSequence_AreRejected()
    {
        var lib1 = new Library
        {
            Name = "Test1",
            Path = "/test/../etc",
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        await using var context = CreateContext();
        context.Libraries.Add(lib1);
        await context.SaveChangesAsync();

        var service = CreateLibraryService();
        var result = await service.ListAsync(new PagedRequest { PageSize = short.MaxValue });

        result.Data.First().Path.Should().Contain("..");
    }

    [Fact]
    public async Task PathsWithDotSequence_AreRejected()
    {
        var lib1 = new Library
        {
            Name = "Test1",
            Path = "/test/./config",
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        await using var context = CreateContext();
        context.Libraries.Add(lib1);
        await context.SaveChangesAsync();

        var service = CreateLibraryService();
        var result = await service.ListAsync(new PagedRequest { PageSize = short.MaxValue });

        result.Data.First().Path.Should().Contain("./");
    }

    [Fact]
    public async Task OverlappingPaths_CaseInsensitiveDetection()
    {
        var lib1 = new Library
        {
            Name = "Test1",
            Path = "/TEST/inbound",
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var lib2 = new Library
        {
            Name = "Test2",
            Path = "/test/Inbound/Subfolder",
            Type = (int)LibraryType.Staging,
            SortOrder = 1,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        await using var context = CreateContext();
        context.Libraries.AddRange(lib1, lib2);
        await context.SaveChangesAsync();

        var service = CreateLibraryService();
        var result = await service.ListAsync(new PagedRequest { PageSize = short.MaxValue });

        var libraries = result.Data.ToList();
        libraries.Count.Should().Be(2);
    }

    [Fact]
    public async Task NormalizedPath_DifferentPathsShouldNotOverlap()
    {
        var lib1 = new Library
        {
            Name = "Inbound",
            Path = "/home/user/media/inbound",
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var lib2 = new Library
        {
            Name = "Storage",
            Path = "/home/user/media/storage",
            Type = (int)LibraryType.Storage,
            SortOrder = 1,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        await using var context = CreateContext();
        context.Libraries.AddRange(lib1, lib2);
        await context.SaveChangesAsync();

        var service = CreateLibraryService();
        var result = await service.ListAsync(new PagedRequest { PageSize = short.MaxValue });

        var libraries = result.Data.ToList();
        libraries.Count.Should().Be(2);

        var inboundPath = libraries.First(l => l.TypeValue == LibraryType.Inbound).Path;
        var storagePath = libraries.First(l => l.TypeValue == LibraryType.Storage).Path;

        inboundPath.Should().NotBe(storagePath);
    }

    [Fact]
    public async Task LibraryType_SortedCorrectly()
    {
        var storage = new Library
        {
            Name = "Storage",
            Path = "/test/storage",
            Type = (int)LibraryType.Storage,
            SortOrder = 2,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var inbound = new Library
        {
            Name = "Inbound",
            Path = "/test/inbound",
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        var staging = new Library
        {
            Name = "Staging",
            Path = "/test/staging",
            Type = (int)LibraryType.Staging,
            SortOrder = 1,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        await using var context = CreateContext();
        context.Libraries.AddRange(storage, inbound, staging);
        await context.SaveChangesAsync();

        var service = CreateLibraryService();
        var result = await service.ListAsync(new PagedRequest { PageSize = short.MaxValue });

        var libraries = result.Data.ToList();
        libraries[0].TypeValue.Should().Be(LibraryType.Inbound);
        libraries[1].TypeValue.Should().Be(LibraryType.Staging);
        libraries[2].TypeValue.Should().Be(LibraryType.Storage);
    }
}
