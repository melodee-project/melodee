using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Melodee.Common.Migrations.ArtistSearchEngine
{
    /// <summary>
    /// DecentDB reports Guid/byte[] columns as UUID in the EF model but stores them as BLOB.
    /// ALTER COLUMN TYPE to UUID is unsupported by DecentDB (only INT64, FLOAT64, TEXT, BOOL).
    /// This migration is a no-op — the columns are already BLOB in the database, which works
    /// correctly for storing Guid values. The migration exists to satisfy EF Core's migration
    /// chain so MigrateAsync does not detect pending changes.
    /// </summary>
    /// <inheritdoc />
    public partial class SyncMusicBrainzUuidColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}