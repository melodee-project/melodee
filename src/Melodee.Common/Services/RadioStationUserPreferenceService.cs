using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for managing user preferences for radio stations
/// </summary>
public class RadioStationUserPreferenceService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    /// <summary>
    /// Gets the preference for a user and radio station, or null if not set
    /// </summary>
    public async Task<OperationResult<RadioStationUserPreference?>> GetPreferenceAsync(
        int userId,
        int radioStationId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x < 1, radioStationId, nameof(radioStationId));

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var preference = await context.RadioStationUserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RadioStationId == radioStationId, cancellationToken);

        return new OperationResult<RadioStationUserPreference?>
        {
            Data = preference
        };
    }

    /// <summary>
    /// Gets all preferences for a user
    /// </summary>
    public async Task<OperationResult<RadioStationUserPreference[]>> GetUserPreferencesAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var preferences = await context.RadioStationUserPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToArrayAsync(cancellationToken);

        return new OperationResult<RadioStationUserPreference[]>
        {
            Data = preferences
        };
    }

    /// <summary>
    /// Updates user preference for a radio station. Creates if doesn't exist.
    /// </summary>
    public async Task<OperationResult<RadioStationUserPreference>> UpdatePreferenceAsync(
        int userId,
        int radioStationId,
        bool? isFavorite = null,
        bool? isHidden = null,
        int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x < 1, radioStationId, nameof(radioStationId));

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var preference = await context.RadioStationUserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RadioStationId == radioStationId, cancellationToken);

        if (preference == null)
        {
            // Create new preference
            preference = new RadioStationUserPreference
            {
                UserId = userId,
                RadioStationId = radioStationId,
                IsFavorite = isFavorite ?? false,
                IsHidden = isHidden ?? false,
                SortOrder = sortOrder ?? 1000,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };
            context.RadioStationUserPreferences.Add(preference);
        }
        else
        {
            // Update existing preference (only update fields that are provided)
            if (isFavorite.HasValue)
            {
                preference.IsFavorite = isFavorite.Value;
            }
            if (isHidden.HasValue)
            {
                preference.IsHidden = isHidden.Value;
            }
            if (sortOrder.HasValue)
            {
                preference.SortOrder = sortOrder.Value;
            }
            preference.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
        }

        await context.SaveChangesAsync(cancellationToken);

        return new OperationResult<RadioStationUserPreference>
        {
            Data = preference
        };
    }

    /// <summary>
    /// Deletes a user preference
    /// </summary>
    public async Task<OperationResult<bool>> DeletePreferenceAsync(
        int userId,
        int radioStationId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x < 1, radioStationId, nameof(radioStationId));

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        var preference = await context.RadioStationUserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RadioStationId == radioStationId, cancellationToken);

        if (preference == null)
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        context.RadioStationUserPreferences.Remove(preference);
        var result = await context.SaveChangesAsync(cancellationToken) > 0;

        return new OperationResult<bool>
        {
            Data = result
        };
    }
}
