using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for managing user device profiles and transcoding decisions.
/// </summary>
public class UserDeviceProfileService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyTemplate = "urn:userdeviceprofile:{0}";
    private const string CacheKeyByUserAndPlayerTemplate = "urn:userdeviceprofile:user:{0}:player:{1}";
    private const string CacheKeyDefaultByUserTemplate = "urn:userdeviceprofile:user:{0}:default";
    private const string CacheRegion = "UserDeviceProfile";

    /// <summary>
    /// Get device profile by ID
    /// </summary>
    public async Task<OperationResult<UserDeviceProfile>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = string.Format(CacheKeyTemplate, id);
        var profile = await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
            return await scopedContext.UserDeviceProfiles
                .Include(p => p.User)
                .Include(p => p.Player)
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        }, cancellationToken, region: CacheRegion);

        if (profile == null)
        {
            return new OperationResult<UserDeviceProfile>("Profile not found")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        return new OperationResult<UserDeviceProfile> { Data = profile };
    }

    /// <summary>
    /// Get device profile for a specific user and player
    /// </summary>
    public async Task<OperationResult<UserDeviceProfile>> GetByUserAndPlayerAsync(int userId, int playerId, CancellationToken cancellationToken = default)
    {
        var cacheKey = string.Format(CacheKeyByUserAndPlayerTemplate, userId, playerId);
        var profile = await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
            return await scopedContext.UserDeviceProfiles
                .Include(p => p.User)
                .Include(p => p.Player)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.PlayerId == playerId, cancellationToken);
        }, cancellationToken, region: CacheRegion);

        if (profile == null)
        {
            return new OperationResult<UserDeviceProfile>("Profile not found")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        return new OperationResult<UserDeviceProfile> { Data = profile };
    }

    /// <summary>
    /// Get default device profile for a user
    /// </summary>
    public async Task<OperationResult<UserDeviceProfile>> GetDefaultByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = string.Format(CacheKeyDefaultByUserTemplate, userId);
        var profile = await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
            return await scopedContext.UserDeviceProfiles
                .Include(p => p.User)
                .Include(p => p.Player)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.IsDefaultProfile, cancellationToken);
        }, cancellationToken, region: CacheRegion);

        if (profile == null)
        {
            return new OperationResult<UserDeviceProfile>("Default profile not found")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        return new OperationResult<UserDeviceProfile> { Data = profile };
    }

    /// <summary>
    /// List all device profiles for a user
    /// </summary>
    public async Task<PagedResult<UserDeviceProfile>> ListByUserAsync(int userId, PagedRequest pagedRequest, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);
        
        var query = scopedContext.UserDeviceProfiles
            .Where(p => p.UserId == userId)
            .Include(p => p.Player)
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        
        var profiles = await query
            .OrderByDescending(p => p.IsDefaultProfile)
            .ThenByDescending(p => p.Priority)
            .ThenBy(p => p.Name)
            .Skip(pagedRequest.SkipValue)
            .Take(pagedRequest.TakeValue)
            .ToArrayAsync(cancellationToken);

        return new PagedResult<UserDeviceProfile>
        {
            TotalCount = totalCount,
            TotalPages = pagedRequest.TotalPages(totalCount),
            Data = profiles
        };
    }

    /// <summary>
    /// Create a new device profile
    /// </summary>
    public async Task<OperationResult<UserDeviceProfile>> CreateAsync(UserDeviceProfile profile, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(profile);

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);

        // If this is a default profile, unset any existing default for this user
        if (profile.IsDefaultProfile)
        {
            var existingDefaults = await scopedContext.UserDeviceProfiles
                .Where(p => p.UserId == profile.UserId && p.IsDefaultProfile)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefaultProfile = false;
            }
        }

        // Validate that DirectPlay and transcoding settings are consistent
        if (profile.DirectPlay && (profile.TargetCodec != null || profile.MaxBitrate != null))
        {
            return new OperationResult<UserDeviceProfile>("DirectPlay profiles should not have TargetCodec or MaxBitrate set")
            {
                Type = OperationResponseType.ValidationFailure,
                Data = null!
            };
        }

        if (!profile.DirectPlay && string.IsNullOrWhiteSpace(profile.TargetCodec))
        {
            return new OperationResult<UserDeviceProfile>("Transcoding profiles must have TargetCodec set")
            {
                Type = OperationResponseType.ValidationFailure,
                Data = null!
            };
        }

        scopedContext.UserDeviceProfiles.Add(profile);
        await scopedContext.SaveChangesAsync(cancellationToken);

        // Clear caches
        InvalidateCaches(profile.UserId, profile.PlayerId);

        Logger.Information("[{ServiceName}] Created device profile [{ProfileId}] for user [{UserId}]", 
            nameof(UserDeviceProfileService), profile.Id, profile.UserId);

        return new OperationResult<UserDeviceProfile> { Data = profile };
    }

    /// <summary>
    /// Update an existing device profile
    /// </summary>
    public async Task<OperationResult<UserDeviceProfile>> UpdateAsync(UserDeviceProfile profile, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(profile);

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await scopedContext.UserDeviceProfiles.FindAsync([profile.Id], cancellationToken);
        if (existing == null)
        {
            return new OperationResult<UserDeviceProfile>("Profile not found")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        // If this is being set as default, unset any existing default for this user
        if (profile.IsDefaultProfile && !existing.IsDefaultProfile)
        {
            var existingDefaults = await scopedContext.UserDeviceProfiles
                .Where(p => p.UserId == profile.UserId && p.IsDefaultProfile && p.Id != profile.Id)
                .ToListAsync(cancellationToken);

            foreach (var otherDefault in existingDefaults)
            {
                otherDefault.IsDefaultProfile = false;
            }
        }

        // Validate consistency
        if (profile.DirectPlay && (profile.TargetCodec != null || profile.MaxBitrate != null))
        {
            return new OperationResult<UserDeviceProfile>("DirectPlay profiles should not have TargetCodec or MaxBitrate set")
            {
                Type = OperationResponseType.ValidationFailure,
                Data = null!
            };
        }

        if (!profile.DirectPlay && string.IsNullOrWhiteSpace(profile.TargetCodec))
        {
            return new OperationResult<UserDeviceProfile>("Transcoding profiles must have TargetCodec set")
            {
                Type = OperationResponseType.ValidationFailure,
                Data = null!
            };
        }

        existing.Name = profile.Name;
        existing.IsDefaultProfile = profile.IsDefaultProfile;
        existing.DirectPlay = profile.DirectPlay;
        existing.TargetCodec = profile.TargetCodec;
        existing.MaxBitrate = profile.MaxBitrate;
        existing.ResampleRate = profile.ResampleRate;
        existing.Priority = profile.Priority;
        existing.PlayerId = profile.PlayerId;

        await scopedContext.SaveChangesAsync(cancellationToken);

        // Clear caches
        InvalidateCaches(profile.UserId, profile.PlayerId);

        Logger.Information("[{ServiceName}] Updated device profile [{ProfileId}] for user [{UserId}]", 
            nameof(UserDeviceProfileService), profile.Id, profile.UserId);

        return new OperationResult<UserDeviceProfile> { Data = existing };
    }

    /// <summary>
    /// Delete a device profile
    /// </summary>
    public async Task<OperationResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var profile = await scopedContext.UserDeviceProfiles.FindAsync([id], cancellationToken);
        if (profile == null)
        {
            return new OperationResult<bool>("Profile not found")
            {
                Type = OperationResponseType.NotFound,
                Data = false
            };
        }

        var userId = profile.UserId;
        var playerId = profile.PlayerId;

        scopedContext.UserDeviceProfiles.Remove(profile);
        await scopedContext.SaveChangesAsync(cancellationToken);

        // Clear caches
        InvalidateCaches(userId, playerId);

        Logger.Information("[{ServiceName}] Deleted device profile [{ProfileId}] for user [{UserId}]", 
            nameof(UserDeviceProfileService), id, userId);

        return new OperationResult<bool> { Data = true };
    }

    /// <summary>
    /// Determine the effective transcoding profile for a user and player.
    /// Implements precedence: per-player > per-user default > global default (direct play).
    /// </summary>
    public async Task<UserDeviceProfile> GetEffectiveProfileAsync(int userId, int? playerId, CancellationToken cancellationToken = default)
    {
        // 1. Check for per-player profile (highest priority)
        if (playerId.HasValue)
        {
            var playerProfile = await GetByUserAndPlayerAsync(userId, playerId.Value, cancellationToken);
            if (playerProfile.IsSuccess && playerProfile.Data != null)
            {
                Logger.Debug("[{ServiceName}] Using per-player profile [{ProfileName}] for user [{UserId}], player [{PlayerId}]",
                    nameof(UserDeviceProfileService), playerProfile.Data.Name, userId, playerId);
                return playerProfile.Data;
            }
        }

        // 2. Check for per-user default profile
        var userDefault = await GetDefaultByUserAsync(userId, cancellationToken);
        if (userDefault.IsSuccess && userDefault.Data != null)
        {
            Logger.Debug("[{ServiceName}] Using user default profile [{ProfileName}] for user [{UserId}]",
                nameof(UserDeviceProfileService), userDefault.Data.Name, userId);
            return userDefault.Data;
        }

        // 3. Fall back to global default (direct play)
        Logger.Debug("[{ServiceName}] Using global default (direct play) for user [{UserId}]",
            nameof(UserDeviceProfileService), userId);
        
        return new UserDeviceProfile
        {
            UserId = userId,
            Name = "Global Default - Direct Play",
            DirectPlay = true,
            IsDefaultProfile = false,
            Priority = 0,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    private void InvalidateCaches(int userId, int? playerId)
    {
        CacheManager.Remove(string.Format(CacheKeyDefaultByUserTemplate, userId), CacheRegion);
        
        if (playerId.HasValue)
        {
            CacheManager.Remove(string.Format(CacheKeyByUserAndPlayerTemplate, userId, playerId.Value), CacheRegion);
        }
    }
}
