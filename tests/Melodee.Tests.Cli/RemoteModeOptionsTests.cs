using FluentAssertions;
using Melodee.Cli.Configuration;

namespace Melodee.Tests.Cli;

/// <summary>
/// Tests for remote mode options resolution and precedence.
/// </summary>
public class RemoteModeOptionsTests
{
    [Fact]
    public void Resolve_WithCliArgs_TakesPrecedenceOverEnvironment()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MELODEE_SERVER", "https://env.example.com");
        Environment.SetEnvironmentVariable("MELODEE_TOKEN", "env-token");
        
        // Act
        var options = RemoteModeOptions.Resolve("https://cli.example.com", "cli-token", null);
        
        // Assert
        options.Server.Should().Be("https://cli.example.com");
        options.Token.Should().Be("cli-token");
        
        // Cleanup
        Environment.SetEnvironmentVariable("MELODEE_SERVER", null);
        Environment.SetEnvironmentVariable("MELODEE_TOKEN", null);
    }

    [Fact]
    public void Resolve_WithoutCliArgs_UsesEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MELODEE_SERVER", "https://env.example.com");
        Environment.SetEnvironmentVariable("MELODEE_TOKEN", "env-token");
        
        // Act
        var options = RemoteModeOptions.Resolve(null, null, null);
        
        // Assert
        options.Server.Should().Be("https://env.example.com");
        options.Token.Should().Be("env-token");
        
        // Cleanup
        Environment.SetEnvironmentVariable("MELODEE_SERVER", null);
        Environment.SetEnvironmentVariable("MELODEE_TOKEN", null);
    }

    [Fact]
    public void GetNormalizedBaseUrl_RemovesTrailingSlash()
    {
        // Arrange
        var options = new RemoteModeOptions { Server = "https://example.com/" };
        
        // Act
        var normalized = options.GetNormalizedBaseUrl();
        
        // Assert
        normalized.Should().Be("https://example.com");
    }

    [Fact]
    public void GetNormalizedBaseUrl_RemovesApiV1Suffix()
    {
        // Arrange
        var options = new RemoteModeOptions { Server = "https://example.com/api/v1" };
        
        // Act
        var normalized = options.GetNormalizedBaseUrl();
        
        // Assert
        normalized.Should().Be("https://example.com");
    }

    [Fact]
    public void GetApiBaseUrl_AppendsApiV1()
    {
        // Arrange
        var options = new RemoteModeOptions { Server = "https://example.com" };
        
        // Act
        var apiUrl = options.GetApiBaseUrl();
        
        // Assert
        apiUrl.Should().Be("https://example.com/api/v1");
    }

    [Fact]
    public void MaskToken_WithGuid_ReturnsStandardMask()
    {
        // Arrange
        var token = "12345678-1234-1234-1234-123456789012";
        
        // Act
        var masked = RemoteModeOptions.MaskToken(token);
        
        // Assert
        masked.Should().Be("********-****-****-****-************");
    }

    [Fact]
    public void MaskToken_WithShortString_MasksAll()
    {
        // Arrange
        var token = "short";
        
        // Act
        var masked = RemoteModeOptions.MaskToken(token);
        
        // Assert
        masked.Should().Be("*****");
    }

    [Fact]
    public void IsRemoteMode_WithServer_ReturnsTrue()
    {
        // Arrange
        var options = new RemoteModeOptions { Server = "https://example.com" };
        
        // Act & Assert
        options.IsRemoteMode.Should().BeTrue();
    }

    [Fact]
    public void IsRemoteMode_WithoutServer_ReturnsFalse()
    {
        // Arrange
        var options = new RemoteModeOptions { Server = null };
        
        // Act & Assert
        options.IsRemoteMode.Should().BeFalse();
    }
}
