using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

internal static class MusicBrainzSchemaInitializer
{
    public static async Task EnsureArtistAliasTableAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken = default)
    {
        await ExecuteIfMissingAsync(
            context,
            """
            CREATE TABLE "ArtistAlias" (
                "MusicBrainzArtistId" BIGINT NOT NULL,
                "NameNormalized" TEXT NOT NULL,
                PRIMARY KEY ("MusicBrainzArtistId", "NameNormalized")
            )
            """,
            cancellationToken);

        await ExecuteIfMissingAsync(
            context,
            """
            CREATE INDEX "IX_ArtistAlias_NameNormalized"
            ON "ArtistAlias" ("NameNormalized")
            """,
            cancellationToken);

        await ExecuteIfMissingAsync(
            context,
            """
            CREATE INDEX "IX_ArtistAlias_MusicBrainzArtistId"
            ON "ArtistAlias" ("MusicBrainzArtistId")
            """,
            cancellationToken);
    }

    private static async Task ExecuteIfMissingAsync(
        MusicBrainzDbContext context,
        string sql,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch (Exception ex) when (AlreadyExists(ex))
        {
        }
    }

    private static bool AlreadyExists(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
               || message.Contains("object already exists", StringComparison.OrdinalIgnoreCase);
    }
}
