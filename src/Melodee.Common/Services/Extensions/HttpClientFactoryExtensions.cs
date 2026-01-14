using System.Net;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Serilog;
using Serilog.Events;

namespace Melodee.Common.Services.Extensions;

public static class HttpClientFactoryExtensions
{
    private const int MaxResponseSizeBytes = 10 * 1024 * 1024; // 10 MiB
    private const int MaxRedirects = 3;

    public static Task<byte[]?> BytesForImageUrlAsync(
        this IHttpClientFactory httpClientFactory,
        string userAgent,
        string? url,
        CancellationToken cancellationToken = default)
    {
        return BytesForImageUrlAsync(httpClientFactory, null, userAgent, url, null, cancellationToken);
    }

    public static async Task<byte[]?> BytesForImageUrlAsync(
        this IHttpClientFactory httpClientFactory,
        ISsrfValidator? ssrfValidator,
        string userAgent,
        string? url,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        if (ssrfValidator != null)
        {
            var validationResult = await ssrfValidator.ValidateUrlAsync(url, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                (logger ?? NoOpLogger.Instance).Warning("[HttpClientFactoryExtensions] SSRF validation failed for URL: {Url}", LogSanitizer.Sanitize(url));
                return null;
            }
        }

        try
        {
            using var client = httpClientFactory.CreateClient("ImageFetch");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                (logger ?? NoOpLogger.Instance).Warning("[HttpClientFactoryExtensions] HTTP request failed for URL [{Url}] with status {StatusCode}",
                    LogSanitizer.Sanitize(url), response.StatusCode);
                return null;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxResponseSizeBytes)
            {
                (logger ?? NoOpLogger.Instance).Warning("[HttpClientFactoryExtensions] Response too large for URL [{Url}]: {ContentLength} bytes (max {MaxBytes})",
                    LogSanitizer.Sanitize(url), contentLength.Value, MaxResponseSizeBytes);
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[8192];
            int bytesRead;
            long totalBytesRead = 0;

            while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > MaxResponseSizeBytes)
                {
                    (logger ?? NoOpLogger.Instance).Warning("[HttpClientFactoryExtensions] Response exceeded size limit while reading URL [{Url}]: {TotalBytesRead} bytes",
                        LogSanitizer.Sanitize(url), totalBytesRead);
                    return null;
                }
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            var imageBytes = memoryStream.ToArray();

            await using var validationStream = new MemoryStream(imageBytes);
            if (!FileTypeValidator.IsValidImage(validationStream))
            {
                (logger ?? NoOpLogger.Instance).Warning("[HttpClientFactoryExtensions] Downloaded file from URL [{Url}] is not a valid image", LogSanitizer.Sanitize(url));
                return null;
            }

            return imageBytes;
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken)
        {
            (logger ?? NoOpLogger.Instance).Warning("[HttpClientFactoryExtensions] Request timeout for URL: {Url}", LogSanitizer.Sanitize(url));
        }
        catch (HttpRequestException ex)
        {
            (logger ?? NoOpLogger.Instance).Warning(ex, "[HttpClientFactoryExtensions] HTTP request error for URL: {Url}", LogSanitizer.Sanitize(url));
        }
        catch (Exception ex)
        {
            (logger ?? NoOpLogger.Instance).Warning(ex, "[HttpClientFactoryExtensions] Unexpected error for URL: {Url}", LogSanitizer.Sanitize(url));
        }

        return null;
    }

    private sealed class NoOpLogger : ILogger
    {
        public static readonly ILogger Instance = new NullLogger();

        public void Write(LogEvent logEvent)
        {
        }

        public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        {
        }

        public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        {
        }

        public bool IsEnabled(LogEventLevel level)
        {
            return false;
        }

        public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
        {
            return this;
        }

        public ILogger ForContext(IEnumerable<KeyValuePair<string, object>> properties)
        {
            return this;
        }
    }

    private sealed class NullLogger : ILogger
    {
        public void Write(LogEvent logEvent)
        {
        }

        public void Write(LogEventLevel level, string messageTemplate, params object?[]? propertyValues)
        {
        }

        public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object?[]? propertyValues)
        {
        }

        public bool IsEnabled(LogEventLevel level)
        {
            return false;
        }

        public ILogger ForContext(string propertyName, object? value, bool destructureObjects = false)
        {
            return this;
        }

        public ILogger ForContext(IEnumerable<KeyValuePair<string, object>> properties)
        {
            return this;
        }
    }
}
