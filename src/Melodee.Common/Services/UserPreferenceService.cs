using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public sealed class UserPreferenceService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<MelodeeModels.OperationResult<bool>> ToggleGenreStarAsync(
        int userId,
        string genreName,
        bool isStarred,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(genreName, nameof(genreName));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<bool>("User not found")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var normalizedGenre = genreName.ToUpperInvariant().Trim();
        var starredGenres = ParsePipeSeparatedList(user.StarredGenres);

        if (isStarred)
        {
            if (!starredGenres.Contains(normalizedGenre))
            {
                starredGenres.Add(normalizedGenre);
            }
        }
        else
        {
            starredGenres.Remove(normalizedGenre);
        }

        user.StarredGenres = starredGenres.Count > 0 ? string.Join("|", starredGenres) : null;
        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> ToggleGenreHatedAsync(
        int userId,
        string genreName,
        bool isHated,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(genreName, nameof(genreName));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<bool>("User not found")
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var normalizedGenre = genreName.ToUpperInvariant().Trim();
        var hatedGenres = ParsePipeSeparatedList(user.HatedGenres);

        if (isHated)
        {
            if (!hatedGenres.Contains(normalizedGenre))
            {
                hatedGenres.Add(normalizedGenre);
            }
        }
        else
        {
            hatedGenres.Remove(normalizedGenre);
        }

        user.HatedGenres = hatedGenres.Count > 0 ? string.Join("|", hatedGenres) : null;
        user.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<MelodeeModels.OperationResult<string[]>> GetStarredGenresAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<string[]>("User not found")
            {
                Data = [],
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<string[]>
        {
            Data = ParsePipeSeparatedList(user.StarredGenres).ToArray()
        };
    }

    public async Task<MelodeeModels.OperationResult<string[]>> GetHatedGenresAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new MelodeeModels.OperationResult<string[]>("User not found")
            {
                Data = [],
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<string[]>
        {
            Data = ParsePipeSeparatedList(user.HatedGenres).ToArray()
        };
    }

    private static List<string> ParsePipeSeparatedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct()
            .ToList();
    }
}
