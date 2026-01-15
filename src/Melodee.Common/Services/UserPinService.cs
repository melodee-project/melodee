using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public sealed class UserPinService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    UserProfileService userProfileService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:user:apikey:{0}";
    private const string CacheKeyDetailByEmailAddressKeyTemplate = "urn:user:emailaddress:{0}";
    private const string CacheKeyDetailByUsernameTemplate = "urn:user:username:{0}";
    private const string CacheKeyDetailTemplate = "urn:user:{0}";

    private readonly UserProfileService _userProfileService = userProfileService;

    public async Task<bool> IsPinned(int userId, UserPinType pinType, int pinId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await scopedContext.UserPins
            .Where(up => up.UserId == userId && up.PinId == pinId && up.PinType == (int)pinType)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> TogglePinnedAsync(int userId, UserPinType pinType, int pinId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        bool result;
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var userPinTypeValue = (int)pinType;
            var userPin = await scopedContext
                .UserPins
                .Where(x => x.UserId == userId && x.PinId == pinId && x.PinType == userPinTypeValue)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (userPin == null)
            {
                userPin = new UserPin
                {
                    UserId = userId,
                    PinId = pinId,
                    PinType = userPinTypeValue,
                    CreatedAt = now
                };
                scopedContext.UserPins.Add(userPin);
            }
            else
            {
                scopedContext.UserPins.Remove(userPin);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            var user = await _userProfileService.GetAsync(userId, cancellationToken).ConfigureAwait(false);
            ClearUserCache(user.Data!);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    private void ClearUserCache(User user)
    {
        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(user.Id));
        CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(user.ApiKey));
        CacheManager.Remove(CacheKeyDetailByEmailAddressKeyTemplate.FormatSmart(user.EmailNormalized));
        CacheManager.Remove(CacheKeyDetailByUsernameTemplate.FormatSmart(user.UserNameNormalized));
    }
}
