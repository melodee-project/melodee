using Melodee.Blazor.Services.Email;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NodaTime;

namespace Melodee.Tests.Blazor.Services.Email;

/// <summary>
/// Tests for email template service security features.
/// Verifies language code validation and path containment.
/// </summary>
public class EmailTemplateServiceSecurityTests
{
    private readonly Mock<IMelodeeConfigurationFactory> _mockConfigFactory;
    private readonly Mock<IMelodeeConfiguration> _mockConfig;
    private readonly Mock<LibraryService> _mockLibraryService;

    public EmailTemplateServiceSecurityTests()
    {
        _mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfig = new Mock<IMelodeeConfiguration>();
        _mockLibraryService = new Mock<LibraryService>();

        _mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConfig.Object);

        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("https://melodee.test");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenderPasswordResetEmailAsync_WithNullOrEmptyLanguageCode_UsesEnglish(string? languageCode)
    {
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var result = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60, languageCode);

        Assert.NotNull(result.subject);
        Assert.NotEmpty(result.subject);
        Assert.NotNull(result.textBody);
        Assert.NotEmpty(result.textBody);
        Assert.NotNull(result.htmlBody);
        Assert.NotEmpty(result.htmlBody);
    }

    [Theory]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("en-us/../secret")]
    [InlineData("en-us\\..\\etc\\passwd")]
    public async Task RenderPasswordResetEmailAsync_WithPathTraversalInLanguageCode_FallsBackToEnglish(string maliciousCode)
    {
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var result = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60, maliciousCode);

        Assert.NotNull(result.subject);
        Assert.NotEmpty(result.subject);
        Assert.NotNull(result.textBody);
        Assert.NotEmpty(result.textBody);
        Assert.NotNull(result.htmlBody);
        Assert.NotEmpty(result.htmlBody);
    }

    [Theory]
    [InlineData("xx-XX")]
    [InlineData("invalid")]
    [InlineData("en-US-invalid")]
    public async Task RenderPasswordResetEmailAsync_WithInvalidLanguageCode_FallsBackToEnglish(string invalidCode)
    {
        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var result = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60, invalidCode);

        Assert.NotNull(result.subject);
        Assert.NotEmpty(result.subject);
        Assert.NotNull(result.textBody);
        Assert.NotEmpty(result.textBody);
        Assert.NotNull(result.htmlBody);
        Assert.NotEmpty(result.htmlBody);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    [InlineData("fr-FR")]
    [InlineData("it-IT")]
    [InlineData("ja-JP")]
    [InlineData("pt-BR")]
    [InlineData("ru-RU")]
    [InlineData("zh-CN")]
    [InlineData("ar-SA")]
    public async Task RenderPasswordResetEmailAsync_WithValidCultureCode_DoesNotFallback(string validCode)
    {
        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.EmailResetPasswordSubject))
            .Returns((string?)null);

        var service = new EmailTemplateService(_mockConfigFactory.Object, _mockLibraryService.Object);
        var result = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60, validCode);

        Assert.Equal("Reset your password", result.subject);
    }
}

/// <summary>
/// Tests for path containment in template loading.
/// Verifies that template loading cannot escape the library root.
/// </summary>
public class EmailTemplateServicePathContainmentTests
{
    private readonly Mock<IMelodeeConfigurationFactory> _mockConfigFactory;
    private readonly Mock<IMelodeeConfiguration> _mockConfig;
    private readonly Mock<ILogger<EmailTemplateService>> _mockLogger;

    private static OperationResult<Library> CreateTemplatesLibraryResult(string path)
    {
        return new OperationResult<Library>
        {
            Data = new Library
            {
                Name = "Templates",
                Type = (int)LibraryType.Templates,
                Path = path,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            }
        };
    }

    public EmailTemplateServicePathContainmentTests()
    {
        _mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfig = new Mock<IMelodeeConfiguration>();
        _mockLogger = new Mock<ILogger<EmailTemplateService>>();

        _mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConfig.Object);

        _mockConfig.Setup(c => c.GetValue<string>(SettingRegistry.SystemBaseUrl))
            .Returns("https://melodee.test");
    }

    [Fact]
    public async Task LoadTemplateFromLibraryAsync_WithValidPath_ReadsSuccessfully()
    {
        var mockLibraryService = new Mock<LibraryService>();
        mockLibraryService.Setup(l => l.GetTemplatesLibraryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTemplatesLibraryResult("/var/templates"));

        var service = new EmailTemplateService(_mockConfigFactory.Object, mockLibraryService.Object, _mockLogger.Object);

        var result = await service.RenderPasswordResetEmailAsync("https://test.com/reset", 60, "en-us");

        Assert.NotNull(result.textBody);
        Assert.NotEmpty(result.textBody);
        Assert.NotNull(result.htmlBody);
        Assert.NotEmpty(result.htmlBody);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("escape root directory")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((o, e) => true)),
            Times.Never);
    }
}
