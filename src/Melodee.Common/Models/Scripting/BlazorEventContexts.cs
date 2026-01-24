namespace Melodee.Common.Models.Scripting;

public record UserRegistrationContext
{
    public int UserNameLength { get; init; }
    public string EmailDomain { get; init; } = string.Empty;
    public string ClientIp { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    public string Now { get; init; } = string.Empty;
}

public record UserLoginContext
{
    public int? UserId { get; init; }
    public string[] Roles { get; init; } = [];
    public string ClientIp { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    public string Now { get; init; } = string.Empty;
}

public record UserProfileUpdateContext
{
    public int UserId { get; init; }
    public string EmailDomain { get; init; } = string.Empty;
    public int ProfileChangesCount { get; init; }
    public string ClientIp { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    public string Now { get; init; } = string.Empty;
}

public record PlaylistCreateContext
{
    public int UserId { get; init; }
    public int NameLength { get; init; }
    public int InitialSongCount { get; init; }
    public string Now { get; init; } = string.Empty;
}

public record PodcastChannelAddContext
{
    public int UserId { get; init; }
    public string FeedUrl { get; init; } = string.Empty;
    public bool IsNewSubscription { get; init; }
    public string Now { get; init; } = string.Empty;
}

public record ShareCreateContext
{
    public int UserId { get; init; }
    public string ShareType { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public int? ExpirationDays { get; init; }
    public string Now { get; init; } = string.Empty;
}

public record RequestCreateContext
{
    public int UserId { get; init; }
    public string RequestType { get; init; } = string.Empty;
    public bool IsFirstRequestToday { get; init; }
    public int DailyRequestCount { get; init; }
    public string Now { get; init; } = string.Empty;
}
