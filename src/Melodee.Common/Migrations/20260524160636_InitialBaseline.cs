using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Charts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsGeneratedPlaylistEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Charts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StartedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    DurationInMs = table.Column<double>(type: "double precision", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    WasManualTrigger = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ArtistCount = table.Column<int>(type: "integer", nullable: true),
                    AlbumCount = table.Column<int>(type: "integer", nullable: true),
                    SongCount = table.Column<int>(type: "integer", nullable: true),
                    Path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    LastScanAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PodcastChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    FeedUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TitleNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    SiteUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CoverArtLocalPath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Etag = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastSyncAttemptAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ConsecutiveFailureCount = table.Column<int>(type: "integer", nullable: false),
                    NextSyncAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    MaxDownloadedEpisodes = table.Column<int>(type: "integer", nullable: true),
                    MaxStorageBytes = table.Column<long>(type: "bigint", nullable: true),
                    AutoDownloadEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RefreshIntervalHours = table.Column<int>(type: "integer", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RadioStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StreamUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HomePageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadioStations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ByUserId = table.Column<int>(type: "integer", nullable: false),
                    ByUserAgent = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SearchQuery = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FoundArtistsCount = table.Column<int>(type: "integer", nullable: false),
                    FoundAlbumsCount = table.Column<int>(type: "integer", nullable: false),
                    FoundSongsCount = table.Column<int>(type: "integer", nullable: false),
                    FoundOtherItems = table.Column<int>(type: "integer", nullable: false),
                    SearchDurationInMs = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Comment = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: true),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShareActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShareId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ByUserAgent = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Client = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserNameNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EmailConfirmedDate = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordEncrypted = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PasswordHashAlgorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OpenSubsonicSecretProtected = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastLoginAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastActivityAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    IsEditor = table.Column<bool>(type: "boolean", nullable: false),
                    HasSettingsRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasDownloadRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasUploadRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasPlaylistRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasCoverArtRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasCommentRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasPodcastRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasStreamRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasJukeboxRole = table.Column<bool>(type: "boolean", nullable: false),
                    HasShareRole = table.Column<bool>(type: "boolean", nullable: false),
                    IsScrobblingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastFmSessionKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PreferredTheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    HatedGenres = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StarredGenres = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PasswordResetToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PasswordResetTokenExpiresAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SortName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RealName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Directory = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Roles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AlbumCount = table.Column<int>(type: "integer", nullable: false),
                    SongCount = table.Column<int>(type: "integer", nullable: false),
                    LibraryId = table.Column<int>(type: "integer", nullable: false),
                    Biography = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    ImageCount = table.Column<int>(type: "integer", nullable: true),
                    MetaDataStatus = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    AlternateNames = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastPlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastMetaDataUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PlayedCount = table.Column<int>(type: "integer", nullable: false),
                    ItunesId = table.Column<string>(type: "text", nullable: true),
                    AmgId = table.Column<string>(type: "text", nullable: true),
                    DeezerId = table.Column<int>(type: "integer", nullable: true),
                    DiscogsId = table.Column<string>(type: "text", nullable: true),
                    WikiDataId = table.Column<string>(type: "text", nullable: true),
                    MusicBrainzId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastFmId = table.Column<string>(type: "text", nullable: true),
                    SpotifyId = table.Column<string>(type: "text", nullable: true),
                    CalculatedRating = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artists_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryScanHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LibraryId = table.Column<int>(type: "integer", nullable: false),
                    ForArtistId = table.Column<int>(type: "integer", nullable: true),
                    ForAlbumId = table.Column<int>(type: "integer", nullable: true),
                    FoundArtistsCount = table.Column<int>(type: "integer", nullable: false),
                    FoundAlbumsCount = table.Column<int>(type: "integer", nullable: false),
                    FoundSongsCount = table.Column<int>(type: "integer", nullable: false),
                    DurationInMs = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryScanHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryScanHistories_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodcastEpisodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PodcastChannelId = table.Column<int>(type: "integer", nullable: false),
                    Guid = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TitleNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    PublishDate = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    EnclosureUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EnclosureLength = table.Column<long>(type: "bigint", nullable: true),
                    MimeType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    EpisodeKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DownloadStatus = table.Column<int>(type: "integer", nullable: false),
                    DownloadError = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LocalPath = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LocalFileSize = table.Column<long>(type: "bigint", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    QueuedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodes_PodcastChannels_PodcastChannelId",
                        column: x => x.PodcastChannelId,
                        principalTable: "PodcastChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryAccessControls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LibraryId = table.Column<int>(type: "integer", nullable: false),
                    UserGroupId = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryAccessControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryAccessControls_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryAccessControls_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JellyfinAccessTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenPrefixHash = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TokenSalt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Client = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Device = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DeviceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Version = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JellyfinAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JellyfinAccessTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartySessionEndpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    LastSeenAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    Room = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartySessionEndpoints", x => x.Id);
                    table.UniqueConstraint("AK_PartySessionEndpoints_ApiKey", x => x.ApiKey);
                    table.ForeignKey(
                        name: "FK_PartySessionEndpoints_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Client = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastSeenAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    MaxBitRate = table.Column<int>(type: "integer", nullable: true),
                    ScrobbleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TranscodingId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Hostname = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HashedToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ReplacedByToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SessionStartedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: false),
                    ArtistName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TargetArtistApiKey = table.Column<Guid>(type: "uuid", nullable: true),
                    AlbumTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TargetAlbumApiKey = table.Column<Guid>(type: "uuid", nullable: true),
                    SongTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TargetSongApiKey = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseYear = table.Column<int>(type: "integer", nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    LastActivityAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastActivityUserId = table.Column<int>(type: "integer", nullable: true),
                    LastActivityType = table.Column<int>(type: "integer", nullable: false),
                    ArtistNameNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AlbumTitleNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SongTitleNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DescriptionNormalized = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Requests_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Requests_Users_LastActivityUserId",
                        column: x => x.LastActivityUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Requests_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ShareId = table.Column<int>(type: "integer", nullable: false),
                    ShareType = table.Column<int>(type: "integer", nullable: false),
                    ShareUniqueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsDownloadable = table.Column<bool>(type: "boolean", nullable: false),
                    LastVisitedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    VisitCount = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shares_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmartPlaylists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MqlQuery = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastResultCount = table.Column<int>(type: "integer", nullable: false),
                    LastEvaluatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    NormalizedQuery = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartPlaylists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartPlaylists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserEqualizerPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BandsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEqualizerPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserEqualizerPresets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    UserGroupId = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserGroupMembers_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroupMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PinId = table.Column<int>(type: "integer", nullable: false),
                    PinType = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPlaybackSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CrossfadeDuration = table.Column<double>(type: "double precision", nullable: false),
                    GaplessPlayback = table.Column<bool>(type: "boolean", nullable: false),
                    VolumeNormalization = table.Column<bool>(type: "boolean", nullable: false),
                    ReplayGain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AudioQuality = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EqualizerPreset = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastUsedDevice = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaybackSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPlaybackSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSocialLogins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    HostedDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastLoginAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSocialLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSocialLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ArtistId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SortName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AlbumStatus = table.Column<short>(type: "smallint", nullable: false),
                    MetaDataStatus = table.Column<int>(type: "integer", nullable: false),
                    ImageCount = table.Column<int>(type: "integer", nullable: true),
                    AlbumType = table.Column<short>(type: "smallint", nullable: false),
                    OriginalReleaseDate = table.Column<LocalDate>(type: "date", nullable: true),
                    ReleaseDate = table.Column<LocalDate>(type: "date", nullable: false),
                    IsCompilation = table.Column<bool>(type: "boolean", nullable: false),
                    SongCount = table.Column<short>(type: "smallint", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: false),
                    Genres = table.Column<string[]>(type: "text[]", maxLength: 2000, nullable: true),
                    Moods = table.Column<string[]>(type: "text[]", maxLength: 2000, nullable: true),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReplayGain = table.Column<double>(type: "double precision", nullable: true),
                    ReplayPeak = table.Column<double>(type: "double precision", nullable: true),
                    Directory = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    AlternateNames = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastPlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastMetaDataUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PlayedCount = table.Column<int>(type: "integer", nullable: false),
                    ItunesId = table.Column<string>(type: "text", nullable: true),
                    AmgId = table.Column<string>(type: "text", nullable: true),
                    DeezerId = table.Column<int>(type: "integer", nullable: true),
                    DiscogsId = table.Column<string>(type: "text", nullable: true),
                    WikiDataId = table.Column<string>(type: "text", nullable: true),
                    MusicBrainzId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastFmId = table.Column<string>(type: "text", nullable: true),
                    SpotifyId = table.Column<string>(type: "text", nullable: true),
                    CalculatedRating = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtistRelation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ArtistId = table.Column<int>(type: "integer", nullable: false),
                    RelatedArtistId = table.Column<int>(type: "integer", nullable: false),
                    ArtistRelationType = table.Column<int>(type: "integer", nullable: false),
                    RelationStart = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    RelationEnd = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistRelation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtistRelation_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArtistRelation_Artists_RelatedArtistId",
                        column: x => x.RelatedArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserArtists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ArtistId = table.Column<int>(type: "integer", nullable: false),
                    IsStarred = table.Column<bool>(type: "boolean", nullable: false),
                    IsHated = table.Column<bool>(type: "boolean", nullable: false),
                    StarredAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserArtists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserArtists_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserArtists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodcastEpisodeBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PodcastEpisodeId = table.Column<int>(type: "integer", nullable: false),
                    PositionSeconds = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodcastEpisodeBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodeBookmarks_PodcastEpisodes_PodcastEpisodeId",
                        column: x => x.PodcastEpisodeId,
                        principalTable: "PodcastEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PodcastEpisodeBookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPodcastEpisodePlayHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PodcastEpisodeId = table.Column<int>(type: "integer", nullable: false),
                    PlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Client = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ByUserAgent = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SecondsPlayed = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<short>(type: "smallint", nullable: false),
                    IsNowPlaying = table.Column<bool>(type: "boolean", nullable: false),
                    LastHeartbeatAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPodcastEpisodePlayHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPodcastEpisodePlayHistories_PodcastEpisodes_PodcastEpis~",
                        column: x => x.PodcastEpisodeId,
                        principalTable: "PodcastEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPodcastEpisodePlayHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartySessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JoinCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActiveEndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    QueueRevision = table.Column<long>(type: "bigint", nullable: false),
                    PlaybackRevision = table.Column<long>(type: "bigint", nullable: false),
                    IsQueueLocked = table.Column<bool>(type: "boolean", nullable: false),
                    IsEndpointOffline = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartySessions_PartySessionEndpoints_ActiveEndpointId",
                        column: x => x.ActiveEndpointId,
                        principalTable: "PartySessionEndpoints",
                        principalColumn: "ApiKey",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PartySessions_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDeviceProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<int>(type: "integer", nullable: true),
                    IsDefaultProfile = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DirectPlay = table.Column<bool>(type: "boolean", nullable: false),
                    TargetCodec = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MaxBitrate = table.Column<int>(type: "integer", nullable: true),
                    ResampleRate = table.Column<int>(type: "integer", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDeviceProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDeviceProfiles_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserDeviceProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<int>(type: "integer", nullable: false),
                    ParentCommentId = table.Column<int>(type: "integer", nullable: true),
                    Body = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestComments_RequestComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "RequestComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestComments_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestComments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RequestParticipants",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    IsCreator = table.Column<bool>(type: "boolean", nullable: false),
                    IsCommenter = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestParticipants", x => new { x.RequestId, x.UserId });
                    table.ForeignKey(
                        name: "FK_RequestParticipants_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestUserStates",
                columns: table => new
                {
                    RequestId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestUserStates", x => new { x.RequestId, x.UserId });
                    table.ForeignKey(
                        name: "FK_RequestUserStates_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestUserStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChartItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChartId = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    ArtistName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AlbumTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ReleaseYear = table.Column<int>(type: "integer", nullable: true),
                    LinkedArtistId = table.Column<int>(type: "integer", nullable: true),
                    LinkedAlbumId = table.Column<int>(type: "integer", nullable: true),
                    LinkStatus = table.Column<short>(type: "smallint", nullable: false),
                    LinkConfidence = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    LinkNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChartItems_Albums_LinkedAlbumId",
                        column: x => x.LinkedAlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChartItems_Artists_LinkedArtistId",
                        column: x => x.LinkedArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChartItems_Charts_ChartId",
                        column: x => x.ChartId,
                        principalTable: "Charts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AlbumId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TitleSort = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TitleNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Genres = table.Column<string[]>(type: "text[]", maxLength: 2000, nullable: true),
                    Moods = table.Column<string[]>(type: "text[]", maxLength: 2000, nullable: true),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReplayGain = table.Column<double>(type: "double precision", nullable: true),
                    ReplayPeak = table.Column<double>(type: "double precision", nullable: true),
                    ImageCount = table.Column<int>(type: "integer", nullable: true),
                    SongNumber = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Lyrics = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PartTitles = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: false),
                    SamplingRate = table.Column<int>(type: "integer", nullable: false),
                    BitRate = table.Column<int>(type: "integer", nullable: false),
                    BitDepth = table.Column<int>(type: "integer", nullable: false),
                    BPM = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ChannelCount = table.Column<int>(type: "integer", nullable: true),
                    IsVbr = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    AlternateNames = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastPlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastMetaDataUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PlayedCount = table.Column<int>(type: "integer", nullable: false),
                    ItunesId = table.Column<string>(type: "text", nullable: true),
                    AmgId = table.Column<string>(type: "text", nullable: true),
                    DeezerId = table.Column<int>(type: "integer", nullable: true),
                    DiscogsId = table.Column<string>(type: "text", nullable: true),
                    WikiDataId = table.Column<string>(type: "text", nullable: true),
                    MusicBrainzId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastFmId = table.Column<string>(type: "text", nullable: true),
                    SpotifyId = table.Column<string>(type: "text", nullable: true),
                    CalculatedRating = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Songs_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAlbums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AlbumId = table.Column<int>(type: "integer", nullable: false),
                    PlayedCount = table.Column<int>(type: "integer", nullable: false),
                    LastPlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsStarred = table.Column<bool>(type: "boolean", nullable: false),
                    IsHated = table.Column<bool>(type: "boolean", nullable: false),
                    StarredAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAlbums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAlbums_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAlbums_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartyAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartySessionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartyAuditEvents_PartySessions_PartySessionId",
                        column: x => x.PartySessionId,
                        principalTable: "PartySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartyAuditEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartyQueueItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartySessionId = table.Column<int>(type: "integer", nullable: false),
                    SongApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    EnqueuedByUserId = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyQueueItems", x => x.Id);
                    table.UniqueConstraint("AK_PartyQueueItems_ApiKey", x => x.ApiKey);
                    table.ForeignKey(
                        name: "FK_PartyQueueItems_PartySessions_PartySessionId",
                        column: x => x.PartySessionId,
                        principalTable: "PartySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartyQueueItems_Users_EnqueuedByUserId",
                        column: x => x.EnqueuedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartySessionParticipants",
                columns: table => new
                {
                    PartySessionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsBanned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartySessionParticipants", x => new { x.PartySessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PartySessionParticipants_PartySessions_PartySessionId",
                        column: x => x.PartySessionId,
                        principalTable: "PartySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartySessionParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SongId = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true),
                    AlternateNames = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastPlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LastMetaDataUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    PlayedCount = table.Column<int>(type: "integer", nullable: false),
                    ItunesId = table.Column<string>(type: "text", nullable: true),
                    AmgId = table.Column<string>(type: "text", nullable: true),
                    DeezerId = table.Column<int>(type: "integer", nullable: true),
                    DiscogsId = table.Column<string>(type: "text", nullable: true),
                    WikiDataId = table.Column<string>(type: "text", nullable: true),
                    MusicBrainzId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastFmId = table.Column<string>(type: "text", nullable: true),
                    SpotifyId = table.Column<string>(type: "text", nullable: true),
                    CalculatedRating = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contributors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Role = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SubRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ArtistId = table.Column<int>(type: "integer", nullable: true),
                    ContributorName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MetaTagIdentifier = table.Column<int>(type: "integer", nullable: false),
                    SongId = table.Column<int>(type: "integer", nullable: true),
                    AlbumId = table.Column<int>(type: "integer", nullable: false),
                    ContributorType = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contributors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contributors_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Contributors_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contributors_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    SongCount = table.Column<short>(type: "smallint", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: false),
                    AllowedUserIds = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SongId = table.Column<int>(type: "integer", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playlists_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Playlists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayQues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SongId = table.Column<int>(type: "integer", nullable: false),
                    SongApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    IsCurrentSong = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Position = table.Column<double>(type: "double precision", nullable: false),
                    PlayQueId = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayQues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayQues_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayQues_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSongPlayHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SongId = table.Column<int>(type: "integer", nullable: false),
                    PlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Client = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ByUserAgent = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SecondsPlayed = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<short>(type: "smallint", nullable: false),
                    IsNowPlaying = table.Column<bool>(type: "boolean", nullable: false),
                    LastHeartbeatAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSongPlayHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSongPlayHistories_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSongPlayHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSongs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SongId = table.Column<int>(type: "integer", nullable: false),
                    PlayedCount = table.Column<int>(type: "integer", nullable: false),
                    LastPlayedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsStarred = table.Column<bool>(type: "boolean", nullable: false),
                    IsHated = table.Column<bool>(type: "boolean", nullable: false),
                    StarredAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSongs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSongs_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSongs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartyPlaybackStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartySessionId = table.Column<int>(type: "integer", nullable: false),
                    CurrentQueueItemApiKey = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsPlaying = table.Column<bool>(type: "boolean", nullable: false),
                    Volume = table.Column<double>(type: "double precision", nullable: true),
                    LastHeartbeatAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyPlaybackStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartyPlaybackStates_PartyQueueItems_CurrentQueueItemApiKey",
                        column: x => x.CurrentQueueItemApiKey,
                        principalTable: "PartyQueueItems",
                        principalColumn: "ApiKey",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PartyPlaybackStates_PartySessions_PartySessionId",
                        column: x => x.PartySessionId,
                        principalTable: "PartySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartyPlaybackStates_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistSong",
                columns: table => new
                {
                    SongId = table.Column<int>(type: "integer", nullable: false),
                    PlaylistId = table.Column<int>(type: "integer", nullable: false),
                    SongApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaylistOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistSong", x => new { x.SongId, x.PlaylistId });
                    table.ForeignKey(
                        name: "FK_PlaylistSong_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistSong_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistUploadedFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    PlaylistId = table.Column<int>(type: "integer", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistUploadedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistUploadedFiles_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "Playlists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaylistUploadedFiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistUploadedFileItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaylistUploadedFileId = table.Column<int>(type: "integer", nullable: false),
                    SongId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RawReference = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    NormalizedReference = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    HintsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastAttemptAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "character varying(62000)", maxLength: 62000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistUploadedFileItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistUploadedFileItems_PlaylistUploadedFiles_PlaylistUpl~",
                        column: x => x.PlaylistUploadedFileId,
                        principalTable: "PlaylistUploadedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistUploadedFileItems_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Libraries",
                columns: new[] { "Id", "AlbumCount", "ApiKey", "ArtistCount", "CreatedAt", "Description", "IsLocked", "LastScanAt", "LastUpdatedAt", "Name", "Notes", "Path", "SongCount", "SortOrder", "Tags", "Type" },
                values: new object[,]
                {
                    { 1, null, new Guid("6d455bb8-7292-cba0-2fd0-c18e40ad8fc5"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "Files in this directory are scanned and Album information is gathered via processing.", false, null, null, "Inbound", null, "/app/inbound/", null, 0, null, 1 },
                    { 2, null, new Guid("020e8374-59db-6d77-bdf8-b308e278b48c"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "The staging directory to place processed files into (Inbound -> Staging -> Library).", false, null, null, "Staging", null, "/app/staging/", null, 0, null, 2 },
                    { 3, null, new Guid("f63a6428-55d5-847b-3d09-3fa3b69b66ae"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "The library directory to place processed, reviewed and ready to use music files into.", false, null, null, "Storage", null, "/app/storage/", null, 0, null, 3 },
                    { 4, null, new Guid("277e8907-d170-780d-816d-92111e007606"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "Library where user images are stored.", false, null, null, "User Images", null, "/app/user-images/", null, 0, null, 4 },
                    { 5, null, new Guid("4be2eea8-571d-6936-ecf6-5f99dd829c04"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "Library where playlist data is stored.", false, null, null, "Playlist Data", null, "/app/playlists/", null, 0, null, 5 },
                    { 6, null, new Guid("62453b56-402b-8f9e-073b-e2d31e9f7cf9"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "Library where templates are stored, organized by language code.", false, null, null, "Templates", null, "/app/templates/", null, 0, null, 7 },
                    { 7, null, new Guid("01d52713-b3cf-48fa-f085-7704baee6dc5"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "Library where podcast media files are stored.", false, null, null, "Podcasts", null, "/app/podcasts/", null, 0, null, 8 },
                    { 8, null, new Guid("f718b349-eccc-ff93-f992-c190e1ed2616"), null, NodaTime.Instant.FromUnixTimeTicks(0L), "Library where custom theme packs are stored.", false, null, null, "Themes", null, "/app/themes/", null, 0, null, 9 }
                });

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "ApiKey", "Category", "Comment", "CreatedAt", "Description", "IsLocked", "Key", "LastUpdatedAt", "Notes", "SortOrder", "Tags", "Value" },
                values: new object[,]
                {
                    { 1, new Guid("5c08b275-6c25-972d-2aef-7e2f6ba227f2"), null, "Add a default filter to show only albums with this or less number of songs.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "filtering.lessThanSongCount", null, null, 0, null, "3" },
                    { 2, new Guid("c4996dec-2489-820e-eb83-6ddbd1144557"), null, "Add a default filter to show only albums with this or less duration.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "filtering.lessThanDuration", null, null, 0, null, "720000" },
                    { 4, new Guid("9a803c96-ca09-9208-d9e6-04083a5a11ea"), null, "Default page size when view including pagination.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "defaults.pagesize", null, null, 0, null, "100" },
                    { 6, new Guid("6b5c2528-7420-0e22-f136-6db9b89d9d7e"), null, "Amount of time to display a Toast then auto-close (in milliseconds.)", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "userinterface.toastAutoCloseTime", null, null, 0, null, "2000" },
                    { 9, new Guid("56a687bc-652d-9128-d7fd-52125c518a1c"), null, "List of ignored articles when scanning media (pipe delimited).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.ignoredArticles", null, null, 0, null, "THE|EL|LA|LOS|LAS|LE|LES|OS|AS|O|A" },
                    { 26, new Guid("cf595b62-3932-5723-49f3-1eba81bbf147"), null, "Fragments of artist names to replace (JSON Dictionary).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.artistNameReplacements", null, null, 0, null, "{'AC/DC': ['AC; DC', 'AC;DC', 'AC/ DC', 'AC DC'] , 'Love/Hate': ['Love; Hate', 'Love;Hate', 'Love/ Hate', 'Love Hate'] }" },
                    { 27, new Guid("fd8eb2e5-9d1d-95ad-93e3-4129f18ca952"), null, "If OrigAlbumYear [TOR, TORY, TDOR] value is invalid use current year.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.doUseCurrentYearAsDefaultOrigAlbumYearValue", null, null, 0, null, "false" },
                    { 28, new Guid("286bf3c1-9d25-a8ce-d78d-964db9d15b37"), null, "Delete original files when processing. When false a copy if made, else original is deleted after processed.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.doDeleteOriginal", null, null, 0, null, "false" },
                    { 29, new Guid("4f830df7-7942-6353-1d84-946f271c084e"), null, "Extension to add to file when converted, leave blank to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.convertedExtension", null, null, 0, null, "_converted" },
                    { 30, new Guid("d2e7b90f-8c28-863f-f96f-14627ac06394"), null, "Extension to add to file when processed, leave blank to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.processedExtension", null, null, 0, null, "_processed" },
                    { 32, new Guid("1e80ad9a-a13e-b515-9262-1c0dd6e51bb9"), null, "When processing over write any existing Melodee data files, otherwise skip and leave in place.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.doOverrideExistingMelodeeDataFiles", null, null, 0, null, "true" },
                    { 34, new Guid("7d283a60-e2c1-e3f3-6b1f-3c988a89cfc9"), null, "The maximum number of files to process, set to zero for unlimited.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.maximumProcessingCount", null, null, 0, null, "0" },
                    { 35, new Guid("2277af16-56ba-327d-44d4-3f1e1dba4366"), null, "Maximum allowed length of album directory name.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.maximumAlbumDirectoryNameLength", null, null, 0, null, "255" },
                    { 36, new Guid("9ebc2634-b7d3-12c4-3487-606d1ed8d376"), null, "Maximum allowed length of artist directory name.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.maximumArtistDirectoryNameLength", null, null, 0, null, "255" },
                    { 37, new Guid("a4f7e266-d355-e402-865f-da369963cc03"), null, "Fragments to remove from album titles (JSON array).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.albumTitleRemovals", null, null, 0, null, "['^', '~', '#']" },
                    { 38, new Guid("f29aff69-bc10-d860-692e-275a4ffa4138"), null, "Fragments to remove from song titles (JSON array).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.songTitleRemovals", null, null, 0, null, "[';', '(Remaster)', 'Remaster']" },
                    { 39, new Guid("4585dcb2-e48c-b99a-8995-91f56931e11e"), null, "Continue processing if an error is encountered.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.doContinueOnDirectoryProcessingErrors", null, null, 0, null, "true" },
                    { 41, new Guid("02088d3e-a9d2-44a4-0975-41c1f695ebdb"), null, "Is scripting enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scripting.enabled", null, null, 0, null, "false" },
                    { 42, new Guid("262c50a8-e2a9-53d6-2bce-82d075d843ec"), null, "Script to run before processing the inbound directory, leave blank to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scripting.preDiscoveryScript", null, null, 0, null, "" },
                    { 43, new Guid("e999453e-9193-fbfe-a533-ab541773943e"), null, "Script to run after processing the inbound directory, leave blank to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scripting.postDiscoveryScript", null, null, 0, null, "" },
                    { 45, new Guid("5f2c94f9-dfb3-2e40-06b1-9dd70a9f9f62"), null, "Don't create performer contributors for these performer names.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.ignoredPerformers", null, null, 0, null, "" },
                    { 46, new Guid("443fb612-30f1-1b13-4903-ad55009dceac"), null, "Don't create production contributors for these production names.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.ignoredProduction", null, null, 0, null, "['www.t.me;pmedia_music']" },
                    { 47, new Guid("7beaf728-5c50-dabd-5ec2-f5a5138c0822"), null, "Don't create publisher contributors for these artist names.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.ignoredPublishers", null, null, 0, null, "['P.M.E.D.I.A','PMEDIA','PMEDIA GROUP']" },
                    { 49, new Guid("44b73f87-3a4a-c6d2-e3cf-b37ea7937563"), null, "Private key used to encrypt/decrypt passwords for Subsonic authentication. Use https://generate-random.org/encryption-key-generator?count=1&bytes=32&cipher=aes-256-cbc&string=&password= to generate a new key.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "encryption.privateKey", null, null, 0, null, "H+Kiik6VMKfTD2MesF1GoMjczTrD5RhuKckJ5+/UQWOdWajGcsEC3yEnlJ5eoy8Y" },
                    { 50, new Guid("582676cf-cf72-3c09-1055-5a3b2de29a6d"), null, "Prefix to apply to indicate an album directory is a duplicate album for an artist. If left blank the default of '__duplicate_' will be used.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.duplicateAlbumPrefix", null, null, 0, null, "_duplicate_ " },
                    { 53, new Guid("b48052d3-aab1-dc24-9188-17617fc90575"), null, "Processing batching size. Allowed range is between [250] and [1000]. ", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "defaults.batchSize", null, null, 0, null, "250" },
                    { 54, new Guid("7464b039-de31-f876-5731-46ce62500117"), null, "When processing folders immediately delete any files with these extensions. (JSON array).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.fileExtensionsToDelete", null, null, 0, null, "['log', 'lnk', 'lrc', 'doc']" },
                    { 100, new Guid("a4c47b7c-30c3-0603-cf8e-79863111f251"), 1, "OpenSubsonic server supported Subsonic API version.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "openSubsonicServer.openSubsonic.serverSupportedVersion", null, null, 0, null, "1.16.1" },
                    { 101, new Guid("5a954c6a-9afc-43eb-8f93-74047d725365"), 1, "OpenSubsonic server name.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "openSubsonicServer.openSubsonicServer.type", null, null, 0, null, "Melodee" },
                    { 103, new Guid("95256bc3-92e8-a83e-e26d-b643d93d621a"), 1, "OpenSubsonic email to use in License responses.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "openSubsonicServer.openSubsonicServerLicenseEmail", null, null, 0, null, "noreply@localhost.lan" },
                    { 104, new Guid("8f6dca18-fe45-9659-260b-41dd9a66cbf3"), 1, "Limit the number of artists to include in an indexes request, set to zero for 32k per index (really not recommended with tens of thousands of artists and mobile clients timeout downloading indexes, a user can find an artist by search)", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "openSubsonicServer.openSubsonicServer.index.artistLimit", null, null, 0, null, "1000" },
                    { 200, new Guid("e0a0ca63-aeb9-650e-99c4-d95a791c4a2e"), 2, "Enable Melodee to convert non-mp3 media files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "conversion.enabled", null, null, 0, null, "true" },
                    { 201, new Guid("5025f51c-262d-e7c5-ad27-70bddf43b476"), 2, "Bitrate to convert non-mp3 media files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "conversion.bitrate", null, null, 0, null, "384" },
                    { 202, new Guid("92cbee43-6e9f-a236-a271-f9cc5bb5d262"), 2, "Vbr to convert non-mp3 media files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "conversion.vbrLevel", null, null, 0, null, "4" },
                    { 203, new Guid("f88fb399-23c1-ef86-3e56-93f63f8bb809"), 2, "Sampling rate to convert non-mp3 media files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "conversion.samplingRate", null, null, 0, null, "48000" },
                    { 300, new Guid("318f1b81-ec0f-a6c6-05e0-805f67b8caab"), 3, "Short Format to use when displaying full dates.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "formatting.dateTimeDisplayFormatShort", null, null, 0, null, "yyyyMMdd HH\\:mm" },
                    { 301, new Guid("3a06decd-3d51-f70b-c0ac-d640e8bd6f40"), 3, "Format to use when displaying activity related dates (e.g., processing messages)", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "formatting.dateTimeDisplayActivityFormat", null, null, 0, null, "hh\\:mm\\:ss\\.ffff" },
                    { 400, new Guid("5dbf9b93-4c1f-e317-37ed-97b3e641772c"), 4, "Include any embedded images from media files into the Melodee data file.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.doLoadEmbeddedImages", null, null, 0, null, "true" },
                    { 401, new Guid("8425f968-cb8a-a4bc-3174-a0b07641102e"), 4, "Small image size (square image, this is both width and height).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.smallSize", null, null, 0, null, "300" },
                    { 402, new Guid("6261b063-df52-a8b2-70f7-9619312364d2"), 4, "Medium image size (square image, this is both width and height).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.mediumSize", null, null, 0, null, "600" },
                    { 403, new Guid("f9d91f6b-172c-e91f-6c90-5257aa9e3e01"), 4, "Large image size (square image, this is both width and height), if larger than will be resized to this image, leave blank to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.largeSize", null, null, 0, null, "1600" },
                    { 404, new Guid("08a6111e-0d45-a09c-86e6-979cd47183be"), 4, "Maximum allowed number of images for an album, this includes all image types (Front, Rear, etc.), set to zero for unlimited.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.maximumNumberOfAlbumImages", null, null, 0, null, "25" },
                    { 405, new Guid("9320ee39-2c29-9fb3-1269-cf38f6cf32d3"), 4, "Maximum allowed number of images for an artist, set to zero for unlimited.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.maximumNumberOfArtistImages", null, null, 0, null, "25" },
                    { 406, new Guid("c0d392bc-7142-5407-4e11-a1f2c6d8eb55"), 4, "Images under this size are considered invalid, set to zero to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "imaging.minimumImageSize", null, null, 0, null, "300" },
                    { 500, new Guid("2ebd9e4b-a639-f66a-0574-69d765fa4a07"), 5, "Is Magic processing enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.enabled", null, null, 0, null, "true" },
                    { 501, new Guid("bd081306-fb20-dbb6-c886-da6a42b080af"), 5, "Renumber songs when doing magic processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doRenumberSongs", null, null, 0, null, "true" },
                    { 502, new Guid("13bde2a9-4729-31d3-5fbf-6e0ab74437a0"), 5, "Remove featured artists from song artist when doing magic.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doRemoveFeaturingArtistFromSongArtist", null, null, 0, null, "true" },
                    { 503, new Guid("c5221bbc-e459-1944-cf36-b874dd93247c"), 5, "Remove featured artists from song title when doing magic.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doRemoveFeaturingArtistFromSongTitle", null, null, 0, null, "true" },
                    { 504, new Guid("30e02344-8dec-c2ea-d203-22a803f93b48"), 5, "Replace song artist separators with standard ID3 separator ('/') when doing magic.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doReplaceSongsArtistSeparators", null, null, 0, null, "true" },
                    { 505, new Guid("163cf2d8-cb34-8509-0df3-8b681a0ae74b"), 5, "Set the song year to current year if invalid or missing when doing magic.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doSetYearToCurrentIfInvalid", null, null, 0, null, "false" },
                    { 506, new Guid("616cc758-2766-8f2f-71ae-2f99b98aba63"), 5, "Remove unwanted text from album title when doing magic.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doRemoveUnwantedTextFromAlbumTitle", null, null, 0, null, "true" },
                    { 507, new Guid("b9afe726-36f8-0b50-3a3d-a6eeb53b8e37"), 5, "Remove unwanted text from song titles when doing magic.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "magic.doRemoveUnwantedTextFromSongTitles", null, null, 0, null, "true" },
                    { 700, new Guid("8ccfdf94-55f8-bd0e-cb7c-8052d6d2ca89"), 7, "Process of CueSheet files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "plugin.cueSheet.enabled", null, null, 0, null, "true" },
                    { 701, new Guid("9edd4162-4e67-68e5-67e6-65a023fa3d41"), 7, "Process of M3U files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "plugin.m3u.enabled", null, null, 0, null, "true" },
                    { 702, new Guid("cd93553f-b424-dd6d-00da-1fd3de10267c"), 7, "Process of NFO files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "plugin.nfo.enabled", null, null, 0, null, "true" },
                    { 703, new Guid("cffd7f2e-95f3-28a2-e315-699f413b13ff"), 7, "Process of Simple File Verification (SFV) files during processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "plugin.simpleFileVerification.enabled", null, null, 0, null, "true" },
                    { 704, new Guid("50894ac8-809a-d90f-79ef-8169b16b0296"), 7, "If true then all comments will be removed from media files.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.doDeleteComments", null, null, 0, null, "true" },
                    { 902, new Guid("1ff4eed4-1cc5-d453-6ee5-947784437a60"), 9, "User agent to send with Search engine requests.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.userAgent", null, null, 0, null, "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0" },
                    { 903, new Guid("b233a0ac-9743-0b2b-1055-014c23f4147f"), 9, "Default page size when performing a search engine search.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.defaultPageSize", null, null, 0, null, "20" },
                    { 904, new Guid("cec2c46f-97dd-347a-53ea-c2b8a8ee6bf2"), 9, "Is MusicBrainz search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.musicbrainz.enabled", null, null, 0, null, "true" },
                    { 905, new Guid("798d3376-ff64-b590-f204-c46bef35339a"), 9, "Storage path to hold MusicBrainz downloaded files and database.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.musicbrainz.storagePath", null, null, 0, null, "/melodee_test/search-engine-storage/musicbrainz/" },
                    { 906, new Guid("2fbfdf98-8a93-ded3-1eed-4582f6ec2dc6"), 9, "Maximum number of batches import from MusicBrainz downloaded db dump (this setting is usually used during debugging), set to zero for unlimited.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.musicbrainz.importMaximumToProcess", null, null, 0, null, "0" },
                    { 907, new Guid("fb35de56-6659-1268-9f28-97e0be7d870c"), 9, "Number of records to import from MusicBrainz downloaded db dump before committing to local database.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.musicbrainz.importBatchSize", null, null, 0, null, "50000" },
                    { 908, new Guid("f5f8842b-1294-e4ab-95e1-2b60fa955b09"), 9, "Timestamp of when last MusicBrainz import was successful.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.musicbrainz.importLastImportTimestamp", null, null, 0, null, "" },
                    { 910, new Guid("1546df1d-4e92-2d14-9092-44d6daeb689e"), 9, "Is Spotify search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.spotify.enabled", null, null, 0, null, "false" },
                    { 911, new Guid("e11913ea-3d25-8024-c207-30837c59fee1"), 9, "ApiKey used used with Spotify. See https://developer.spotify.com/ for more details.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.spotify.apiKey", null, null, 0, null, "" },
                    { 912, new Guid("0c683b52-4b31-ea62-1421-f895264e8b29"), 9, "Shared secret used with Spotify. See https://developer.spotify.com/ for more details.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.spotify.sharedSecret", null, null, 0, null, "" },
                    { 913, new Guid("7c9b3a2a-91ad-0f5a-cca2-d2a9ab7f4379"), 9, "Token obtained from Spotify using the ApiKey and the Secret, this json contains expiry information.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.spotify.accessToken", null, null, 0, null, "" },
                    { 914, new Guid("4a089459-cc6b-d516-42c3-22ead8d2c7ac"), 9, "Is ITunes search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.itunes.enabled", null, null, 0, null, "true" },
                    { 915, new Guid("b63db7ba-321a-46a2-7e6a-8dc75313945f"), 9, "Is LastFM search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.lastFm.Enabled", null, null, 0, null, "true" },
                    { 916, new Guid("6c1087d4-e491-5a75-293d-c80ba2e59acb"), 9, "When performing a search engine search, the maximum allowed page size.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.maximumAllowedPageSize", null, null, 0, null, "1000" },
                    { 917, new Guid("a9dddd78-8c93-9f48-fe2c-7d6cd303c32f"), 9, "Refresh albums for artists from search engine database every x days, set to zero to not refresh.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.artistSearchDatabaseRefreshInDays", null, null, 0, null, "14" },
                    { 918, new Guid("dfc917eb-2be2-6a79-2f66-8fba157d5778"), 9, "Is Deezer search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.deezer.enabled", null, null, 0, null, "true" },
                    { 919, new Guid("de923cf1-09d4-8a9d-14a2-d4dda9eb8556"), 9, "Is Metal API search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.metalApi.enabled", null, null, 0, null, "false" },
                    { 1000, new Guid("26666288-7cc7-7af2-3404-8e026f1cb6a7"), 10, "Is scrobbling enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scrobbling.enabled", null, null, 0, null, "true" },
                    { 1001, new Guid("8d90f3ba-2a9d-9f11-e8e9-684e2d1c013d"), 10, "Is scrobbling to Last.fm enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scrobbling.lastFm.Enabled", null, null, 0, null, "false" },
                    { 1002, new Guid("d0716532-ca01-997a-75e1-45ca0b56e999"), 10, "ApiKey used used with last FM. See https://www.last.fm/api/authentication for more details.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scrobbling.lastFm.apiKey", null, null, 0, null, "" },
                    { 1003, new Guid("244b20d4-551f-dd7e-fd6c-81caefa013e7"), 10, "Shared secret used with last FM. See https://www.last.fm/api/authentication for more details.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "scrobbling.lastFm.sharedSecret", null, null, 0, null, "" },
                    { 1100, new Guid("84de96d4-42f4-1056-b509-d68d5ded3457"), 11, "Base URL for Melodee to use when building shareable links and image urls (e.g., 'https://server.domain.com:8080', 'http://server.domain.com').", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "system.baseUrl", null, null, 0, null, "** REQUIRED: THIS MUST BE EDITED **" },
                    { 1101, new Guid("42a71bd4-6390-1880-cd7c-e5e19a4092b1"), 11, "Is downloading enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "system.isDownloadingEnabled", null, null, 0, null, "true" },
                    { 1102, new Guid("79457a59-de2d-667d-2813-a79cd70427cc"), 11, "Maximum upload size in bytes for UI uploads.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "system.maxUploadSize", null, null, 0, null, "5242880" },
                    { 1103, new Guid("9468bf96-8fea-8dfb-c1a9-7b764c5178c6"), 11, "Name for this Melodee instance (used in emails and UI branding).", NodaTime.Instant.FromUnixTimeTicks(0L), "Customize the display name of your Melodee instance. Defaults to 'Melodee' if not set.", false, "system.siteName", null, null, 0, null, "Melodee" },
                    { 1200, new Guid("e0cefa09-426a-e3dd-a65a-498708d55e72"), 12, "Default format for transcoding.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "transcoding.default", null, null, 0, null, "raw" },
                    { 1201, new Guid("e2be036e-1bfa-44bb-c8ee-abb86ba87fbf"), 12, "Default command to transcode MP3 for streaming.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "transcoding.command.mp3", null, null, 0, null, "{ 'format': 'Mp3', 'bitrate: 192, 'command': 'ffmpeg -i %s -ss %t -map 0:a:0 -b:a %bk -v 0 -f mp3 -' }" },
                    { 1202, new Guid("17e73900-e7f3-a01b-2710-cbc01e43f7c5"), 12, "Default command to transcode using libopus for streaming.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "transcoding.command.opus", null, null, 0, null, "{ 'format': 'Opus', 'bitrate: 128, 'command': 'ffmpeg -i %s -ss %t -map 0:a:0 -b:a %bk -v 0 -c:a libopus -f opus -' }" },
                    { 1203, new Guid("f160bbd0-5316-bf0e-2d20-498426f48241"), 12, "Default command to transcode to aac for streaming.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "transcoding.command.aac", null, null, 0, null, "{ 'format': 'Aac', 'bitrate: 256, 'command': 'ffmpeg -i %s -ss %t -map 0:a:0 -b:a %bk -v 0 -c:a aac -f adts -' }" },
                    { 1300, new Guid("3ff6d2e5-dd61-c1de-c556-0a8f1169aa43"), 13, "The maximum value a song number can have for an album.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "validation.maximumSongNumber", null, null, 0, null, "9999" },
                    { 1301, new Guid("70f56e2f-1c9a-05dc-7da7-c6347e3f1947"), 13, "Minimum allowed year for an album.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "validation.minimumAlbumYear", null, null, 0, null, "1860" },
                    { 1302, new Guid("b257b1e3-3731-c980-137d-c4d0197753ce"), 13, "Maximum allowed year for an album.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "validation.maximumAlbumYear", null, null, 0, null, "2150" },
                    { 1303, new Guid("b9fe8d2e-01b4-ed09-7d3a-23cfdd6ba221"), 13, "Minimum number of songs an album has to have to be considered valid, set to 0 to disable check.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "validation.minimumSongCount", null, null, 0, null, "3" },
                    { 1304, new Guid("d9b766a1-cf5f-a185-028b-8303ecb12b4a"), 13, "Minimum duration of an album to be considered valid (in minutes), set to 0 to disable check.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "validation.minimumAlbumDuration", null, null, 0, null, "10" },
                    { 1400, new Guid("a6bc32c4-deb2-21c3-b5a9-0aa463d6247a"), 14, "Cron expression to run the artist housekeeping job, set empty to disable. Default of '0 0 0/1 1/1 * ? *' will run every hour. See https://www.freeformatter.com/cron-expression-generator-quartz.html", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.artistHousekeeping.cronExpression", null, null, 0, null, "0 0 0/1 1/1 * ? *" },
                    { 1401, new Guid("5ef2d5be-debf-facc-6a06-0055acb63c74"), 14, "Cron expression to run the library process job, set empty to disable. Default of '0 */10 * ? * *' Every 10 minutes. See https://www.freeformatter.com/cron-expression-generator-quartz.html", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.libraryProcess.cronExpression", null, null, 0, null, "0 */10 * ? * *" },
                    { 1402, new Guid("67dc3cad-e46b-ad78-c9bc-25a65e487114"), 14, "Cron expression to run the library scan job, set empty to disable. Default of '0 0 0 * * ?' will run every day at 00:00. See https://www.freeformatter.com/cron-expression-generator-quartz.html", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.libraryInsert.cronExpression", null, null, 0, null, "0 0 0 * * ?" },
                    { 1403, new Guid("fab2408d-06d8-5ba8-78ff-db4b8d0a5c58"), 14, "Cron expression to run the musicbrainz database house keeping job, set empty to disable. Default of '0 0 12 1 * ?' will run first day of the month. See https://www.freeformatter.com/cron-expression-generator-quartz.html", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.musicbrainzUpdateDatabase.cronExpression", null, null, 0, null, "0 0 12 1 * ?" },
                    { 1404, new Guid("219f3b33-dc1f-b3c2-143c-582a023e5b25"), 14, "Cron expression to run the artist search engine house keeping job, set empty to disable. Default of '0 0 0 * * ?' will run every day at 00:00. See https://www.freeformatter.com/cron-expression-generator-quartz.html", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.artistSearchEngineHousekeeping.cronExpression", null, null, 0, null, "0 0 0 * * ?" },
                    { 1405, new Guid("c3f25109-36ca-e223-69a9-71a3d4083f00"), 14, "Cron expression to run the chart update job which links chart items to albums, set empty to disable. Default of '0 0 2 * * ?' will run every day at 02:00. See https://www.freeformatter.com/cron-expression-generator-quartz.html", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.chartUpdate.cronExpression", null, null, 0, null, "0 0 2 * * ?" },
                    { 1406, new Guid("dcf2a737-2724-2310-abec-6d0204ff4bff"), 14, "Cron expression for staging auto-move job. Moves 'Ok' albums to storage. Default '0 */15 * * * ?' runs every 15 min. Also triggered after inbound processing.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.stagingAutoMove.cronExpression", null, null, 0, null, "0 */15 * * * ?" },
                    { 1500, new Guid("77c527bc-5317-46da-d778-e7114791749f"), null, "Enable or disable email sending functionality", NodaTime.Instant.FromUnixTimeTicks(0L), "When true, enables SMTP email sending for password resets and notifications", false, "email.enabled", null, null, 0, null, "false" },
                    { 1501, new Guid("1836553b-06a0-2fe4-35c0-fdf088520e61"), null, "Display name in From field of outgoing emails", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "email.fromName", null, null, 0, null, "Melodee" },
                    { 1502, new Guid("28ce7a91-9dd3-bcdb-7cf2-2249037ff4a5"), null, "Email address in From field (REQUIRED for email sending)", NodaTime.Instant.FromUnixTimeTicks(0L), "Example: noreply@yourdomain.com", false, "email.fromEmail", null, null, 0, null, "" },
                    { 1503, new Guid("100f5f84-1a12-8af4-1b43-349bfea18d90"), null, "SMTP server hostname (REQUIRED for email sending)", NodaTime.Instant.FromUnixTimeTicks(0L), "Example: smtp.gmail.com or smtp.sendgrid.net", false, "email.smtpHost", null, null, 0, null, "" },
                    { 1504, new Guid("0f9b5ef0-1b03-2319-7e19-5fc2e9e7287d"), null, "SMTP server port", NodaTime.Instant.FromUnixTimeTicks(0L), "Common values: 587 (StartTLS), 465 (SSL), 25 (unencrypted)", false, "email.smtpPort", null, null, 0, null, "587" },
                    { 1505, new Guid("41c53bd6-7fd6-bd69-673c-e352fa5f84a5"), null, "SMTP authentication username (optional)", NodaTime.Instant.FromUnixTimeTicks(0L), "Leave empty if SMTP server does not require authentication", false, "email.smtpUsername", null, null, 0, null, "" },
                    { 1506, new Guid("893a9053-2b8f-8a32-4e6c-c9b3541341db"), null, "SMTP authentication password (optional, use env var email_smtpPassword)", NodaTime.Instant.FromUnixTimeTicks(0L), "For security, set via environment variable: email_smtpPassword", false, "email.smtpPassword", null, null, 0, null, "" },
                    { 1507, new Guid("9a20a527-a2d9-628f-914a-c2fab2dc8496"), null, "Use SSL connection for SMTP", NodaTime.Instant.FromUnixTimeTicks(0L), "Set to true for port 465 (SSL), false for port 587 (StartTLS)", false, "email.smtpUseSsl", null, null, 0, null, "false" },
                    { 1508, new Guid("1f6249d4-fb89-6266-9672-41d7a6109260"), null, "Use StartTLS for SMTP", NodaTime.Instant.FromUnixTimeTicks(0L), "Recommended: true for port 587", false, "email.smtpUseStartTls", null, null, 0, null, "true" },
                    { 1509, new Guid("a268fe56-a265-c29d-fd82-e5efc61f0505"), null, "Password reset email subject line", NodaTime.Instant.FromUnixTimeTicks(0L), "Subject for password reset emails", false, "email.resetPassword.subject", null, null, 0, null, "Reset your Melodee password" },
                    { 1600, new Guid("f27eb478-3910-50ce-7a05-86aff6d0f1ca"), null, "Password reset token expiry time in minutes", NodaTime.Instant.FromUnixTimeTicks(0L), "How long password reset links remain valid (default: 60 minutes)", false, "security.passwordResetTokenExpiryMinutes", null, null, 0, null, "60" },
                    { 1700, new Guid("226cfbc6-3866-fa17-7729-23849a7b8077"), null, "Enable Jellyfin API compatibility", NodaTime.Instant.FromUnixTimeTicks(0L), "When enabled, Melodee exposes Jellyfin-compatible endpoints for third-party music players", false, "jellyfin.enabled", null, null, 0, null, "true" },
                    { 1701, new Guid("eefa4040-71d4-b7b0-4218-52b5aa1c7408"), null, "Internal route prefix for Jellyfin API", NodaTime.Instant.FromUnixTimeTicks(0L), "The internal route prefix used for Jellyfin API endpoints (default: /api/jf)", false, "jellyfin.routePrefix", null, null, 0, null, "/api/jf" },
                    { 1702, new Guid("57d8a083-6ad7-9d6f-a31f-8b4f94e7a2a0"), null, "Jellyfin token expiry time in hours", NodaTime.Instant.FromUnixTimeTicks(0L), "How long Jellyfin access tokens remain valid (default: 168 hours / 7 days)", false, "jellyfin.token.expiresAfterHours", null, null, 0, null, "168" },
                    { 1703, new Guid("1696717a-dbe7-3278-52c1-bc43a5c7ed86"), null, "Maximum active Jellyfin tokens per user", NodaTime.Instant.FromUnixTimeTicks(0L), "The maximum number of active Jellyfin tokens allowed per user (default: 10)", false, "jellyfin.token.maxActivePerUser", null, null, 0, null, "10" },
                    { 1704, new Guid("732d29c7-1df6-4084-b126-f485463a10a4"), null, "Allow legacy Emby/MediaBrowser headers", NodaTime.Instant.FromUnixTimeTicks(0L), "Allow X-Emby-* and X-MediaBrowser-* headers for authentication (default: true)", false, "jellyfin.token.allowLegacyHeaders", null, null, 0, null, "true" },
                    { 1705, new Guid("57ef8277-a41c-a3e3-d68b-3e6c16a98728"), null, "Secret pepper for Jellyfin token hashing", NodaTime.Instant.FromUnixTimeTicks(0L), "Server-side secret used in token hash computation. Change this value in production for added security.", false, "jellyfin.token.pepper", null, null, 0, null, "ChangeThisPepperInProduction" },
                    { 1706, new Guid("191427dc-3a4b-e304-fe21-9457435456d7"), null, "API requests allowed per period", NodaTime.Instant.FromUnixTimeTicks(0L), "Maximum number of Jellyfin API requests allowed per rate limit period (default: 200)", false, "jellyfin.rateLimit.apiRequestsPerPeriod", null, null, 0, null, "200" },
                    { 1707, new Guid("e10e7d3e-d4e8-a507-7a8e-ff526828ddd1"), null, "Rate limit period in seconds", NodaTime.Instant.FromUnixTimeTicks(0L), "Duration of the rate limit period in seconds (default: 60)", false, "jellyfin.rateLimit.apiPeriodSeconds", null, null, 0, null, "60" },
                    { 1708, new Guid("96e4d8c5-a98c-ecd1-755a-eaccd69eaa20"), null, "Concurrent streams per user", NodaTime.Instant.FromUnixTimeTicks(0L), "Maximum number of concurrent audio streams allowed per user (default: 2)", false, "jellyfin.rateLimit.streamConcurrentPerUser", null, null, 0, null, "2" },
                    { 1709, new Guid("c7b11e69-6582-e227-97ae-37435339e58e"), 9, "Is Discogs search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.discogs.enabled", null, null, 0, null, "false" },
                    { 1710, new Guid("33a0d80a-8a65-e692-30a9-e3d571759efe"), 9, "Discogs API user token for authentication.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.discogs.userToken", null, null, 0, null, "" },
                    { 1711, new Guid("21837867-a824-2a66-fa7c-3583974874e4"), 9, "Is WikiData search engine enabled.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "searchEngine.wikidata.enabled", null, null, 0, null, "false" },
                    { 1800, new Guid("8ee4c50d-9a7a-a4ef-66f1-74614a24313e"), 15, "Enable podcast support.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.enabled", null, null, 0, null, "true" },
                    { 1801, new Guid("c3d99d92-ab8d-bdca-ab08-3cc6ea2d2860"), 15, "Allow HTTP (non-secure) URLs for podcast feeds.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.http.allowHttp", null, null, 0, null, "false" },
                    { 1802, new Guid("93b35ab7-14d0-0814-0d66-fe040e3ae4b8"), 15, "Timeout in seconds for HTTP requests to podcast feeds.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.http.timeoutSeconds", null, null, 0, null, "30" },
                    { 1803, new Guid("6b35ba44-07ac-645d-b2a3-9cadaa60ff3d"), 15, "Maximum number of HTTP redirects to follow for podcast feeds. Podcast CDNs often use multiple analytics redirects, so 10 is recommended.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.http.maxRedirects", null, null, 0, null, "10" },
                    { 1804, new Guid("13168117-a286-23b5-5858-9f91485c6432"), 15, "Maximum size in bytes for podcast feed responses.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.http.maxFeedBytes", null, null, 0, null, "10485760" },
                    { 1805, new Guid("1fceaf81-79eb-433c-de79-eabe193c46f8"), 15, "Maximum number of episodes to store per podcast channel.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.refresh.maxItemsPerChannel", null, null, 0, null, "500" },
                    { 1806, new Guid("525bb5dc-989c-5154-0c7e-7f4b336032e3"), 15, "Maximum concurrent podcast episode downloads (global).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.download.maxConcurrent.global", null, null, 0, null, "2" },
                    { 1807, new Guid("380ed177-9320-92a0-5a93-48bdcc040d35"), 15, "Maximum concurrent podcast episode downloads per user.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.download.maxConcurrent.perUser", null, null, 0, null, "1" },
                    { 1808, new Guid("2d5158e7-495e-44a6-e06a-b5f1359f8ea2"), 15, "Maximum size in bytes for podcast episode downloads.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.download.maxEnclosureBytes", null, null, 0, null, "2147483648" },
                    { 1809, new Guid("908afec1-3a49-5e62-26f5-d6977ef6b00c"), 15, "Number of days to keep downloaded episodes. 0 to disable retention.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.retention.downloadedEpisodesInDays", null, null, 0, null, "0" },
                    { 1810, new Guid("6f86302a-1d6d-b574-c77a-b6cfbefb5e0a"), 15, "Threshold in minutes to consider a downloading episode as stuck.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.recovery.stuckDownloadThresholdMinutes", null, null, 0, null, "60" },
                    { 1811, new Guid("8d257a4b-b566-e0af-1044-9658d5ac27ea"), 15, "Threshold in hours to consider a temporary file orphaned.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.recovery.orphanedUsageThresholdHours", null, null, 0, null, "12" },
                    { 1812, new Guid("737e544b-7490-d53e-a092-3fd6e2b629b4"), 15, "Maximum total storage in bytes for all podcasts per user. 0 for unlimited.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.quota.maxBytesPerUser", null, null, 0, null, "5368709120" },
                    { 1813, new Guid("153a12d4-77b4-ccc3-1584-f3685d6c9e2e"), 15, "Keep only the last N downloaded episodes per channel. 0 to disable this policy.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.retention.keepLastNEpisodes", null, null, 0, null, "0" },
                    { 1814, new Guid("3da9402e-9566-c883-66e5-d232de677199"), 15, "Delete downloaded episodes after they have been played. false to disable.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "podcast.retention.keepUnplayedOnly", null, null, 0, null, "false" },
                    { 1850, new Guid("dc79ceff-cd68-f412-8f99-7529615cb3e8"), 14, "Cron expression to run the podcast refresh job, set empty to disable. Default of '0 */15 * ? * *' runs every 15 minutes.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.podcastRefresh.cronExpression", null, null, 0, null, "0 */15 * ? * *" },
                    { 1851, new Guid("d29b11cc-d892-271a-9e2a-5eeacb795e39"), 14, "Cron expression to run the podcast download job, set empty to disable. Default of '0 */5 * ? * *' runs every 5 minutes.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.podcastDownload.cronExpression", null, null, 0, null, "0 */5 * ? * *" },
                    { 1852, new Guid("3b2df55c-cd9c-a51b-2c4c-8f566bf7b6d8"), 14, "Cron expression to run the podcast cleanup job, set empty to disable. Default of '0 0 2 * * ?' runs daily at 2 AM.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.podcastCleanup.cronExpression", null, null, 0, null, "0 0 2 * * ?" },
                    { 1853, new Guid("17b25fcb-6a54-291d-5927-28ade4b15a93"), 14, "Cron expression to run the podcast recovery job, set empty to disable. Default of '0 */30 * ? * *' runs every 30 minutes.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.podcastRecovery.cronExpression", null, null, 0, null, "0 */30 * ? * *" },
                    { 1900, new Guid("541a397c-740c-8b9d-f1ed-5f990cab92a1"), 16, "Enable Jukebox support for server-side playback.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jukebox.enabled", null, null, 0, null, "false" },
                    { 1901, new Guid("4c886427-ffc2-d277-5950-6cf4b880b7be"), 16, "The type of backend to use for jukebox playback (e.g., 'mpv', 'mpd'). Leave empty for no backend.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jukebox.backendType", null, null, 0, null, "" },
                    { 1910, new Guid("e39d8312-cae1-ee40-266d-533077dbfdbb"), 16, "Path to the MPV executable. Leave empty to use system PATH.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpv.path", null, null, 0, null, "" },
                    { 1911, new Guid("945df58f-0546-2e6c-ccc8-210b41e719b7"), 16, "Audio device to use for MPV playback. Leave empty for default device.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpv.audioDevice", null, null, 0, null, "" },
                    { 1912, new Guid("7b99ed1d-9c95-3a2a-9aa7-aca68cda0223"), 16, "Extra command-line arguments to pass to MPV.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpv.extraArgs", null, null, 0, null, "" },
                    { 1913, new Guid("45dfa023-d926-4364-33d1-245a9623dece"), 16, "Path for the MPV IPC socket. Leave empty for auto temp directory.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpv.socketPath", null, null, 0, null, "" },
                    { 1914, new Guid("ac4199ff-57a6-9ded-7a8b-037b9df29a7f"), 16, "Initial volume level for MPV (0.0 to 1.0). Default is 0.8.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpv.initialVolume", null, null, 0, null, "0.8" },
                    { 1915, new Guid("7893e826-0cc8-a0a2-12dc-5c2556212c4a"), 16, "Enable verbose debug output for MPV.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpv.enableDebugOutput", null, null, 0, null, "false" },
                    { 1920, new Guid("bfcce639-8b21-dcc7-b54f-ce1d3ad074f0"), 16, "Unique name/identifier for this MPD instance (for multi-instance support).", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.instanceName", null, null, 0, null, "" },
                    { 1921, new Guid("275a59ef-fe5d-c2b8-28df-a7bc4a04abdb"), 16, "Hostname or IP address of the MPD server.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.host", null, null, 0, null, "localhost" },
                    { 1922, new Guid("515116f0-99ba-30cc-4b18-d722da60cd7f"), 16, "Port number for MPD connection.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.port", null, null, 0, null, "6600" },
                    { 1923, new Guid("dbc39d88-00c0-0710-201e-dd387d745589"), 16, "Password for MPD authentication. Leave empty if no password.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.password", null, null, 0, null, "" },
                    { 1924, new Guid("d1d4df5f-fb55-011e-ad6a-c29db5896073"), 16, "Timeout for MPD TCP connection and operations in milliseconds.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.timeoutMs", null, null, 0, null, "10000" },
                    { 1925, new Guid("416030fd-3e69-d30e-789f-9203464ebc86"), 16, "Initial volume level for MPD (0.0 to 1.0). Default is 0.8.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.initialVolume", null, null, 0, null, "0.8" },
                    { 1926, new Guid("5819d3ec-0b14-1731-2179-69ab1328140b"), 16, "Enable debug logging for MPD commands.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "mpd.enableDebugOutput", null, null, 0, null, "false" },
                    { 1927, new Guid("df8f5291-a7c1-797c-1dea-5d302116b2c9"), 11, "Enable per-user and per-device transcoding profiles.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "userDeviceProfile.enabled", null, null, 0, null, "true" }
                });

            migrationBuilder.InsertData(
                table: "UserGroups",
                columns: new[] { "Id", "ApiKey", "CreatedAt", "Description", "IsLocked", "LastUpdatedAt", "Name", "Notes", "SortOrder", "Tags" },
                values: new object[] { 1, new Guid("5dd33e32-e1b8-a880-64a9-fdf28e2da613"), NodaTime.Instant.FromUnixTimeTicks(0L), "Default group for all users", false, null, "All Users", null, 0, null });

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ApiKey",
                table: "Albums",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ArtistId_Name",
                table: "Albums",
                columns: new[] { "ArtistId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ArtistId_NameNormalized",
                table: "Albums",
                columns: new[] { "ArtistId", "NameNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Albums_ArtistId_SortName",
                table: "Albums",
                columns: new[] { "ArtistId", "SortName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Albums_MusicBrainzId",
                table: "Albums",
                column: "MusicBrainzId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Albums_SpotifyId",
                table: "Albums",
                column: "SpotifyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistRelation_ApiKey",
                table: "ArtistRelation",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistRelation_ArtistId_RelatedArtistId",
                table: "ArtistRelation",
                columns: new[] { "ArtistId", "RelatedArtistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistRelation_RelatedArtistId",
                table: "ArtistRelation",
                column: "RelatedArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_ApiKey",
                table: "Artists",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_LibraryId",
                table: "Artists",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_MusicBrainzId",
                table: "Artists",
                column: "MusicBrainzId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Name",
                table: "Artists",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_NameNormalized",
                table: "Artists",
                column: "NameNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_SortName",
                table: "Artists",
                column: "SortName");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_SpotifyId",
                table: "Artists",
                column: "SpotifyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_ApiKey",
                table: "Bookmarks",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_MusicBrainzId",
                table: "Bookmarks",
                column: "MusicBrainzId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_SongId",
                table: "Bookmarks",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_SpotifyId",
                table: "Bookmarks",
                column: "SpotifyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_SongId",
                table: "Bookmarks",
                columns: new[] { "UserId", "SongId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChartItems_ChartId_LinkedAlbumId",
                table: "ChartItems",
                columns: new[] { "ChartId", "LinkedAlbumId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChartItems_ChartId_Rank",
                table: "ChartItems",
                columns: new[] { "ChartId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChartItems_LinkedAlbumId",
                table: "ChartItems",
                column: "LinkedAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_ChartItems_LinkedArtistId",
                table: "ChartItems",
                column: "LinkedArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_Charts_ApiKey",
                table: "Charts",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Charts_Slug",
                table: "Charts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_AlbumId",
                table: "Contributors",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_ApiKey",
                table: "Contributors",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_ArtistId_MetaTagIdentifier_SongId",
                table: "Contributors",
                columns: new[] { "ArtistId", "MetaTagIdentifier", "SongId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_ContributorName_MetaTagIdentifier_SongId",
                table: "Contributors",
                columns: new[] { "ContributorName", "MetaTagIdentifier", "SongId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_SongId",
                table: "Contributors",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinAccessTokens_TokenHash",
                table: "JellyfinAccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinAccessTokens_TokenPrefixHash",
                table: "JellyfinAccessTokens",
                column: "TokenPrefixHash");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinAccessTokens_UserId",
                table: "JellyfinAccessTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_JellyfinAccessTokens_UserId_ExpiresAt_RevokedAt",
                table: "JellyfinAccessTokens",
                columns: new[] { "UserId", "ExpiresAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobHistories_JobName_StartedAt",
                table: "JobHistories",
                columns: new[] { "JobName", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobHistories_StartedAt",
                table: "JobHistories",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_ApiKey",
                table: "Libraries",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_Type",
                table: "Libraries",
                column: "Type",
                unique: true,
                filter: "\"Type\" != 3");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryAccessControls_ApiKey",
                table: "LibraryAccessControls",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryAccessControls_LibraryId",
                table: "LibraryAccessControls",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryAccessControls_LibraryId_UserGroupId",
                table: "LibraryAccessControls",
                columns: new[] { "LibraryId", "UserGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryAccessControls_UserGroupId",
                table: "LibraryAccessControls",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryScanHistories_CreatedAt",
                table: "LibraryScanHistories",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryScanHistories_ForAlbumId",
                table: "LibraryScanHistories",
                column: "ForAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryScanHistories_ForArtistId",
                table: "LibraryScanHistories",
                column: "ForArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryScanHistories_LibraryId",
                table: "LibraryScanHistories",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryScanHistories_LibraryId_CreatedAt",
                table: "LibraryScanHistories",
                columns: new[] { "LibraryId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyAuditEvents_ApiKey",
                table: "PartyAuditEvents",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyAuditEvents_PartySessionId",
                table: "PartyAuditEvents",
                column: "PartySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyAuditEvents_UserId",
                table: "PartyAuditEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyPlaybackStates_ApiKey",
                table: "PartyPlaybackStates",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyPlaybackStates_CurrentQueueItemApiKey",
                table: "PartyPlaybackStates",
                column: "CurrentQueueItemApiKey");

            migrationBuilder.CreateIndex(
                name: "IX_PartyPlaybackStates_IsPlaying",
                table: "PartyPlaybackStates",
                column: "IsPlaying");

            migrationBuilder.CreateIndex(
                name: "IX_PartyPlaybackStates_LastHeartbeatAt",
                table: "PartyPlaybackStates",
                column: "LastHeartbeatAt");

            migrationBuilder.CreateIndex(
                name: "IX_PartyPlaybackStates_PartySessionId",
                table: "PartyPlaybackStates",
                column: "PartySessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyPlaybackStates_UpdatedByUserId",
                table: "PartyPlaybackStates",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_ApiKey",
                table: "PartyQueueItems",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_EnqueuedAt",
                table: "PartyQueueItems",
                column: "EnqueuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_EnqueuedByUserId",
                table: "PartyQueueItems",
                column: "EnqueuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_PartySessionId",
                table: "PartyQueueItems",
                column: "PartySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_PartySessionId_SortOrder",
                table: "PartyQueueItems",
                columns: new[] { "PartySessionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_SongApiKey",
                table: "PartyQueueItems",
                column: "SongApiKey");

            migrationBuilder.CreateIndex(
                name: "IX_PartyQueueItems_SortOrder",
                table: "PartyQueueItems",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionEndpoints_ApiKey",
                table: "PartySessionEndpoints",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionEndpoints_IsShared",
                table: "PartySessionEndpoints",
                column: "IsShared");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionEndpoints_LastSeenAt",
                table: "PartySessionEndpoints",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionEndpoints_OwnerUserId",
                table: "PartySessionEndpoints",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionEndpoints_Room",
                table: "PartySessionEndpoints",
                column: "Room");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionEndpoints_Type",
                table: "PartySessionEndpoints",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionParticipants_IsBanned",
                table: "PartySessionParticipants",
                column: "IsBanned");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionParticipants_PartySessionId_UserId",
                table: "PartySessionParticipants",
                columns: new[] { "PartySessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionParticipants_Role",
                table: "PartySessionParticipants",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessionParticipants_UserId",
                table: "PartySessionParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessions_ActiveEndpointId",
                table: "PartySessions",
                column: "ActiveEndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessions_ApiKey",
                table: "PartySessions",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySessions_OwnerUserId",
                table: "PartySessions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PartySessions_Status",
                table: "PartySessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Players_ApiKey",
                table: "Players",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_UserId_Client_UserAgent",
                table: "Players",
                columns: new[] { "UserId", "Client", "UserAgent" });

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_ApiKey",
                table: "Playlists",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_SongId",
                table: "Playlists",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_UserId_Name",
                table: "Playlists",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSong_PlaylistId",
                table: "PlaylistSong",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSong_SongId_PlaylistId",
                table: "PlaylistSong",
                columns: new[] { "SongId", "PlaylistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFileItems_ApiKey",
                table: "PlaylistUploadedFileItems",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFileItems_PlaylistUploadedFileId_SortOrder",
                table: "PlaylistUploadedFileItems",
                columns: new[] { "PlaylistUploadedFileId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFileItems_SongId",
                table: "PlaylistUploadedFileItems",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFileItems_Status",
                table: "PlaylistUploadedFileItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFiles_ApiKey",
                table: "PlaylistUploadedFiles",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFiles_PlaylistId",
                table: "PlaylistUploadedFiles",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistUploadedFiles_UserId_OriginalFileName",
                table: "PlaylistUploadedFiles",
                columns: new[] { "UserId", "OriginalFileName" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayQues_ApiKey",
                table: "PlayQues",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayQues_SongId",
                table: "PlayQues",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayQues_UserId",
                table: "PlayQues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PodcastChannels_ApiKey",
                table: "PodcastChannels",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastChannels_IsDeleted",
                table: "PodcastChannels",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_PodcastChannels_NextSyncAt",
                table: "PodcastChannels",
                column: "NextSyncAt");

            migrationBuilder.CreateIndex(
                name: "IX_PodcastChannels_UserId_FeedUrl",
                table: "PodcastChannels",
                columns: new[] { "UserId", "FeedUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodeBookmarks_PodcastEpisodeId",
                table: "PodcastEpisodeBookmarks",
                column: "PodcastEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodeBookmarks_UserId_PodcastEpisodeId",
                table: "PodcastEpisodeBookmarks",
                columns: new[] { "UserId", "PodcastEpisodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_ApiKey",
                table: "PodcastEpisodes",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_PodcastChannelId_DownloadStatus",
                table: "PodcastEpisodes",
                columns: new[] { "PodcastChannelId", "DownloadStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_PodcastChannelId_EpisodeKey",
                table: "PodcastEpisodes",
                columns: new[] { "PodcastChannelId", "EpisodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PodcastEpisodes_PodcastChannelId_PublishDate",
                table: "PodcastEpisodes",
                columns: new[] { "PodcastChannelId", "PublishDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RadioStations_ApiKey",
                table: "RadioStations",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ApiKey",
                table: "RefreshTokens",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAt",
                table: "RefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_HashedToken",
                table: "RefreshTokens",
                column: "HashedToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenFamily",
                table: "RefreshTokens",
                column: "TokenFamily");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_ApiKey",
                table: "RequestComments",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_CreatedByUserId",
                table: "RequestComments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_ParentCommentId",
                table: "RequestComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_RequestId_CreatedAt_Id",
                table: "RequestComments",
                columns: new[] { "RequestId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_RequestId_ParentCommentId_CreatedAt_Id",
                table: "RequestComments",
                columns: new[] { "RequestId", "ParentCommentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestParticipants_UserId_RequestId",
                table: "RequestParticipants",
                columns: new[] { "UserId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ApiKey",
                table: "Requests",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CreatedByUserId_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "CreatedByUserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_LastActivityAt_Id",
                table: "Requests",
                columns: new[] { "LastActivityAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_LastActivityUserId",
                table: "Requests",
                column: "LastActivityUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "Status", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_Status_CreatedByUserId_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "Status", "CreatedByUserId", "CreatedAt", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_TargetAlbumApiKey_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "TargetAlbumApiKey", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_TargetArtistApiKey_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "TargetArtistApiKey", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_UpdatedByUserId",
                table: "Requests",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestUserStates_UserId_LastSeenAt",
                table: "RequestUserStates",
                columns: new[] { "UserId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_ApiKey",
                table: "Settings",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Category",
                table: "Settings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shares_ApiKey",
                table: "Shares",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shares_UserId",
                table: "Shares",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlaylists_ApiKey",
                table: "SmartPlaylists",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlaylists_IsPublic",
                table: "SmartPlaylists",
                column: "IsPublic");

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlaylists_UserId",
                table: "SmartPlaylists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartPlaylists_UserId_Name",
                table: "SmartPlaylists",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Songs_AlbumId_SongNumber",
                table: "Songs",
                columns: new[] { "AlbumId", "SongNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Songs_ApiKey",
                table: "Songs",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Songs_MusicBrainzId",
                table: "Songs",
                column: "MusicBrainzId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Songs_SpotifyId",
                table: "Songs",
                column: "SpotifyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Songs_Title",
                table: "Songs",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlbums_AlbumId",
                table: "UserAlbums",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAlbums_ApiKey",
                table: "UserAlbums",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAlbums_UserId_AlbumId",
                table: "UserAlbums",
                columns: new[] { "UserId", "AlbumId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserArtists_ApiKey",
                table: "UserArtists",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserArtists_ArtistId",
                table: "UserArtists",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_UserArtists_UserId_ArtistId",
                table: "UserArtists",
                columns: new[] { "UserId", "ArtistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDeviceProfiles_ApiKey",
                table: "UserDeviceProfiles",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDeviceProfiles_PlayerId",
                table: "UserDeviceProfiles",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDeviceProfiles_UserId_IsDefaultProfile",
                table: "UserDeviceProfiles",
                columns: new[] { "UserId", "IsDefaultProfile" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDeviceProfiles_UserId_PlayerId",
                table: "UserDeviceProfiles",
                columns: new[] { "UserId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserEqualizerPresets_ApiKey",
                table: "UserEqualizerPresets",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserEqualizerPresets_UserId_Name",
                table: "UserEqualizerPresets",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMembers_ApiKey",
                table: "UserGroupMembers",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMembers_UserGroupId",
                table: "UserGroupMembers",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMembers_UserId",
                table: "UserGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMembers_UserId_UserGroupId",
                table: "UserGroupMembers",
                columns: new[] { "UserId", "UserGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_ApiKey",
                table: "UserGroups",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_Name",
                table: "UserGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPins_ApiKey",
                table: "UserPins",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPins_UserId_PinId_PinType",
                table: "UserPins",
                columns: new[] { "UserId", "PinId", "PinType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackSettings_ApiKey",
                table: "UserPlaybackSettings",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaybackSettings_UserId",
                table: "UserPlaybackSettings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPodcastEpisodePlayHistories_PodcastEpisodeId_PlayedAt",
                table: "UserPodcastEpisodePlayHistories",
                columns: new[] { "PodcastEpisodeId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPodcastEpisodePlayHistories_UserId_PodcastEpisodeId_Pla~",
                table: "UserPodcastEpisodePlayHistories",
                columns: new[] { "UserId", "PodcastEpisodeId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ApiKey",
                table: "Users",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSocialLogins_ApiKey",
                table: "UserSocialLogins",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSocialLogins_Provider_Subject",
                table: "UserSocialLogins",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSocialLogins_UserId",
                table: "UserSocialLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongPlayHistories_IsNowPlaying",
                table: "UserSongPlayHistories",
                column: "IsNowPlaying");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongPlayHistories_PlayedAt",
                table: "UserSongPlayHistories",
                column: "PlayedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongPlayHistories_SongId",
                table: "UserSongPlayHistories",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongPlayHistories_SongId_PlayedAt",
                table: "UserSongPlayHistories",
                columns: new[] { "SongId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSongPlayHistories_UserId",
                table: "UserSongPlayHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongPlayHistories_UserId_PlayedAt",
                table: "UserSongPlayHistories",
                columns: new[] { "UserId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSongs_ApiKey",
                table: "UserSongs",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSongs_SongId",
                table: "UserSongs",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSongs_UserId_SongId",
                table: "UserSongs",
                columns: new[] { "UserId", "SongId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistRelation");

            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "ChartItems");

            migrationBuilder.DropTable(
                name: "Contributors");

            migrationBuilder.DropTable(
                name: "JellyfinAccessTokens");

            migrationBuilder.DropTable(
                name: "JobHistories");

            migrationBuilder.DropTable(
                name: "LibraryAccessControls");

            migrationBuilder.DropTable(
                name: "LibraryScanHistories");

            migrationBuilder.DropTable(
                name: "PartyAuditEvents");

            migrationBuilder.DropTable(
                name: "PartyPlaybackStates");

            migrationBuilder.DropTable(
                name: "PartySessionParticipants");

            migrationBuilder.DropTable(
                name: "PlaylistSong");

            migrationBuilder.DropTable(
                name: "PlaylistUploadedFileItems");

            migrationBuilder.DropTable(
                name: "PlayQues");

            migrationBuilder.DropTable(
                name: "PodcastEpisodeBookmarks");

            migrationBuilder.DropTable(
                name: "RadioStations");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RequestComments");

            migrationBuilder.DropTable(
                name: "RequestParticipants");

            migrationBuilder.DropTable(
                name: "RequestUserStates");

            migrationBuilder.DropTable(
                name: "SearchHistories");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "ShareActivities");

            migrationBuilder.DropTable(
                name: "Shares");

            migrationBuilder.DropTable(
                name: "SmartPlaylists");

            migrationBuilder.DropTable(
                name: "UserAlbums");

            migrationBuilder.DropTable(
                name: "UserArtists");

            migrationBuilder.DropTable(
                name: "UserDeviceProfiles");

            migrationBuilder.DropTable(
                name: "UserEqualizerPresets");

            migrationBuilder.DropTable(
                name: "UserGroupMembers");

            migrationBuilder.DropTable(
                name: "UserPins");

            migrationBuilder.DropTable(
                name: "UserPlaybackSettings");

            migrationBuilder.DropTable(
                name: "UserPodcastEpisodePlayHistories");

            migrationBuilder.DropTable(
                name: "UserSocialLogins");

            migrationBuilder.DropTable(
                name: "UserSongPlayHistories");

            migrationBuilder.DropTable(
                name: "UserSongs");

            migrationBuilder.DropTable(
                name: "Charts");

            migrationBuilder.DropTable(
                name: "PartyQueueItems");

            migrationBuilder.DropTable(
                name: "PlaylistUploadedFiles");

            migrationBuilder.DropTable(
                name: "Requests");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropTable(
                name: "PodcastEpisodes");

            migrationBuilder.DropTable(
                name: "PartySessions");

            migrationBuilder.DropTable(
                name: "Playlists");

            migrationBuilder.DropTable(
                name: "PodcastChannels");

            migrationBuilder.DropTable(
                name: "PartySessionEndpoints");

            migrationBuilder.DropTable(
                name: "Songs");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "Artists");

            migrationBuilder.DropTable(
                name: "Libraries");
        }
    }
}
