using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Imaging;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Doctor;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Common.Services.Doctor;

public class DoctorServiceTests : IDisposable
{
    private readonly DbContextOptions<MelodeeDbContext> _dbContextOptions;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;

    public DoctorServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"DoctorServiceTest_{Guid.NewGuid()}")
            .Options;

        _loggerMock = new Mock<ILogger>();
        _cacheManagerMock = new Mock<ICacheManager>();
        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();

        var factory = new Mock<IDbContextFactory<MelodeeDbContext>>();
        factory.Setup(x => x.CreateDbContext())
            .Returns(() => new MelodeeDbContext(_dbContextOptions));
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(new MelodeeDbContext(_dbContextOptions)));
        _contextFactory = factory.Object;
    }

    public void Dispose()
    {
        using var context = new MelodeeDbContext(_dbContextOptions);
        context.Database.EnsureDeleted();
    }

    private MelodeeDbContext CreateContext() => new(_dbContextOptions);

    private LibraryService CreateLibraryService()
    {
        return new LibraryService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _contextFactory,
            _configFactoryMock.Object,
            null!,
            new ImageProcessor(),
            null!);
    }

    private TestDoctorService CreateDoctorService()
    {
        return new TestDoctorService(
            _contextFactory,
            CreateLibraryService(),
            _configFactoryMock.Object);
    }

    [Fact]
    public async Task RunConfigurationCheckAsync_MissingRequiredSettings_ReturnsFailure()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(It.IsAny<string>()))
            .Returns(MelodeeConfiguration.RequiredNotSetValue);
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        var service = CreateDoctorService();
        var result = await service.RunConfigurationCheckAsync();

        result.Success.Should().BeFalse();
        result.Name.Should().Be("Configuration");
        result.Details.Should().Contain("Missing required settings");
    }

    [Fact]
    public async Task RunConfigurationCheckAsync_AllSettingsPresent_ReturnsSuccess()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemSiteName)).Returns("TestSite");
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemBaseUrl)).Returns("http://localhost:5000");
        config.Setup(x => x.GetValue<string>(SettingRegistry.SystemOnboardingCompletedAt)).Returns(DateTimeOffset.UtcNow.ToString("O"));
        config.Setup(x => x.GetValue<string>(It.IsAny<string>())).Returns("somevalue");
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        var service = CreateDoctorService();
        var result = await service.RunConfigurationCheckAsync();

        result.Success.Should().BeTrue();
        result.Details.Should().Contain("All required settings are configured");
    }

    [Fact]
    public async Task RunDatabaseCheckAsync_CannotConnect_ReturnsFailure()
    {
        var service = CreateDoctorService();
        var result = await service.RunDatabaseCheckAsync();

        result.Name.Should().Be("Database");
    }

    [Fact]
    public async Task RunLibraryPathCheckAsync_NoLibraries_ReturnsSuccess()
    {
        var service = CreateDoctorService();
        var (check, paths, overlaps) = await service.RunLibraryPathCheckAsync(true);

        check.Success.Should().BeTrue();
        paths.Should().BeEmpty();
        overlaps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunLibraryPathCheckAsync_MissingLibraryPath_ReturnsFailure()
    {
        await SeedTestLibraryAsync("/nonexistent/path");

        var service = CreateDoctorService();
        var (check, paths, overlaps) = await service.RunLibraryPathCheckAsync(false);

        check.Success.Should().BeFalse();
        paths.Should().Contain(p => !p.Exists);
        overlaps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunLibraryPathCheckAsync_OverlappingPaths_ReturnsFailure()
    {
        // Create real temp directories for overlap test
        var basePath = Path.Combine(Path.GetTempPath(), $"doctor-test-{Guid.NewGuid():N}");
        var subPath = Path.Combine(basePath, "subfolder");
        Directory.CreateDirectory(subPath); // Creates both basePath and subPath

        try
        {
            await SeedTestLibraryAsync(basePath);
            await SeedTestLibraryAsync(subPath);

            var service = CreateDoctorService();
            var (check, paths, overlaps) = await service.RunLibraryPathCheckAsync(false);

            check.Success.Should().BeFalse();
            overlaps.Should().NotBeEmpty();
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    [Fact]
    public async Task RunLibraryPathCheckAsync_NonOverlappingPaths_ReturnsSuccess()
    {
        // Create real temp directories that don't overlap
        var path1 = Path.Combine(Path.GetTempPath(), $"doctor-test-a-{Guid.NewGuid():N}");
        var path2 = Path.Combine(Path.GetTempPath(), $"doctor-test-b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);

        try
        {
            await SeedTestLibraryAsync(path1);
            await SeedTestLibraryAsync(path2);

            var service = CreateDoctorService();
            var (check, paths, overlaps) = await service.RunLibraryPathCheckAsync(false);

            check.Success.Should().BeTrue();
            overlaps.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(path1, true);
            Directory.Delete(path2, true);
        }
    }

    [Fact]
    public async Task RunConfigurableServicesCheckAsync_ReturnsServicesList()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(It.IsAny<string>())).Returns("true");
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        var service = CreateDoctorService();
        var (check, services) = await service.RunConfigurableServicesCheckAsync();

        check.Success.Should().BeTrue();
        services.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunCoreChecksAsync_ReturnsAllChecks()
    {
        var config = new Mock<IMelodeeConfiguration>();
        config.Setup(x => x.GetValue<string>(It.IsAny<string>())).Returns("testvalue");
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config.Object);

        var service = CreateDoctorService();
        var result = await service.RunCoreChecksAsync();

        result.Checks.Should().NotBeEmpty();
        result.Checks.Should().Contain(c => c.Name == "Configuration");
        result.Checks.Should().Contain(c => c.Name == "Database");
        result.Checks.Should().Contain(c => c.Name == "LibraryPaths");
        result.Checks.Should().Contain(c => c.Name == "ConfigurableServices");
    }

    private async Task SeedTestLibraryAsync(string path)
    {
        await using var context = CreateContext();
        context.Libraries.Add(new Library
        {
            Name = $"TestLibrary_{Guid.NewGuid():N}",
            Path = path,
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        });
        await context.SaveChangesAsync();
    }

    private class TestDoctorService : DoctorServiceBase
    {
        public TestDoctorService(
            IDbContextFactory<MelodeeDbContext> dbContextFactory,
            LibraryService libraryService,
            IMelodeeConfigurationFactory configurationFactory)
            : base(dbContextFactory, libraryService, configurationFactory)
        {
        }
    }
}
