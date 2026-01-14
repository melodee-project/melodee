using Melodee.Common.Constants;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Melodee.Tests.Blazor.Services;

public class BaseUrlServiceAsyncTests
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<IMelodeeConfigurationFactory> _mockConfigurationFactory;
    private readonly Mock<IMelodeeConfiguration> _mockConfiguration;
    private readonly BaseUrlService _service;

    public BaseUrlServiceAsyncTests()
    {
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockConfigurationFactory = new Mock<IMelodeeConfigurationFactory>();
        _mockConfiguration = new Mock<IMelodeeConfiguration>();

        _mockConfigurationFactory.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockConfiguration.Object);

        _service = new BaseUrlService(_mockHttpContextAccessor.Object, _mockConfigurationFactory.Object);
    }

    [Fact]
    public async Task GetBaseUrlAsync_WithValidConfiguration_ReturnsConfiguredUrl()
    {
        const string expectedUrl = "https://example.com";
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns(expectedUrl);

        var result = await _service.GetBaseUrlAsync();

        Assert.Equal(expectedUrl, result);
    }

    [Fact]
    public async Task GetBaseUrlAsync_WithValidConfiguration_CachesResult()
    {
        const string expectedUrl = "https://cached.example.com";
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns(expectedUrl);

        var result1 = await _service.GetBaseUrlAsync();
        var result2 = await _service.GetBaseUrlAsync();

        Assert.Equal(expectedUrl, result1);
        Assert.Equal(expectedUrl, result2);
        _mockConfigurationFactory.Verify(
            x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBaseUrlAsync_WithMissingSystemBaseUrl_ReturnsNull()
    {
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns((string?)null);

        var result = await _service.GetBaseUrlAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBaseUrlAsync_WithRequiredNotSetValue_ReturnsNull()
    {
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns(MelodeeConfiguration.RequiredNotSetValue);

        var result = await _service.GetBaseUrlAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBaseUrlAsync_WithEmptyConfiguration_ReturnsNull()
    {
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns(string.Empty);

        var result = await _service.GetBaseUrlAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBaseUrlAsync_WithWhitespaceConfiguration_ReturnsNull()
    {
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns("   ");

        var result = await _service.GetBaseUrlAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBaseUrlAsync_DoesNotFallBackToHttpContext()
    {
        _mockConfiguration.Setup(x => x.GetValue<string?>(SettingRegistry.SystemBaseUrl, null))
            .Returns((string?)null);

        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(x => x.Scheme).Returns("https");
        mockRequest.Setup(x => x.Host).Returns(new HostString("should-not-be-used.com"));
        mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);

        var result = await _service.GetBaseUrlAsync();

        Assert.Null(result);
    }
}
