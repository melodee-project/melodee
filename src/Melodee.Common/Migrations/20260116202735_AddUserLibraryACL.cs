using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLibraryACL : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PartySessions_PartySessionEndpoints_ActiveEndpointId1",
                table: "PartySessions");

            migrationBuilder.DropIndex(
                name: "IX_PartySessions_ActiveEndpointId1",
                table: "PartySessions");

            migrationBuilder.DropColumn(
                name: "ActiveEndpointId1",
                table: "PartySessions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PartySessionEndpoints_ApiKey",
                table: "PartySessionEndpoints",
                column: "ApiKey");

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

            migrationBuilder.InsertData(
                table: "UserGroups",
                columns: new[] { "Id", "ApiKey", "CreatedAt", "Description", "IsLocked", "LastUpdatedAt", "Name", "Notes", "SortOrder", "Tags" },
                values: new object[] { 1, new Guid("5dd33e32-e1b8-a880-64a9-fdf28e2da613"), NodaTime.Instant.FromUnixTimeTicks(0L), "Default group for all users", false, null, "All Users", null, 0, null });

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

            migrationBuilder.AddForeignKey(
                name: "FK_PartySessions_PartySessionEndpoints_ActiveEndpointId",
                table: "PartySessions",
                column: "ActiveEndpointId",
                principalTable: "PartySessionEndpoints",
                principalColumn: "ApiKey",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PartySessions_PartySessionEndpoints_ActiveEndpointId",
                table: "PartySessions");

            migrationBuilder.DropTable(
                name: "LibraryAccessControls");

            migrationBuilder.DropTable(
                name: "UserGroupMembers");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PartySessionEndpoints_ApiKey",
                table: "PartySessionEndpoints");

            migrationBuilder.AddColumn<int>(
                name: "ActiveEndpointId1",
                table: "PartySessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartySessions_ActiveEndpointId1",
                table: "PartySessions",
                column: "ActiveEndpointId1");

            migrationBuilder.AddForeignKey(
                name: "FK_PartySessions_PartySessionEndpoints_ActiveEndpointId1",
                table: "PartySessions",
                column: "ActiveEndpointId1",
                principalTable: "PartySessionEndpoints",
                principalColumn: "Id");
        }
    }
}
