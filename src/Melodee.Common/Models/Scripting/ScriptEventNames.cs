namespace Melodee.Common.Models.Scripting;

public static class ScriptEventNames
{
    public const string DirectoryProcessingStart = "directoryProcessingStart";
    public const string DirectoryProcessingDelete = "directoryProcessingDelete";

    public const string UserRegistrationStart = "userRegistrationStart";
    public const string UserLoginStart = "userLoginStart";
    public const string UserProfileUpdateStart = "userProfileUpdateStart";

    public const string PlaylistCreateStart = "playlistCreateStart";
    public const string PodcastChannelAddStart = "podcastChannelAddStart";
    public const string ShareCreateStart = "shareCreateStart";
    public const string RequestCreateStart = "requestCreateStart";

    public static readonly string[] All =
    [
        DirectoryProcessingStart,
        DirectoryProcessingDelete,
        UserRegistrationStart,
        UserLoginStart,
        UserProfileUpdateStart,
        PlaylistCreateStart,
        PodcastChannelAddStart,
        ShareCreateStart,
        RequestCreateStart
    ];
}

