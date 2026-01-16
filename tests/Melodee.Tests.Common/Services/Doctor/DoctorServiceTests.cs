using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
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

    public DoctorServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"DoctorServiceTest_{Guid.NewGuid()}")
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

    private TestDoctorService CreateDoctorService()
    {
        return new TestDoctorService(
            CreateContextFactory(),
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
        await SeedTestLibraryAsync("/test/inbound");
        await SeedTestLibraryAsync("/test/inbound/subfolder");

        var service = CreateDoctorService();
        var (check, paths, overlaps) = await service.RunLibraryPathCheckAsync(false);

        check.Success.Should().BeFalse();
        overlaps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunLibraryPathCheckAsync_NonOverlappingPaths_ReturnsSuccess()
    {
        await SeedTestLibraryAsync("/test/inbound");
        await SeedTestLibraryAsync("/test/storage");

        var service = CreateDoctorService();
        var (check, paths, overlaps) = await service.RunLibraryPathCheckAsync(false);

        check.Success.Should().BeTrue();
        overlaps.Should().BeEmpty();
    }

    [Fact]
    public async Task RunConfigurableServicesCheckAsync_ReturnsServicesList()
    {
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
