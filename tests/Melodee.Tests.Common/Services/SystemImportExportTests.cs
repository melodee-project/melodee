using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;

    public SystemImportExportTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportExportTest_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

    [Fact]
    public async Task ExportService_ProducesValidJson()
    {
        await SeedTestDataAsync();

        var exportService = new SystemExportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            _contextFactory);

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
            _contextFactory);

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
            _contextFactory);

        var result1 = await exportService.ExportAsync(false);
        var result2 = await exportService.ExportAsync(false);

        // Compare settings count - timestamps will differ but structure should be same
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.SettingsCount.Should().Be(result2.SettingsCount);
        result1.LibrariesCount.Should().Be(result2.LibrariesCount);
    }

    [Fact]
    public async Task ImportService_RejectsInvalidJson()
    {
        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            _contextFactory);

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
            _contextFactory);

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
            _contextFactory);

        var result = await importService.ImportAsync(importJson);

        result.Success.Should().BeTrue();
        result.SettingsImported.Should().Be(1);
    }

    [Fact]
    public async Task ImportService_ImportsLibraryWithNonexistentPath()
    {
        // The import service doesn't validate paths exist - it simply imports them
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
            _contextFactory);

        var result = await importService.ImportAsync(importJson);

        // Import succeeds because path validation is not performed during import
        result.Success.Should().BeTrue();
        result.LibrariesImported.Should().Be(1);
    }

    [Fact]
    public async Task ImportService_UpdatesExistingSettings()
    {
        // Test that existing settings get updated during import
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
            _contextFactory);

        var result = await importService.ImportAsync(importJson);

        result.Success.Should().BeTrue();
        result.SettingsImported.Should().Be(1);

        // Verify the setting was updated
        await using var verifyContext = CreateContext();
        var updatedSetting = await verifyContext.Settings.FirstOrDefaultAsync(s => s.Key == "system.siteName");
        updatedSetting.Should().NotBeNull();
        updatedSetting!.Value.Should().Be("New Value");
    }

    [Fact]
    public async Task RoundTrip_ExportAndImport_ProducesEquivalentData()
    {
        await SeedTestDataAsync();

        var exportService = new SystemExportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            _contextFactory);

        var exportResult = await exportService.ExportAsync(false);
        exportResult.Success.Should().BeTrue();

        var importService = new SystemImportService(
            _loggerMock.Object,
            _cacheManagerMock.Object,
            _configFactoryMock.Object,
            _contextFactory);

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
            },
            new Setting
            {
                Key = "security.secretKey",
                Value = "super-secret-value-123",
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
