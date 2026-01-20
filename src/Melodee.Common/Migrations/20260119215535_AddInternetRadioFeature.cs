using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddInternetRadioFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "RadioStations",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LanguageCode",
                table: "RadioStations",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastBitrateKbps",
                table: "RadioStations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastContentType",
                table: "RadioStations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "LastHealthCheckAt",
                table: "RadioStations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthError",
                table: "RadioStations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "LastHealthOkAt",
                table: "RadioStations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHealthStatus",
                table: "RadioStations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastResolvedStreamUrl",
                table: "RadioStations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoCacheKey",
                table: "RadioStations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "RadioStations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "NowPlayingCapturedAt",
                table: "RadioStations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NowPlayingRaw",
                table: "RadioStations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RadioStationNowPlayingHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RadioStationId = table.Column<int>(type: "integer", nullable: false),
                    CapturedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    NowPlayingRaw = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadioStationNowPlayingHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadioStationNowPlayingHistories_RadioStations_RadioStationId",
                        column: x => x.RadioStationId,
                        principalTable: "RadioStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RadioStationUserPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RadioStationId = table.Column<int>(type: "integer", nullable: false),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadioStationUserPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadioStationUserPreferences_RadioStations_RadioStationId",
                        column: x => x.RadioStationId,
                        principalTable: "RadioStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RadioStationUserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RadioStationNowPlayingHistories_CapturedAt",
                table: "RadioStationNowPlayingHistories",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RadioStationNowPlayingHistories_RadioStationId",
                table: "RadioStationNowPlayingHistories",
                column: "RadioStationId");

            migrationBuilder.CreateIndex(
                name: "IX_RadioStationNowPlayingHistories_RadioStationId_CapturedAt",
                table: "RadioStationNowPlayingHistories",
                columns: new[] { "RadioStationId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RadioStationUserPreferences_RadioStationId",
                table: "RadioStationUserPreferences",
                column: "RadioStationId");

            migrationBuilder.CreateIndex(
                name: "IX_RadioStationUserPreferences_UserId",
                table: "RadioStationUserPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RadioStationUserPreferences_UserId_RadioStationId",
                table: "RadioStationUserPreferences",
                columns: new[] { "UserId", "RadioStationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RadioStationNowPlayingHistories");

            migrationBuilder.DropTable(
                name: "RadioStationUserPreferences");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LanguageCode",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastBitrateKbps",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastContentType",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastHealthError",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastHealthOkAt",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastHealthStatus",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LastResolvedStreamUrl",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LogoCacheKey",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "NowPlayingCapturedAt",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "NowPlayingRaw",
                table: "RadioStations");
        }
    }
}
