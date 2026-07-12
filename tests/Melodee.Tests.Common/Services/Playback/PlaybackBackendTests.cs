using System.Net;
using System.Net.Sockets;
using System.Text;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Services.Playback;
using Melodee.Common.Services.Playback.Backends;
using Melodee.Common.Services.Playback.Factory;
using Melodee.Tests.Common.TestHelpers;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.Playback;

public class PlaybackBackendFactoryTests
{
    private static IMelodeeConfigurationFactory CreateMockConfigurationFactory(
        string? mpvPath = null,
        string? mpvAudioDevice = null,
        string? mpvExtraArgs = null,
        string? mpvSocketPath = null,
        double mpvInitialVolume = 0.8,
        bool mpvEnableDebugOutput = false,
        string? mpdInstanceName = null,
        string mpdHost = "localhost",
        int mpdPort = 6600,
        string? mpdPassword = null,
        int mpdTimeoutMs = 10000,
        double mpdInitialVolume = 0.8,
        bool mpdEnableDebugOutput = false)
    {
        var mockConfig = new Mock<IMelodeeConfiguration>();

        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpvPath)).Returns(mpvPath);
        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpvAudioDevice)).Returns(mpvAudioDevice);
        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpvExtraArgs)).Returns(mpvExtraArgs);
        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpvSocketPath)).Returns(mpvSocketPath);
        mockConfig.Setup(x => x.GetValue<double>(SettingRegistry.MpvInitialVolume)).Returns(mpvInitialVolume);
        mockConfig.Setup(x => x.GetValue<bool>(SettingRegistry.MpvEnableDebugOutput)).Returns(mpvEnableDebugOutput);

        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpdInstanceName)).Returns(mpdInstanceName);
        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpdHost)).Returns(mpdHost);
        mockConfig.Setup(x => x.GetValue<int>(SettingRegistry.MpdPort)).Returns(mpdPort);
        mockConfig.Setup(x => x.GetValue<string>(SettingRegistry.MpdPassword)).Returns(mpdPassword);
        mockConfig.Setup(x => x.GetValue<int>(SettingRegistry.MpdTimeoutMs)).Returns(mpdTimeoutMs);
        mockConfig.Setup(x => x.GetValue<double>(SettingRegistry.MpdInitialVolume)).Returns(mpdInitialVolume);
        mockConfig.Setup(x => x.GetValue<bool>(SettingRegistry.MpdEnableDebugOutput)).Returns(mpdEnableDebugOutput);

        var mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        mockConfigFactory.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(mockConfig.Object);

        return mockConfigFactory.Object;
    }

    [Fact]
    public async Task CreateBackendAsync_WithNullBackendType_ReturnsNull()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync(null);

        Assert.Null(backend);
    }

    [Fact]
    public async Task CreateBackendAsync_WithEmptyBackendType_ReturnsNull()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync("");

        Assert.Null(backend);
    }

    [Fact]
    public async Task CreateBackendAsync_WithMpvType_CreatesMpvBackend()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync("mpv");

        Assert.NotNull(backend);
        Assert.IsType<MpvPlaybackBackend>(backend);
    }

    [Fact]
    public async Task CreateBackendAsync_WithMpvUppercase_CreatesMpvBackend()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync("MPV");

        Assert.NotNull(backend);
        Assert.IsType<MpvPlaybackBackend>(backend);
    }

    [Fact]
    public async Task CreateBackendAsync_WithMpdType_CreatesMpdBackend()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync("mpd");

        Assert.NotNull(backend);
        Assert.IsType<MpdPlaybackBackend>(backend);
    }

    [Fact]
    public async Task CreateBackendAsync_WithMpdUppercase_CreatesMpdBackend()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync("MPD");

        Assert.NotNull(backend);
        Assert.IsType<MpdPlaybackBackend>(backend);
    }

    [Fact]
    public async Task CreateBackendAsync_WithUnknownType_ReturnsNull()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.CreateBackendAsync("unknown");

        Assert.Null(backend);
    }

    [Fact]
    public async Task GetOrCreateMpvBackendAsync_CalledTwice_ReturnsSameInstance()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend1 = await factory.GetOrCreateMpvBackendAsync();
        var backend2 = await factory.GetOrCreateMpvBackendAsync();

        Assert.NotNull(backend1);
        Assert.NotNull(backend2);
        Assert.Same(backend1, backend2);
    }

    [Fact]
    public async Task GetOrCreateMpdBackendAsync_CalledTwice_ReturnsSameInstance()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend1 = await factory.GetOrCreateMpdBackendAsync();
        var backend2 = await factory.GetOrCreateMpdBackendAsync();

        Assert.NotNull(backend1);
        Assert.NotNull(backend2);
        Assert.Same(backend1, backend2);
    }

    [Fact]
    public async Task DisposeBackend_DisposesCachedBackend()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var configFactory = CreateMockConfigurationFactory();
        var factory = new PlaybackBackendFactory(logger, configFactory);

        var backend = await factory.GetOrCreateMpvBackendAsync();
        Assert.NotNull(backend);

        factory.DisposeBackend();
    }
}

public class BackendCapabilitiesTests
{
    [Fact]
    public void DefaultCapabilities_HaveExpectedValues()
    {
        var capabilities = new BackendCapabilities();

        Assert.True(capabilities.CanPlay);
        Assert.True(capabilities.CanPause);
        Assert.True(capabilities.CanStop);
        Assert.True(capabilities.CanSeek);
        Assert.True(capabilities.CanSkip);
        Assert.True(capabilities.CanSetVolume);
        Assert.True(capabilities.CanReportPosition);
        Assert.True(capabilities.IsAvailable);
        Assert.Null(capabilities.BackendInfo);
    }

    [Fact]
    public void Capabilities_CanBeSetWithInitializer()
    {
        var capabilities = new BackendCapabilities
        {
            CanPlay = false,
            CanPause = false,
            CanSeek = false,
            CanSkip = false,
            IsAvailable = false,
            BackendInfo = "Test Backend v1.0"
        };

        Assert.False(capabilities.CanPlay);
        Assert.False(capabilities.CanPause);
        Assert.False(capabilities.CanSeek);
        Assert.False(capabilities.CanSkip);
        Assert.False(capabilities.IsAvailable);
        Assert.Equal("Test Backend v1.0", capabilities.BackendInfo);
    }
}

public class BackendStatusTests
{
    [Fact]
    public void DefaultStatus_HasExpectedValues()
    {
        var status = new BackendStatus();

        Assert.False(status.IsPlaying);
        Assert.Equal(0, status.PositionSeconds);
        Assert.Null(status.Volume);
        Assert.Null(status.CurrentItemApiKey);
        Assert.False(status.IsConnected);
        Assert.Null(status.StatusMessage);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public void Status_CanBeSetWithInitializer()
    {
        var currentItemId = Guid.NewGuid();
        var status = new BackendStatus
        {
            IsPlaying = true,
            PositionSeconds = 120.5,
            Volume = 0.8,
            CurrentItemApiKey = currentItemId,
            IsConnected = true,
            StatusMessage = "Playing track",
            ErrorMessage = null
        };

        Assert.True(status.IsPlaying);
        Assert.Equal(120.5, status.PositionSeconds);
        Assert.Equal(0.8, status.Volume);
        Assert.Equal(currentItemId, status.CurrentItemApiKey);
        Assert.True(status.IsConnected);
        Assert.Equal("Playing track", status.StatusMessage);
        Assert.Null(status.ErrorMessage);
    }
}

public class MpdPlaybackBackendTests
{
    [Fact]
    public void MpdPlaybackBackend_CanBeCreated_WithValidParameters()
    {
        var logger = new LoggerConfiguration().CreateLogger();

        var backend = new MpdPlaybackBackend(
            logger,
            instanceName: null,
            host: "localhost",
            port: 6600,
            password: null,
            timeoutMs: 10000,
            initialVolume: 0.8,
            enableDebugOutput: false);

        Assert.NotNull(backend);
    }

    [Fact]
    public async Task MpdPlaybackBackend_GetCapabilities_ReturnsExpectedCapabilities()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpdPlaybackBackend(
            logger,
            instanceName: null,
            host: "localhost",
            port: 6600,
            password: null,
            timeoutMs: 10000,
            initialVolume: 0.8,
            enableDebugOutput: false);

        var capabilities = await backend.GetCapabilitiesAsync();

        Assert.True(capabilities.CanPlay);
        Assert.True(capabilities.CanPause);
        Assert.True(capabilities.CanStop);
        Assert.True(capabilities.CanSeek);
        Assert.True(capabilities.CanSkip);
        Assert.True(capabilities.CanSetVolume);
        Assert.True(capabilities.CanReportPosition);
        Assert.False(capabilities.IsAvailable);
        Assert.Null(capabilities.BackendInfo);
    }

    [Fact]
    public async Task MpdPlaybackBackend_GetStatus_ReturnsDisconnectedStatus_WhenNotInitialized()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpdPlaybackBackend(
            logger,
            instanceName: null,
            host: "localhost",
            port: 6600,
            password: null,
            timeoutMs: 10000,
            initialVolume: 0.8,
            enableDebugOutput: false);

        var status = await backend.GetStatusAsync();

        Assert.False(status.IsConnected);
        Assert.False(status.IsPlaying);
        Assert.Equal(0, status.PositionSeconds);
        Assert.Equal(0.8, status.Volume);
        Assert.Equal("Not connected to MPD", status.StatusMessage);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task MpdPlaybackBackend_Shutdown_DoesNotThrow_WhenNotInitialized()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpdPlaybackBackend(
            logger,
            instanceName: null,
            host: "localhost",
            port: 6600,
            password: null,
            timeoutMs: 10000,
            initialVolume: 0.8,
            enableDebugOutput: false);

        await backend.ShutdownAsync();
    }

    [Fact]
    public void MpdPlaybackBackend_Dispose_DoesNotThrow()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpdPlaybackBackend(
            logger,
            instanceName: null,
            host: "localhost",
            port: 6600,
            password: null,
            timeoutMs: 10000,
            initialVolume: 0.8,
            enableDebugOutput: false);

        backend.Dispose();
    }

    [Fact]
    public async Task MpdPlaybackBackend_InitializeWithPassword_SendsPasswordWithoutLoggingIt()
    {
        const string password = "never-log-this-password-a31f0ec8";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                await stream.WriteAsync(Encoding.UTF8.GetBytes("OK MPD 0.24.0\n"));

                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                var command = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await stream.WriteAsync(Encoding.UTF8.GetBytes("OK\n"));
                return command;
            });

            var sink = new RecordingLogEventSink();
            using var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            using var backend = new MpdPlaybackBackend(
                logger,
                instanceName: null,
                host: IPAddress.Loopback.ToString(),
                port: port,
                password: password,
                timeoutMs: 5000,
                initialVolume: 0.8,
                enableDebugOutput: true);

            await backend.InitializeAsync();
            var receivedCommand = await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal($"password {password}", receivedCommand);
            Assert.Contains("Authenticated with MPD", sink.Output);
            Assert.DoesNotContain(password, sink.Output);
        }
        finally
        {
            listener.Stop();
        }
    }
}

public class MpvPlaybackBackendTests
{
    [Fact]
    public void MpvPlaybackBackend_CanBeCreated_WithValidParameters()
    {
        var logger = new LoggerConfiguration().CreateLogger();

        var backend = new MpvPlaybackBackend(
            logger,
            mpvPath: null,
            audioDevice: null,
            extraArgs: null,
            socketPath: null,
            initialVolume: 0.8,
            enableDebugOutput: false);

        Assert.NotNull(backend);
    }

    [Fact]
    public async Task MpvPlaybackBackend_GetCapabilities_ReturnsExpectedCapabilities()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpvPlaybackBackend(
            logger,
            mpvPath: null,
            audioDevice: null,
            extraArgs: null,
            socketPath: null,
            initialVolume: 0.8,
            enableDebugOutput: false);

        var capabilities = await backend.GetCapabilitiesAsync();

        Assert.True(capabilities.CanPlay);
        Assert.True(capabilities.CanPause);
        Assert.True(capabilities.CanStop);
        Assert.True(capabilities.CanSeek);
        Assert.True(capabilities.CanSkip);
        Assert.True(capabilities.CanSetVolume);
        Assert.True(capabilities.CanReportPosition);
        Assert.False(capabilities.IsAvailable);
        Assert.Null(capabilities.BackendInfo);
    }

    [Fact]
    public async Task MpvPlaybackBackend_GetStatus_ReturnsDisconnectedStatus_WhenNotInitialized()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpvPlaybackBackend(
            logger,
            mpvPath: null,
            audioDevice: null,
            extraArgs: null,
            socketPath: null,
            initialVolume: 0.8,
            enableDebugOutput: false);

        var status = await backend.GetStatusAsync();

        Assert.False(status.IsConnected);
        Assert.False(status.IsPlaying);
        Assert.Equal(0, status.PositionSeconds);
        Assert.Equal(0.8, status.Volume);
        Assert.Equal("MPV process not running or IPC not connected", status.StatusMessage);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task MpvPlaybackBackend_Shutdown_DoesNotThrow_WhenNotInitialized()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpvPlaybackBackend(
            logger,
            mpvPath: null,
            audioDevice: null,
            extraArgs: null,
            socketPath: null,
            initialVolume: 0.8,
            enableDebugOutput: false);

        await backend.ShutdownAsync();
    }

    [Fact]
    public void MpvPlaybackBackend_Dispose_DoesNotThrow()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var backend = new MpvPlaybackBackend(
            logger,
            mpvPath: null,
            audioDevice: null,
            extraArgs: null,
            socketPath: null,
            initialVolume: 0.8,
            enableDebugOutput: false);

        backend.Dispose();
    }
}
