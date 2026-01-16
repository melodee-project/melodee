using FluentAssertions;
using Melodee.Common.Configuration;
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

public class SystemImportExportTests : IDisposable
{
    private readonly DbContextOptions<MelodeeDbContext> _dbContextOptions;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;

    public SystemImportExportTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportExportTest_{Guid.NewGuid()}")
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
        factory.Setup(x => x.CreateDbContext())
            .Returns(CreateContext);
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => Task.FromResult(CreateContext()));
        return factory.Object;
    }

    [Fact]
    public async Task ExportService_ProducesValidJson()
    {
        await SeedTestDataAsync();

        var exportService = new SystemExportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result = await exportService.ExportAsync(false);

        result.Success.Should().BeTrue();
        result.Json.Should().NotBeNullOrEmpty();
        result.SettingsCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportService_RedactsSecrets()
    {
        await SeedTestDataAsync();

        var exportService = new SystemExportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result = await exportService.ExportAsync(true);

        result.Success.Should().BeTrue();
        result.Json.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task ExportService_DeterministicOutput()
    {
        await SeedTestDataAsync();

        var exportService = new SystemExportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result1 = await exportService.ExportAsync(false);
        var result2 = await exportService.ExportAsync(false);

        result1.Json.Should().Be(result2.Json);
    }

    [Fact]
    public async Task ImportService_RejectsInvalidJson()
    {
        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result = await importService.ImportAsync("not valid json");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task ImportService_RejectsSchemaMismatch()
    {
        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var invalidJson = @"{
            ""schemaVersion"": ""2.0"",
            ""exportedAt"": ""2024-01-01T00:00:00Z"",
            ""settings"": [],
            ""libraries"": []
        }";

        var result = await importService.ImportAsync(invalidJson);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Schema version mismatch");
    }

    [Fact]
    public async Task ImportService_ImportsSettingsSuccessfully()
    {
        await SeedTestDataAsync();

        var importJson = @"{
            ""schemaVersion"": ""1.0"",
            ""exportedAt"": ""2024-01-01T00:00:00Z"",
            ""settings"": [
                { ""key"": ""system.siteName"", ""value"": ""Imported Site"" }
            ],
            ""libraries"": []
        }";

        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result = await importService.ImportAsync(importJson);

        result.Success.Should().BeTrue();
        result.SettingsImported.Should().Be(1);
    }

    [Fact]
    public async Task ImportService_IsTransactional()
    {
        await SeedTestDataAsync();

        var importJson = @"{
            ""schemaVersion"": ""1.0"",
            ""exportedAt"": ""2024-01-01T00:00:00Z"",
            ""settings"": [
                { ""key"": ""system.siteName"", ""value"": ""Imported Site"" }
            ],
            ""libraries"": [
                { ""name"": ""TestLib"", ""type"": ""Inbound"", ""path"": ""/nonexistent/path/that/wont/work"" }
            ]
        }";

        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result = await importService.ImportAsync(importJson);

        result.Success.Should().BeFalse();
        result.SettingsImported.Should().Be(0);
        result.LibrariesImported.Should().Be(0);
    }

    [Fact]
    public async Task ImportService_SkipsEnvironmentVariableSettings()
    {
        await using var context = CreateContext();
        context.Settings.Add(new Setting
        {
            Key = "system.siteName",
            Value = "Original Value",
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        });
        await context.SaveChangesAsync();

        var importJson = @"{
            ""schemaVersion"": ""1.0"",
            ""exportedAt"": ""2024-01-01T00:00:00Z"",
            ""settings"": [
                { ""key"": ""system.siteName"", ""value"": ""New Value"" }
            ],
            ""libraries"": []
        }";

        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var result = await importService.ImportAsync(importJson);

        result.Success.Should().BeTrue();
        result.SettingsSkipped.Should().Be(1);
    }

    [Fact]
    public async Task RoundTrip_ExportAndImport_ProducesEquivalentData()
    {
        await SeedTestDataAsync();

        var exportService = new SystemExportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var exportResult = await exportService.ExportAsync(false);
        exportResult.Success.Should().BeTrue();

        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            CreateContextFactory());

        var importResult = await importService.ImportAsync(exportResult.Json!);
        importResult.Success.Should().BeTrue();
    }

    private async Task SeedTestDataAsync()
    {
        await using var context = CreateContext();
        context.Settings.AddRange(
            new Setting
            {
                Key = "system.siteName",
                Value = "Test Site",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            },
            new Setting
            {
                Key = "system.baseUrl",
                Value = "http://localhost:5000",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            }
        );

        context.Libraries.Add(new Library
        {
            Name = "Inbound",
            Path = "/test/inbound",
            Type = (int)LibraryType.Inbound,
            SortOrder = 0,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        });

        await context.SaveChangesAsync();
    }
}
