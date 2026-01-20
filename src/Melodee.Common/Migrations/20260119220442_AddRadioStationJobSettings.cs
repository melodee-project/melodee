using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Melodee.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddRadioStationJobSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Settings",
                columns: new[] { "Id", "ApiKey", "Category", "Comment", "CreatedAt", "Description", "IsLocked", "Key", "LastUpdatedAt", "Notes", "SortOrder", "Tags", "Value" },
                values: new object[,]
                {
                    { 1928, new Guid("1e9aff97-edef-3730-9660-b405005417c2"), 14, "Cron expression to run the radio station health probe job, set empty to disable. Default of '0 */15 * ? * *' runs every 15 minutes.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.radioStationHealthProbe.cronExpression", null, null, 0, null, "0 */15 * ? * *" },
                    { 1929, new Guid("4d84ca71-d6f3-a448-4aae-fff832acf0d6"), 14, "Cron expression to run the radio station now-playing capture job, set empty to disable. Default of '0 */5 * ? * *' runs every 5 minutes.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.radioStationNowPlayingCapture.cronExpression", null, null, 0, null, "0 */5 * ? * *" },
                    { 1930, new Guid("d8b0e586-7ec5-8ae1-0e0f-5bdc9cc17fdc"), 14, "Cron expression to run the radio station now-playing history cleanup job, set empty to disable. Default of '0 0 3 ? * *' runs daily at 3 AM.", NodaTime.Instant.FromUnixTimeTicks(0L), null, false, "jobs.radioStationNowPlayingHistoryCleanup.cronExpression", null, null, 0, null, "0 0 3 ? * *" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1928);

            migrationBuilder.DeleteData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1929);

            migrationBuilder.DeleteData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1930);
        }
    }
}
