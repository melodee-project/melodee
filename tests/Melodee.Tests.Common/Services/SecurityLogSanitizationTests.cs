using System.Reflection;
using System.Text;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Melodee.Common.Services.SearchEngines;
using Melodee.Tests.Common.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Common.Services;

public sealed class SecurityLogSanitizationTests : ServiceTestBase
{
    [Fact]
    public async Task DeviceIdentification_PlayerCreatedAndUpdated_SanitizesClientInLogs()
    {
        const string client = "trusted\r\nforged";
        const string deviceId = "device\u2028forged";
        var sink = new RecordingLogEventSink();
        using var logger = CreateLogger(sink);
        var service = new DeviceIdentificationService(logger, CacheManager, MockFactory());
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Emby-Client"] = client;
        httpContext.Request.Headers["X-Emby-Device-Id"] = deviceId;

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Users.Add(TestDataFactory.CreateTestUser());
            await context.SaveChangesAsync();
        }

        var userId = await GetFirstUserIdAsync();
        await service.GetOrCreatePlayerFromJellyfinAsync(userId, httpContext);
        await service.GetOrCreatePlayerFromJellyfinAsync(userId, httpContext);

        Assert.Contains("trusted[CR][LF]forged-device[LS]forged", sink.Output);
        Assert.DoesNotContain($"{client}-{deviceId}", sink.Output);
    }

    [Fact]
    public async Task PlaylistImport_UserProvidedName_SanitizesNameInLog()
    {
        const string playlistName = "Road Trip\r\nForged entry";
        var sink = new RecordingLogEventSink();
        using var logger = CreateLogger(sink);
        var service = new PlaylistImportService(
            logger,
            CacheManager,
            MockFactory(),
            new Serializer(logger));

        int userId;
        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            var user = TestDataFactory.CreateTestUser();
            var artist = TestDataFactory.CreateTestArtist();
            var album = TestDataFactory.CreateTestAlbum(artist);
            var song = TestDataFactory.CreateTestSong(album, "Song One", "song1.mp3", 1);
            context.AddRange(user, artist, album, song);
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        var result = await service.ImportPlaylistAsync(
            userId,
            "playlist.m3u",
            Encoding.UTF8.GetBytes("song1.mp3"),
            playlistName);

        Assert.True(result.IsSuccess);
        Assert.Contains("Road Trip[CR][LF]Forged entry", sink.Output);
        Assert.DoesNotContain(playlistName, sink.Output);
    }

    [Fact]
    public async Task ScriptAdmin_InvalidConfiguration_SanitizesSettingKeyInLog()
    {
        const string eventName = "albumAdded\r\nForged entry";
        var sink = new RecordingLogEventSink();
        using var logger = CreateLogger(sink);
        var settingService = new SettingService(
            logger,
            CacheManager,
            MockConfigurationFactory(),
            MockFactory());
        var service = new ScriptAdminService(settingService, new Serializer(logger), logger);

        await using (var context = await MockFactory().CreateDbContextAsync())
        {
            context.Settings.Add(new Setting
            {
                Key = $"script.{eventName}",
                Value = "{",
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            });
            await context.SaveChangesAsync();
        }

        var result = await service.GetAsync(eventName);

        Assert.NotNull(result);
        Assert.Contains("script.albumAdded[CR][LF]Forged entry", sink.Output);
        Assert.DoesNotContain($"script.{eventName}", sink.Output);
    }

    [Fact]
    public async Task SettingLookup_DatabaseFailure_SanitizesKeyInLog()
    {
        const string key = "system.setting\r\nForged entry";
        var sink = new RecordingLogEventSink();
        using var logger = CreateLogger(sink);
        var contextFactory = new Mock<IDbContextFactory<MelodeeDbContext>>();
        contextFactory
            .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));
        var service = new SettingService(
            logger,
            new FakeCacheManager(logger, TimeSpan.FromMinutes(1), new Serializer(logger)),
            MockConfigurationFactory(),
            contextFactory.Object);

        var result = await service.GetAsync(key);

        Assert.False(result.IsSuccess);
        Assert.Contains("system.setting[CR][LF]Forged entry", sink.Output);
        Assert.DoesNotContain(key, sink.Output);
    }

    [Fact]
    public async Task ArtistLookup_ProviderFailure_SanitizesProviderAndQueryInLog()
    {
        const string providerName = "Provider\r\nForged provider";
        const string artistName = "Artist\u2029Forged artist";
        var sink = new RecordingLogEventSink();
        using var logger = CreateLogger(sink);
        var service = new ArtistSearchEngineService(
            logger,
            CacheManager,
            MockSettingService(),
            MockSpotifyClientBuilder(),
            MockConfigurationFactory(),
            MockFactory(),
            MockArtistSearchEngineFactory(),
            MockMusicBrainzRepository(),
            new Serializer(logger),
            MockHttpClientFactory());
        await service.InitializeAsync();

        var plugin = new Mock<IArtistSearchEnginePlugin>();
        plugin.SetupGet(x => x.Id).Returns("failing-provider");
        plugin.SetupGet(x => x.DisplayName).Returns(providerName);
        plugin.SetupGet(x => x.IsEnabled).Returns(true);
        plugin.SetupGet(x => x.SortOrder).Returns(1);
        plugin
            .Setup(x => x.DoArtistSearchAsync(
                It.IsAny<ArtistQuery>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable"));

        var pluginsField = typeof(ArtistSearchEngineService).GetField(
            "_artistSearchEnginePlugins",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(pluginsField);
        pluginsField.SetValue(service, new[] { plugin.Object });

        var result = await service.LookupAsync(artistName, 10, null);

        Assert.True(result.HasPartialFailures);
        Assert.Contains("Provider[CR][LF]Forged provider", sink.Output);
        Assert.Contains("Artist[PS]Forged artist", sink.Output);
        Assert.DoesNotContain(providerName, sink.Output);
        Assert.DoesNotContain(artistName, sink.Output);
    }

    private async Task<int> GetFirstUserIdAsync()
    {
        await using var context = await MockFactory().CreateDbContextAsync();
        return await context.Users.Select(x => x.Id).FirstAsync();
    }

    private static Serilog.Core.Logger CreateLogger(RecordingLogEventSink sink)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
    }
}
