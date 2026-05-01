using Melodee.Blazor.Controllers.Jellyfin.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Serialization;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Blazor.Controllers.Jellyfin;

[ApiController]
[Route("api/jf/[controller]")]
[ApiExplorerSettings(GroupName = "jellyfin")]
[EnableRateLimiting("jellyfin-api")]
public class UserViewsController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    [HttpGet]
    public async Task<IActionResult> GetUserViewsAsync(CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var libraries = await dbContext.Libraries
            .AsNoTracking()
            .Where(l => l.Type == (int)LibraryType.Storage && !l.IsLocked)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync(cancellationToken);

        var views = libraries.Select(lib => new JellyfinUserView
        {
            Name = lib.Name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(lib.ApiKey),
            Etag = ComputeEtag(lib.ApiKey, lib.LastUpdatedAt ?? lib.CreatedAt),
            DateCreated = FormatInstantForJellyfin(lib.CreatedAt),
            CanDelete = false,
            CanDownload = user.HasDownloadRole,
            SortName = lib.Name,
            IsFolder = true,
            Type = "UserView",
            CollectionType = "music",
            PlayAccess = user.HasStreamRole ? "Full" : "None",
            LocationType = "Virtual",
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            UserData = new JellyfinUserItemData
            {
                PlaybackPositionTicks = 0,
                PlayCount = 0,
                IsFavorite = false,
                Played = false,
                Key = lib.ApiKey.ToString("N")
            }
        }).ToArray();

        if (views.Length == 0)
        {
            var defaultViewId = Guid.NewGuid();
            views =
            [
                new JellyfinUserView
                {
                    Name = "Music",
                    ServerId = GetServerId(),
                    Id = defaultViewId.ToString("N"),
                    CanDownload = user.HasDownloadRole,
                    IsFolder = true,
                    Type = "UserView",
                    CollectionType = "music",
                    PlayAccess = user.HasStreamRole ? "Full" : "None",
                    LocationType = "Virtual"
                }
            ];
        }

        return Ok(new JellyfinUserViewsResult
        {
            Items = views,
            TotalRecordCount = views.Length,
            StartIndex = 0
        });
    }

    private static string ComputeEtag(Guid apiKey, Instant lastUpdated)
    {
        var input = $"{apiKey:N}-{lastUpdated.ToUnixTimeTicks()}";
        // NOTE: MD5 is used here for generating ETag values for HTTP caching in Jellyfin API compatibility.
        // This is NOT a cryptographic use - ETags are public cache identifiers, not security tokens.
        // lgtm[cs/weak-crypto] MD5 used for non-cryptographic ETag generation, not for security
        return HashHelper.CreateMd5(input) ?? string.Empty;
    }
}
