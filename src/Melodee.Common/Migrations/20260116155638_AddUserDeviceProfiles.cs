using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDeviceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "ApiKey", "Category", "Comment", "CreatedAt", "Description", "IsLocked", "Key", "LastUpdatedAt", "Notes", "SortOrder", "Tags", "Value" },
                values: new object[] { 1929, new Guid("df8f5291-a7c1-797c-1dea-5d302116b2c9"), 11, "Enable per-user and per-device transcoding profiles.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "userDeviceProfile.enabled", null, null, 0, null, "true" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDeviceProfiles");

            migrationBuilder.DeleteData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1929);
        }
    }
}
