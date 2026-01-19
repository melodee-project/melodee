using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Melodee.Cli.Models;

namespace Melodee.Cli.Client;

/// <summary>
/// Remote Melodee client that uses HTTP REST API calls.
/// </summary>
public class RemoteMelodeeClient : IMelodeeClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public RemoteMelodeeClient(string baseUrl, string token, string? userAgent = null)
    {
        _apiBaseUrl = baseUrl.TrimEnd('/') + "/api/v1";
        
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var version = typeof(RemoteMelodeeClient).Assembly.GetName().Version;
        var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent ?? $"mcli/{versionString}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/system/info", cancellationToken);
            await EnsureSuccessWithDetailedError(response);
            
            var result = await response.Content.ReadFromJsonAsync<SystemInfoDto>(_jsonOptions, cancellationToken);
            return result ?? throw new InvalidOperationException("Failed to deserialize system info");
        }
        catch (HttpRequestException ex)
        {
            throw new MelodeeRemoteException("Network error while fetching system info", ex, MelodeeRemoteException.RemoteErrorType.NetworkError);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new MelodeeRemoteException("Request timed out while fetching system info", ex, MelodeeRemoteException.RemoteErrorType.Timeout);
        }
    }

    public async Task<UserMeDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/user/me", cancellationToken);
            await EnsureSuccessWithDetailedError(response);
            
            var result = await response.Content.ReadFromJsonAsync<UserMeDto>(_jsonOptions, cancellationToken);
            return result ?? throw new InvalidOperationException("Failed to deserialize user info");
        }
        catch (HttpRequestException ex)
        {
            throw new MelodeeRemoteException("Network error while fetching user info", ex, MelodeeRemoteException.RemoteErrorType.NetworkError);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new MelodeeRemoteException("Request timed out while fetching user info", ex, MelodeeRemoteException.RemoteErrorType.Timeout);
        }
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetAdminUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/admin/users", cancellationToken);
            await EnsureSuccessWithDetailedError(response);
            
            var result = await response.Content.ReadFromJsonAsync<List<AdminUserDto>>(_jsonOptions, cancellationToken);
            return result ?? new List<AdminUserDto>();
        }
        catch (HttpRequestException ex)
        {
            throw new MelodeeRemoteException("Network error while fetching admin users", ex, MelodeeRemoteException.RemoteErrorType.NetworkError);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new MelodeeRemoteException("Request timed out while fetching admin users", ex, MelodeeRemoteException.RemoteErrorType.Timeout);
        }
    }

    public async Task<SearchResultsDto> SearchAsync(SearchRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_apiBaseUrl}/search", request, cancellationToken);
            await EnsureSuccessWithDetailedError(response);
            
            // Parse as dynamic JSON since the structure can vary
            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(jsonString);
            
            var data = doc.RootElement.GetProperty("data");
            var meta = doc.RootElement.GetProperty("meta");
            
            return new SearchResultsDto(
                JsonSerializer.Deserialize<object>(data.GetRawText(), _jsonOptions) ?? new object(),
                JsonSerializer.Deserialize<object>(meta.GetRawText(), _jsonOptions) ?? new object()
            );
        }
        catch (HttpRequestException ex)
        {
            throw new MelodeeRemoteException("Network error while searching", ex, MelodeeRemoteException.RemoteErrorType.NetworkError);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new MelodeeRemoteException("Request timed out while searching", ex, MelodeeRemoteException.RemoteErrorType.Timeout);
        }
    }

    private static async Task EnsureSuccessWithDetailedError(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = (int)response.StatusCode;
        var reasonPhrase = response.ReasonPhrase ?? "Unknown";
        var content = await response.Content.ReadAsStringAsync();

        var errorType = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => MelodeeRemoteException.RemoteErrorType.Unauthorized,
            HttpStatusCode.Forbidden => MelodeeRemoteException.RemoteErrorType.Forbidden,
            HttpStatusCode.NotFound => MelodeeRemoteException.RemoteErrorType.NotFound,
            _ when statusCode >= 500 => MelodeeRemoteException.RemoteErrorType.ServerError,
            _ => MelodeeRemoteException.RemoteErrorType.UnexpectedError
        };

        var message = $"HTTP {statusCode} {reasonPhrase}";
        if (!string.IsNullOrWhiteSpace(content) && content.Length < 200)
        {
            message += $": {content}";
        }

        throw new MelodeeRemoteException(message, null, errorType, statusCode);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
