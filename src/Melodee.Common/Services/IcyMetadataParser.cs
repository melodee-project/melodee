using Melodee.Common.Enums;
using Melodee.Common.Models;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Parser for ICY (SHOUTcast/Icecast) metadata in streaming audio
/// </summary>
public class IcyMetadataParser(ILogger logger)
{
    private const int MaxBufferSize = 65536; // 64KB
    private const int StreamReadTimeoutSeconds = 10;

    public record NowPlayingResult(
        string? Title,
        NowPlayingSource Source);

    /// <summary>
    /// Extracts now-playing metadata from a radio stream
    /// </summary>
    public async Task<OperationResult<NowPlayingResult>> ExtractNowPlayingAsync(
        string streamUrl,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(StreamReadTimeoutSeconds);

            var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
            request.Headers.Add("Icy-MetaData", "1");
            request.Headers.Add("User-Agent", "Melodee/1.0");

            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new OperationResult<NowPlayingResult>
                {
                    Data = new NowPlayingResult(null, NowPlayingSource.Unknown),
                    Type = OperationResponseType.Error
                };
            }

            // Check for ICY metadata interval
            if (response.Headers.TryGetValues("icy-metaint", out var metaIntValues))
            {
                var metaIntStr = metaIntValues.FirstOrDefault();
                if (int.TryParse(metaIntStr, out var metaInt) && metaInt > 0)
                {
                    var title = await ExtractIcyMetadataAsync(response, metaInt, cancellationToken);
                    return new OperationResult<NowPlayingResult>
                    {
                        Data = new NowPlayingResult(title, NowPlayingSource.Icy)
                    };
                }
            }

            // Check if it's an Ogg stream
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.Contains("ogg", StringComparison.OrdinalIgnoreCase) == true)
            {
                var title = await ExtractOggMetadataAsync(response, cancellationToken);
                return new OperationResult<NowPlayingResult>
                {
                    Data = new NowPlayingResult(title, NowPlayingSource.Ogg)
                };
            }

            // No metadata available
            return new OperationResult<NowPlayingResult>
            {
                Data = new NowPlayingResult(null, NowPlayingSource.Unknown)
            };
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error extracting now-playing metadata from {StreamUrl}", streamUrl);
            return new OperationResult<NowPlayingResult>
            {
                Data = new NowPlayingResult(null, NowPlayingSource.Unknown),
                Type = OperationResponseType.Error
            };
        }
    }

    private async Task<string?> ExtractIcyMetadataAsync(
        HttpResponseMessage response,
        int metaInt,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[metaInt];
            var totalRead = 0;

            // Read up to the first metadata block
            while (totalRead < metaInt)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, metaInt - totalRead), cancellationToken);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            if (totalRead < metaInt)
            {
                return null;
            }

            // Read metadata length (1 byte, multiply by 16 to get actual length)
            var metaLengthByte = new byte[1];
            var read = await stream.ReadAsync(metaLengthByte, cancellationToken);
            if (read == 0)
            {
                return null;
            }

            var metaLength = metaLengthByte[0] * 16;
            if (metaLength == 0)
            {
                return null;
            }

            // Read metadata
            var metadata = new byte[metaLength];
            totalRead = 0;
            while (totalRead < metaLength)
            {
                var bytesRead = await stream.ReadAsync(metadata.AsMemory(totalRead, metaLength - totalRead), cancellationToken);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            var metadataString = System.Text.Encoding.UTF8.GetString(metadata).TrimEnd('\0');

            // Parse StreamTitle='...'
            const string streamTitlePrefix = "StreamTitle='";
            var titleStart = metadataString.IndexOf(streamTitlePrefix, StringComparison.Ordinal);
            if (titleStart >= 0)
            {
                titleStart += streamTitlePrefix.Length;
                var titleEnd = metadataString.IndexOf("';", titleStart, StringComparison.Ordinal);
                if (titleEnd > titleStart)
                {
                    return metadataString.Substring(titleStart, titleEnd - titleStart).Trim();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error parsing ICY metadata");
            return null;
        }
    }

    private async Task<string?> ExtractOggMetadataAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxBufferSize];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

            if (bytesRead == 0)
            {
                return null;
            }

            var content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Simple search for TITLE= in Vorbis comments
            // This is a best-effort approach; proper Ogg parsing is complex
            const string titlePrefix = "TITLE=";
            var titleIndex = content.IndexOf(titlePrefix, StringComparison.Ordinal);
            if (titleIndex >= 0)
            {
                titleIndex += titlePrefix.Length;
                var titleEnd = content.IndexOfAny(['\0', '\r', '\n'], titleIndex);
                if (titleEnd > titleIndex)
                {
                    return content.Substring(titleIndex, titleEnd - titleIndex).Trim();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error parsing Ogg metadata");
            return null;
        }
    }
}
