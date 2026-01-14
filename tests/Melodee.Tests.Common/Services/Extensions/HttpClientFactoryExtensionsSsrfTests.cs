using System.Net;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Services.Extensions;
using Melodee.Common.Services.Security;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.Extensions;

public class HttpClientFactoryExtensionsSsrfTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    public HttpClientFactoryExtensionsSsrfTests()
    {
        _loggerMock = new Mock<ILogger>();
        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();

        var configMock = new Mock<IMelodeeConfiguration>();
        configMock.Setup(x => x.GetValue<bool>(SettingRegistry.PodcastHttpAllowHttp)).Returns(false);
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configMock.Object);

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
    }

    private ISsrfValidator CreateValidator()
    {
        return new SsrfValidator(_loggerMock.Object, _configFactoryMock.Object);
    }

    [Fact]
    public async Task BytesForImageUrlAsync_WithLocalhostUrl_ReturnsNull()
    {
        var validator = CreateValidator();

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            validator,
            "test-agent",
            "http://127.0.0.1/image.jpg",
            _loggerMock.Object,
            CancellationToken.None);

        result.Should().BeNull();
        _loggerMock.Verify(
            x => x.Warning(It.Is<string>(s => s.Contains("SSRF validation failed")), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task BytesForImageUrlAsync_WithPrivateIpUrl_ReturnsNull()
    {
        var validator = CreateValidator();

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            validator,
            "test-agent",
            "http://192.168.1.1/image.jpg",
            _loggerMock.Object,
            CancellationToken.None);

        result.Should().BeNull();
        _loggerMock.Verify(
            x => x.Warning(It.Is<string>(s => s.Contains("SSRF validation failed")), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task BytesForImageUrlAsync_WithPrivate10NetworkUrl_ReturnsNull()
    {
        var validator = CreateValidator();

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            validator,
            "test-agent",
            "http://10.0.0.1/image.jpg",
            _loggerMock.Object,
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost/image.jpg")]
    [InlineData("http://127.0.0.1:8080/image.jpg")]
    [InlineData("http://[::1]/image.jpg")]
    public async Task BytesForImageUrlAsync_WithVariousLocalhostUrls_ReturnsNull(string url)
    {
        var validator = CreateValidator();

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            validator,
            "test-agent",
            url,
            _loggerMock.Object,
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task BytesForImageUrlAsync_WithoutSsrfValidator_StillProcessesRequest()
    {
        var handlerStub = new HttpHandlerStubDelegate((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]) // PNG header
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        });

        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerStub));

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            null,
            "test-agent",
            "https://example.com/image.png",
            _loggerMock.Object,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().HaveCount(4);
    }

    [Fact]
    public async Task BytesForImageUrlAsync_WithoutSsrfValidatorAndWithoutLogger_StillProcessesRequest()
    {
        var handlerStub = new HttpHandlerStubDelegate((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]) // PNG header
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        });

        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerStub));

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            null,
            "test-agent",
            "https://example.com/image.png",
            null,
            CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BytesForImageUrlAsync_WithNonSuccessStatusCode_ReturnsNull()
    {
        var handlerStub = new HttpHandlerStubDelegate((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        });

        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerStub));

        var validator = CreateValidator();

        var result = await _httpClientFactoryMock.Object.BytesForImageUrlAsync(
            validator,
            "test-agent",
            "https://example.com/image.jpg",
            _loggerMock.Object,
            CancellationToken.None);

        result.Should().BeNull();
        _loggerMock.Verify(
            x => x.Warning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()),
            Times.Once);
    }
}
