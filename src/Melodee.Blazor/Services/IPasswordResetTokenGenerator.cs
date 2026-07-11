using Melodee.Common.Models;
using Melodee.Common.Services;

namespace Melodee.Blazor.Services;

/// <summary>
/// Generates password reset tokens without coupling UI components to the full user service facade.
/// </summary>
public interface IPasswordResetTokenGenerator
{
    /// <summary>
    /// Generates a reset token for an eligible account identified by email address.
    /// </summary>
    Task<OperationResult<string?>> GeneratePasswordResetTokenAsync(
        string email,
        CancellationToken cancellationToken = default);
}

internal sealed class UserServicePasswordResetTokenGenerator(UserService userService) : IPasswordResetTokenGenerator
{
    public Task<OperationResult<string?>> GeneratePasswordResetTokenAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        return userService.GeneratePasswordResetTokenAsync(email, cancellationToken);
    }
}
