using System.Data.Common;
using System.Text;
using Melodee.Common.Extensions;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Staging;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using SerilogTimings;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
/// Streaming import service for MusicBrainz data using DecentDB.
/// Adapted from StreamingMusicBrainzImporter without Lucene index creation or database-specific PRAGMAs.
/// </summary>
public sealed class DecentDBStreamingMusicBrainzImporter(ILogger logger)
{
    private const int StagingRowsPerInsertStatement = 1000;
    private const int StagingRowsPerTransaction = 20000;
    private const int MaxIndexSize = 255;
    private const int TotalImportSteps = 18;

    public async Task ImportAsync(
        MusicBrainzDbContext context,
        string storagePath,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        await ImportAsync(
                _ => Task.FromResult(context),
                storagePath,
                progressCallback,
                ownsCreatedContexts: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ImportAsync(
        Func<CancellationToken, Task<MusicBrainzDbContext>> contextFactory,
        string storagePath,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        await ImportAsync(
                contextFactory,
                storagePath,
                progressCallback,
                ownsCreatedContexts: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ImportAsync(
        Func<CancellationToken, Task<MusicBrainzDbContext>> contextFactory,
        string storagePath,
        ImportProgressCallback? progressCallback,
        bool ownsCreatedContexts,
        CancellationToken cancellationToken)
    {
        var mbDumpPath = Path.Combine(storagePath, "staging/mbdump");
        var context = await contextFactory(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ownsCreatedContexts)
            {
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            }

            await ImportArtistStagingDataAsync(context, mbDumpPath, progressCallback, cancellationToken)
                .ConfigureAwait(false);
            ResetContextState(context, cancellationToken);

            await MaterializeArtistsAsync(context, progressCallback, cancellationToken).ConfigureAwait(false);
            ResetContextState(context, cancellationToken);

            await MaterializeArtistRelationsAsync(context, progressCallback, cancellationToken).ConfigureAwait(false);
            ResetContextState(context, cancellationToken);

            await DropArtistStagingTablesAsync(context, progressCallback, cancellationToken).ConfigureAwait(false);
            ResetContextState(context, cancellationToken);

            var releaseCount = await ImportAlbumStagingDataAsync(context, mbDumpPath, progressCallback, cancellationToken)
                .ConfigureAwait(false);
            ResetContextState(context, cancellationToken);

            await MaterializeAlbumsAsync(context, releaseCount, progressCallback, cancellationToken).ConfigureAwait(false);
            ResetContextState(context, cancellationToken);

            await DropAlbumStagingTablesAsync(context, progressCallback, cancellationToken).ConfigureAwait(false);
            ResetContextState(context, cancellationToken);
        }
        finally
        {
            if (ownsCreatedContexts)
            {
                await context.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    #region Phase 1: Artist Staging Data

    private async Task ImportArtistStagingDataAsync(
        MusicBrainzDbContext context,
        string mbDumpPath,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Artist staging data"))
        {
            await MusicBrainzSchemaInitializer.EnsureArtistAliasTableAsync(context, cancellationToken);
            await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistRelation""", cancellationToken);
            await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistAlias""", cancellationToken);
            await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Artist""", cancellationToken);
            await DropMaterializedArtistLookupIndexesAsync(context, cancellationToken).ConfigureAwait(false);

            progressCallback?.Invoke("Loading Artists", 0, 4, WithImportStep(1, "Streaming artist file to materialized table..."));
            var artistCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist"),
                nameof(Artist),
                ["MusicBrainzArtistId", "MusicBrainzIdRaw", "Name", "NameNormalized", "SortName"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);

                    var name = ToString(p2);
                    var sortName = ToString(p3);

                    return
                    [
                        ToLong(p0),
                        (Guid.TryParse(p1, out var g) ? g : Guid.Empty).ToString(),
                        name.CleanString().TruncateLongString(MaxIndexSize) ?? string.Empty,
                        name.CleanString().TruncateLongString(MaxIndexSize)?.ToNormalizedString() ?? name,
                        sortName.CleanString(true).TruncateLongString(MaxIndexSize) ?? name
                    ];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 1, 4, WithImportStep(1, $"Streamed {artistCount:N0} artists to materialized table"));

            progressCallback?.Invoke("Loading Artists", 1, 4, WithImportStep(2, "Streaming artist aliases to lookup table..."));
            var aliasCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist_alias"),
                "ArtistAlias",
                ["MusicBrainzArtistId", "NameNormalized"],
                span =>
                {
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var name = ToString(p2);

                    return
                    [
                        ToLong(p1),
                        name.CleanString().TruncateLongString(MaxIndexSize)?.ToNormalizedString() ?? name
                    ];
                },
                cancellationToken,
                values => values[1] is null || values[1] is string text && string.IsNullOrEmpty(text),
                onConflictDoNothing: true);
            progressCallback?.Invoke("Loading Artists", 2, 4, WithImportStep(2, $"Streamed {aliasCount:N0} artist aliases to lookup table"));

            progressCallback?.Invoke("Loading Artists", 2, 4, WithImportStep(3, "Streaming links to staging..."));
            var linkCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "link"),
                nameof(LinkStaging),
                ["LinkId", "BeginDate", "EndDate"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var pBeginY = GetColumn(span, 2);
                    var pBeginM = GetColumn(span, 3);
                    var pBeginD = GetColumn(span, 4);
                    var pEndY = GetColumn(span, 5);
                    var pEndM = GetColumn(span, 6);
                    var pEndD = GetColumn(span, 7);

                    return
                    [
                        ToLong(p0),
                        ToDateValue(pBeginY, pBeginM, pBeginD),
                        ToDateValue(pEndY, pEndM, pEndD)
                    ];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 3, 4, WithImportStep(3, $"Streamed {linkCount:N0} links to staging"));

            progressCallback?.Invoke("Loading Artists", 3, 4, WithImportStep(4, "Streaming artist links to staging..."));
            var artistLinkCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "l_artist_artist"),
                nameof(LinkArtistToArtistStaging),
                ["LinkId", "Artist0", "Artist1", "LinkOrder"],
                span =>
                {
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p6 = GetColumn(span, 6);

                    return
                    [
                        ToLong(p1),
                        ToLong(p2),
                        ToLong(p3),
                        ToInt(p6)
                    ];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 4, 4, WithImportStep(4, $"Streamed {artistLinkCount:N0} artist links to staging"));

            progressCallback?.Invoke("Loading Artists", 4, 4, WithImportStep(5, "Creating staging indices..."));
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LinkArtistToArtistStaging_Artist0" ON "LinkArtistToArtistStaging" ("Artist0")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LinkArtistToArtistStaging_Artist1" ON "LinkArtistToArtistStaging" ("Artist1")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LinkArtistToArtistStaging_LinkId" ON "LinkArtistToArtistStaging" ("LinkId")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_LinkStaging_LinkId" ON "LinkStaging" ("LinkId")""",
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 4, 4, WithImportStep(5, "Rebuilding artist lookup indices..."));
            await RebuildMaterializedArtistLookupIndexesAsync(context, cancellationToken).ConfigureAwait(false);
            progressCallback?.Invoke("Loading Artists", 4, 4, WithImportStep(5, "Staging indices created"));
            progressCallback?.Invoke("Loading Artists", 4, 4, WithImportStep(5, "Artist staging data loaded"));
        }
    }

    #endregion

    private static async Task DropMaterializedArtistLookupIndexesAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        foreach (var dropSql in new[]
                 {
                     """DROP INDEX IF EXISTS "IX_Artist_MusicBrainzIdRaw" """,
                     """DROP INDEX IF EXISTS "IX_Artist_NameNormalized" """,
                     """DROP INDEX IF EXISTS "IX_Artist_MusicBrainzArtistId" """,
                     """DROP INDEX IF EXISTS "IX_ArtistAlias_NameNormalized" """,
                     """DROP INDEX IF EXISTS "IX_ArtistAlias_MusicBrainzArtistId" """
                 })
        {
            await context.Database.ExecuteSqlRawAsync(dropSql, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RebuildMaterializedArtistLookupIndexesAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Artist_MusicBrainzIdRaw" ON "Artist" ("MusicBrainzIdRaw")""",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Artist_NameNormalized" ON "Artist" ("NameNormalized")""",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Artist_MusicBrainzArtistId" ON "Artist" ("MusicBrainzArtistId")""",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ArtistAlias_NameNormalized" ON "ArtistAlias" ("NameNormalized")""",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ArtistAlias_MusicBrainzArtistId" ON "ArtistAlias" ("MusicBrainzArtistId")""",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task DropAlbumStagingLookupIndexesAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        foreach (var dropSql in new[]
                 {
                     """DROP INDEX IF EXISTS "IX_ArtistCreditStaging_ArtistCreditId" """,
                     """DROP INDEX IF EXISTS "IX_ArtistCreditNameStaging_ArtistCreditId" """,
                     """DROP INDEX IF EXISTS "IX_ArtistCreditNameStaging_ArtistId" """,
                     """DROP INDEX IF EXISTS "IX_ReleaseCountryStaging_ReleaseId" """,
                     """DROP INDEX IF EXISTS "IX_ReleaseGroupStaging_ReleaseGroupId" """,
                     """DROP INDEX IF EXISTS "IX_ReleaseGroupMetaStaging_ReleaseGroupId" """,
                     """DROP INDEX IF EXISTS "IX_ReleaseStaging_ReleaseId" """,
                     """DROP INDEX IF EXISTS "IX_ReleaseStaging_ReleaseGroupId" """,
                     """DROP INDEX IF EXISTS "IX_ReleaseStaging_ArtistCreditId" """
                 })
        {
            await context.Database.ExecuteSqlRawAsync(dropSql, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task DropMaterializedAlbumLookupIndexesAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        foreach (var dropSql in new[]
                 {
                     """DROP INDEX IF EXISTS "IX_Album_MusicBrainzIdRaw" """,
                     """DROP INDEX IF EXISTS "IX_Album_MusicBrainzArtistId" """,
                     """DROP INDEX IF EXISTS "IX_Album_NameNormalized" """
                 })
        {
            await context.Database.ExecuteSqlRawAsync(dropSql, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RebuildMaterializedAlbumLookupIndexesAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Album_MusicBrainzIdRaw" ON "Album" ("MusicBrainzIdRaw")""",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Album_MusicBrainzArtistId" ON "Album" ("MusicBrainzArtistId")""",
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Album_NameNormalized" ON "Album" ("NameNormalized")""",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureAlbumHelperTablesAsync(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ReleaseCountryResolvedStaging" (
                    "ReleaseId" BIGINT NOT NULL PRIMARY KEY,
                    "DateYear" INTEGER NOT NULL,
                    "DateMonth" INTEGER NOT NULL,
                    "DateDay" INTEGER NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "ArtistCreditPrimaryArtistStaging" (
                    "ArtistCreditId" BIGINT NOT NULL PRIMARY KEY,
                    "ArtistId" BIGINT NOT NULL
                )
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    #region Phase 2: Materialize Artists

    private async Task MaterializeArtistsAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Materialize artists"))
        {
            var totalArtists = await context.Artists.CountAsync(cancellationToken);
            var totalAliasRows = await context.ArtistAliases.CountAsync(cancellationToken);
            var totalArtistsForProgress = Math.Max(totalArtists, 1);
            progressCallback?.Invoke(
                "Materializing Artists",
                0,
                totalArtistsForProgress,
                WithImportStep(6, "Verifying streamed materialized artists..."));

            logger.Debug(
                "DecentDbStreamingImporter: Materialized {Count} artists with {AliasRows} indexed alias rows",
                totalArtists,
                totalAliasRows);
            progressCallback?.Invoke(
                "Materializing Artists",
                totalArtistsForProgress,
                totalArtistsForProgress,
                WithImportStep(
                    6,
                    $"Materialized {totalArtists:N0} artists with {totalAliasRows:N0} alias lookup rows from streamed source files"));
        }
    }

    #endregion

    #region Phase 3: Materialize Artist Relations

    private async Task MaterializeArtistRelationsAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Materialize artist relations"))
        {
            progressCallback?.Invoke("Materializing Relations", 0, 1, WithImportStep(7, "Creating artist relations from staging..."));

            var sql = @"
                INSERT INTO ""ArtistRelation"" (""ArtistId"", ""RelatedArtistId"", ""ArtistRelationType"", ""SortOrder"", ""RelationStart"", ""RelationEnd"")
                SELECT 
                    a1.""Id"",
                    a2.""Id"",
                    0,
                    laa.""LinkOrder"",
                    l.""BeginDate"",
                    l.""EndDate""
                FROM ""LinkArtistToArtistStaging"" laa
                INNER JOIN ""Artist"" a1 ON a1.""MusicBrainzArtistId"" = laa.""Artist0""
                INNER JOIN ""Artist"" a2 ON a2.""MusicBrainzArtistId"" = laa.""Artist1""
                LEFT JOIN ""LinkStaging"" l ON l.""LinkId"" = laa.""LinkId""";

            var rowsAffected = await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.Debug("DecentDbStreamingImporter: Materialized {Count} artist relations", rowsAffected);

            progressCallback?.Invoke("Materializing Relations", 1, 1, WithImportStep(7, $"Materialized {rowsAffected:N0} artist relations"));
        }
    }

    #endregion

    #region Phase 4: Drop Artist Staging Tables

    private async Task DropArtistStagingTablesAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Cleanup", 0, 1, WithImportStep(8, "Dropping artist staging tables..."));

        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistAliasStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""LinkStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""LinkArtistToArtistStaging""", cancellationToken);

        progressCallback?.Invoke("Cleanup", 1, 1, WithImportStep(8, "Artist staging tables cleared"));
    }

    #endregion

    #region Phase 5: Album Staging Data

    private async Task<int> ImportAlbumStagingDataAsync(
        MusicBrainzDbContext context,
        string mbDumpPath,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Album staging data"))
        {
            await DropAlbumStagingLookupIndexesAsync(context, cancellationToken).ConfigureAwait(false);
            await EnsureAlbumHelperTablesAsync(context, cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseCountryResolvedStaging""", cancellationToken);
            await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistCreditPrimaryArtistStaging""", cancellationToken);

            progressCallback?.Invoke("Loading Albums", 0, 6, WithImportStep(9, "Skipping unused artist credit staging..."));
            progressCallback?.Invoke("Loading Albums", 1, 6, WithImportStep(9, "Artist credit staging skipped"));

            progressCallback?.Invoke("Loading Albums", 1, 6, WithImportStep(10, "Streaming primary artist credits to helper table..."));
            var creditNameCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist_credit_name"),
                "ArtistCreditPrimaryArtistStaging",
                ["ArtistCreditId", "ArtistId"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    return [ToLong(p0), ToInt(p1), ToLong(p2)];
                },
                cancellationToken,
                values => values[1] is not int position || position != 0 || values[0] is not long creditId || creditId <= 0 || values[2] is not long artistId || artistId <= 0,
                onConflictDoNothing: true,
                valueProjector: values => [values[0], values[2]]);
            progressCallback?.Invoke("Loading Albums", 2, 6, WithImportStep(10, $"Streamed {creditNameCount:N0} primary artist credits"));

            progressCallback?.Invoke("Loading Albums", 2, 6, WithImportStep(11, "Skipping release-country dates; using release-group dates..."));
            progressCallback?.Invoke("Loading Albums", 3, 6, WithImportStep(11, "Release-country date staging skipped"));

            progressCallback?.Invoke("Loading Albums", 3, 6, WithImportStep(12, "Streaming release groups to staging..."));
            var groupCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "release_group"),
                nameof(ReleaseGroupStaging),
                ["ReleaseGroupId", "MusicBrainzIdRaw", "ArtistCreditId", "ReleaseType"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);
                    return [ToLong(p0), ToString(p1), ToLong(p3), ToInt(p4)];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 4, 6, WithImportStep(12, $"Streamed {groupCount:N0} release groups"));

            progressCallback?.Invoke("Loading Albums", 4, 6, WithImportStep(13, "Streaming release group meta to staging..."));
            var metaCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "release_group_meta"),
                nameof(ReleaseGroupMetaStaging),
                ["ReleaseGroupId", "DateYear", "DateMonth", "DateDay"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);
                    return [ToLong(p0), ToInt(p2), ToInt(p3), ToInt(p4)];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 5, 6, WithImportStep(13, $"Streamed {metaCount:N0} release group meta"));

            progressCallback?.Invoke("Loading Albums", 5, 6, WithImportStep(14, "Streaming releases to staging..."));
            var releaseCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "release"),
                nameof(ReleaseStaging),
                ["ReleaseId", "MusicBrainzIdRaw", "Name", "NameNormalized", "SortName", "ReleaseGroupId", "ArtistCreditId"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);

                    var name = ToString(p2);

                    return
                    [
                        ToLong(p0),
                        ToString(p1),
                        name.CleanString().TruncateLongString(MaxIndexSize) ?? string.Empty,
                        name.CleanString().TruncateLongString(MaxIndexSize)?.ToNormalizedString() ?? name,
                        name.CleanString(true).TruncateLongString(MaxIndexSize) ?? name,
                        ToLong(p4),
                        ToLong(p3)
                    ];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 6, 6, WithImportStep(14, $"Streamed {releaseCount:N0} releases"));

            progressCallback?.Invoke("Loading Albums", 6, 6, WithImportStep(15, "Creating staging indices..."));
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ReleaseStaging_ReleaseGroupId" ON "ReleaseStaging" ("ReleaseGroupId")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ReleaseStaging_ReleaseId" ON "ReleaseStaging" ("ReleaseId")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ReleaseStaging_ArtistCreditId" ON "ReleaseStaging" ("ArtistCreditId")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ReleaseGroupStaging_ReleaseGroupId" ON "ReleaseGroupStaging" ("ReleaseGroupId")""",
                cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_ReleaseGroupMetaStaging_ReleaseGroupId" ON "ReleaseGroupMetaStaging" ("ReleaseGroupId")""",
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 6, 6, WithImportStep(15, "Staging indices created"));

            progressCallback?.Invoke("Loading Albums", 6, 6, WithImportStep(16, "Creating resolved helper tables..."));
            progressCallback?.Invoke("Loading Albums", 6, 6, WithImportStep(16, "Album staging data loaded"));

            return releaseCount;
        }
    }

    #endregion

    #region Phase 6: Materialize Albums

    private async Task MaterializeAlbumsAsync(
        MusicBrainzDbContext context,
        int expectedAlbumCount,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Materialize albums"))
        {
            var progressTotal = Math.Max(expectedAlbumCount, 1);
            progressCallback?.Invoke("Materializing Albums", 0, progressTotal, WithImportStep(17, "Creating materialized albums from staging..."));
            await DropMaterializedAlbumLookupIndexesAsync(context, cancellationToken).ConfigureAwait(false);

            // PRINTF is not implemented in the DecentDB version in use, so we
            // assemble the ISO-8601 literal via string concatenation + LPAD.
            var insertSql = @"
                INSERT INTO ""Album"" (""MusicBrainzArtistId"", ""MusicBrainzIdRaw"", ""Name"", ""NameNormalized"", ""SortName"",
                                      ""ReleaseGroupMusicBrainzIdRaw"", ""ReleaseType"", ""ReleaseDate"", ""ContributorIds"")
                SELECT
                    acp.""ArtistId"",
                    r.""MusicBrainzIdRaw"",
                    r.""Name"",
                    r.""NameNormalized"",
                    r.""SortName"",
                    rg.""MusicBrainzIdRaw"",
                    rg.""ReleaseType"",
                    CASE
                        WHEN rgm.""DateYear"" > 0 AND rgm.""DateMonth"" > 0 AND rgm.""DateDay"" > 0 THEN
                            CAST(
                                LPAD(CAST(rgm.""DateYear"" AS TEXT), 4, '0') || '-' ||
                                LPAD(CAST(rgm.""DateMonth"" AS TEXT), 2, '0') || '-' ||
                                LPAD(CAST(
                                    CASE
                                        WHEN rgm.""DateMonth"" IN (1,3,5,7,8,10,12) AND rgm.""DateDay"" > 31 THEN 31
                                        WHEN rgm.""DateMonth"" IN (4,6,9,11) AND rgm.""DateDay"" > 30 THEN 30
                                        WHEN rgm.""DateMonth"" = 2 AND rgm.""DateDay"" > 29 THEN 29
                                        WHEN rgm.""DateMonth"" = 2 AND rgm.""DateDay"" = 29
                                            AND NOT (rgm.""DateYear"" % 4 = 0 AND (rgm.""DateYear"" % 100 != 0 OR rgm.""DateYear"" % 400 = 0))
                                            THEN 28
                                        ELSE rgm.""DateDay""
                                    END
                                AS TEXT), 2, '0') || ' 00:00:00'
                            AS TIMESTAMP)
                        ELSE NULL
                    END,
                    NULL
                FROM ""ReleaseStaging"" r
                INNER JOIN ""ReleaseGroupStaging"" rg ON rg.""ReleaseGroupId"" = r.""ReleaseGroupId""
                LEFT JOIN ""ReleaseGroupMetaStaging"" rgm ON rgm.""ReleaseGroupId"" = r.""ReleaseGroupId""
                INNER JOIN ""ArtistCreditPrimaryArtistStaging"" acp ON acp.""ArtistCreditId"" = r.""ArtistCreditId""
                WHERE r.""Name"" IS NOT NULL
                  AND r.""Name"" != ''
                  AND rg.""MusicBrainzIdRaw"" IS NOT NULL
                  AND rgm.""DateYear"" > 0
                  AND rgm.""DateMonth"" > 0
                  AND rgm.""DateDay"" > 0";

            var rowsAffected = await context.Database.ExecuteSqlRawAsync(insertSql, cancellationToken);
            progressCallback?.Invoke("Materializing Albums", progressTotal, progressTotal, WithImportStep(17, "Rebuilding album lookup indices..."));
            await RebuildMaterializedAlbumLookupIndexesAsync(context, cancellationToken).ConfigureAwait(false);

            logger.Debug("DecentDbStreamingImporter: Materialized {Count} albums", rowsAffected);
            progressCallback?.Invoke("Materializing Albums", 1, 1, WithImportStep(17, $"Materialized {rowsAffected:N0} albums"));
        }
    }

    #endregion

    #region Phase 7: Drop Album Staging Tables

    private async Task DropAlbumStagingTablesAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Cleanup", 0, 1, WithImportStep(18, "Dropping album staging tables..."));

        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistCreditStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistCreditNameStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseCountryStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseGroupStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseGroupMetaStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseCountryResolvedStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistCreditPrimaryArtistStaging""", cancellationToken);

        progressCallback?.Invoke("Cleanup", 1, 1, WithImportStep(18, "Album staging tables cleared"));
    }

    #endregion

    #region Helper Methods

    private static void ResetContextState(
        MusicBrainzDbContext context,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string WithImportStep(int stepNumber, string message)
    {
        return $"({stepNumber}/{TotalImportSteps}) {message}";
    }

    private readonly record struct AlbumInsertRow(
        long MusicBrainzArtistId,
        string MusicBrainzIdRaw,
        string Name,
        string NameNormalized,
        string SortName,
        string ReleaseGroupMusicBrainzIdRaw,
        int ReleaseType,
        DateTime ReleaseDate);

    private async Task<int> StreamFileToStagingRawAsync(
        MusicBrainzDbContext context,
        string filePath,
        string tableName,
        string[] columns,
        Func<ReadOnlySpan<char>, object?[]> parser,
        CancellationToken cancellationToken,
        Func<object?[], bool>? shouldSkipRow = null,
        bool onConflictDoNothing = false,
        Func<object?[], object?[]>? valueProjector = null)
    {
        if (!File.Exists(filePath))
        {
            logger.Warning("DecentDbStreamingImporter: File not found: {FilePath}", filePath);
            return 0;
        }

        var totalCount = 0;
        var pendingRows = new List<object?[]>(StagingRowsPerTransaction);
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        using var reader = new StreamReader(filePath);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            try
            {
                var values = parser(line.AsSpan());
                if (shouldSkipRow?.Invoke(values) == true)
                {
                    continue;
                }

                if (valueProjector is not null)
                {
                    values = valueProjector(values);
                }

                pendingRows.Add(values);
                totalCount++;

                if (pendingRows.Count >= StagingRowsPerTransaction)
                {
                    await FlushPendingRowsAsync(
                            connection,
                            tableName,
                            columns,
                            pendingRows,
                            cancellationToken,
                            onConflictDoNothing)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.Debug("DecentDbStreamingImporter: Skipped malformed line in {File}: {Error}",
                    Path.GetFileName(filePath), ex.Message);
            }
        }

        if (pendingRows.Count > 0)
        {
            await FlushPendingRowsAsync(
                    connection,
                    tableName,
                    columns,
                    pendingRows,
                    cancellationToken,
                    onConflictDoNothing)
                .ConfigureAwait(false);
        }

        return totalCount;
    }

    private static async Task FlushPendingRowsAsync(
        DbConnection connection,
        string tableName,
        string[] columns,
        List<object?[]> pendingRows,
        CancellationToken cancellationToken,
        bool onConflictDoNothing)
    {
        if (pendingRows.Count == 0)
        {
            return;
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        DbTransaction? transaction = null;
        try
        {
            transaction = connection.BeginTransaction();

            for (var offset = 0; offset < pendingRows.Count; offset += StagingRowsPerInsertStatement)
            {
                var rowCount = Math.Min(StagingRowsPerInsertStatement, pendingRows.Count - offset);
                await using var command = CreateMultiRowInsertCommand(
                    connection,
                    transaction,
                    tableName,
                    columns,
                    pendingRows,
                    offset,
                    rowCount,
                    onConflictDoNothing);

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            pendingRows.Clear();
        }
        catch
        {
            if (transaction != null)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                }
            }

            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    private static string BuildInsertCommandText(
        string tableName,
        string[] columns,
        int rowCount,
        bool onConflictDoNothing)
    {
        var commandText = new StringBuilder();
        commandText.Append($"INSERT INTO \"{tableName}\" (");
        commandText.Append(string.Join(", ", columns.Select(column => $"\"{column}\"")));
        commandText.Append(") VALUES (");
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            if (rowIndex > 0)
            {
                commandText.Append(", (");
            }

            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                if (columnIndex > 0)
                {
                    commandText.Append(", ");
                }

                commandText.Append($"@p{rowIndex}_{columnIndex}");
            }

            commandText.Append(')');
        }

        if (onConflictDoNothing)
        {
            commandText.Append(" ON CONFLICT DO NOTHING");
        }

        return commandText.ToString();
    }

    private static DbCommand CreateMultiRowInsertCommand(
        DbConnection connection,
        DbTransaction transaction,
        string tableName,
        string[] columns,
        List<object?[]> pendingRows,
        int offset,
        int rowCount,
        bool onConflictDoNothing)
    {
        var commandText = BuildInsertCommandText(tableName, columns, rowCount, onConflictDoNothing);
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var values = pendingRows[offset + rowIndex];
            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                var param = command.CreateParameter();
                param.ParameterName = $"@p{rowIndex}_{columnIndex}";
                var value = values[columnIndex];
                if (value is string text && string.IsNullOrEmpty(text))
                {
                    param.Value = DBNull.Value;
                }
                else
                {
                    param.Value = value ?? DBNull.Value;
                }

                command.Parameters.Add(param);
            }
        }

        return command;
    }

    #endregion

    #region Span Helpers

    private static ReadOnlySpan<char> GetColumn(ReadOnlySpan<char> line, int index)
    {
        var slice = line;
        for (var i = 0; i < index; i++)
        {
            var tabIndex = slice.IndexOf('\t');
            if (tabIndex == -1) return ReadOnlySpan<char>.Empty;
            slice = slice[(tabIndex + 1)..];
        }

        var nextTab = slice.IndexOf('\t');
        return nextTab == -1 ? slice : slice[..nextTab];
    }

    private static long ToLong(ReadOnlySpan<char> span) =>
        long.TryParse(span, out var result) ? result : 0;

    private static int ToInt(ReadOnlySpan<char> span) =>
        int.TryParse(span, out var result) ? result : 0;

    private static string ToString(ReadOnlySpan<char> span) =>
        span.ToString();

    private static DateTime? ToDateValue(ReadOnlySpan<char> year, ReadOnlySpan<char> month, ReadOnlySpan<char> day)
    {
        var y = int.TryParse(year, out var vy) ? (int?)vy : null;
        var m = int.TryParse(month, out var vm) ? (int?)vm : null;
        var d = int.TryParse(day, out var vd) ? (int?)vd : null;

        if (y is > 0 and < 9999)
        {
            var actualYear = Math.Clamp(y.Value, 1, 9999);
            var actualMonth = m is > 0 and <= 12 ? m.Value : 1;
            var maxDay = DateTime.DaysInMonth(actualYear, actualMonth);
            var actualDay = d is > 0 ? Math.Min(d.Value, maxDay) : 1;
            return new DateTime(actualYear, actualMonth, actualDay, 0, 0, 0, DateTimeKind.Utc);
        }
        return null;
    }

    #endregion
}
