using System.Globalization;
using Melodee.Common.Utility;

namespace Melodee.Common.Configuration;

/// <summary>
/// Redacts configuration values before they are written to diagnostic logs.
/// </summary>
public static class ConfigurationLogRedactor
{
    /// <summary>
    /// Replacement written for configuration values that are not explicitly safe to log.
    /// </summary>
    public const string RedactedValue = "[REDACTED]";

    private static readonly char[] KeySeparators = ['.', ':', '_', '-'];

    private static readonly HashSet<string> SafeExactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "aspnet.version",
        "aspnetcore.environment",
        "aspnetcore.http.ports",
        "aspnetcore.https.ports",
        "aspnetcore.urls",
        "db.database",
        "db.host",
        "db.max.pool.size",
        "db.min.pool.size",
        "db.name",
        "db.port",
        "dotnet.running.in.container",
        "dotnet.version",
        "jellyfin.token.allow.legacy.headers",
        "jellyfin.token.allowlegacyheaders",
        "jellyfin.token.expires.after.hours",
        "jellyfin.token.expiresafterhours",
        "jellyfin.token.max.active.per.user",
        "jellyfin.token.maxactiveperuser",
        "jwt.audience",
        "jwt.issuer",
        "lang",
        "lc.all",
        "melodee.auth.token.hours",
        "melodee.image",
        "melodee.port",
        "melodee.profile",
        "melodee.running.as.user",
        "melodee.server",
        "melodee.skip.db.registration",
        "melodee.auth.settings.token.hours",
        "melodeeauthsettings.tokenhours",
        "search.engine.brave.base.url",
        "searchengine.brave.baseurl",
        "security.password.reset.token.expiry.minutes",
        "security.passwordresettokenexpiryminutes",
        "system.base.url",
        "system.baseurl",
        "tz"
    };

    private static readonly HashSet<string> SafeUrlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "aspnetcore.urls",
        "jwt.issuer",
        "melodee.server",
        "search.engine.brave.base.url",
        "searchengine.brave.baseurl",
        "system.base.url",
        "system.baseurl"
    };

    private static readonly HashSet<string> SafeOperationalSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bytes",
        "count",
        "days",
        "disabled",
        "enabled",
        "environment",
        "extension",
        "format",
        "host",
        "hours",
        "limit",
        "minutes",
        "path",
        "port",
        "ports",
        "rate",
        "seconds",
        "size",
        "timeout",
        "type",
        "version",
        "volume"
    };

    private static readonly string[] SensitiveIdentifiers =
    [
        "accesstoken",
        "apikey",
        "authorization",
        "connectionstring",
        "credential",
        "cookie",
        "databaseurl",
        "passphrase",
        "passwd",
        "password",
        "pepper",
        "privatecode",
        "privatekey",
        "secret",
        "signature",
        "token"
    ];

    /// <summary>
    /// Sanitizes a configuration key so it cannot inject additional diagnostic log lines.
    /// </summary>
    /// <param name="key">Configuration key to sanitize.</param>
    /// <returns>The key with supported line-ending characters escaped.</returns>
    public static string SanitizeKey(string? key)
    {
        return LogSanitizer.Sanitize(key) ?? string.Empty;
    }

    /// <summary>
    /// Returns a log-safe value only when the key identifies an explicitly safe operational setting.
    /// All sensitive and unrecognized configuration values are redacted.
    /// </summary>
    /// <param name="key">Configuration or environment-variable key associated with the value.</param>
    /// <param name="value">Configuration value to evaluate.</param>
    /// <returns>A sanitized operational value or <see cref="RedactedValue"/>.</returns>
    public static string RedactValue(string? key, object? value)
    {
        var segments = GetKeySegments(key);
        if (segments.Length == 0)
        {
            return RedactedValue;
        }

        var normalizedKey = string.Join('.', segments);
        var isExplicitlySafe = SafeExactKeys.Contains(normalizedKey);
        if (!isExplicitlySafe && (ContainsSensitiveIdentifier(segments) || !IsSafeOperationalKey(segments)))
        {
            return RedactedValue;
        }

        var renderedValue = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        } ?? string.Empty;

        if (SafeUrlKeys.Contains(normalizedKey) && ContainsUrlSecret(renderedValue))
        {
            return RedactedValue;
        }

        return LogSanitizer.Sanitize(renderedValue) ?? string.Empty;
    }

    private static bool ContainsSensitiveIdentifier(IEnumerable<string> segments)
    {
        foreach (var segment in segments)
        {
            if (string.Equals(segment, "connection", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "dsn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "key", StringComparison.OrdinalIgnoreCase)
                || SensitiveIdentifiers.Any(identifier => segment.Contains(identifier, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUrlSecret(string value)
    {
        return value.Contains('@', StringComparison.Ordinal)
               || value.Contains('?', StringComparison.Ordinal)
               || value.Contains('#', StringComparison.Ordinal);
    }

    private static string[] GetKeySegments(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return [];
        }

        return key.Split(KeySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitIdentifierWords)
            .Select(segment => segment.ToLowerInvariant())
            .ToArray();
    }

    private static bool IsSafeOperationalKey(IReadOnlyList<string> segments)
    {
        return SafeOperationalSuffixes.Contains(segments[^1])
               || segments.Count >= 2
               && string.Equals(segments[^2], "cron", StringComparison.OrdinalIgnoreCase)
               && string.Equals(segments[^1], "expression", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitIdentifierWords(string identifier)
    {
        var wordStart = 0;
        for (var i = 1; i < identifier.Length; i++)
        {
            var currentIsUpper = char.IsUpper(identifier[i]);
            var previousIsLowerOrDigit = char.IsLower(identifier[i - 1]) || char.IsDigit(identifier[i - 1]);
            var startsWordAfterAcronym = currentIsUpper
                                         && char.IsUpper(identifier[i - 1])
                                         && i + 1 < identifier.Length
                                         && char.IsLower(identifier[i + 1]);
            if (!currentIsUpper || (!previousIsLowerOrDigit && !startsWordAfterAcronym))
            {
                continue;
            }

            yield return identifier[wordStart..i];
            wordStart = i;
        }

        yield return identifier[wordStart..];
    }
}
