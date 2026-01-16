using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Extensions;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SmartFormat;

namespace Melodee.Common.Services;

/// <summary>
/// Handles library access control authorization.
/// Policy: If a library has no access controls, it's accessible to all authenticated users.
/// If a library has one or more access controls, users must be in at least one allowed group.
/// </summary>
public sealed class LibraryAuthorizationService : ServiceBase
{
    private const string CacheKeyUserCanAccessLibraryTemplate = "urn:user:{0}:library:{1}:access";
    private const string CacheKeyUserAccessibleLibrariesTemplate = "urn:user:{0}:accessible-libraries";
    private const string CacheKeyLibraryHasRestrictionsTemplate = "urn:library:{0}:has-restrictions";

    public LibraryAuthorizationService(
        ILogger logger,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory) : base(logger, cacheManager, contextFactory)
    {
    }

    /// <summary>
    /// Checks if a user can access a specific library.
    /// </summary>
    public async Task<bool> CanUserAccessLibraryAsync(int userId, int libraryId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x < 1, libraryId, nameof(libraryId));

        var cacheKey = CacheKeyUserCanAccessLibraryTemplate.FormatSmart(userId, libraryId);
        
        return await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Check if library has any access controls
            var hasRestrictions = await LibraryHasRestrictionsAsync(libraryId, scopedContext, cancellationToken).ConfigureAwait(false);
            
            if (!hasRestrictions)
            {
                // No restrictions = accessible to all authenticated users
                return true;
            }

            // Library has restrictions - check if user is in an allowed group
            var hasAccess = await scopedContext.LibraryAccessControls
                .AsNoTracking()
                .Where(lac => lac.LibraryId == libraryId)
                .Join(
                    scopedContext.UserGroupMembers.Where(ugm => ugm.UserId == userId),
                    lac => lac.UserGroupId,
                    ugm => ugm.UserGroupId,
                    (lac, ugm) => lac.Id
                )
                .AnyAsync(cancellationToken)
                .ConfigureAwait(false);

            return hasAccess;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets all library IDs that a user can access.
    /// </summary>
    public async Task<int[]> GetAccessibleLibraryIdsForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var cacheKey = CacheKeyUserAccessibleLibrariesTemplate.FormatSmart(userId);
        
        return await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Get all libraries
            var allLibraryIds = await scopedContext.Libraries
                .AsNoTracking()
                .Select(l => l.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            // Get libraries with no restrictions (accessible to all)
            var unrestrictedLibraryIds = await scopedContext.Libraries
                .AsNoTracking()
                .Where(l => !scopedContext.LibraryAccessControls.Any(lac => lac.LibraryId == l.Id))
                .Select(l => l.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            // Get libraries where user is in an allowed group
            var restrictedAccessibleLibraryIds = await scopedContext.LibraryAccessControls
                .AsNoTracking()
                .Join(
                    scopedContext.UserGroupMembers.Where(ugm => ugm.UserId == userId),
                    lac => lac.UserGroupId,
                    ugm => ugm.UserGroupId,
                    (lac, ugm) => lac.LibraryId
                )
                .Distinct()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            // Combine both sets
            var accessibleLibraryIds = unrestrictedLibraryIds
                .Concat(restrictedAccessibleLibraryIds)
                .Distinct()
                .ToArray();

            return accessibleLibraryIds;
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if a library has any access control restrictions.
    /// </summary>
    private async Task<bool> LibraryHasRestrictionsAsync(int libraryId, MelodeeDbContext context, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeyLibraryHasRestrictionsTemplate.FormatSmart(libraryId);
        
        return await CacheManager.GetAsync(cacheKey, async () =>
        {
            return await context.LibraryAccessControls
                .AsNoTracking()
                .AnyAsync(lac => lac.LibraryId == libraryId, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Clears all authorization-related caches. Should be called when access controls are modified.
    /// </summary>
    public void ClearAuthorizationCache()
    {
        CacheManager.ClearRegion("urn:region:library-authorization");
    }
}
