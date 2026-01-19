namespace Melodee.Cli.Client;

/// <summary>
/// Exception thrown when a remote Melodee API call fails.
/// Includes error type for deterministic exit code mapping.
/// </summary>
public class MelodeeRemoteException : Exception
{
    public enum RemoteErrorType
    {
        NetworkError,      // Exit code 10: DNS, connection refused, TLS handshake
        Timeout,           // Exit code 11
        Unauthorized,      // Exit code 12: HTTP 401
        Forbidden,         // Exit code 12: HTTP 403 (same as unauthorized)
        NotFound,          // Exit code 13: HTTP 404
        ServerError,       // Exit code 14: HTTP 5xx
        UnexpectedError    // Exit code 15: Serialization, unexpected responses
    }

    public RemoteErrorType ErrorType { get; }
    public int? HttpStatusCode { get; }

    public MelodeeRemoteException(string message, Exception? innerException, RemoteErrorType errorType, int? httpStatusCode = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Get the exit code for this error type.
    /// </summary>
    public int GetExitCode()
    {
        return ErrorType switch
        {
            RemoteErrorType.NetworkError => 10,
            RemoteErrorType.Timeout => 11,
            RemoteErrorType.Unauthorized => 12,
            RemoteErrorType.Forbidden => 12,
            RemoteErrorType.NotFound => 13,
            RemoteErrorType.ServerError => 14,
            RemoteErrorType.UnexpectedError => 15,
            _ => 15
        };
    }
}
