using System.Net.Sockets;
using System.Text;
using Melodee.Common.Data.Models;
using Melodee.Common.Utility;
using Serilog;

namespace Melodee.Common.Services.Playback.Backends;

/// <summary>
/// MPD (Music Player Daemon) playback backend implementation using TCP socket communication.
/// </summary>
public sealed class MpdPlaybackBackend : IPlaybackBackend, IDisposable
{
    private readonly ILogger _logger;
    private readonly string? _instanceName;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _password;
    private readonly int _timeoutMs;
    private readonly double _initialVolume;
    private readonly bool _enableDebugOutput;

    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private bool _disposed;
    private bool _isInitialized;
    private readonly object _lock = new();
    private string? _mpdVersion;

    public MpdPlaybackBackend(
        ILogger logger,
        string? instanceName,
        string host,
        int port,
        string? password,
        int timeoutMs,
        double initialVolume,
        bool enableDebugOutput)
    {
        _logger = logger;
        _instanceName = instanceName;
        _host = host;
        _port = port;
        _password = password;
        _timeoutMs = timeoutMs;
        _initialVolume = initialVolume > 0 ? initialVolume : 0.8;
        _enableDebugOutput = enableDebugOutput;
    }

    /// <inheritdoc />
    public Task<BackendCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BackendCapabilities
        {
            CanPlay = true,
            CanPause = true,
            CanStop = true,
            CanSeek = true,
            CanSkip = true,
            CanSetVolume = true,
            CanReportPosition = true,
            IsAvailable = IsConnected(),
            BackendInfo = _mpdVersion
        });
    }

    /// <inheritdoc />
    public Task PlayAsync(PartyQueueItem item, double startPositionSeconds = 0, CancellationToken cancellationToken = default)
    {
        // MPD uses playlist indices - the SortOrder maps to the queue position
        return ExecuteCommandAsync($"play {item.SortOrder}", cancellationToken);
    }

    /// <inheritdoc />
    public async Task PlayFileAsync(string filePath, Guid songApiKey, double startPositionSeconds = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            _logger.Warning("[MpdPlaybackBackend] Cannot play empty file path");
            return;
        }

        _logger.Information("[MpdPlaybackBackend] Adding and playing file: {FilePath} (SongApiKey: {SongApiKey})", filePath, songApiKey);

        // Clear playlist and add the file, then play
        await ExecuteCommandAsync("clear", cancellationToken).ConfigureAwait(false);
        await ExecuteCommandAsync($"add \"{filePath}\"", cancellationToken).ConfigureAwait(false);
        await ExecuteCommandAsync("play", cancellationToken).ConfigureAwait(false);

        if (startPositionSeconds > 0)
        {
            await SeekAsync(startPositionSeconds, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync("pause 1", cancellationToken);
    }

    /// <inheritdoc />
    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync("pause 0", cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync("stop", cancellationToken);
    }

    /// <inheritdoc />
    public Task SeekAsync(double positionSeconds, CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync($"seekcur {positionSeconds:F3}", cancellationToken);
    }

    /// <inheritdoc />
    public Task SetVolumeAsync(double volume01, CancellationToken cancellationToken = default)
    {
        var volumePercent = (int)(volume01 * 100);
        return ExecuteCommandAsync($"setvol {volumePercent}", cancellationToken);
    }

    /// <inheritdoc />
    public Task SkipNextAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync("next", cancellationToken);
    }

    /// <inheritdoc />
    public Task SkipPreviousAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync("previous", cancellationToken);
    }

    /// <inheritdoc />
    public Task<BackendStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var isConnected = IsConnected();

        if (!isConnected)
        {
            return Task.FromResult(new BackendStatus
            {
                IsPlaying = false,
                PositionSeconds = 0,
                Volume = _initialVolume,
                CurrentItemApiKey = null,
                IsConnected = false,
                StatusMessage = "Not connected to MPD",
                ErrorMessage = null
            });
        }

        try
        {
            var statusResponse = ExecuteCommand("status", out var isError);
            if (isError || string.IsNullOrEmpty(statusResponse))
            {
                return Task.FromResult(new BackendStatus
                {
                    IsPlaying = false,
                    PositionSeconds = 0,
                    Volume = _initialVolume,
                    CurrentItemApiKey = null,
                    IsConnected = true,
                    StatusMessage = "Error getting status",
                    ErrorMessage = null
                });
            }

            var statusLines = statusResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var isPlaying = false;
            var positionSeconds = 0.0;
            var volume = _initialVolume;
            Guid? currentItemApiKey = null;

            foreach (var line in statusLines)
            {
                if (line.StartsWith("state: "))
                {
                    var state = line["state: ".Length..].Trim();
                    isPlaying = state.Equals("play", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("time: "))
                {
                    var timePart = line["time: ".Length..].Trim();
                    var parts = timePart.Split(':');
                    if (parts.Length >= 2 && double.TryParse(parts[0], out var position))
                    {
                        positionSeconds = position;
                    }
                }
                else if (line.StartsWith("volume: "))
                {
                    var volumePart = line["volume: ".Length..].Trim();
                    if (int.TryParse(volumePart, out var vol) && vol >= 0)
                    {
                        volume = vol / 100.0;
                    }
                }
                else if (line.StartsWith("songid: "))
                {
                    var songId = line["songid: ".Length..].Trim();
                    if (Guid.TryParse(songId, out var apiKey))
                    {
                        currentItemApiKey = apiKey;
                    }
                }
            }

            return Task.FromResult(new BackendStatus
            {
                IsPlaying = isPlaying,
                PositionSeconds = positionSeconds,
                Volume = volume,
                CurrentItemApiKey = currentItemApiKey,
                IsConnected = true,
                StatusMessage = isPlaying ? "Playing" : "Connected",
                ErrorMessage = null
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[MpdPlaybackBackend] Error getting MPD status");
            return Task.FromResult(new BackendStatus
            {
                IsPlaying = false,
                PositionSeconds = 0,
                Volume = _initialVolume,
                CurrentItemApiKey = null,
                IsConnected = false,
                StatusMessage = "Error",
                ErrorMessage = "Failed to get MPD status"
            });
        }
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return Task.CompletedTask;
        }

        lock (_lock)
        {
            if (_isInitialized)
            {
                return Task.CompletedTask;
            }

            try
            {
                _logger.Information("[MpdPlaybackBackend] Connecting to MPD at {Host}:{Port}",
                    _host, _port);

                _tcpClient = new TcpClient
                {
                    ReceiveTimeout = _timeoutMs,
                    SendTimeout = _timeoutMs
                };

                _tcpClient.Connect(_host, _port);
                _networkStream = _tcpClient.GetStream();

                var welcomeResponse = ReadResponse();
                if (welcomeResponse.StartsWith("OK MPD "))
                {
                    _mpdVersion = welcomeResponse["OK MPD ".Length..].Trim();
                    _logger.Information("[MpdPlaybackBackend] Connected to MPD version: {Version}", LogSanitizer.Sanitize(_mpdVersion));
                }
                else
                {
                    _logger.Warning("[MpdPlaybackBackend] Unexpected MPD welcome response: {Response}", LogSanitizer.Sanitize(welcomeResponse));
                }

                if (!string.IsNullOrEmpty(_password))
                {
                    var passwordResponse = ExecuteCommandCore(
                        $"password {_password}",
                        "password ***",
                        out var isError);
                    if (isError)
                    {
                        _logger.Error("[MpdPlaybackBackend] Failed to authenticate with MPD");
                        throw new InvalidOperationException("MPD authentication failed");
                    }
                    _logger.Information("[MpdPlaybackBackend] Authenticated with MPD");
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[MpdPlaybackBackend] Failed to initialize MPD backend");
                Cleanup();
            }

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Cleanup();
            _isInitialized = false;
            return Task.CompletedTask;
        }
    }

    private bool IsConnected()
    {
        return _tcpClient?.Connected == true && _networkStream != null;
    }

    /// <summary>
    /// Returns a version of the command that is safe to log by masking sensitive data.
    /// </summary>
    /// <param name="command">The raw MPD command.</param>
    /// <returns>A sanitized/masked representation of the command for logging.</returns>
    private static string GetSafeCommandForLogging(string? command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return command ?? string.Empty;
        }

        // Mask password commands to avoid logging sensitive data
        if (command.StartsWith("password ", StringComparison.OrdinalIgnoreCase))
        {
            return "password ***";
        }

        return LogSanitizer.Sanitize(command) ?? command;
    }

    private Task ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return ExecuteCommandCoreAsync(command, GetSafeCommandForLogging(command), cancellationToken);
    }

    private Task ExecuteCommandCoreAsync(
        string wireCommand,
        string safeCommand,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsConnected())
            {
                _logger.Warning("[MpdPlaybackBackend] Not connected to MPD, cannot execute command: {Command}", safeCommand);
                return Task.CompletedTask;
            }

            var fullCommand = $"{wireCommand}\n";
            var commandBytes = Encoding.UTF8.GetBytes(fullCommand);
            _networkStream!.Write(commandBytes, 0, commandBytes.Length);

            if (_enableDebugOutput)
            {
                _logger.Debug("[MpdPlaybackBackend] Sent command: {Command}", safeCommand);
            }

            var response = ReadResponse();
            if (response.StartsWith("ACK"))
            {
                _logger.Warning("[MpdPlaybackBackend] MPD command failed: {Command} -> {Response}", safeCommand, response);
            }
            else if (_enableDebugOutput)
            {
                _logger.Debug("[MpdPlaybackBackend] Command response: {Response}", response);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[MpdPlaybackBackend] Error executing command: {Command}", safeCommand);
        }

        return Task.CompletedTask;
    }

    private string ExecuteCommand(string command, out bool isError)
    {
        return ExecuteCommandCore(command, GetSafeCommandForLogging(command), out isError);
    }

    private string ExecuteCommandCore(string wireCommand, string safeCommand, out bool isError)
    {
        isError = false;
        try
        {
            if (!IsConnected())
            {
                _logger.Warning("[MpdPlaybackBackend] Not connected to MPD, cannot execute command: {Command}", safeCommand);
                return string.Empty;
            }

            var fullCommand = $"{wireCommand}\n";
            var commandBytes = Encoding.UTF8.GetBytes(fullCommand);
            _networkStream!.Write(commandBytes, 0, commandBytes.Length);

            var response = ReadResponse();
            isError = response.StartsWith("ACK");
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[MpdPlaybackBackend] Error executing command: {Command}", safeCommand);
            isError = true;
            return string.Empty;
        }
    }

    private string ReadResponse()
    {
        var buffer = new MemoryStream();
        var bufferBytes = new byte[1024];
        int bytesRead;

        while ((bytesRead = _networkStream!.Read(bufferBytes, 0, bufferBytes.Length)) > 0)
        {
            buffer.Write(bufferBytes, 0, bytesRead);
            var content = Encoding.UTF8.GetString(buffer.ToArray());
            if (content.Contains("OK") || content.Contains("ACK"))
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).Trim();
    }

    private void Cleanup()
    {
        try
        {
            if (_networkStream != null)
            {
                _networkStream.Close();
                _networkStream.Dispose();
                _networkStream = null;
            }

            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient.Dispose();
                _tcpClient = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[MpdPlaybackBackend] Error during cleanup");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = ShutdownAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore dispose errors
        }

        _disposed = true;
    }
}
