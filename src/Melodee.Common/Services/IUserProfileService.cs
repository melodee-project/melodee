using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Models.Collection;
using Microsoft.EntityFrameworkCore;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

/// <summary>
/// Service interface for user profile management operations.
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// Gets the database context factory for direct database access when needed.
    /// </summary>
    IDbContextFactory<MelodeeDbContext> GetContextFactory();

    /// <summary>
    /// Lists users with pagination and filtering.
    /// </summary>
    Task<MelodeeModels.PagedResult<UserDataInfo>> ListAsync(
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their ID.
    /// </summary>
    Task<MelodeeModels.OperationResult<User?>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their email address.
    /// </summary>
    Task<MelodeeModels.OperationResult<User?>> GetByEmailAddressAsync(
        string emailAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their username.
    /// </summary>
    Task<MelodeeModels.OperationResult<User?>> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user by their API key.
    /// </summary>
    Task<MelodeeModels.OperationResult<User?>> GetByApiKeyAsync(
        Guid apiKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user artist relationship.
    /// </summary>
    Task<UserArtist?> UserArtistAsync(int userId, Guid artistApiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user is an administrator.
    /// </summary>
    Task<bool> IsUserAdminAsync(
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user profile.
    /// </summary>
    Task<MelodeeModels.OperationResult<bool>> UpdateAsync(
        User currentUser,
        User detailToUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes users by their IDs.
    /// </summary>
    Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a user's profile image.
    /// </summary>
    Task<MelodeeModels.OperationResult<bool>> SaveProfileImageAsync(
        int userId,
        byte[] imageBytes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new user.
    /// </summary>
    Task<MelodeeModels.OperationResult<User?>> RegisterAsync(
        string username,
        string emailAddress,
        string plainTextPassword,
        string? registerPrivateCode,
        CancellationToken cancellationToken = default);
}
