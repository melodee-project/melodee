using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using Melodee.Blazor.Components;
using Melodee.Blazor.Components.Pages.Account;
using Melodee.Blazor.Services.Email;
using Melodee.Common.Constants;
using Melodee.Common.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Radzen;

namespace Melodee.Tests.Blazor.Components.Pages;

public sealed class ForgotPasswordTests : BunitContext
{
    private const string SensitiveEmail = "sensitive.user@example.test";
    private const string MaskedEmail = "se***@example.test";
    private const string ResetToken = "sensitive-reset-token-123456";
    private const string ConfiguredSubject = "Sensitive configured reset subject";
    private const string SafeBaseUrl = "https://public.example.test/melodee";
    private const string CredentialBearingBaseUrl = "https://admin:database-password@example.test/melodee";

    private readonly MelodeeConfiguration _configuration;
    private readonly Mock<IPasswordResetTokenGenerator> _tokenGenerator = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IEmailTemplateService> _emailTemplateService = new();
    private readonly Mock<IRateLimiterService> _rateLimiter = new();
    private readonly RecordingLogger<ForgotPassword> _logger = new();

    public ForgotPasswordTests()
    {
        _configuration = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            [SettingRegistry.EmailEnabled] = true,
            [SettingRegistry.SystemBaseUrl] = SafeBaseUrl,
            [SettingRegistry.SecurityPasswordResetTokenExpiryMinutes] = 60,
            [SettingRegistry.DefaultsPageSize] = 25,
            [SettingRegistry.UserInterfaceToastAutoCloseTime] = 1000
        });

        var configurationFactory = new Mock<IMelodeeConfigurationFactory>();
        configurationFactory
            .Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_configuration);

        var localizationService = new Mock<ILocalizationService>();
        localizationService.Setup(x => x.Localize(It.IsAny<string>()))
            .Returns<string>(key => key);
        localizationService.Setup(x => x.GetUserCultureAsync())
            .ReturnsAsync(CultureInfo.GetCultureInfo("en-US"));
        localizationService.Setup(x => x.SetCultureAsync(It.IsAny<CultureInfo>()))
            .Returns(Task.CompletedTask);

        var authenticationStateProvider = new Mock<AuthenticationStateProvider>();
        authenticationStateProvider.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        _rateLimiter.Setup(x => x.IsAllowed(It.IsAny<string>(), 3, 60)).Returns(true);
        _tokenGenerator
            .Setup(x => x.GeneratePasswordResetTokenAsync(SensitiveEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<string?> { Data = ResetToken });
        _emailTemplateService
            .Setup(x => x.RenderPasswordResetEmailAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfiguredSubject, "Reset email text", "<p>Reset email HTML</p>"));
        _emailSender
            .Setup(x => x.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Services.AddSingleton(configurationFactory.Object);
        Services.AddSingleton(localizationService.Object);
        Services.AddSingleton(authenticationStateProvider.Object);
        Services.AddSingleton(_tokenGenerator.Object);
        Services.AddSingleton(_emailSender.Object);
        Services.AddSingleton(_emailTemplateService.Object);
        Services.AddSingleton(_rateLimiter.Object);
        Services.AddSingleton<ILogger<ForgotPassword>>(_logger);
        Services.AddSingleton<DialogService>();

        ComponentFactories.AddStub<CustomBlock>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Submit_WithEligibleAccount_SendsResetEmailWithoutLoggingSensitiveValues()
    {
        var component = Render<ForgotPassword>();

        await SubmitAsync(component);

        component.Markup.Should().Contain("Auth.PasswordResetEmailSent");
        _emailTemplateService.Verify(x => x.RenderPasswordResetEmailAsync(
            $"{SafeBaseUrl}/account/reset-password?token={ResetToken}",
            60,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _emailSender.Verify(x => x.SendAsync(
            SensitiveEmail,
            ConfiguredSubject,
            "Reset email text",
            "<p>Reset email HTML</p>",
            It.IsAny<CancellationToken>()), Times.Once);
        _rateLimiter.Verify(x => x.RecordAttempt($"forgot-password:{SensitiveEmail}", 60), Times.Once);
        _logger.Output.Should().Contain("Token length");
        _logger.Output.Should().Contain("Text length");
        AssertSensitiveValuesAreNotLogged(SafeBaseUrl);
    }

    [Fact]
    public async Task Submit_WhenAccountIsUnknown_ShowsSameSuccessResponseWithoutLoggingSensitiveValues()
    {
        _tokenGenerator
            .Setup(x => x.GeneratePasswordResetTokenAsync(SensitiveEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<string?>
            {
                Data = null,
                Type = OperationResponseType.NotFound
            });
        var component = Render<ForgotPassword>();

        await SubmitAsync(component);

        component.Markup.Should().Contain("Auth.PasswordResetEmailSent");
        _emailTemplateService.Verify(x => x.RenderPasswordResetEmailAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _logger.Output.Should().Contain("without generating a token");
        AssertSensitiveValuesAreNotLogged(SafeBaseUrl);
    }

    [Fact]
    public async Task Submit_WhenEmailDeliveryFails_KeepsGenericResponseAndDoesNotLogSubject()
    {
        _emailSender
            .Setup(x => x.SendAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var component = Render<ForgotPassword>();

        await SubmitAsync(component);

        component.Markup.Should().Contain("Auth.PasswordResetEmailSent");
        _logger.Output.Should().Contain("Failed to send a password reset email");
        AssertSensitiveValuesAreNotLogged(SafeBaseUrl);
    }

    [Fact]
    public async Task Submit_WhenRateLimited_DoesNotLogEmailOrAttemptTokenGeneration()
    {
        _rateLimiter.Setup(x => x.IsAllowed(It.IsAny<string>(), 3, 60)).Returns(false);
        var component = Render<ForgotPassword>();

        await SubmitAsync(component);

        component.Markup.Should().Contain("Auth.TooManyAttempts");
        _tokenGenerator.Verify(x => x.GeneratePasswordResetTokenAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        AssertSensitiveValuesAreNotLogged(SafeBaseUrl);
    }

    [Fact]
    public async Task Submit_WhenDependencyThrows_DoesNotLogExceptionPayloadAndKeepsGenericResponse()
    {
        var sensitiveExceptionMessage = string.Join(' ',
            SensitiveEmail,
            MaskedEmail,
            ResetToken,
            CredentialBearingBaseUrl,
            ConfiguredSubject);
        _tokenGenerator
            .Setup(x => x.GeneratePasswordResetTokenAsync(SensitiveEmail, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(sensitiveExceptionMessage));
        var component = Render<ForgotPassword>();

        await SubmitAsync(component);

        component.Markup.Should().Contain("Auth.PasswordResetEmailSent");
        _logger.Output.Should().Contain("Exception type: InvalidOperationException");
        _logger.Output.Should().NotContain(sensitiveExceptionMessage);
        AssertSensitiveValuesAreNotLogged(SafeBaseUrl);
    }

    [Fact]
    public async Task Submit_WithCredentialBearingBaseUrl_RejectsUrlWithoutLoggingIt()
    {
        _configuration.SetSetting(SettingRegistry.SystemBaseUrl, CredentialBearingBaseUrl);
        var component = Render<ForgotPassword>();

        await SubmitAsync(component);

        component.Markup.Should().Contain("Auth.PasswordResetUnavailable");
        component.Markup.Should().NotContain("Auth.PasswordResetEmailSent");
        _tokenGenerator.Verify(x => x.GeneratePasswordResetTokenAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        AssertSensitiveValuesAreNotLogged(CredentialBearingBaseUrl);
    }

    [Theory]
    [InlineData("https://example.test", "https://example.test")]
    [InlineData("https://example.test/", "https://example.test")]
    [InlineData(" http://example.test:8080/melodee/ ", "http://example.test:8080/melodee")]
    public void TryGetSafeBaseUrl_WithSafeAbsoluteHttpUrl_NormalizesUrl(string configuredBaseUrl, string expected)
    {
        var isValid = ForgotPassword.TryGetSafeBaseUrl(configuredBaseUrl, out var actual);

        isValid.Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("** REQUIRED: THIS MUST BE EDITED **")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.test")]
    [InlineData("http:/missing-host")]
    [InlineData("https://admin:password@example.test")]
    [InlineData("https://example.test/melodee?returnUrl=https://attacker.test")]
    [InlineData("https://example.test/melodee#sensitive-token")]
    public void TryGetSafeBaseUrl_WithUnsafeOrMalformedUrl_RejectsUrl(string? configuredBaseUrl)
    {
        var isValid = ForgotPassword.TryGetSafeBaseUrl(configuredBaseUrl, out var actual);

        isValid.Should().BeFalse();
        actual.Should().BeEmpty();
    }

    private static async Task SubmitAsync(IRenderedComponent<ForgotPassword> component)
    {
        component.Find("input[name='Email']").Change(SensitiveEmail);
        await component.Find("form").SubmitAsync();
    }

    private void AssertSensitiveValuesAreNotLogged(params string[] additionalValues)
    {
        var sensitiveValues = new[]
            {
                SensitiveEmail,
                MaskedEmail,
                ResetToken,
                CredentialBearingBaseUrl,
                ConfiguredSubject
            }
            .Concat(additionalValues);

        _logger.Output.Should().NotContainAny(sensitiveValues);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public string Output => string.Join(Environment.NewLine, _entries);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                _entries.Enqueue(exception.ToString());
            }
        }
    }
}
