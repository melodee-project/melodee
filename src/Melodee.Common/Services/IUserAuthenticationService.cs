using Melodee.Common.Data.Models;
using Melodee.Common.Models;

namespace Melodee.Common.Services;

/// <summary>
/// Service interface for user authentication operations.
/// </summary>
public interface IUserAuthenticationService
{
    /// <summary>
    /// Logs a user in using their username and password.
    /// </summary>
    Task<OperationResult<User?>> LoginUserByUsernameAsync(
        string userName,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a user in using their email address and password.
    /// </summary>
    Task<OperationResult<User?>> LoginUserAsync(
        string emailAddress,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates credentials and applies login side effects for an identified user.
    /// </summary>
    Task<OperationResult<User?>> CompleteLoginAsync(
        User user,
        string password,
        string identifier,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a user token for OpenSubsonic API authentication.
    /// </summary>
    Task<OperationResult<User?>> ValidateTokenAsync(
        string username,
        string token,
        string salt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a salt for password hashing.
    /// </summary>
    string GenerateSalt(int saltLength = 16, int logRounds = 10);

    /// <summary>
    /// Generate a secure OpenSubsonic secret.
    /// </summary>
    string GenerateOpenSubsonicSecret();
}
