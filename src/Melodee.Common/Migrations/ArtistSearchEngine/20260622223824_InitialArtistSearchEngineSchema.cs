using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Melodee.Common.Migrations.ArtistSearchEngine
{
    /// <inheritdoc />
    public partial class InitialArtistSearchEngineSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Artists" (
                    "Id" INTEGER NOT NULL,
                    "Name" TEXT NOT NULL,
                    "NameNormalized" TEXT NOT NULL,
                    "AlternateNames" TEXT NULL,
                    "SortName" TEXT NOT NULL,
                    "ItunesId" TEXT NULL,
                    "AmgId" TEXT NULL,
                    "DiscogsId" TEXT NULL,
                    "WikiDataId" TEXT NULL,
                    "MusicBrainzId" UUID NULL,
                    "LastFmId" TEXT NULL,
                    "SpotifyId" TEXT NULL,
                    "IsLocked" INTEGER NULL,
                    "LastRefreshed" INTEGER NULL,
                    CONSTRAINT "PK_Artists" PRIMARY KEY ("Id")
                )
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Albums" (
                    "Id" INTEGER NOT NULL,
                    "ArtistId" INTEGER NOT NULL,
                    "SortName" TEXT NOT NULL,
                    "AlbumType" INTEGER NOT NULL,
                    "MusicBrainzId" UUID NULL,
                    "MusicBrainzReleaseGroupId" UUID NULL,
                    "SpotifyId" TEXT NULL,
                    "CoverUrl" TEXT NULL,
                    "Name" TEXT NOT NULL,
                    "NameNormalized" TEXT NOT NULL,
                    "Year" INTEGER NOT NULL,
                    CONSTRAINT "PK_Albums" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_Albums_Artists_ArtistId" FOREIGN KEY ("ArtistId") REFERENCES "Artists" ("Id") ON DELETE CASCADE
                )
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ArtistAliases" (
                    "Id" INTEGER NOT NULL,
                    "ArtistId" INTEGER NOT NULL,
                    "NameNormalized" TEXT NOT NULL,
                    CONSTRAINT "PK_ArtistAliases" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_ArtistAliases_Artists_ArtistId" FOREIGN KEY ("ArtistId") REFERENCES "Artists" ("Id") ON DELETE CASCADE
                )
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Albums_ArtistId_NameNormalized_Year" ON "Albums" ("ArtistId", "NameNormalized", "Year")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArtistAliases_ArtistId_NameNormalized" ON "ArtistAliases" ("ArtistId", "NameNormalized")
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ArtistAliases_NameNormalized" ON "ArtistAliases" ("NameNormalized")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_AmgId" ON "Artists" ("AmgId")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_DiscogsId" ON "Artists" ("DiscogsId")
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Artists_IsLocked_LastRefreshed" ON "Artists" ("IsLocked", "LastRefreshed")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_ItunesId" ON "Artists" ("ItunesId")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_LastFmId" ON "Artists" ("LastFmId")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_MusicBrainzId" ON "Artists" ("MusicBrainzId")
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Artists_Name" ON "Artists" ("Name")
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Artists_NameNormalized" ON "Artists" ("NameNormalized")
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Artists_SortName" ON "Artists" ("SortName")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_SpotifyId" ON "Artists" ("SpotifyId")
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Artists_WikiDataId" ON "Artists" ("WikiDataId")
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Albums\"");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"ArtistAliases\"");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Artists\"");
        }
    }
}