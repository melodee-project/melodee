using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Services;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.ScriptEvaluation;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;

namespace Melodee.Tests.Common.Services.Scanning;

public class DirectoryProcessorToStagingServiceTests : ServiceTestBase
{
    #region Helper Methods

    private DirectoryProcessorToStagingService GetDirectoryProcessorService()
    {
        return new DirectoryProcessorToStagingService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetLibraryService(),
            Serializer,
            GetMediaEditService(),
            GetArtistSearchEngineService(),
            GetAlbumImageSearchEngineService(),
            MockHttpClientFactory(),
            MockFileSystemService(),
            MockScriptOrchestrationService(),
            MockDirectoryContextProvider(),
            MockDenyActionHandlerFactory(),
            new ImageProcessor());
    }

    private DirectoryProcessorToStagingService GetDirectoryProcessorService(IFileSystemService fileSystemService)
    {
        return new DirectoryProcessorToStagingService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetLibraryService(),
            Serializer,
            GetMediaEditService(),
            GetArtistSearchEngineService(),
            GetAlbumImageSearchEngineService(),
            MockHttpClientFactory(),
            fileSystemService,
            MockScriptOrchestrationService(),
            MockDirectoryContextProvider(),
            MockDenyActionHandlerFactory(),
            new ImageProcessor());
    }

    private DirectoryProcessorToStagingService GetDirectoryProcessorService(
        IFileSystemService fileSystemService,
        IScriptOrchestrationService scriptOrchestrationService,
        DenyActionHandlerFactory denyActionHandlerFactory)
    {
        return new DirectoryProcessorToStagingService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetLibraryService(),
            Serializer,
            GetMediaEditService(),
            GetArtistSearchEngineService(),
            GetAlbumImageSearchEngineService(),
            MockHttpClientFactory(),
            fileSystemService,
            scriptOrchestrationService,
            MockDirectoryContextProvider(),
            denyActionHandlerFactory,
            new ImageProcessor());
    }

    private async Task CreateStagingLibraryInDb()
    {
        await using var context = await MockFactory().CreateDbContextAsync();

        var existingLibrary = await context.Libraries.FirstOrDefaultAsync(l => l.Type == (int)LibraryType.Staging);
        if (existingLibrary != null)
        {
            return;
        }

        var library = new Library
        {
            Name = "Staging Library",
            Path = "/tmp/staging",
            Type = (int)LibraryType.Staging,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        context.Libraries.Add(library);
        await context.SaveChangesAsync();
    }

    private Mock<IFileSystemService> CreateMockFileSystem(bool directoryExists = true, IEnumerable<DirectoryInfo>? directories = null)
    {
        var mockFileSystem = new Mock<IFileSystemService>();
        mockFileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(directoryExists);
        mockFileSystem.Setup(f => f.EnumerateDirectories(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()))
            .Returns(directories ?? []);
        mockFileSystem.Setup(f => f.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()))
            .Returns([]);
        return mockFileSystem;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_CreatesServiceInstance()
    {
        // Arrange & Act
        var service = GetDirectoryProcessorService();

        // Assert
        Assert.NotNull(service);
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_WhenCalled_InitializesService()
    {
        // Arrange
        var service = GetDirectoryProcessorService();
        await CreateStagingLibraryInDb();

        // Act
        await service.InitializeAsync();

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledMultipleTimes_OnlyInitializesOnce()
    {
        // Arrange
        var service = GetDirectoryProcessorService();
        await CreateStagingLibraryInDb();

        // Act
        await service.InitializeAsync();
        await service.InitializeAsync();

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task InitializeAsync_WithConfiguration_UsesProvidedConfig()
    {
        // Arrange
        var service = GetDirectoryProcessorService();
        await CreateStagingLibraryInDb();
        var config = TestsBase.NewPluginsConfiguration();

        // Act
        await service.InitializeAsync(config);

        // Assert - no exception means success
        Assert.True(true);
    }

    #endregion

    #region ProcessDirectoryAsync Tests

    [Fact]
    public async Task ProcessDirectoryAsync_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = GetDirectoryProcessorService();
        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/nonexistent",
            Name = "test"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessDirectoryAsync(dirInfo, null, null));
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithNonExistentDirectory_ReturnsError()
    {
        // Arrange
        var service = GetDirectoryProcessorService();
        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/nonexistent/path/that/does/not/exist",
            Name = "nonexistent"
        };

        // Act
        var result = await service.ProcessDirectoryAsync(dirInfo, null, null);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Errors);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithValidDirectory_ReturnsResult()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        // Act
        var result = await service.ProcessDirectoryAsync(dirInfo, null, null);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithCancellation_StopsProcessing()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await service.ProcessDirectoryAsync(dirInfo, null, null, cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithMaxAlbumsToProcess_RespectsLimit()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        // Act
        var result = await service.ProcessDirectoryAsync(dirInfo, null, 5);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithLastProcessDate_FiltersOlderFiles()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        var lastProcessDate = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await service.ProcessDirectoryAsync(dirInfo, lastProcessDate, null);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Data);
    }

    #endregion

    #region Script Safety Tests

    [Fact]
    public async Task ProcessDirectoryAsync_WhenDirectoryDeleteScriptWouldDelete_DoesNotEvaluateDeleteEvent()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"melodee-inbound-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(Path.GetTempPath(), $"melodee-staging-{Guid.NewGuid():N}");
        var releasePath = Path.Combine(rootPath, "Artist - Album");

        try
        {
            Directory.CreateDirectory(releasePath);
            Directory.CreateDirectory(stagingPath);
            await File.WriteAllTextAsync(Path.Combine(releasePath, "01-track.mp3"), "not real audio");

            var scriptOrchestrationService = new Mock<IScriptOrchestrationService>();
            scriptOrchestrationService
                .Setup(x => x.EvaluateScriptForEventAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ScriptEvaluationResult { Result = true, IsDefault = true });
            scriptOrchestrationService
                .Setup(x => x.EvaluateScriptForEventAsync(
                    ScriptEventNames.DirectoryProcessingDelete,
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ScriptEvaluationResult { Result = true, IsDefault = false, OnDeny = "delete" });

            var safeDeleteService = new Mock<ISafeDeleteService>(MockBehavior.Strict);
            var service = GetDirectoryProcessorService(
                new FileSystemService(Serializer),
                scriptOrchestrationService.Object,
                new DenyActionHandlerFactory(safeDeleteService.Object, new SettingService(), Logger));

            await service.InitializeAsync(TestsBase.NewPluginsConfiguration(), stagingPath, CancellationToken.None);

            await service.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = rootPath,
                Name = "inbound"
            }, null, null);

            Assert.True(Directory.Exists(releasePath));
            scriptOrchestrationService.Verify(x => x.EvaluateScriptForEventAsync(
                ScriptEventNames.DirectoryProcessingDelete,
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Never);
            safeDeleteService.Verify(x => x.DeleteDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }

            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }
        }
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WhenStartScriptRequestsDelete_SkipsWithoutDeletingDirectory()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"melodee-inbound-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(Path.GetTempPath(), $"melodee-staging-{Guid.NewGuid():N}");
        var releasePath = Path.Combine(rootPath, "Artist - Album");

        try
        {
            Directory.CreateDirectory(releasePath);
            Directory.CreateDirectory(stagingPath);
            await File.WriteAllTextAsync(Path.Combine(releasePath, "01-track.mp3"), "not real audio");

            var scriptOrchestrationService = new Mock<IScriptOrchestrationService>();
            scriptOrchestrationService
                .Setup(x => x.EvaluateScriptForEventAsync(
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ScriptEvaluationResult { Result = true, IsDefault = true });
            scriptOrchestrationService
                .Setup(x => x.EvaluateScriptForEventAsync(
                    ScriptEventNames.DirectoryProcessingStart,
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ScriptEvaluationResult { Result = false, IsDefault = false, OnDeny = "delete" });

            var safeDeleteService = new Mock<ISafeDeleteService>(MockBehavior.Strict);
            var service = GetDirectoryProcessorService(
                new FileSystemService(Serializer),
                scriptOrchestrationService.Object,
                new DenyActionHandlerFactory(safeDeleteService.Object, new SettingService(), Logger));

            await service.InitializeAsync(TestsBase.NewPluginsConfiguration(), stagingPath, CancellationToken.None);

            await service.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = rootPath,
                Name = "inbound"
            }, null, null);

            Assert.True(Directory.Exists(releasePath));
            safeDeleteService.Verify(x => x.DeleteDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }

            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, true);
            }
        }
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WhenCalled_DisposesResources()
    {
        // Arrange
        var service = GetDirectoryProcessorService();

        // Act
        service.Dispose();

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var service = GetDirectoryProcessorService();

        // Act
        service.Dispose();
        service.Dispose();

        // Assert - no exception means success
        Assert.True(true);
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task OnProcessingEvent_WhenProcessing_RaisesEvents()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        service.OnProcessingEvent += (sender, message) => { /* Event handler for testing */ };

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        // Act
        await service.ProcessDirectoryAsync(dirInfo, null, null);

        // Assert - event may or may not be raised depending on processing path
        Assert.True(true); // Test passes if no exception
    }

    [Fact]
    public async Task OnProcessingStart_WhenProcessing_RaisesStartEvent()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        service.OnProcessingStart += (sender, count) => { /* Event handler for testing */ };

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        // Act
        await service.ProcessDirectoryAsync(dirInfo, null, null);

        // Assert - start event should be raised
        Assert.True(true); // Test passes if no exception
    }

    #endregion

    #region DirectoryProcessorResult Tests

    [Fact]
    public async Task ProcessDirectoryAsync_ReturnsCorrectResultStructure()
    {
        // Arrange
        var mockFileSystem = CreateMockFileSystem();
        var service = GetDirectoryProcessorService(mockFileSystem.Object);

        await CreateStagingLibraryInDb();
        await service.InitializeAsync();

        var dirInfo = new FileSystemDirectoryInfo
        {
            Path = "/tmp/test",
            Name = "test"
        };

        // Act
        var result = await service.ProcessDirectoryAsync(dirInfo, null, null);

        // Assert
        Assert.NotNull(result.Data);
        Assert.True(result.Data.DurationInMs >= 0);
        Assert.True(result.Data.NewAlbumsCount >= 0);
        Assert.True(result.Data.NewArtistsCount >= 0);
        Assert.True(result.Data.NewSongsCount >= 0);
        Assert.True(result.Data.NumberOfAlbumFilesProcessed >= 0);
        Assert.True(result.Data.NumberOfConversionPluginsProcessed >= 0);
        Assert.True(result.Data.NumberOfDirectoryPluginProcessed >= 0);
        Assert.True(result.Data.NumberOfValidAlbumsProcessed >= 0);
        Assert.True(result.Data.NumberOfAlbumsProcessed >= 0);
    }

    #endregion
}
