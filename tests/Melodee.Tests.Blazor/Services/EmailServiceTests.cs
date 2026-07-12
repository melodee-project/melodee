using System.Collections.Concurrent;
using FluentAssertions;
using Melodee.Blazor.Services.Email;
using Melodee.Common.Constants;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Melodee.Tests.Blazor.Services;

/// <summary>
/// Tests for SMTP email sender service.
/// Verifies configuration handling and error scenarios.
/// </summary>
public class SmtpEmailSenderTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IMelodeeConfigurationFactory> _mockConfigFactory;
    private readonly Mock<IMelodeeConfiguration> _mockConfig;

    public SmtpEmailSenderTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfig = new Mock<IMelodeeConfiguration>();

        _mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConfig.Object);
    }

    [Fact]
    public async Task SendAsync_WhenEmailDisabled_ReturnsFalse()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailEnabled)).Returns(false);

        var sender = new SmtpEmailSender(_mockLogger.Object, _mockConfigFactory.Object);

        // Act
        var result = await sender.SendAsync("test@example.com", "Test", "Body");

        // Assert
        Assert.False(result);

        // Verify warning was logged (no parameters in this log call)
        _mockLogger.Verify(
            l => l.Warning(It.Is<string>(s => s.Contains("Email sending is disabled"))),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenFromEmailMissing_ReturnsFalse()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailEnabled)).Returns(true);
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailFromEmail)).Returns((string?)null);
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailSmtpHost)).Returns("smtp.example.com");

        var sender = new SmtpEmailSender(_mockLogger.Object, _mockConfigFactory.Object);

        // Act
        var result = await sender.SendAsync("test@example.com", "Test", "Body");

        // Assert
        Assert.False(result);

        // Verify warning about missing configuration
        _mockLogger.Verify(
            l => l.Warning(It.Is<string>(s => s.Contains("configuration incomplete"))),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_WhenSmtpHostMissing_ReturnsFalse()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailEnabled)).Returns(true);
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailFromEmail)).Returns("sender@example.com");
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailSmtpHost)).Returns((string?)null);

        var sender = new SmtpEmailSender(_mockLogger.Object, _mockConfigFactory.Object);

        // Act
        var result = await sender.SendAsync("test@example.com", "Test", "Body");

        // Assert
        Assert.False(result);

        // Verify warning about missing configuration
        _mockLogger.Verify(
            l => l.Warning(It.Is<string>(s => s.Contains("configuration incomplete"))),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_DoesNotLogSensitiveData()
    {
        // Arrange - when email is disabled, it logs a warning without any email info
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailEnabled)).Returns(false);

        var sender = new SmtpEmailSender(_mockLogger.Object, _mockConfigFactory.Object);
        var sensitiveEmail = "sensitive@example.com";

        // Act
        await sender.SendAsync(sensitiveEmail, "Test", "Body with secret token");

        // Assert - verify warning was logged but doesn't contain the sensitive email
        _mockLogger.Verify(
            l => l.Warning(It.Is<string>(s => !s.Contains(sensitiveEmail))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task SendAsync_WhenConfigurationThrows_DoesNotLogSensitiveExceptionPayload()
    {
        const string sensitiveEmail = "sensitive.user@example.test";
        const string sensitiveSubject = "Sensitive configured reset subject";
        const string sensitiveToken = "sensitive-reset-token-123456";
        var sensitiveException = new InvalidOperationException(
            $"{sensitiveEmail} {sensitiveSubject} {sensitiveToken}");
        var configFactory = new Mock<IMelodeeConfigurationFactory>();
        configFactory.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(sensitiveException);
        var sink = new RecordingLogEventSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var sender = new SmtpEmailSender(logger, configFactory.Object);

        var result = await sender.SendAsync(
            sensitiveEmail,
            sensitiveSubject,
            $"Reset URL token: {sensitiveToken}");

        result.Should().BeFalse();
        sink.Output.Should().Contain("Failed to send email");
        sink.Output.Should().Contain("InvalidOperationException");
        sink.Output.Should().NotContainAny(sensitiveEmail, sensitiveSubject, sensitiveToken);
    }

    [Fact]
    public async Task SendAsync_WhenSmtpConnectionThrows_DoesNotLogConfiguredHostOrMessagePayload()
    {
        const string sensitiveEmail = "sensitive.user@example.test";
        const string sensitiveSubject = "Sensitive configured reset subject";
        const string sensitiveToken = "sensitive-reset-token-123456";
        const string sensitiveHost = "sensitive.internal.example.test";
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailEnabled)).Returns(true);
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailFromEmail)).Returns("sender@example.test");
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailSmtpHost)).Returns($"\0{sensitiveHost}");
        _mockConfig.Setup(c => c.GetValue<int?>(SettingRegistry.EmailSmtpPort)).Returns(587);
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailSmtpUseSsl)).Returns(false);
        _mockConfig.Setup(c => c.GetValue<bool?>(SettingRegistry.EmailSmtpUseStartTls)).Returns(false);
        var sink = new RecordingLogEventSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var sender = new SmtpEmailSender(logger, _mockConfigFactory.Object);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await sender.SendAsync(
            sensitiveEmail,
            sensitiveSubject,
            $"Reset URL token: {sensitiveToken}",
            cancellationToken: cancellationTokenSource.Token);

        result.Should().BeFalse();
        sink.Output.Should().Contain("SMTP error sending email");
        sink.Output.Should().Contain("Exception type");
        sink.Output.Should().NotContainAny(
            sensitiveEmail,
            sensitiveSubject,
            sensitiveToken,
            sensitiveHost);
    }

    private sealed class RecordingLogEventSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public string Output => string.Join(
            Environment.NewLine,
            _events.Select(x => $"{x.RenderMessage()} {x.Exception}"));

        public void Emit(LogEvent logEvent)
        {
            _events.Enqueue(logEvent);
        }
    }
}

/// <summary>
/// Tests for email template service.
/// Verifies template rendering and variable substitution.
/// </summary>
public class EmailTemplateServiceTests
{
    private readonly Mock<IMelodeeConfigurationFactory> _mockConfigFactory;
    private readonly Mock<IMelodeeConfiguration> _mockConfig;
    private readonly Mock<LibraryService> _mockLibraryService;

    public EmailTemplateServiceTests()
    {
        _mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfig = new Mock<IMelodeeConfiguration>();
        _mockLibraryService = new Mock<LibraryService>();

        _mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConfig.Object);

        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("https://melodee.test");
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_ReplacesResetUrl()
    {
        // Arrange
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = "https://melodee.test/reset?token=abc123";

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, 60);

        // Assert
        Assert.Contains(resetUrl, textBody);
        Assert.Contains(resetUrl, htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_ReplacesExpiryMinutes()
    {
        // Arrange
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var expiryMinutes = 120;

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com", expiryMinutes);

        // Assert
        Assert.Contains("120", textBody);
        Assert.Contains("120", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_UsesCustomTemplateFromSettings()
    {
        // Arrange
        var customSubject = "Custom Reset Subject";
        var customTextTemplate = "Custom text with {resetUrl} and {expiryMinutes}";
        var customHtmlTemplate = "<html>{resetUrl} expires in {expiryMinutes}</html>";

        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailResetPasswordSubject))
            .Returns(customSubject);
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailResetPasswordTextBodyTemplate))
            .Returns(customTextTemplate);
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailResetPasswordHtmlBodyTemplate))
            .Returns(customHtmlTemplate);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert
        Assert.Equal(customSubject, subject);
        Assert.Contains("https://test.com/reset", textBody);
        Assert.Contains("60", textBody);
        Assert.Contains("https://test.com/reset", htmlBody);
        Assert.Contains("60", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithValidBaseUrl_PopulatesBaseUrlVariable()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("https://melodee.example.com");

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = "https://melodee.example.com/account/reset-password?token=xyz789";

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, 60);

        // Assert - baseUrl should appear in footer
        Assert.Contains("https://melodee.example.com", textBody);
        Assert.Contains("https://melodee.example.com", htmlBody);
        Assert.Contains("This email was sent from https://melodee.example.com", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithNullBaseUrl_UsesFallback()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns((string?)null);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = "https://melodee.app/account/reset-password?token=xyz789";

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, 60);

        // Assert - should use fallback baseUrl
        Assert.Contains("https://melodee.app", textBody);
        Assert.Contains("https://melodee.app", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithEmptyBaseUrl_UsesFallback()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns(string.Empty);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - should use fallback baseUrl
        Assert.Contains("https://melodee.app", textBody);
        Assert.Contains("https://melodee.app", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithPlaceholderBaseUrl_UsesFallback()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("** REQUIRED: THIS MUST BE EDITED **");

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - should use fallback baseUrl
        Assert.Contains("https://melodee.app", textBody);
        Assert.Contains("https://melodee.app", htmlBody);
        Assert.DoesNotContain("REQUIRED", htmlBody);
        Assert.DoesNotContain("EDIT", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_TrimsTrailingSlashFromBaseUrl()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("https://melodee.example.com/");

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - trailing slash should be removed
        Assert.Contains("https://melodee.example.com", htmlBody);
        Assert.DoesNotContain("https://melodee.example.com//", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_HtmlDoesNotContainDoubleBraces()
    {
        // Arrange
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - CSS should not have double braces
        Assert.DoesNotContain("{{", htmlBody);
        Assert.DoesNotContain("}}", htmlBody);
        Assert.Contains("body {", htmlBody); // Should have normal single braces
        Assert.Contains(".header {", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_ReplacesAllTemplateVariables()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("https://melodee.example.com");
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemSiteName))
            .Returns("My Custom Melodee");

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = "https://melodee.example.com/account/reset-password?token=test123";
        var expiryMinutes = 90;

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, expiryMinutes);

        // Assert - verify all template variables are replaced
        Assert.DoesNotContain("{resetUrl}", textBody);
        Assert.DoesNotContain("{resetUrl}", htmlBody);
        Assert.DoesNotContain("{expiryMinutes}", textBody);
        Assert.DoesNotContain("{expiryMinutes}", htmlBody);
        Assert.DoesNotContain("{siteName}", textBody);
        Assert.DoesNotContain("{siteName}", htmlBody);
        Assert.DoesNotContain("{appName}", textBody);
        Assert.DoesNotContain("{appName}", htmlBody);
        Assert.DoesNotContain("{baseUrl}", textBody);
        Assert.DoesNotContain("{baseUrl}", htmlBody);

        // Verify actual values are present
        Assert.Contains(resetUrl, textBody);
        Assert.Contains(resetUrl, htmlBody);
        Assert.Contains("90", textBody);
        Assert.Contains("90", htmlBody);
        Assert.Contains("My Custom Melodee", htmlBody);
        Assert.Contains("https://melodee.example.com", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithCustomSiteName_UsesCustomName()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemSiteName))
            .Returns("Steve's Music Server");

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - custom site name should appear in email
        Assert.Contains("Steve's Music Server", textBody);
        Assert.Contains("Steve's Music Server", htmlBody);
        Assert.Contains("<h1>Steve's Music Server</h1>", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithNullSiteName_UsesDefaultMelodee()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemSiteName))
            .Returns((string?)null);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - should default to "Melodee"
        Assert.Contains("Melodee", textBody);
        Assert.Contains("Melodee", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_WithEmptySiteName_UsesDefaultMelodee()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemSiteName))
            .Returns(string.Empty);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - should default to "Melodee"
        Assert.Contains("Melodee", textBody);
        Assert.Contains("Melodee", htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_SupportsLegacyAppNameVariable()
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemSiteName))
            .Returns("My Music");

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);

        // Act - use a template with legacy {appName} variable
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60);

        // Assert - {appName} should be replaced with siteName for backwards compatibility
        Assert.DoesNotContain("{appName}", htmlBody);
        Assert.DoesNotContain("{appName}", textBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_HtmlContainsValidStructure()
    {
        // Arrange
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = "https://melodee.example.com/account/reset-password?token=abc123";

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, 60);

        // Assert - verify HTML structure
        Assert.Contains("<!DOCTYPE html>", htmlBody);
        Assert.Contains("<html>", htmlBody);
        Assert.Contains("</html>", htmlBody);
        Assert.Contains("<style>", htmlBody);
        Assert.Contains("</style>", htmlBody);
        Assert.Contains($"<a href=\"{resetUrl}\"", htmlBody);
        Assert.Contains("Reset Password", htmlBody);
        Assert.Contains("Password Reset Request", htmlBody);
    }

    [Theory]
    [InlineData("http://localhost:5157")]
    [InlineData("https://melodee.production.com")]
    [InlineData("https://melodee.staging.com:8443")]
    [InlineData("http://192.168.1.100:5000")]
    public async Task RenderPasswordResetEmailAsync_WithVariousValidBaseUrls_UsesProvidedValue(string baseUrl)
    {
        // Arrange
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns(baseUrl);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = $"{baseUrl.TrimEnd('/')}/account/reset-password?token=xyz";

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, 60);

        // Assert - baseUrl should be present in output
        var expectedBaseUrl = baseUrl.TrimEnd('/');
        Assert.Contains(expectedBaseUrl, htmlBody);
        Assert.Contains(resetUrl, htmlBody);
    }

    [Fact]
    public async Task RenderPasswordResetEmailAsync_TextBodyContainsResetUrl()
    {
        // Arrange
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var resetUrl = "https://melodee.example.com/account/reset-password?token=plaintext123";

        // Act
        var (subject, textBody, htmlBody) = await service.RenderPasswordResetEmailAsync(resetUrl, 60);

        // Assert - text body should contain clickable URL for plain text email clients
        Assert.Contains(resetUrl, textBody);
        Assert.Contains("Reset your password using this link", textBody);
    }
}
