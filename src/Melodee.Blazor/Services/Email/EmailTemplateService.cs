using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Melodee.Blazor.Services.Email;

/// <summary>
/// Email template rendering service.
/// Supports variable substitution and localization from Templates library.
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Renders the password reset email template.
    /// </summary>
    /// <param name="resetUrl">Password reset URL</param>
    /// <param name="expiryMinutes">Token expiry time in minutes</param>
    /// <param name="languageCode">Language code for localization (optional, defaults to en-US)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (subject, textBody, htmlBody)</returns>
    Task<(string subject, string textBody, string htmlBody)> RenderPasswordResetEmailAsync(
        string resetUrl,
        int expiryMinutes,
        string? languageCode = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of email template service.
/// Loads templates from Templates library organized by language.
/// </summary>
public sealed class EmailTemplateService : IEmailTemplateService
{
    private static readonly HashSet<string> AllowedCultureCodes =
    [
        "en-US", "de-DE", "es-ES", "fr-FR", "it-IT", "ja-JP", "pt-BR", "ru-RU", "zh-CN", "ar-SA"
    ];

    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly LibraryService _libraryService;
    private readonly ILogger<EmailTemplateService> _logger;

    public EmailTemplateService(
        IMelodeeConfigurationFactory configurationFactory,
        LibraryService libraryService,
        ILogger<EmailTemplateService>? logger = null)
    {
        _configurationFactory = configurationFactory;
        _libraryService = libraryService;
        _logger = logger ?? NullLogger<EmailTemplateService>.Instance;
    }

    public async Task<(string subject, string textBody, string htmlBody)> RenderPasswordResetEmailAsync(
        string resetUrl,
        int expiryMinutes,
        string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var baseUrl = config.GetValue<string>(SettingRegistry.SystemBaseUrl);

        // Validate and sanitize baseUrl
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("REQUIRED") || baseUrl.Contains("EDIT"))
        {
            baseUrl = "https://melodee.app";
        }
        else
        {
            baseUrl = baseUrl.TrimEnd('/'); // Remove trailing slash for consistency
        }

        // Get site name from configuration, default to "Melodee"
        var siteName = config.GetValue<string>(SettingRegistry.SystemSiteName);
        if (string.IsNullOrWhiteSpace(siteName))
        {
            siteName = SettingDefaults.DefaultSiteName;
        }

        // Normalize language code (e.g., "en-US" or default to "en-US")
        var normalizedLanguage = NormalizeLanguageCode(languageCode);

        // Get subject from settings with fallback to default
        var subject = config.GetValue<string>(SettingRegistry.EmailResetPasswordSubject)
            ?? GetDefaultSubject(normalizedLanguage);

        // Load templates from Templates library by language
        var textTemplate = await LoadTemplateFromLibraryAsync($"{normalizedLanguage}/PasswordReset.txt", cancellationToken)
            ?? GetDefaultTextTemplate(normalizedLanguage);

        var htmlTemplate = await LoadTemplateFromLibraryAsync($"{normalizedLanguage}/PasswordReset.html", cancellationToken)
            ?? GetDefaultHtmlTemplate(normalizedLanguage);

        // Replace template variables
        var textBody = ReplaceVariables(textTemplate, resetUrl, expiryMinutes, siteName, baseUrl);
        var htmlBody = ReplaceVariables(htmlTemplate, resetUrl, expiryMinutes, siteName, baseUrl);

        return (subject, textBody, htmlBody);
    }

    private async Task<string?> LoadTemplateFromLibraryAsync(string relativeTemplatePath, CancellationToken cancellationToken)
    {
        try
        {
            var libraryResult = await _libraryService.GetTemplatesLibraryAsync(cancellationToken);
            if (!libraryResult.IsSuccess || libraryResult.Data == null)
            {
                return null;
            }

            var root = Path.GetFullPath(libraryResult.Data.Path);
            var candidate = Path.GetFullPath(Path.Combine(root, relativeTemplatePath));

            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Template path attempted to escape root directory: {RelativePath}", relativeTemplatePath);
                return null;
            }

            if (!File.Exists(candidate))
            {
                return null;
            }

            return await File.ReadAllTextAsync(candidate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading template: {RelativePath}", relativeTemplatePath);
            return null;
        }
    }

    private string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "en-us";
        }

        var normalized = languageCode.ToLowerInvariant();

        if (!AllowedCultureCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return "en-us";
        }

        if (normalized.Contains('/') ||
            normalized.Contains('\\') ||
            normalized.Contains(".."))
        {
            return "en-us";
        }

        return normalized;
    }

    private static string ReplaceVariables(string template, string resetUrl, int expiryMinutes, string siteName, string baseUrl)
    {
        return template
            .Replace("{resetUrl}", resetUrl)
            .Replace("{expiryMinutes}", expiryMinutes.ToString())
            .Replace("{siteName}", siteName)
            .Replace("{appName}", siteName) // Support legacy {appName} variable
            .Replace("{baseUrl}", baseUrl);
    }

    private static string GetDefaultSubject(string? languageCode)
    {
        // Future: implement localized subjects
        return "Reset your password";
    }

    private static string GetDefaultTextTemplate(string? languageCode)
    {
        // Future: implement localized templates
        return @"Someone requested a password reset for your {siteName} account.

Reset your password using this link (valid for {expiryMinutes} minutes):
{resetUrl}

If you didn't request this, you can ignore this email. Your account is safe and your password has not been changed.

---
This email was sent from {baseUrl}.";
    }

    private static string GetDefaultHtmlTemplate(string? languageCode)
    {
        // Future: implement localized templates
        return @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Reset your password</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; border-radius: 8px 8px 0 0; }
        .header h1 { color: white; margin: 0; font-size: 24px; }
        .content { background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 8px 8px; }
        .button { display: inline-block; padding: 12px 24px; background: #667eea; color: white; text-decoration: none; border-radius: 6px; margin: 20px 0; }
        .footer { text-align: center; margin-top: 20px; color: #666; font-size: 12px; }
        .warning { background: #fff3cd; border-left: 4px solid #ffc107; padding: 12px; margin: 20px 0; }
    </style>
</head>
<body>
    <div class=""header"">
        <h1>{siteName}</h1>
    </div>
    <div class=""content"">
        <h2>Password Reset Request</h2>
        <p>Someone requested a password reset for your {siteName} account.</p>
        <p>Click the button below to reset your password:</p>
        <p style=""text-align: center;"">
            <a href=""{resetUrl}"" class=""button"">Reset Password</a>
        </p>
        <p style=""color: #666; font-size: 14px;"">This link is valid for {expiryMinutes} minutes.</p>
        <div class=""warning"">
            <strong>⚠️ Didn't request this?</strong><br>
            If you didn't request a password reset, you can safely ignore this email. Your account is secure and your password has not been changed.
        </div>
        <p style=""color: #666; font-size: 12px; margin-top: 20px;"">
            If the button doesn't work, copy and paste this link into your browser:<br>
            <a href=""{resetUrl}"" style=""color: #667eea; word-break: break-all;"">{resetUrl}</a>
        </p>
    </div>
    <div class=""footer"">
        <p>This email was sent from {baseUrl}</p>
    </div>
</body>
</html>";
    }
}
