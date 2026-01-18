using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistUploadedFileTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistUploadedFileItems");

            migrationBuilder.DropTable(
                name: "PlaylistUploadedFiles");
        }
    }
}
