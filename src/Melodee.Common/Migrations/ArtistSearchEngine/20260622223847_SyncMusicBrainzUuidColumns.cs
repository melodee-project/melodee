using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Melodee.Common.Migrations.ArtistSearchEngine
{
    /// <inheritdoc />
    public partial class SyncMusicBrainzUuidColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "MusicBrainzId",
                table: "Artists",
                type: "UUID",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "MusicBrainzReleaseGroupId",
                table: "Albums",
                type: "UUID",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "MusicBrainzId",
                table: "Albums",
                type: "UUID",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "BLOB",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "MusicBrainzId",
                table: "Artists",
                type: "BLOB",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "UUID",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "MusicBrainzReleaseGroupId",
                table: "Albums",
                type: "BLOB",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "UUID",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "MusicBrainzId",
                table: "Albums",
                type: "BLOB",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "UUID",
                oldNullable: true);
        }
    }
}
