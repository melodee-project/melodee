using Melodee.Common.Enums;
using Melodee.Common.Models;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for probing radio station health and metadata
/// </summary>
public class RadioStationProbeService(ILogger logger, IHttpClientFactory httpClientFactory)
{
    private const int MaxRedirects = 5;
    private const int ProbeTimeoutSeconds = 10;
    
    public record ProbeResult(
        bool IsHealthy,
        string? ResolvedStreamUrl,
        string? ContentType,
        int? BitrateKbps,
        string? ErrorMessage);

    /// <summary>
    /// Probes a radio station to determine its health and capture diagnostic information
    /// </summary>
    public async Task<OperationResult<ProbeResult>> ProbeStationAsync(
        string streamUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds);
            
            // Try HEAD first
            var request = new HttpRequestMessage(HttpMethod.Head, streamUrl);
            request.Headers.Add("Icy-MetaData", "1");
            request.Headers.Add("User-Agent", "Melodee/1.0");
            
            HttpResponseMessage? response = null;
            string? resolvedUrl = streamUrl;
            
            try
            {
                response = await httpClient.SendAsync(request, cancellationToken);
                resolvedUrl = response.RequestMessage?.RequestUri?.ToString() ?? streamUrl;
            }
            catch (Exception headEx)
            {
                logger.Debug("HEAD request failed for {StreamUrl}, trying GET: {Error}", 
                    streamUrl, headEx.Message);
                
                // Fallback to GET with Range header
                request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                request.Headers.Add("Icy-MetaData", "1");
                request.Headers.Add("User-Agent", "Melodee/1.0");
                request.Headers.Add("Range", "bytes=0-8191");
                
                try
                {
                    response = await httpClient.SendAsync(request, cancellationToken);
                    resolvedUrl = response.RequestMessage?.RequestUri?.ToString() ?? streamUrl;
                }
                catch (Exception getEx)
                {
                    return new OperationResult<ProbeResult>
                    {
                        Data = new ProbeResult(
                            false,
                            null,
                            null,
                            null,
                            $"Connection failed: {getEx.Message}"),
                        Type = OperationResponseType.Error
                    };
                }
            }
            
            // Check if response indicates success
            var isHealthy = response.IsSuccessStatusCode;
            string? errorMessage = null;
            
            if (!isHealthy)
            {
                errorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            
            // Extract content type
            var contentType = response.Content.Headers.ContentType?.MediaType;
            
            // Validate content type for audio streams
            if (isHealthy && contentType != null)
            {
                var isAudioContent = contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
                                    contentType.Equals("application/ogg", StringComparison.OrdinalIgnoreCase);
                
                if (!isAudioContent)
                {
                    isHealthy = false;
                    errorMessage = $"Invalid content type: {contentType}";
                }
            }
            
            // Try to extract bitrate from ICY headers
            int? bitrateKbps = null;
            if (response.Headers.TryGetValues("icy-br", out var icyBrValues))
            {
                var brValue = icyBrValues.FirstOrDefault();
                if (int.TryParse(brValue, out var br))
                {
                    bitrateKbps = br;
                }
            }
            
            var result = new ProbeResult(
                isHealthy,
                resolvedUrl,
                contentType,
                bitrateKbps,
                errorMessage);
            
            return new OperationResult<ProbeResult>
            {
                Data = result
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error probing radio station {StreamUrl}", streamUrl);
            return new OperationResult<ProbeResult>
            {
                Data = new ProbeResult(
                    false,
                    null,
                    null,
                    null,
                    $"Probe failed: {ex.Message}"),
                Type = OperationResponseType.Error
            };
        }
    }
}
