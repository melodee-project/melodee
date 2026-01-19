using FluentAssertions;
using Melodee.Cli.Client;

namespace Melodee.Tests.Cli;

/// <summary>
/// Tests for remote exception exit code mapping.
/// </summary>
public class MelodeeRemoteExceptionTests
{
    [Fact]
    public void NetworkError_ReturnsExitCode10()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Network error",
            null,
            MelodeeRemoteException.RemoteErrorType.NetworkError);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(10);
    }

    [Fact]
    public void Timeout_ReturnsExitCode11()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Timeout",
            null,
            MelodeeRemoteException.RemoteErrorType.Timeout);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(11);
    }

    [Fact]
    public void Unauthorized_ReturnsExitCode12()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Unauthorized",
            null,
            MelodeeRemoteException.RemoteErrorType.Unauthorized,
            401);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(12);
        exception.HttpStatusCode.Should().Be(401);
    }

    [Fact]
    public void Forbidden_ReturnsExitCode12()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Forbidden",
            null,
            MelodeeRemoteException.RemoteErrorType.Forbidden,
            403);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(12);
        exception.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public void NotFound_ReturnsExitCode13()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Not found",
            null,
            MelodeeRemoteException.RemoteErrorType.NotFound,
            404);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(13);
    }

    [Fact]
    public void ServerError_ReturnsExitCode14()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Server error",
            null,
            MelodeeRemoteException.RemoteErrorType.ServerError,
            500);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(14);
    }

    [Fact]
    public void UnexpectedError_ReturnsExitCode15()
    {
        // Arrange
        var exception = new MelodeeRemoteException(
            "Unexpected error",
            null,
            MelodeeRemoteException.RemoteErrorType.UnexpectedError);
        
        // Act
        var exitCode = exception.GetExitCode();
        
        // Assert
        exitCode.Should().Be(15);
    }
}
