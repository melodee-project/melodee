using Melodee.Common.Configuration;
using Melodee.Common.Enums;

namespace Melodee.Common.Constants;

/// <summary>
///     Defines the required settings and library types that must be configured
///     for onboarding completion.
/// </summary>
public static class OnboardingRequirements
{
    /// <summary>
    ///     Required library types that must exist for onboarding to be complete.
    /// </summary>
    public static readonly LibraryType[] RequiredLibraryTypes =
    {
        LibraryType.Inbound,
        LibraryType.Staging,
        LibraryType.Storage
    };

    /// <summary>
    ///     Explicit required settings keys that block onboarding completion,
    ///     regardless of their current value.
    /// </summary>
    public static readonly string[] RequiredSettingsKeys =
    {
        SettingRegistry.SystemBaseUrl,
        SettingRegistry.SystemSiteName,
        SettingRegistry.SecuritySecretKey,
        SettingRegistry.SystemOnboardingCompletedAt
    };

    /// <summary>
    ///     Default paths for required libraries when seeded.
    /// </summary>
    public static class DefaultLibraryPaths
    {
        public const string Inbound = "/app/inbound/";
        public const string Staging = "/app/staging/";
        public const string Storage = "/app/storage/";
        public const string UserImages = "/app/user-images/";
        public const string Playlist = "/app/playlists/";
        public const string Templates = "/app/templates/";
        public const string Podcast = "/app/podcasts/";
        public const string Theme = "/app/themes/";
    }

    /// <summary>
    ///     Default values for settings as seeded in the database.
    /// </summary>
    public static class DefaultSettingValues
    {
        public const string SystemBaseUrl = MelodeeConfiguration.RequiredNotSetValue;
        public const string SystemSiteName = "Melodee";
    }
}
