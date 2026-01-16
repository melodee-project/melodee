using System.Security.Cryptography;
using System.Text;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models.OpenSubsonic.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NodaTime;

namespace Melodee.Common.Data;

public class MelodeeDbContext(DbContextOptions<MelodeeDbContext> options) : DbContext(options)
{
    public DbSet<Album> Albums { get; set; }

    public DbSet<Artist> Artists { get; set; }

    public DbSet<ArtistRelation> ArtistRelation { get; set; }

    public DbSet<Bookmark> Bookmarks { get; set; }

    public DbSet<Contributor> Contributors { get; set; }

    public DbSet<Library> Libraries { get; set; }

    public DbSet<LibraryScanHistory> LibraryScanHistories { get; set; }

    public DbSet<Player> Players { get; set; }

    public DbSet<Playlist> Playlists { get; set; }

    public DbSet<PlaylistSong> PlaylistSong { get; set; }

    public DbSet<PlayQueue> PlayQues { get; set; }

    public DbSet<RadioStation> RadioStations { get; set; }

    public DbSet<Setting> Settings { get; set; }

    public DbSet<SearchHistory> SearchHistories { get; set; }

    public DbSet<Share> Shares { get; set; }

    public DbSet<ShareActivity> ShareActivities { get; set; }

    public DbSet<Song> Songs { get; set; }

    public DbSet<User> Users { get; set; }

    public DbSet<UserAlbum> UserAlbums { get; set; }

    public DbSet<UserArtist> UserArtists { get; set; }

    public DbSet<UserPin> UserPins { get; set; }

    public DbSet<UserSong> UserSongs { get; set; }

    public DbSet<UserSongPlayHistory> UserSongPlayHistories { get; set; }

    public DbSet<UserPlaybackSettings> UserPlaybackSettings { get; set; }

    public DbSet<UserDeviceProfile> UserDeviceProfiles { get; set; }

    public DbSet<UserEqualizerPreset> UserEqualizerPresets { get; set; }

    public DbSet<UserSocialLogin> UserSocialLogins { get; set; }

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<Chart> Charts { get; set; }

    public DbSet<ChartItem> ChartItems { get; set; }

    public DbSet<JobHistory> JobHistories { get; set; }

    public DbSet<Request> Requests { get; set; }

    public DbSet<RequestComment> RequestComments { get; set; }

    public DbSet<RequestUserState> RequestUserStates { get; set; }

    public DbSet<RequestParticipant> RequestParticipants { get; set; }

    public DbSet<JellyfinAccessToken> JellyfinAccessTokens { get; set; }

    public DbSet<SmartPlaylist> SmartPlaylists { get; set; }

    public DbSet<PodcastChannel> PodcastChannels { get; set; }

    public DbSet<PodcastEpisode> PodcastEpisodes { get; set; }

    public DbSet<UserPodcastEpisodePlayHistory> UserPodcastEpisodePlayHistories { get; set; }

    public DbSet<PodcastEpisodeBookmark> PodcastEpisodeBookmarks { get; set; }

    public DbSet<PartySession> PartySessions { get; set; }

    public DbSet<PartySessionParticipant> PartySessionParticipants { get; set; }

    public DbSet<PartyQueueItem> PartyQueueItems { get; set; }

    public DbSet<PartyPlaybackState> PartyPlaybackStates { get; set; }

    public DbSet<PartySessionEndpoint> PartySessionEndpoints { get; set; }

    public DbSet<PartyAuditEvent> PartyAuditEvents { get; set; }

    public DbSet<UserGroup> UserGroups { get; set; }

    public DbSet<UserGroupMember> UserGroupMembers { get; set; }

    public DbSet<LibraryAccessControl> LibraryAccessControls { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use a fixed timestamp for seed data to prevent migration churn
        // This is the Unix epoch (1970-01-01 00:00:00 UTC) - a stable, deterministic value
        var seedDataTimestamp = Instant.FromUnixTimeSeconds(0);

        // Generate deterministic GUIDs for seed data based on entity type and ID
        // This prevents new GUIDs being generated on each migration
        // NOTE: MD5 is used here for deterministic GUID generation from seed data identifiers.
        // This is NOT a cryptographic use - it's purely for generating stable, reproducible GUIDs for database seeding.
        // lgtm[cs/weak-crypto] MD5 used for non-cryptographic GUID generation, not for security
        Guid SeedGuid(string entityType, int id) =>
            new(MD5.HashData(Encoding.UTF8.GetBytes($"Melodee.Seed.{entityType}.{id}")));

        modelBuilder.Entity<Library>(s =>
        {
            // Add filtered unique index to allow multiple Storage libraries but only one of each other type
            s.HasIndex(e => e.Type)
                .IsUnique()
                .HasFilter("\"Type\" != 3"); // Exclude LibraryType.Storage (value 3) from unique constraint

            s.HasData(new Library
            {
                Id = 1,
                ApiKey = SeedGuid("Library", 1),
                Name = "Inbound",
                Description =
                        "Files in this directory are scanned and Album information is gathered via processing.",
                Path = "/app/inbound/",
                Type = (int)LibraryType.Inbound,
                CreatedAt = seedDataTimestamp
            },
                new Library
                {
                    Id = 2,
                    ApiKey = SeedGuid("Library", 2),
                    Name = "Staging",
                    Description =
                        "The staging directory to place processed files into (Inbound -> Staging -> Library).",
                    Path = "/app/staging/",
                    Type = (int)LibraryType.Staging,
                    CreatedAt = seedDataTimestamp
                },
                new Library
                {
                    Id = 3,
                    ApiKey = SeedGuid("Library", 3),
                    Name = "Storage",
                    Description =
                        "The library directory to place processed, reviewed and ready to use music files into.",
                    Path = "/app/storage/",
                    Type = (int)LibraryType.Storage,
                    CreatedAt = seedDataTimestamp
                },
                new Library
                {
                    Id = 4,
                    ApiKey = SeedGuid("Library", 4),
                    Name = "User Images",
                    Description = "Library where user images are stored.",
                    Path = "/app/user-images/",
                    Type = (int)LibraryType.UserImages,
                    CreatedAt = seedDataTimestamp
                },
                new Library
                {
                    Id = 5,
                    ApiKey = SeedGuid("Library", 5),
                    Name = "Playlist Data",
                    Description = "Library where playlist data is stored.",
                    Path = "/app/playlists/",
                    Type = (int)LibraryType.Playlist,
                    CreatedAt = seedDataTimestamp
                },
                new Library
                {
                    Id = 6,
                    ApiKey = SeedGuid("Library", 6),
                    Name = "Templates",
                    Description = "Library where templates are stored, organized by language code.",
                    Path = "/app/templates/",
                    Type = (int)LibraryType.Templates,
                    CreatedAt = seedDataTimestamp
                },
                new Library
                {
                    Id = 7,
                    ApiKey = SeedGuid("Library", 7),
                    Name = "Podcasts",
                    Description = "Library where podcast media files are stored.",
                    Path = "/app/podcasts/",
                    Type = (int)LibraryType.Podcast,
                    CreatedAt = seedDataTimestamp
                },
                new Library
                {
                    Id = 8,
                    ApiKey = SeedGuid("Library", 8),
                    Name = "Themes",
                    Description = "Library where custom theme packs are stored.",
                    Path = "/app/themes/",
                    Type = (int)LibraryType.Theme,
                    CreatedAt = seedDataTimestamp
                });
        });

        modelBuilder.Entity<Setting>(s =>
        {
            s.HasData(
                new Setting
                {
                    Id = 1,
                    ApiKey = SeedGuid("Setting", 1),
                    Key = SettingRegistry.FilteringLessThanSongCount,
                    Comment = "Add a default filter to show only albums with this or less number of songs.",
                    Value = "3",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 2,
                    ApiKey = SeedGuid("Setting", 2),
                    Key = SettingRegistry.FilteringLessThanDuration,
                    Comment = "Add a default filter to show only albums with this or less duration.",
                    Value = "720000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 4,
                    ApiKey = SeedGuid("Setting", 4),
                    Key = SettingRegistry.DefaultsPageSize,
                    Comment = "Default page size when view including pagination.",
                    Value = "100",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 6,
                    ApiKey = SeedGuid("Setting", 6),
                    Key = SettingRegistry.UserInterfaceToastAutoCloseTime,
                    Comment = "Amount of time to display a Toast then auto-close (in milliseconds.)",
                    Value = "2000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 300,
                    ApiKey = SeedGuid("Setting", 300),
                    Category = (int)SettingCategory.Formatting,
                    Key = SettingRegistry.FormattingDateTimeDisplayFormatShort,
                    Comment = "Short Format to use when displaying full dates.",
                    Value = "yyyyMMdd HH\\:mm",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 301,
                    ApiKey = SeedGuid("Setting", 301),
                    Category = (int)SettingCategory.Formatting,
                    Key = SettingRegistry.FormattingDateTimeDisplayActivityFormat,
                    Comment = "Format to use when displaying activity related dates (e.g., processing messages)",
                    Value = MelodeeConfiguration.FormattingDateTimeDisplayActivityFormatDefault,
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 9,
                    ApiKey = SeedGuid("Setting", 9),
                    Key = SettingRegistry.ProcessingIgnoredArticles,
                    Comment = "List of ignored articles when scanning media (pipe delimited).",
                    Value = "THE|EL|LA|LOS|LAS|LE|LES|OS|AS|O|A",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 500,
                    ApiKey = SeedGuid("Setting", 500),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicEnabled,
                    Comment = "Is Magic processing enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 501,
                    ApiKey = SeedGuid("Setting", 501),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoRenumberSongs,
                    Comment = "Renumber songs when doing magic processing.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 502,
                    ApiKey = SeedGuid("Setting", 502),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoRemoveFeaturingArtistFromSongArtist,
                    Comment = "Remove featured artists from song artist when doing magic.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 503,
                    ApiKey = SeedGuid("Setting", 503),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoRemoveFeaturingArtistFromSongTitle,
                    Comment = "Remove featured artists from song title when doing magic.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 504,
                    ApiKey = SeedGuid("Setting", 504),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoReplaceSongsArtistSeparators,
                    Comment = "Replace song artist separators with standard ID3 separator ('/') when doing magic.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 505,
                    ApiKey = SeedGuid("Setting", 505),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoSetYearToCurrentIfInvalid,
                    Comment = "Set the song year to current year if invalid or missing when doing magic.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 506,
                    ApiKey = SeedGuid("Setting", 506),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoRemoveUnwantedTextFromAlbumTitle,
                    Comment = "Remove unwanted text from album title when doing magic.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 507,
                    ApiKey = SeedGuid("Setting", 507),
                    Category = (int)SettingCategory.Magic,
                    Key = SettingRegistry.MagicDoRemoveUnwantedTextFromSongTitles,
                    Comment = "Remove unwanted text from song titles when doing magic.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 200,
                    ApiKey = SeedGuid("Setting", 200),
                    Category = (int)SettingCategory.Conversion,
                    Key = SettingRegistry.ConversionEnabled,
                    Comment = "Enable Melodee to convert non-mp3 media files during processing.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 201,
                    ApiKey = SeedGuid("Setting", 201),
                    Category = (int)SettingCategory.Conversion,
                    Key = SettingRegistry.ConversionBitrate,
                    Comment = "Bitrate to convert non-mp3 media files during processing.",
                    Value = "384",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 202,
                    ApiKey = SeedGuid("Setting", 202),
                    Category = (int)SettingCategory.Conversion,
                    Key = SettingRegistry.ConversionVbrLevel,
                    Comment = "Vbr to convert non-mp3 media files during processing.",
                    Value = "4",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 203,
                    ApiKey = SeedGuid("Setting", 203),
                    Category = (int)SettingCategory.Conversion,
                    Key = SettingRegistry.ConversionSamplingRate,
                    Comment = "Sampling rate to convert non-mp3 media files during processing.",
                    Value = "48000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 700,
                    ApiKey = SeedGuid("Setting", 700),
                    Category = (int)SettingCategory.PluginProcess,
                    Key = SettingRegistry.PluginEnabledCueSheet,
                    Comment = "Process of CueSheet files during processing.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 701,
                    ApiKey = SeedGuid("Setting", 701),
                    Category = (int)SettingCategory.PluginProcess,
                    Key = SettingRegistry.PluginEnabledM3u,
                    Comment = "Process of M3U files during processing.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 702,
                    ApiKey = SeedGuid("Setting", 702),
                    Category = (int)SettingCategory.PluginProcess,
                    Key = SettingRegistry.PluginEnabledNfo,
                    Comment = "Process of NFO files during processing.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 703,
                    ApiKey = SeedGuid("Setting", 703),
                    Category = (int)SettingCategory.PluginProcess,
                    Key = SettingRegistry.PluginEnabledSimpleFileVerification,
                    Comment = "Process of Simple File Verification (SFV) files during processing.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 704,
                    ApiKey = SeedGuid("Setting", 704),
                    Category = (int)SettingCategory.PluginProcess,
                    Key = SettingRegistry.ProcessingDoDeleteComments,
                    Comment = "If true then all comments will be removed from media files.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 26,
                    ApiKey = SeedGuid("Setting", 26),
                    Key = SettingRegistry.ProcessingArtistNameReplacements,
                    Comment = "Fragments of artist names to replace (JSON Dictionary).",
                    Value =
                        "{'AC/DC': ['AC; DC', 'AC;DC', 'AC/ DC', 'AC DC'] , 'Love/Hate': ['Love; Hate', 'Love;Hate', 'Love/ Hate', 'Love Hate'] }",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 27,
                    ApiKey = SeedGuid("Setting", 27),
                    Key = SettingRegistry.ProcessingDoUseCurrentYearAsDefaultOrigAlbumYearValue,
                    Comment = "If OrigAlbumYear [TOR, TORY, TDOR] value is invalid use current year.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 28,
                    ApiKey = SeedGuid("Setting", 28),
                    Key = SettingRegistry.ProcessingDoDeleteOriginal,
                    Comment =
                        "Delete original files when processing. When false a copy if made, else original is deleted after processed.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 29,
                    ApiKey = SeedGuid("Setting", 29),
                    Key = SettingRegistry.ProcessingConvertedExtension,
                    Comment = "Extension to add to file when converted, leave blank to disable.",
                    Value = "_converted",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 30,
                    ApiKey = SeedGuid("Setting", 30),
                    Key = SettingRegistry.ProcessingProcessedExtension,
                    Comment = "Extension to add to file when processed, leave blank to disable.",
                    Value = "_processed",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 32,
                    ApiKey = SeedGuid("Setting", 32),
                    Key = SettingRegistry.ProcessingDoOverrideExistingMelodeeDataFiles,
                    Comment =
                        "When processing over write any existing Melodee data files, otherwise skip and leave in place.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 34,
                    ApiKey = SeedGuid("Setting", 34),
                    Key = SettingRegistry.ProcessingMaximumProcessingCount,
                    Comment = "The maximum number of files to process, set to zero for unlimited.",
                    Value = "0",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 35,
                    ApiKey = SeedGuid("Setting", 35),
                    Key = SettingRegistry.ProcessingMaximumAlbumDirectoryNameLength,
                    Comment = "Maximum allowed length of album directory name.",
                    Value = "255",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 36,
                    ApiKey = SeedGuid("Setting", 36),
                    Key = SettingRegistry.ProcessingMaximumArtistDirectoryNameLength,
                    Comment = "Maximum allowed length of artist directory name.",
                    Value = "255",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 37,
                    ApiKey = SeedGuid("Setting", 37),
                    Key = SettingRegistry.ProcessingAlbumTitleRemovals,
                    Comment = "Fragments to remove from album titles (JSON array).",
                    Value = "['^', '~', '#']",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 38,
                    ApiKey = SeedGuid("Setting", 38),
                    Key = SettingRegistry.ProcessingSongTitleRemovals,
                    Comment = "Fragments to remove from song titles (JSON array).",
                    Value = "[';', '(Remaster)', 'Remaster']",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 39,
                    ApiKey = SeedGuid("Setting", 39),
                    Key = SettingRegistry.ProcessingDoContinueOnDirectoryProcessingErrors,
                    Comment = "Continue processing if an error is encountered.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 41,
                    ApiKey = SeedGuid("Setting", 41),
                    Key = SettingRegistry.ScriptingEnabled,
                    Comment = "Is scripting enabled.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 42,
                    ApiKey = SeedGuid("Setting", 42),
                    Key = SettingRegistry.ScriptingPreDiscoveryScript,
                    Comment = "Script to run before processing the inbound directory, leave blank to disable.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 43,
                    ApiKey = SeedGuid("Setting", 43),
                    Key = SettingRegistry.ScriptingPostDiscoveryScript,
                    Comment = "Script to run after processing the inbound directory, leave blank to disable.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 45,
                    ApiKey = SeedGuid("Setting", 45),
                    Key = SettingRegistry.ProcessingIgnoredPerformers,
                    Comment = "Don't create performer contributors for these performer names.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 46,
                    ApiKey = SeedGuid("Setting", 46),
                    Key = SettingRegistry.ProcessingIgnoredProduction,
                    Comment = "Don't create production contributors for these production names.",
                    Value = "['www.t.me;pmedia_music']",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 47,
                    ApiKey = SeedGuid("Setting", 47),
                    Key = SettingRegistry.ProcessingIgnoredPublishers,
                    Comment = "Don't create publisher contributors for these artist names.",
                    Value = "['P.M.E.D.I.A','PMEDIA','PMEDIA GROUP']",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 49,
                    ApiKey = SeedGuid("Setting", 49),
                    Key = SettingRegistry.EncryptionPrivateKey,
                    Comment =
                        "Private key used to encrypt/decrypt passwords for Subsonic authentication. Use https://generate-random.org/encryption-key-generator?count=1&bytes=32&cipher=aes-256-cbc&string=&password= to generate a new key.",
                    Value = "H+Kiik6VMKfTD2MesF1GoMjczTrD5RhuKckJ5+/UQWOdWajGcsEC3yEnlJ5eoy8Y",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 50,
                    ApiKey = SeedGuid("Setting", 50),
                    Key = SettingRegistry.ProcessingDuplicateAlbumPrefix,
                    Comment =
                        "Prefix to apply to indicate an album directory is a duplicate album for an artist. If left blank the default of '__duplicate_' will be used.",
                    Value = "_duplicate_ ",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1300,
                    ApiKey = SeedGuid("Setting", 1300),
                    Category = (int)SettingCategory.Validation,
                    Key = SettingRegistry.ValidationMaximumSongNumber,
                    Comment = "The maximum value a song number can have for an album.",
                    Value = "9999",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1301,
                    ApiKey = SeedGuid("Setting", 1301),
                    Category = (int)SettingCategory.Validation,
                    Key = SettingRegistry.ValidationMinimumAlbumYear,
                    Comment = "Minimum allowed year for an album.",
                    Value = "1860",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1302,
                    ApiKey = SeedGuid("Setting", 1302),
                    Category = (int)SettingCategory.Validation,
                    Key = SettingRegistry.ValidationMaximumAlbumYear,
                    Comment = "Maximum allowed year for an album.",
                    Value = "2150",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1303,
                    ApiKey = SeedGuid("Setting", 1303),
                    Category = (int)SettingCategory.Validation,
                    Key = SettingRegistry.ValidationMinimumSongCount,
                    Comment =
                        "Minimum number of songs an album has to have to be considered valid, set to 0 to disable check.",
                    Value = "3",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1304,
                    ApiKey = SeedGuid("Setting", 1304),
                    Category = (int)SettingCategory.Validation,
                    Key = SettingRegistry.ValidationMinimumAlbumDuration,
                    Comment =
                        "Minimum duration of an album to be considered valid (in minutes), set to 0 to disable check.",
                    Value = "10",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 100,
                    ApiKey = SeedGuid("Setting", 100),
                    Category = (int)SettingCategory.Api,
                    Key = SettingRegistry.OpenSubsonicServerSupportedVersion,
                    Comment = "OpenSubsonic server supported Subsonic API version.",
                    Value = "1.16.1",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 101,
                    ApiKey = SeedGuid("Setting", 101),
                    Category = (int)SettingCategory.Api,
                    Key = SettingRegistry.OpenSubsonicServerType,
                    Comment = "OpenSubsonic server name.",
                    Value = "Melodee",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 103,
                    ApiKey = SeedGuid("Setting", 103),
                    Category = (int)SettingCategory.Api,
                    Key = SettingRegistry.OpenSubsonicServerLicenseEmail,
                    Comment = "OpenSubsonic email to use in License responses.",
                    Value = "noreply@localhost.lan",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 104,
                    ApiKey = SeedGuid("Setting", 104),
                    Category = (int)SettingCategory.Api,
                    Key = SettingRegistry.OpenSubsonicIndexesArtistLimit,
                    Comment =
                        "Limit the number of artists to include in an indexes request, set to zero for 32k per index (really not recommended with tens of thousands of artists and mobile clients timeout downloading indexes, a user can find an artist by search)",
                    Value = "1000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 53,
                    ApiKey = SeedGuid("Setting", 53),
                    Key = SettingRegistry.DefaultsBatchSize,
                    Comment =
                        $"Processing batching size. Allowed range is between [{MelodeeConfiguration.BatchSizeDefault}] and [{MelodeeConfiguration.BatchSizeMaximum}]. ",
                    Value = "250",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 54,
                    ApiKey = SeedGuid("Setting", 54),
                    Key = SettingRegistry.ProcessingFileExtensionsToDelete,
                    Comment =
                        "When processing folders immediately delete any files with these extensions. (JSON array).",
                    Value = "['log', 'lnk', 'lrc', 'doc']",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 902,
                    ApiKey = SeedGuid("Setting", 902),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineUserAgent,
                    Comment = "User agent to send with Search engine requests.",
                    Value = "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 903,
                    ApiKey = SeedGuid("Setting", 903),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineDefaultPageSize,
                    Comment = "Default page size when performing a search engine search.",
                    Value = "20",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 904,
                    ApiKey = SeedGuid("Setting", 904),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMusicBrainzEnabled,
                    Comment = "Is MusicBrainz search engine enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 905,
                    ApiKey = SeedGuid("Setting", 905),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMusicBrainzStoragePath,
                    Comment = "Storage path to hold MusicBrainz downloaded files and SQLite db.",
                    Value = "/melodee_test/search-engine-storage/musicbrainz/",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 906,
                    ApiKey = SeedGuid("Setting", 906),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMusicBrainzImportMaximumToProcess,
                    Comment =
                        "Maximum number of batches import from MusicBrainz downloaded db dump (this setting is usually used during debugging), set to zero for unlimited.",
                    Value = "0",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 907,
                    ApiKey = SeedGuid("Setting", 907),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMusicBrainzImportBatchSize,
                    Comment =
                        "Number of records to import from MusicBrainz downloaded db dump before commiting to local SQLite database.",
                    Value = "50000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 908,
                    ApiKey = SeedGuid("Setting", 908),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMusicBrainzImportLastImportTimestamp,
                    Comment = "Timestamp of when last MusicBrainz import was successful.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 910,
                    ApiKey = SeedGuid("Setting", 910),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineSpotifyEnabled,
                    Comment = "Is Spotify search engine enabled.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 911,
                    ApiKey = SeedGuid("Setting", 911),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineSpotifyApiKey,
                    Comment = "ApiKey used used with Spotify. See https://developer.spotify.com/ for more details.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 912,
                    ApiKey = SeedGuid("Setting", 912),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineSpotifyClientSecret,
                    Comment = "Shared secret used with Spotify. See https://developer.spotify.com/ for more details.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 913,
                    ApiKey = SeedGuid("Setting", 913),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineSpotifyAccessToken,
                    Comment =
                        "Token obtained from Spotify using the ApiKey and the Secret, this json contains expiry information.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 914,
                    ApiKey = SeedGuid("Setting", 914),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineITunesEnabled,
                    Comment = "Is ITunes search engine enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 915,
                    ApiKey = SeedGuid("Setting", 915),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineLastFmEnabled,
                    Comment = "Is LastFM search engine enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 916,
                    ApiKey = SeedGuid("Setting", 916),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMaximumAllowedPageSize,
                    Comment = "When performing a search engine search, the maximum allowed page size.",
                    Value = "1000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 917,
                    ApiKey = SeedGuid("Setting", 917),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineArtistSearchDatabaseRefreshInDays,
                    Comment =
                        "Refresh albums for artists from search engine database every x days, set to zero to not refresh.",
                    Value = "14",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 918,
                    ApiKey = SeedGuid("Setting", 918),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineDeezerEnabled,
                    Comment = "Is Deezer search engine enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 919,
                    ApiKey = SeedGuid("Setting", 919),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineMetalApiEnabled,
                    Comment = "Is Metal API search engine enabled.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 400,
                    ApiKey = SeedGuid("Setting", 400),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingDoLoadEmbeddedImages,
                    Comment = "Include any embedded images from media files into the Melodee data file.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 401,
                    ApiKey = SeedGuid("Setting", 401),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingSmallSize,
                    Comment = "Small image size (square image, this is both width and height).",
                    Value = "300",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 402,
                    ApiKey = SeedGuid("Setting", 402),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingMediumSize,
                    Comment = "Medium image size (square image, this is both width and height).",
                    Value = "600",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 403,
                    ApiKey = SeedGuid("Setting", 403),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingLargeSize,
                    Comment =
                        "Large image size (square image, this is both width and height), if larger than will be resized to this image, leave blank to disable.",
                    Value = "1600",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 404,
                    ApiKey = SeedGuid("Setting", 404),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingMaximumNumberOfAlbumImages,
                    Comment =
                        "Maximum allowed number of images for an album, this includes all image types (Front, Rear, etc.), set to zero for unlimited.",
                    Value = "25",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 405,
                    ApiKey = SeedGuid("Setting", 405),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingMaximumNumberOfArtistImages,
                    Comment = "Maximum allowed number of images for an artist, set to zero for unlimited.",
                    Value = "25",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 406,
                    ApiKey = SeedGuid("Setting", 406),
                    Category = (int)SettingCategory.Imaging,
                    Key = SettingRegistry.ImagingMinimumImageSize,
                    Comment = "Images under this size are considered invalid, set to zero to disable.",
                    Value = "300",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1200,
                    ApiKey = SeedGuid("Setting", 1200),
                    Category = (int)SettingCategory.Transcoding,
                    Key = SettingRegistry.TranscodingDefault,
                    Comment = "Default format for transcoding.",
                    Value = "raw",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1201,
                    ApiKey = SeedGuid("Setting", 1201),
                    Category = (int)SettingCategory.Transcoding,
                    Key = SettingRegistry.TranscodingCommandMp3,
                    Comment = "Default command to transcode MP3 for streaming.",
                    Value =
                        $"{{ 'format': '{TranscodingFormat.Mp3.ToString()}', 'bitrate: 192, 'command': 'ffmpeg -i %s -ss %t -map 0:a:0 -b:a %bk -v 0 -f mp3 -' }}",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1202,
                    ApiKey = SeedGuid("Setting", 1202),
                    Category = (int)SettingCategory.Transcoding,
                    Key = SettingRegistry.TranscodingCommandOpus,
                    Comment = "Default command to transcode using libopus for streaming.",
                    Value =
                        $"{{ 'format': '{TranscodingFormat.Opus.ToString()}', 'bitrate: 128, 'command': 'ffmpeg -i %s -ss %t -map 0:a:0 -b:a %bk -v 0 -c:a libopus -f opus -' }}",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1203,
                    ApiKey = SeedGuid("Setting", 1203),
                    Category = (int)SettingCategory.Transcoding,
                    Key = SettingRegistry.TranscodingCommandAac,
                    Comment = "Default command to transcode to aac for streaming.",
                    Value =
                        $"{{ 'format': '{TranscodingFormat.Aac.ToString()}', 'bitrate: 256, 'command': 'ffmpeg -i %s -ss %t -map 0:a:0 -b:a %bk -v 0 -c:a aac -f adts -' }}",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1000,
                    ApiKey = SeedGuid("Setting", 1000),
                    Category = (int)SettingCategory.Scrobbling,
                    Key = SettingRegistry.ScrobblingEnabled,
                    Comment = "Is scrobbling enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1001,
                    ApiKey = SeedGuid("Setting", 1001),
                    Category = (int)SettingCategory.Scrobbling,
                    Key = SettingRegistry.ScrobblingLastFmEnabled,
                    Comment = "Is scrobbling to Last.fm enabled.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1002,
                    ApiKey = SeedGuid("Setting", 1002),
                    Category = (int)SettingCategory.Scrobbling,
                    Key = SettingRegistry.ScrobblingLastFmApiKey,
                    Comment =
                        "ApiKey used used with last FM. See https://www.last.fm/api/authentication for more details.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1003,
                    ApiKey = SeedGuid("Setting", 1003),
                    Category = (int)SettingCategory.Scrobbling,
                    Key = SettingRegistry.ScrobblingLastFmSharedSecret,
                    Comment =
                        "Shared secret used with last FM. See https://www.last.fm/api/authentication for more details.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1100,
                    ApiKey = SeedGuid("Setting", 1100),
                    Category = (int)SettingCategory.System,
                    Key = SettingRegistry.SystemBaseUrl,
                    Comment =
                        "Base URL for Melodee to use when building shareable links and image urls (e.g., 'https://server.domain.com:8080', 'http://server.domain.com').",
                    Value = MelodeeConfiguration.RequiredNotSetValue,
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1103,
                    ApiKey = SeedGuid("Setting", 1103),
                    Category = (int)SettingCategory.System,
                    Key = SettingRegistry.SystemSiteName,
                    Comment = "Name for this Melodee instance (used in emails and UI branding).",
                    Description = "Customize the display name of your Melodee instance. Defaults to 'Melodee' if not set.",
                    Value = "Melodee",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1101,
                    ApiKey = SeedGuid("Setting", 1101),
                    Category = (int)SettingCategory.System,
                    Key = SettingRegistry.SystemIsDownloadingEnabled,
                    Comment = "Is downloading enabled.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1102,
                    ApiKey = SeedGuid("Setting", 1102),
                    Category = (int)SettingCategory.System,
                    Key = SettingRegistry.SystemMaxUploadSize,
                    Comment = "Maximum upload size in bytes for UI uploads.",
                    Value = (5 * 1024 * 1024).ToString(),
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1400,
                    ApiKey = SeedGuid("Setting", 1400),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsArtistHousekeepingCronExpression,
                    Comment =
                        "Cron expression to run the artist housekeeping job, set empty to disable. Default of '0 0 0/1 1/1 * ? *' will run every hour. See https://www.freeformatter.com/cron-expression-generator-quartz.html",
                    Value = "0 0 0/1 1/1 * ? *",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1401,
                    ApiKey = SeedGuid("Setting", 1401),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsLibraryProcessCronExpression,
                    Comment =
                        "Cron expression to run the library process job, set empty to disable. Default of '0 */10 * ? * *' Every 10 minutes. See https://www.freeformatter.com/cron-expression-generator-quartz.html",
                    Value = "0 */10 * ? * *",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1402,
                    ApiKey = SeedGuid("Setting", 1402),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsLibraryInsertCronExpression,
                    Comment =
                        "Cron expression to run the library scan job, set empty to disable. Default of '0 0 0 * * ?' will run every day at 00:00. See https://www.freeformatter.com/cron-expression-generator-quartz.html",
                    Value = "0 0 0 * * ?",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1403,
                    ApiKey = SeedGuid("Setting", 1403),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsMusicBrainzUpdateDatabaseCronExpression,
                    Comment =
                        "Cron expression to run the musicbrainz database house keeping job, set empty to disable. Default of '0 0 12 1 * ?' will run first day of the month. See https://www.freeformatter.com/cron-expression-generator-quartz.html",
                    Value = "0 0 12 1 * ?",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1404,
                    ApiKey = SeedGuid("Setting", 1404),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsArtistSearchEngineHousekeepingCronExpression,
                    Comment =
                        "Cron expression to run the artist search engine house keeping job, set empty to disable. Default of '0 0 0 * * ?' will run every day at 00:00. See https://www.freeformatter.com/cron-expression-generator-quartz.html",
                    Value = "0 0 0 * * ?",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1405,
                    ApiKey = SeedGuid("Setting", 1405),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsChartUpdateCronExpression,
                    Comment =
                        "Cron expression to run the chart update job which links chart items to albums, set empty to disable. Default of '0 0 2 * * ?' will run every day at 02:00. See https://www.freeformatter.com/cron-expression-generator-quartz.html",
                    Value = "0 0 2 * * ?",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1406,
                    ApiKey = SeedGuid("Setting", 1406),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsStagingAutoMoveCronExpression,
                    Comment =
                        "Cron expression for staging auto-move job. Moves 'Ok' albums to storage. Default '0 */15 * * * ?' runs every 15 min. Also triggered after inbound processing.",
                    Value = "0 */15 * * * ?",
                    CreatedAt = seedDataTimestamp
                },
                // Email settings
                new Setting
                {
                    Id = 1500,
                    ApiKey = SeedGuid("Setting", 1500),
                    Key = SettingRegistry.EmailEnabled,
                    Comment = "Enable or disable email sending functionality",
                    Description = "When true, enables SMTP email sending for password resets and notifications",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1501,
                    ApiKey = SeedGuid("Setting", 1501),
                    Key = SettingRegistry.EmailFromName,
                    Comment = "Display name in From field of outgoing emails",
                    Value = "Melodee",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1502,
                    ApiKey = SeedGuid("Setting", 1502),
                    Key = SettingRegistry.EmailFromEmail,
                    Comment = "Email address in From field (REQUIRED for email sending)",
                    Description = "Example: noreply@yourdomain.com",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1503,
                    ApiKey = SeedGuid("Setting", 1503),
                    Key = SettingRegistry.EmailSmtpHost,
                    Comment = "SMTP server hostname (REQUIRED for email sending)",
                    Description = "Example: smtp.gmail.com or smtp.sendgrid.net",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1504,
                    ApiKey = SeedGuid("Setting", 1504),
                    Key = SettingRegistry.EmailSmtpPort,
                    Comment = "SMTP server port",
                    Description = "Common values: 587 (StartTLS), 465 (SSL), 25 (unencrypted)",
                    Value = "587",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1505,
                    ApiKey = SeedGuid("Setting", 1505),
                    Key = SettingRegistry.EmailSmtpUsername,
                    Comment = "SMTP authentication username (optional)",
                    Description = "Leave empty if SMTP server does not require authentication",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1506,
                    ApiKey = SeedGuid("Setting", 1506),
                    Key = SettingRegistry.EmailSmtpPassword,
                    Comment = "SMTP authentication password (optional, use env var email_smtpPassword)",
                    Description = "For security, set via environment variable: email_smtpPassword",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1507,
                    ApiKey = SeedGuid("Setting", 1507),
                    Key = SettingRegistry.EmailSmtpUseSsl,
                    Comment = "Use SSL connection for SMTP",
                    Description = "Set to true for port 465 (SSL), false for port 587 (StartTLS)",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1508,
                    ApiKey = SeedGuid("Setting", 1508),
                    Key = SettingRegistry.EmailSmtpUseStartTls,
                    Comment = "Use StartTLS for SMTP",
                    Description = "Recommended: true for port 587",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1509,
                    ApiKey = SeedGuid("Setting", 1509),
                    Key = SettingRegistry.EmailResetPasswordSubject,
                    Comment = "Password reset email subject line",
                    Description = "Subject for password reset emails",
                    Value = "Reset your Melodee password",
                    CreatedAt = seedDataTimestamp
                },
                // Security settings
                new Setting
                {
                    Id = 1600,
                    ApiKey = SeedGuid("Setting", 1600),
                    Key = SettingRegistry.SecurityPasswordResetTokenExpiryMinutes,
                    Comment = "Password reset token expiry time in minutes",
                    Description = "How long password reset links remain valid (default: 60 minutes)",
                    Value = "60",
                    CreatedAt = seedDataTimestamp
                },
                // Jellyfin API settings
                new Setting
                {
                    Id = 1700,
                    ApiKey = SeedGuid("Setting", 1700),
                    Key = SettingRegistry.JellyfinEnabled,
                    Comment = "Enable Jellyfin API compatibility",
                    Description = "When enabled, Melodee exposes Jellyfin-compatible endpoints for third-party music players",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1701,
                    ApiKey = SeedGuid("Setting", 1701),
                    Key = SettingRegistry.JellyfinRoutePrefix,
                    Comment = "Internal route prefix for Jellyfin API",
                    Description = "The internal route prefix used for Jellyfin API endpoints (default: /api/jf)",
                    Value = "/api/jf",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1702,
                    ApiKey = SeedGuid("Setting", 1702),
                    Key = SettingRegistry.JellyfinTokenExpiresAfterHours,
                    Comment = "Jellyfin token expiry time in hours",
                    Description = "How long Jellyfin access tokens remain valid (default: 168 hours / 7 days)",
                    Value = "168",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1703,
                    ApiKey = SeedGuid("Setting", 1703),
                    Key = SettingRegistry.JellyfinTokenMaxActivePerUser,
                    Comment = "Maximum active Jellyfin tokens per user",
                    Description = "The maximum number of active Jellyfin tokens allowed per user (default: 10)",
                    Value = "10",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1704,
                    ApiKey = SeedGuid("Setting", 1704),
                    Key = SettingRegistry.JellyfinTokenAllowLegacyHeaders,
                    Comment = "Allow legacy Emby/MediaBrowser headers",
                    Description = "Allow X-Emby-* and X-MediaBrowser-* headers for authentication (default: true)",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1705,
                    ApiKey = SeedGuid("Setting", 1705),
                    Key = SettingRegistry.JellyfinTokenPepper,
                    Comment = "Secret pepper for Jellyfin token hashing",
                    Description = "Server-side secret used in token hash computation. Change this value in production for added security.",
                    Value = "ChangeThisPepperInProduction",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1706,
                    ApiKey = SeedGuid("Setting", 1706),
                    Key = SettingRegistry.JellyfinRateLimitApiRequestsPerPeriod,
                    Comment = "API requests allowed per period",
                    Description = "Maximum number of Jellyfin API requests allowed per rate limit period (default: 200)",
                    Value = "200",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1707,
                    ApiKey = SeedGuid("Setting", 1707),
                    Key = SettingRegistry.JellyfinRateLimitApiPeriodSeconds,
                    Comment = "Rate limit period in seconds",
                    Description = "Duration of the rate limit period in seconds (default: 60)",
                    Value = "60",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1708,
                    ApiKey = SeedGuid("Setting", 1708),
                    Key = SettingRegistry.JellyfinRateLimitStreamConcurrentPerUser,
                    Comment = "Concurrent streams per user",
                    Description = "Maximum number of concurrent audio streams allowed per user (default: 2)",
                    Value = "2",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1709,
                    ApiKey = SeedGuid("Setting", 1709),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineDiscogsEnabled,
                    Comment = "Is Discogs search engine enabled.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1710,
                    ApiKey = SeedGuid("Setting", 1710),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineDiscogsUserToken,
                    Comment = "Discogs API user token for authentication.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1711,
                    ApiKey = SeedGuid("Setting", 1711),
                    Category = (int)SettingCategory.SearchEngine,
                    Key = SettingRegistry.SearchEngineWikiDataEnabled,
                    Comment = "Is WikiData search engine enabled.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                // Podcast settings
                new Setting
                {
                    Id = 1800,
                    ApiKey = SeedGuid("Setting", 1800),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastEnabled,
                    Comment = "Enable podcast support.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1801,
                    ApiKey = SeedGuid("Setting", 1801),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastHttpAllowHttp,
                    Comment = "Allow HTTP (non-secure) URLs for podcast feeds.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1802,
                    ApiKey = SeedGuid("Setting", 1802),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastHttpTimeoutSeconds,
                    Comment = "Timeout in seconds for HTTP requests to podcast feeds.",
                    Value = "30",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1803,
                    ApiKey = SeedGuid("Setting", 1803),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastHttpMaxRedirects,
                    Comment = "Maximum number of HTTP redirects to follow for podcast feeds. Podcast CDNs often use multiple analytics redirects, so 10 is recommended.",
                    Value = "10",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1804,
                    ApiKey = SeedGuid("Setting", 1804),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastHttpMaxFeedBytes,
                    Comment = "Maximum size in bytes for podcast feed responses.",
                    Value = "10485760",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1805,
                    ApiKey = SeedGuid("Setting", 1805),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastRefreshMaxItemsPerChannel,
                    Comment = "Maximum number of episodes to store per podcast channel.",
                    Value = "500",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1806,
                    ApiKey = SeedGuid("Setting", 1806),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastDownloadMaxConcurrentGlobal,
                    Comment = "Maximum concurrent podcast episode downloads (global).",
                    Value = "2",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1807,
                    ApiKey = SeedGuid("Setting", 1807),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastDownloadMaxConcurrentPerUser,
                    Comment = "Maximum concurrent podcast episode downloads per user.",
                    Value = "1",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1808,
                    ApiKey = SeedGuid("Setting", 1808),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastDownloadMaxEnclosureBytes,
                    Comment = "Maximum size in bytes for podcast episode downloads.",
                    Value = "2147483648",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1850,
                    ApiKey = SeedGuid("Setting", 1850),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsPodcastRefreshCronExpression,
                    Comment =
                        "Cron expression to run the podcast refresh job, set empty to disable. Default of '0 */15 * ? * *' runs every 15 minutes.",
                    Value = "0 */15 * ? * *",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1851,
                    ApiKey = SeedGuid("Setting", 1851),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsPodcastDownloadCronExpression,
                    Comment =
                        "Cron expression to run the podcast download job, set empty to disable. Default of '0 */5 * ? * *' runs every 5 minutes.",
                    Value = "0 */5 * ? * *",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1809,
                    ApiKey = SeedGuid("Setting", 1809),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastRetentionDownloadedEpisodesInDays,
                    Comment = "Number of days to keep downloaded episodes. 0 to disable retention.",
                    Value = "0",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1810,
                    ApiKey = SeedGuid("Setting", 1810),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastRecoveryStuckDownloadThresholdMinutes,
                    Comment = "Threshold in minutes to consider a downloading episode as stuck.",
                    Value = "60",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1811,
                    ApiKey = SeedGuid("Setting", 1811),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastRecoveryOrphanedUsageThresholdHours,
                    Comment = "Threshold in hours to consider a temporary file orphaned.",
                    Value = "12",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1852,
                    ApiKey = SeedGuid("Setting", 1852),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsPodcastCleanupCronExpression,
                    Comment =
                        "Cron expression to run the podcast cleanup job, set empty to disable. Default of '0 0 2 * * ?' runs daily at 2 AM.",
                    Value = "0 0 2 * * ?",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1853,
                    ApiKey = SeedGuid("Setting", 1853),
                    Category = (int)SettingCategory.Jobs,
                    Key = SettingRegistry.JobsPodcastRecoveryCronExpression,
                    Comment =
                        "Cron expression to run the podcast recovery job, set empty to disable. Default of '0 */30 * ? * *' runs every 30 minutes.",
                    Value = "0 */30 * ? * *",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1812,
                    ApiKey = SeedGuid("Setting", 1812),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastQuotaMaxBytesPerUser,
                    Comment = "Maximum total storage in bytes for all podcasts per user. 0 for unlimited.",
                    Value = "5368709120", // 5GB
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1813,
                    ApiKey = SeedGuid("Setting", 1813),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastRetentionKeepLastNEpisodes,
                    Comment = "Keep only the last N downloaded episodes per channel. 0 to disable this policy.",
                    Value = "0",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1814,
                    ApiKey = SeedGuid("Setting", 1814),
                    Category = (int)SettingCategory.Podcast,
                    Key = SettingRegistry.PodcastRetentionKeepUnplayedOnly,
                    Comment = "Delete downloaded episodes after they have been played. false to disable.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                // Jukebox settings
                new Setting
                {
                    Id = 1900,
                    ApiKey = SeedGuid("Setting", 1900),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.JukeboxEnabled,
                    Comment = "Enable Jukebox support for server-side playback.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1901,
                    ApiKey = SeedGuid("Setting", 1901),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.JukeboxBackendType,
                    Comment = "The type of backend to use for jukebox playback (e.g., 'mpv', 'mpd'). Leave empty for no backend.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                // MPV Backend settings
                new Setting
                {
                    Id = 1910,
                    ApiKey = SeedGuid("Setting", 1910),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpvPath,
                    Comment = "Path to the MPV executable. Leave empty to use system PATH.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1911,
                    ApiKey = SeedGuid("Setting", 1911),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpvAudioDevice,
                    Comment = "Audio device to use for MPV playback. Leave empty for default device.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1912,
                    ApiKey = SeedGuid("Setting", 1912),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpvExtraArgs,
                    Comment = "Extra command-line arguments to pass to MPV.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1913,
                    ApiKey = SeedGuid("Setting", 1913),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpvSocketPath,
                    Comment = "Path for the MPV IPC socket. Leave empty for auto temp directory.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1914,
                    ApiKey = SeedGuid("Setting", 1914),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpvInitialVolume,
                    Comment = "Initial volume level for MPV (0.0 to 1.0). Default is 0.8.",
                    Value = "0.8",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1915,
                    ApiKey = SeedGuid("Setting", 1915),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpvEnableDebugOutput,
                    Comment = "Enable verbose debug output for MPV.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                // MPD Backend settings
                new Setting
                {
                    Id = 1920,
                    ApiKey = SeedGuid("Setting", 1920),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdInstanceName,
                    Comment = "Unique name/identifier for this MPD instance (for multi-instance support).",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1921,
                    ApiKey = SeedGuid("Setting", 1921),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdHost,
                    Comment = "Hostname or IP address of the MPD server.",
                    Value = "localhost",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1922,
                    ApiKey = SeedGuid("Setting", 1922),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdPort,
                    Comment = "Port number for MPD connection.",
                    Value = "6600",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1923,
                    ApiKey = SeedGuid("Setting", 1923),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdPassword,
                    Comment = "Password for MPD authentication. Leave empty if no password.",
                    Value = "",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1924,
                    ApiKey = SeedGuid("Setting", 1924),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdTimeoutMs,
                    Comment = "Timeout for MPD TCP connection and operations in milliseconds.",
                    Value = "10000",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1925,
                    ApiKey = SeedGuid("Setting", 1925),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdInitialVolume,
                    Comment = "Initial volume level for MPD (0.0 to 1.0). Default is 0.8.",
                    Value = "0.8",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1926,
                    ApiKey = SeedGuid("Setting", 1926),
                    Category = (int)SettingCategory.Jukebox,
                    Key = SettingRegistry.MpdEnableDebugOutput,
                    Comment = "Enable debug logging for MPD commands.",
                    Value = "false",
                    CreatedAt = seedDataTimestamp
                },
                new Setting
                {
                    Id = 1927,
                    ApiKey = SeedGuid("Setting", 1927),
                    Category = (int)SettingCategory.System,
                    Key = SettingRegistry.UserDeviceProfileEnabled,
                    Comment = "Enable per-user and per-device transcoding profiles.",
                    Value = "true",
                    CreatedAt = seedDataTimestamp
                }
            );
        });

        modelBuilder.Entity<UserSongPlayHistory>(s =>
        {
            s.HasIndex(x => new { x.UserId, x.PlayedAt });
            s.HasIndex(x => new { x.SongId, x.PlayedAt });
            s.HasIndex(x => x.PlayedAt);
        });

        modelBuilder.Entity<ChartItem>(ci =>
        {
            ci.HasOne(x => x.Chart)
                .WithMany(c => c.Items)
                .HasForeignKey(x => x.ChartId)
                .OnDelete(DeleteBehavior.Cascade);

            ci.HasOne(x => x.LinkedArtist)
                .WithMany()
                .HasForeignKey(x => x.LinkedArtistId)
                .OnDelete(DeleteBehavior.SetNull);

            ci.HasOne(x => x.LinkedAlbum)
                .WithMany()
                .HasForeignKey(x => x.LinkedAlbumId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Request>(r =>
        {
            r.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            r.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            r.HasOne(x => x.LastActivityUser)
                .WithMany()
                .HasForeignKey(x => x.LastActivityUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Index for index-page default sort
            r.HasIndex(x => new { x.CreatedAt, x.Id })
                .IsDescending(true, true);

            // Filter + sort combos
            r.HasIndex(x => new { x.Status, x.CreatedAt, x.Id })
                .IsDescending(false, true, true);

            r.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt, x.Id })
                .IsDescending(false, true, true);

            r.HasIndex(x => new { x.Status, x.CreatedByUserId, x.CreatedAt, x.Id })
                .IsDescending(false, false, true, true);

            // Entity filter (ApiKey-based)
            r.HasIndex(x => new { x.TargetArtistApiKey, x.CreatedAt, x.Id })
                .IsDescending(false, true, true);

            r.HasIndex(x => new { x.TargetAlbumApiKey, x.CreatedAt, x.Id })
                .IsDescending(false, true, true);

            // Activity hot path
            r.HasIndex(x => new { x.LastActivityAt, x.Id })
                .IsDescending(true, true);
        });

        modelBuilder.Entity<RequestComment>(rc =>
        {
            rc.HasOne(x => x.Request)
                .WithMany(r => r.Comments)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            rc.HasOne(x => x.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(x => x.ParentCommentId)
                .OnDelete(DeleteBehavior.Cascade);

            rc.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Chronological ordering for comments
            rc.HasIndex(x => new { x.RequestId, x.CreatedAt, x.Id });

            // Replies ordering
            rc.HasIndex(x => new { x.RequestId, x.ParentCommentId, x.CreatedAt, x.Id });
        });

        modelBuilder.Entity<RequestUserState>(rus =>
        {
            rus.HasOne(x => x.Request)
                .WithMany(r => r.UserStates)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            rus.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Dashboard list index
            rus.HasIndex(x => new { x.UserId, x.LastSeenAt });
        });

        modelBuilder.Entity<RequestParticipant>(rp =>
        {
            rp.HasOne(x => x.Request)
                .WithMany(r => r.Participants)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            rp.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Navbar unread lookup index
            rp.HasIndex(x => new { x.UserId, x.RequestId });
        });

        modelBuilder.Entity<JellyfinAccessToken>(jat =>
        {
            jat.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SmartPlaylist>(sp =>
        {
            sp.HasIndex(x => new { x.UserId, x.Name })
                .IsUnique();

            sp.HasIndex(x => x.IsPublic);

            sp.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PodcastChannel>(pc =>
        {
            pc.HasIndex(x => new { x.UserId, x.FeedUrl })
                .IsUnique();

            pc.HasIndex(x => x.IsDeleted);

            pc.HasIndex(x => x.NextSyncAt);

            pc.HasMany(x => x.Episodes)
                .WithOne(e => e.PodcastChannel)
                .HasForeignKey(e => e.PodcastChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            pc.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<PodcastEpisode>(pe =>
        {
            pe.HasIndex(x => new { x.PodcastChannelId, x.PublishDate });

            pe.HasIndex(x => new { x.PodcastChannelId, x.EpisodeKey })
                .IsUnique();

            pe.HasIndex(x => new { x.PodcastChannelId, x.DownloadStatus });

            pe.HasOne(x => x.PodcastChannel)
                .WithMany(c => c.Episodes)
                .HasForeignKey(x => x.PodcastChannelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // sph; left here for example of GIN FTS. More info here https://www.npgsql.org/efcore/mapping/full-text-search.html?tabs=pg12%2Cv5
        // modelBuilder.Entity<Song>()
        //     .HasIndex(s => new
        //     {
        //         SongTitle= s.Title,
        //         AlbumTitle= s.AlbumDisc.Title,
        //         ArtistName = s.AlbumDisc.Album.Name
        //     })
        //     .HasMethod("GIN")
        //     .IsTsVectorExpressionIndex("english");

        // modelBuilder.Entity<User>()
        //     .HasGeneratedTsVectorColumn(u => u.SearchVector, "english", u => new { u.Email, u.UserName })
        //     .HasIndex(u => u.Email)
        //     .HasMethod("GIN");

        // Party Mode entities
        modelBuilder.Entity<PartySession>(ps =>
        {
            ps.HasIndex(x => x.OwnerUserId);
            ps.HasIndex(x => x.Status);
            ps.HasIndex(x => x.ActiveEndpointId);

            ps.HasOne(x => x.ActiveEndpoint)
                .WithMany()
                .HasForeignKey(x => x.ActiveEndpointId)
                .HasPrincipalKey(x => x.ApiKey)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PartySessionParticipant>(psp =>
        {
            psp.HasIndex(x => new { x.PartySessionId, x.UserId })
                .IsUnique();

            psp.HasIndex(x => x.UserId);
            psp.HasIndex(x => x.Role);
            psp.HasIndex(x => x.IsBanned);

            psp.HasOne(x => x.PartySession)
                .WithMany(s => s.Participants)
                .HasForeignKey(x => x.PartySessionId)
                .OnDelete(DeleteBehavior.Cascade);

            psp.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PartyQueueItem>(pqi =>
        {
            pqi.HasIndex(x => new { x.PartySessionId, x.SortOrder });

            pqi.HasIndex(x => x.SongApiKey);
            pqi.HasIndex(x => x.EnqueuedByUserId);
            pqi.HasIndex(x => x.EnqueuedAt);

            pqi.HasOne(x => x.PartySession)
                .WithMany(s => s.QueueItems)
                .HasForeignKey(x => x.PartySessionId)
                .OnDelete(DeleteBehavior.Cascade);

            pqi.HasOne(x => x.EnqueuedByUser)
                .WithMany()
                .HasForeignKey(x => x.EnqueuedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PartyPlaybackState>(pps =>
        {
            pps.HasIndex(x => x.PartySessionId)
                .IsUnique();

            pps.HasIndex(x => x.CurrentQueueItemApiKey);
            pps.HasIndex(x => x.LastHeartbeatAt);
            pps.HasIndex(x => x.IsPlaying);

            pps.HasOne(x => x.PartySession)
                .WithOne(s => s.PlaybackState)
                .HasForeignKey<PartyPlaybackState>(x => x.PartySessionId)
                .OnDelete(DeleteBehavior.Cascade);

            pps.HasOne(x => x.CurrentQueueItem)
                .WithMany()
                .HasForeignKey(x => x.CurrentQueueItemApiKey)
                .HasPrincipalKey(x => x.ApiKey)
                .OnDelete(DeleteBehavior.SetNull);

            pps.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PartySessionEndpoint>(e =>
        {
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.LastSeenAt);
            e.HasIndex(x => x.Type);
            e.HasIndex(x => x.IsShared);
            e.HasIndex(x => x.Room);

            e.HasOne(x => x.OwnerUser)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UserSongPlayHistory>(ush =>
        {
            ush.HasIndex(x => x.UserId);
            ush.HasIndex(x => x.PlayedAt);
            ush.HasIndex(x => new { x.UserId, x.PlayedAt });
            ush.HasIndex(x => x.SongId);
            ush.HasIndex(x => x.IsNowPlaying);
        });

        modelBuilder.Entity<PartyQueueItem>(pqi =>
        {
            pqi.HasIndex(x => x.PartySessionId);
            pqi.HasIndex(x => x.SortOrder);
            pqi.HasIndex(x => new { x.PartySessionId, x.SortOrder });
            pqi.HasIndex(x => x.EnqueuedByUserId);
            pqi.HasIndex(x => x.EnqueuedAt);
        });

        modelBuilder.Entity<LibraryScanHistory>(lsh =>
        {
            lsh.HasIndex(x => x.LibraryId);
            lsh.HasIndex(x => x.CreatedAt);
            lsh.HasIndex(x => new { x.LibraryId, x.CreatedAt });
            lsh.HasIndex(x => x.ForArtistId);
            lsh.HasIndex(x => x.ForAlbumId);
        });

        modelBuilder.Entity<UserGroup>(ug =>
        {
            ug.HasIndex(x => x.Name).IsUnique();

            // NOTE: The "All Users" group is seeded as a convenience placeholder.
            // Users are NOT automatically added to this group; membership must be managed
            // explicitly by application logic if this group is to be used for library access control.
            ug.HasData(new UserGroup
            {
                Id = 1,
                ApiKey = SeedGuid("UserGroup", 1),
                Name = "All Users",
                Description = "Default group for all users",
                CreatedAt = seedDataTimestamp
            });
        });

        modelBuilder.Entity<UserGroupMember>(ugm =>
        {
            ugm.HasIndex(x => x.UserId);
            ugm.HasIndex(x => x.UserGroupId);
            ugm.HasIndex(x => new { x.UserId, x.UserGroupId }).IsUnique();

            ugm.HasOne(x => x.User)
                .WithMany(u => u.GroupMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            ugm.HasOne(x => x.UserGroup)
                .WithMany(g => g.Members)
                .HasForeignKey(x => x.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LibraryAccessControl>(lac =>
        {
            lac.HasIndex(x => x.LibraryId);
            lac.HasIndex(x => x.UserGroupId);
            lac.HasIndex(x => new { x.LibraryId, x.UserGroupId }).IsUnique();

            lac.HasOne(x => x.Library)
                .WithMany(l => l.AccessControls)
                .HasForeignKey(x => x.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

            lac.HasOne(x => x.UserGroup)
                .WithMany(g => g.LibraryAccessControls)
                .HasForeignKey(x => x.UserGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
        {
            w.Ignore(RelationalEventId.PendingModelChangesWarning);
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning);
        });
    }
}
