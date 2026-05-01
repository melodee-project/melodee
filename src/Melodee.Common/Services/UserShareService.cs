using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services;

public sealed class UserShareService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<Share[]?> UserSharesAsync(int userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.Shares
            .Include(s => s.User)
            .Where(s => s.UserId == userId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
