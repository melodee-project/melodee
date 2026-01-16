using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Constants;
using Melodee.Common.Models.OpenSubsonic;

namespace Melodee.Common.Data.Models.Extensions;

public static class ShareExtensions
{
    public static string ToApiKey(this Share share)
    {
        return $"share{OpenSubsonicServer.ApiIdSeparator}{share.ApiKey}";
    }

    public static string ToUrl(this Share share, IMelodeeConfiguration configuration)
    {
        var baseUrl = configuration.GetValue<string>(SettingRegistry.SystemBaseUrl);
        return $"{baseUrl}/share/{share.ShareUniqueId}";
    }

    public static Common.Models.OpenSubsonic.Share ToApiShare(this Share share, string url, Child[] entry)
    {
        return new Common.Models.OpenSubsonic.Share
        {
            Id = share.ToApiKey(),
            Url = url,
            Description = share.Description,
            UserName = share.User?.UserName ?? string.Empty,
            Created = share.CreatedAt.ToString(),
            Expires = share.ExpiresAt?.ToString(),
            LastVisited = share.LastVisitedAt?.ToString(),
            VisitCount = share.VisitCount,
            Entry = entry
        };
    }
}
