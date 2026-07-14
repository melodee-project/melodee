using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddDeleteSourceResidueAfterIngestSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "ApiKey", "Category", "Comment", "CreatedAt", "Description", "IsLocked", "Key", "LastUpdatedAt", "Notes", "SortOrder", "Tags", "Value" },
                values: new object[] { 55, new Guid("3225bc34-a416-3dc3-dd9c-5a4a5e592189"), null, "Delete leftover residue (logs, sidecars, images, failed transcodes) from media-free release directories after ingest, even when keeping the original media (copy mode). Defaults on.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "processing.deleteSourceResidueAfterIngest", null, null, 0, null, "true" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 55);
        }
    }
}
