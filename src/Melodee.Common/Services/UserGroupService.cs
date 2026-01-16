using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;

namespace Melodee.Common.Services;

public sealed class UserGroupService : ServiceBase
{
    private const string CacheKeyDetailTemplate = $"{UserGroup.CacheRegion}:urn:usergroup:{{0}}";
    private const string CacheKeyDetailByApiKeyTemplate = $"{UserGroup.CacheRegion}:urn:usergroup:apikey:{{0}}";
    private const string CacheKeyListAll = $"{UserGroup.CacheRegion}:urn:usergroups:all";
    private const string CacheKeyUserGroupsForUserTemplate = $"{UserGroup.CacheRegion}:urn:user:{{0}}:groups";

    public UserGroupService(
        ILogger logger,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory) : base(logger, cacheManager, contextFactory)
    {
    }

    public async Task<OperationResult<UserGroup?>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext
                .UserGroups
                .AsNoTracking()
                .Include(x => x.Members)
                .ThenInclude(x => x.User)
                .Include(x => x.LibraryAccessControls)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

        return new OperationResult<UserGroup?>
        {
            Data = result
        };
    }

    public async Task<OperationResult<UserGroup?>> GetByApiKeyAsync(Guid apiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => apiKey == Guid.Empty, apiKey, nameof(apiKey));

        var id = await CacheManager.GetAsync(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext.UserGroups
                .AsNoTracking()
                .Where(x => x.ApiKey == apiKey)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

        if (id == null)
        {
            return new OperationResult<UserGroup?>("Unknown user group.")
            {
                Data = null
            };
        }

        return await GetByIdAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<UserGroup[]>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = await CacheManager.GetAsync(CacheKeyListAll, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext
                .UserGroups
                .AsNoTracking()
                .Include(x => x.Members)
                .OrderBy(x => x.Name)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

        return new OperationResult<UserGroup[]>
        {
            Data = result
        };
    }

    public async Task<OperationResult<UserGroup[]>> GetGroupsForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        var result = await CacheManager.GetAsync(CacheKeyUserGroupsForUserTemplate.FormatSmart(userId), async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext
                .UserGroupMembers
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Include(x => x.UserGroup)
                .Select(x => x.UserGroup!)
                .OrderBy(x => x.Name)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

        return new OperationResult<UserGroup[]>
        {
            Data = result
        };
    }

    public async Task<OperationResult<UserGroup>> CreateAsync(UserGroup userGroup, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(userGroup, nameof(userGroup));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var existing = await scopedContext.UserGroups
            .FirstOrDefaultAsync(x => x.Name == userGroup.Name, cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            return new OperationResult<UserGroup>("User group with this name already exists.")
            {
                Type = OperationResponseType.Error,
                Data = existing
            };
        }
        
        scopedContext.UserGroups.Add(userGroup);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache();

        return new OperationResult<UserGroup>
        {
            Data = userGroup
        };
    }

    public async Task<OperationResult<UserGroup>> UpdateAsync(UserGroup userGroup, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(userGroup, nameof(userGroup));
        Guard.Against.Expression(x => x < 1, userGroup.Id, nameof(userGroup.Id));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var existing = await scopedContext.UserGroups
            .FirstOrDefaultAsync(x => x.Id == userGroup.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing == null)
        {
            return new OperationResult<UserGroup>("User group not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = userGroup
            };
        }

        existing.Name = userGroup.Name;
        existing.Description = userGroup.Description;
        existing.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache();

        return new OperationResult<UserGroup>
        {
            Data = existing
        };
    }

    public async Task<OperationResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        
        var userGroup = await scopedContext.UserGroups
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (userGroup == null)
        {
            return new OperationResult<bool>("User group not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = false
            };
        }

        scopedContext.UserGroups.Remove(userGroup);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache();

        return new OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<OperationResult<bool>> AddUserToGroupAsync(int userId, int userGroupId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x < 1, userGroupId, nameof(userGroupId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await scopedContext.UserGroupMembers
            .FirstOrDefaultAsync(x => x.UserId == userId && x.UserGroupId == userGroupId, cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            return new OperationResult<bool>("User is already a member of this group.")
            {
                Type = OperationResponseType.Error,
                Data = false
            };
        }

        var member = new UserGroupMember
        {
            UserId = userId,
            UserGroupId = userGroupId,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        scopedContext.UserGroupMembers.Add(member);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache();

        return new OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<OperationResult<bool>> RemoveUserFromGroupAsync(int userId, int userGroupId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x < 1, userGroupId, nameof(userGroupId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var member = await scopedContext.UserGroupMembers
            .FirstOrDefaultAsync(x => x.UserId == userId && x.UserGroupId == userGroupId, cancellationToken)
            .ConfigureAwait(false);

        if (member == null)
        {
            return new OperationResult<bool>("User is not a member of this group.")
            {
                Type = OperationResponseType.NotFound,
                Data = false
            };
        }

        scopedContext.UserGroupMembers.Remove(member);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache();

        return new OperationResult<bool>
        {
            Data = true
        };
    }

    private void ClearCache()
    {
        CacheManager.ClearRegion(UserGroup.CacheRegion);
    }
}
