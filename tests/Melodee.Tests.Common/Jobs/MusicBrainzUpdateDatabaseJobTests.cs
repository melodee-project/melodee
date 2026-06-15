using System.Net;
using DecentDB.AdoNet;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Jobs;
using Melodee.Common.Models;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services;
using Melodee.Tests.Common.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Melodee.Tests.Common.Jobs;

public class MusicBrainzUpdateDatabaseJobTests : ServiceTestBase
{
    [Fact]
    public async Task Execute_WhenImportIsCancelled_RestoresDatabaseAndRemovesLockFile()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-job-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-job-db-{Guid.NewGuid():N}.ddb");
        Directory.CreateDirectory(storagePath);

        try
        {
            const string latestVersion = "20260418-002325";
            await SeedPreparedStorageAsync(storagePath, latestVersion);

            var configurationFactory = CreateConfigurationFactory(storagePath);
            var settingService = new SettingService(Logger, CacheManager, configurationFactory, MockFactory());
            var musicBrainzDbContextFactory = CreateMusicBrainzDbContextFactory(databasePath);
            var warmupService = new MusicBrainzDecentDbWarmupService(Logger, musicBrainzDbContextFactory);
            var repositoryMock = new Mock<IMusicBrainzRepository>();
            MusicBrainzImportRequest? capturedRequest = null;
            repositoryMock.Setup(repository => repository.ImportData(
                    It.IsAny<MusicBrainzImportRequest>(),
                    It.IsAny<ImportProgressCallback?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<MusicBrainzImportRequest, ImportProgressCallback?, CancellationToken>((request, _, _) =>
                {
                    capturedRequest = request;
                })
                .ThrowsAsync(new OperationCanceledException());

            var job = new MusicBrainzUpdateDatabaseJob(
                Logger,
                configurationFactory,
                settingService,
                CreateHttpClientFactory(latestVersion),
                musicBrainzDbContextFactory,
                repositoryMock.Object,
                warmupService);
            var context = new MelodeeJobExecutionContext(CancellationToken.None);

            File.Exists(databasePath).Should().BeTrue();

            await job.Execute(context);

            context.JobResult.Should().NotBeNull();
            context.JobResult!.Status.Should().Be(JobResultStatus.Failed);
            context.JobResult.Message.ToLowerInvariant().Should().Contain("cancelled");
            File.Exists(Path.Combine(storagePath, "MusicBrainzUpdateDatabaseJob.lock")).Should().BeFalse();
            File.Exists(databasePath).Should().BeTrue();
            Directory.EnumerateFiles(storagePath, "*.db", SearchOption.TopDirectoryOnly).Should().BeEmpty();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.StoragePath.Should().Be(storagePath);
            capturedRequest.TargetDatabasePath.Should().NotBeNullOrWhiteSpace();
            capturedRequest.TargetDatabasePath.Should().NotBe(databasePath);
        }
        finally
        {
            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
                if (File.Exists($"{databasePath}.wal"))
                {
                    File.Delete($"{databasePath}.wal");
                }
                if (File.Exists($"{databasePath}.shm"))
                {
                    File.Delete($"{databasePath}.shm");
                }

                if (Directory.Exists(storagePath))
                {
                    Directory.Delete(storagePath, true);
                }
            }
            catch
            {
                // Best effort cleanup for test temp files.
            }
        }
    }

    [Fact]
    public async Task Execute_WhenRepositoryReturnsFailure_IncludesMeaningfulErrorMessage()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-job-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-job-db-{Guid.NewGuid():N}.ddb");
        Directory.CreateDirectory(storagePath);

        try
        {
            const string latestVersion = "20260418-002325";
            await SeedPreparedStorageAsync(storagePath, latestVersion);

            var configurationFactory = CreateConfigurationFactory(storagePath);
            var settingService = new SettingService(Logger, CacheManager, configurationFactory, MockFactory());
            var musicBrainzDbContextFactory = CreateMusicBrainzDbContextFactory(databasePath);
            var warmupService = new MusicBrainzDecentDbWarmupService(Logger, musicBrainzDbContextFactory);
            var repositoryMock = new Mock<IMusicBrainzRepository>();
            repositoryMock.Setup(repository => repository.ImportData(
                    It.IsAny<MusicBrainzImportRequest>(),
                    It.IsAny<ImportProgressCallback?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OperationResult<bool>
                {
                    Data = false,
                    Type = OperationResponseType.Error,
                    Errors = [new InvalidOperationException("Artist materialization failed in a test scenario.")]
                });

            var job = new MusicBrainzUpdateDatabaseJob(
                Logger,
                configurationFactory,
                settingService,
                CreateHttpClientFactory(latestVersion),
                musicBrainzDbContextFactory,
                repositoryMock.Object,
                warmupService);
            var context = new MelodeeJobExecutionContext(CancellationToken.None);

            await job.Execute(context);

            context.JobResult.Should().NotBeNull();
            context.JobResult!.Status.Should().Be(JobResultStatus.Failed);
            context.JobResult.Message.Should().Contain("Artist materialization failed in a test scenario.");
        }
        finally
        {
            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
                if (File.Exists($"{databasePath}.wal"))
                {
                    File.Delete($"{databasePath}.wal");
                }
                if (File.Exists($"{databasePath}.shm"))
                {
                    File.Delete($"{databasePath}.shm");
                }

                if (Directory.Exists(storagePath))
                {
                    Directory.Delete(storagePath, true);
                }
            }
            catch
            {
                // Best effort cleanup for test temp files.
            }
        }
    }

    [Fact]
    public async Task Execute_WhenImportSucceeds_CheckpointsImportedDatabaseBeforePromotion()
    {
        var storagePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-job-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-job-db-{Guid.NewGuid():N}.ddb");
        Directory.CreateDirectory(storagePath);

        try
        {
            const string latestVersion = "20260418-002325";
            await SeedPreparedStorageAsync(storagePath, latestVersion);

            var configurationFactory = CreateConfigurationFactory(storagePath);
            var settingService = new SettingService(Logger, CacheManager, configurationFactory, MockFactory());
            var musicBrainzDbContextFactory = CreateMusicBrainzDbContextFactory(databasePath);
            var warmupService = new MusicBrainzDecentDbWarmupService(Logger, musicBrainzDbContextFactory);
            var repositoryMock = new Mock<IMusicBrainzRepository>();
            MusicBrainzImportRequest? capturedRequest = null;
            repositoryMock.Setup(repository => repository.ImportData(
                    It.IsAny<MusicBrainzImportRequest>(),
                    It.IsAny<ImportProgressCallback?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<MusicBrainzImportRequest, ImportProgressCallback?, CancellationToken>((request, _, _) =>
                {
                    capturedRequest = request;
                    SeedImportedDecentDb(request.TargetDatabasePath!);
                })
                .ReturnsAsync(new OperationResult<bool>
                {
                    Data = true
                });

            var job = new MusicBrainzUpdateDatabaseJob(
                Logger,
                configurationFactory,
                settingService,
                CreateHttpClientFactory(latestVersion),
                musicBrainzDbContextFactory,
                repositoryMock.Object,
                warmupService);
            var context = new MelodeeJobExecutionContext(CancellationToken.None);

            await job.Execute(context);

            context.JobResult.Should().NotBeNull();
            context.JobResult!.Status.Should().Be(JobResultStatus.Success);
            capturedRequest.Should().NotBeNull();
            File.Exists(databasePath).Should().BeTrue();
            CountCheckpointProbeRows(databasePath).Should().Be(1);
            File.Exists(capturedRequest.TargetDatabasePath!).Should().BeFalse();
        }
        finally
        {
            try
            {
                DeleteDatabaseArtifacts(databasePath);

                if (Directory.Exists(storagePath))
                {
                    Directory.Delete(storagePath, true);
                }
            }
            catch
            {
                // Best effort cleanup for test temp files.
            }
        }
    }

    private IMelodeeConfigurationFactory CreateConfigurationFactory(string storagePath)
    {
        var settings = TestsBase.NewConfiguration();
        settings[SettingRegistry.SearchEngineMusicBrainzEnabled] = "true";
        settings[SettingRegistry.SearchEngineMusicBrainzStoragePath] = storagePath;

        var configurationFactory = new Mock<IMelodeeConfigurationFactory>();
        configurationFactory
            .Setup(factory => factory.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MelodeeConfiguration(settings));
        return configurationFactory.Object;
    }

    private static IHttpClientFactory CreateHttpClientFactory(string latestVersion)
    {
        var handler = new StaticResponseHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(latestVersion)
            });
        var httpClient = new HttpClient(handler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return httpClientFactory.Object;
    }

    private static IDbContextFactory<MusicBrainzDbContext> CreateMusicBrainzDbContextFactory(string databasePath)
    {
        var dbContextOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={databasePath}")
            .Options;

        using (var context = new MusicBrainzDbContext(dbContextOptions))
        {
            context.Database.EnsureCreated();
            context.SaveChanges();
        }

        var dbContextFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        dbContextFactory.Setup(factory => factory.CreateDbContext())
            .Returns(() => new MusicBrainzDbContext(dbContextOptions));
        dbContextFactory.Setup(factory => factory.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MusicBrainzDbContext(dbContextOptions));
        return dbContextFactory.Object;
    }

    private static async Task SeedPreparedStorageAsync(string storagePath, string latestVersion)
    {
        var stagingPath = Path.Combine(storagePath, "staging");
        var mbDumpPath = Path.Combine(stagingPath, "mbdump");
        Directory.CreateDirectory(mbDumpPath);
        await File.WriteAllTextAsync(Path.Combine(stagingPath, "VERSION"), latestVersion);
        await File.WriteAllTextAsync(Path.Combine(mbDumpPath, "artist"),
            "1\t11111111-1111-1111-1111-111111111111\tExample Artist\tArtist, Example");
        foreach (var fileName in new[]
                 {
                     "artist_alias",
                     "link",
                     "l_artist_artist",
                     "artist_credit",
                     "artist_credit_name",
                     "release_country",
                     "release_group",
                     "release_group_meta",
                     "release"
                 })
        {
            await File.WriteAllTextAsync(Path.Combine(mbDumpPath, fileName), string.Empty);
        }
        await File.WriteAllBytesAsync(Path.Combine(stagingPath, "mbdump.tar.bz2"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(stagingPath, "mbdump-derived.tar.bz2"), [1]);
    }

    private static void SeedImportedDecentDb(string databasePath)
    {
        using var connection = new DecentDBConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE checkpoint_probe (
                                  id INTEGER PRIMARY KEY,
                                  name TEXT NOT NULL
                              );
                              INSERT INTO checkpoint_probe (id, name) VALUES (1, 'imported');
                              """;
        command.ExecuteNonQuery();
    }

    private static long CountCheckpointProbeRows(string databasePath)
    {
        using var connection = new DecentDBConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM checkpoint_probe";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void DeleteDatabaseArtifacts(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     $"{databasePath}.wal",
                     $"{databasePath}-wal",
                     $"{databasePath}.shm",
                     $"{databasePath}-shm",
                     $"{databasePath}.coord"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class StaticResponseHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
