using System.Text;
using Melodee.Common.Extensions;
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
    private const int BatchSize = 25000;
    private const int MaxIndexSize = 255;

    public async Task ImportAsync(
        MusicBrainzDbContext context,
        string storagePath,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var mbDumpPath = Path.Combine(storagePath, "staging/mbdump");

        // Phase 1: Stream artist data to staging tables
        await ImportArtistStagingDataAsync(context, mbDumpPath, progressCallback, cancellationToken);

        // Phase 2: Materialize artists using SQL
        await MaterializeArtistsAsync(context, progressCallback, cancellationToken);

        // Phase 3: Materialize artist relations using SQL
        await MaterializeArtistRelationsAsync(context, progressCallback, cancellationToken);

        // Phase 4: Drop artist staging tables
        await DropArtistStagingTablesAsync(context, progressCallback, cancellationToken);

        // Phase 5: Stream album support data to staging tables
        await ImportAlbumStagingDataAsync(context, mbDumpPath, progressCallback, cancellationToken);

        // Phase 6: Materialize albums using SQL
        await MaterializeAlbumsAsync(context, progressCallback, cancellationToken);

        // Phase 7: Drop album staging tables
        await DropAlbumStagingTablesAsync(context, progressCallback, cancellationToken);
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
            progressCallback?.Invoke("Loading Artists", 0, 4, "Streaming artist file to staging...");
            var artistCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist"),
                nameof(ArtistStaging),
                ["ArtistId", "MusicBrainzIdRaw", "Name", "NameNormalized", "SortName"],
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
            progressCallback?.Invoke("Loading Artists", 1, 4, $"Streamed {artistCount:N0} artists to staging");

            progressCallback?.Invoke("Loading Artists", 1, 4, "Streaming artist aliases to staging...");
            var aliasCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist_alias"),
                nameof(ArtistAliasStaging),
                ["ArtistId", "NameNormalized"],
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
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 2, 4, $"Streamed {aliasCount:N0} artist aliases to staging");

            progressCallback?.Invoke("Loading Artists", 2, 4, "Streaming links to staging...");
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
                        ToDate(pBeginY, pBeginM, pBeginD),
                        ToDate(pEndY, pEndM, pEndD)
                    ];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 3, 4, $"Streamed {linkCount:N0} links to staging");

            progressCallback?.Invoke("Loading Artists", 3, 4, "Streaming artist links to staging...");
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

                    return [ToLong(p1), ToLong(p2), ToLong(p3), ToInt(p6)];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 4, 4, $"Streamed {artistLinkCount:N0} artist links to staging");

            progressCallback?.Invoke("Loading Artists", 4, 4, "Creating staging indices...");
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ArtistStaging_ArtistId ON ArtistStaging(ArtistId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ArtistAliasStaging_ArtistId ON ArtistAliasStaging(ArtistId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ArtistAliasStaging_NameNormalized ON ArtistAliasStaging(NameNormalized)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_LinkArtistToArtistStaging_Artist0 ON LinkArtistToArtistStaging(Artist0)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_LinkArtistToArtistStaging_Artist1 ON LinkArtistToArtistStaging(Artist1)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_LinkArtistToArtistStaging_LinkId ON LinkArtistToArtistStaging(LinkId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_LinkStaging_LinkId ON LinkStaging(LinkId)", cancellationToken);
            progressCallback?.Invoke("Loading Artists", 4, 4, "Staging indices created");
        }
    }

    #endregion

    #region Phase 2: Materialize Artists

    private async Task MaterializeArtistsAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Materialize artists"))
        {
            progressCallback?.Invoke("Materializing Artists", 0, 1, "Creating materialized artists from staging...");

            var sql = @"
                INSERT INTO Artist (MusicBrainzArtistId, MusicBrainzIdRaw, Name, NameNormalized, SortName, AlternateNames)
                SELECT 
                    a.ArtistId,
                    a.MusicBrainzIdRaw,
                    a.Name,
                    a.NameNormalized,
                    a.SortName,
                    (SELECT GROUP_CONCAT(NameNormalized, '|') 
                     FROM (SELECT DISTINCT aa.NameNormalized FROM ArtistAliasStaging aa WHERE aa.ArtistId = a.ArtistId))
                FROM ArtistStaging a";

            var rowsAffected = await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.Debug("DecentDbStreamingImporter: Materialized {Count} artists", rowsAffected);

            progressCallback?.Invoke("Materializing Artists", 1, 1, $"Materialized {rowsAffected:N0} artists");
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
            progressCallback?.Invoke("Materializing Relations", 0, 1, "Creating artist relations from staging...");

            var sql = @"
                INSERT INTO ArtistRelation (ArtistId, RelatedArtistId, ArtistRelationType, SortOrder, RelationStart, RelationEnd)
                SELECT 
                    a1.Id,
                    a2.Id,
                    0,
                    laa.LinkOrder,
                    l.BeginDate,
                    l.EndDate
                FROM LinkArtistToArtistStaging laa
                INNER JOIN Artist a1 ON a1.MusicBrainzArtistId = laa.Artist0
                INNER JOIN Artist a2 ON a2.MusicBrainzArtistId = laa.Artist1
                LEFT JOIN LinkStaging l ON l.LinkId = laa.LinkId";

            var rowsAffected = await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.Debug("DecentDbStreamingImporter: Materialized {Count} artist relations", rowsAffected);

            progressCallback?.Invoke("Materializing Relations", 1, 1, $"Materialized {rowsAffected:N0} artist relations");
        }
    }

    #endregion

    #region Phase 4: Drop Artist Staging Tables

    private async Task DropArtistStagingTablesAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Cleanup", 0, 1, "Dropping artist staging tables...");

        await context.Database.ExecuteSqlRawAsync("DELETE FROM ArtistStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ArtistAliasStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM LinkStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM LinkArtistToArtistStaging", cancellationToken);

        progressCallback?.Invoke("Cleanup", 1, 1, "Artist staging tables cleared");
    }

    #endregion

    #region Phase 5: Album Staging Data

    private async Task ImportAlbumStagingDataAsync(
        MusicBrainzDbContext context,
        string mbDumpPath,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Album staging data"))
        {
            progressCallback?.Invoke("Loading Albums", 0, 6, "Streaming artist credits to staging...");
            var creditCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist_credit"),
                nameof(ArtistCreditStaging),
                ["ArtistCreditId", "ArtistCount"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p2 = GetColumn(span, 2);
                    return [ToLong(p0), ToInt(p2)];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 1, 6, $"Streamed {creditCount:N0} artist credits");

            progressCallback?.Invoke("Loading Albums", 1, 6, "Streaming artist credit names to staging...");
            var creditNameCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "artist_credit_name"),
                nameof(ArtistCreditNameStaging),
                ["ArtistCreditId", "Position", "ArtistId"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    return [ToLong(p0), ToInt(p1), ToLong(p2)];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 2, 6, $"Streamed {creditNameCount:N0} artist credit names");

            progressCallback?.Invoke("Loading Albums", 2, 6, "Streaming release countries to staging...");
            var countryCount = await StreamFileToStagingRawAsync(
                context,
                Path.Combine(mbDumpPath, "release_country"),
                nameof(ReleaseCountryStaging),
                ["ReleaseId", "DateYear", "DateMonth", "DateDay"],
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);
                    return [ToLong(p0), ToInt(p2), ToInt(p3), ToInt(p4)];
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 3, 6, $"Streamed {countryCount:N0} release countries");

            progressCallback?.Invoke("Loading Albums", 3, 6, "Streaming release groups to staging...");
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
            progressCallback?.Invoke("Loading Albums", 4, 6, $"Streamed {groupCount:N0} release groups");

            progressCallback?.Invoke("Loading Albums", 4, 6, "Streaming release group meta to staging...");
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
            progressCallback?.Invoke("Loading Albums", 5, 6, $"Streamed {metaCount:N0} release group meta");

            progressCallback?.Invoke("Loading Albums", 5, 6, "Streaming releases to staging...");
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
            progressCallback?.Invoke("Loading Albums", 6, 6, $"Streamed {releaseCount:N0} releases");

            progressCallback?.Invoke("Loading Albums", 6, 6, "Creating staging indices...");
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ReleaseStaging_ReleaseGroupId ON ReleaseStaging(ReleaseGroupId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ReleaseStaging_ReleaseId ON ReleaseStaging(ReleaseId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ReleaseStaging_ArtistCreditId ON ReleaseStaging(ArtistCreditId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ReleaseGroupStaging_ReleaseGroupId ON ReleaseGroupStaging(ReleaseGroupId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ReleaseGroupMetaStaging_ReleaseGroupId ON ReleaseGroupMetaStaging(ReleaseGroupId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ReleaseCountryStaging_ReleaseId ON ReleaseCountryStaging(ReleaseId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ArtistCreditStaging_ArtistCreditId ON ArtistCreditStaging(ArtistCreditId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ArtistCreditNameStaging_ArtistCreditId ON ArtistCreditNameStaging(ArtistCreditId)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_ArtistCreditNameStaging_ArtistId ON ArtistCreditNameStaging(ArtistId)", cancellationToken);
            progressCallback?.Invoke("Loading Albums", 6, 6, "Staging indices created");
        }
    }

    #endregion

    #region Phase 6: Materialize Albums

    private async Task MaterializeAlbumsAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbStreamingImporter: Materialize albums"))
        {
            progressCallback?.Invoke("Materializing Albums", 0, 1, "Creating materialized albums from staging...");

            var sql = @"
                INSERT INTO Album (MusicBrainzArtistId, MusicBrainzIdRaw, Name, NameNormalized, SortName, 
                                   ReleaseGroupMusicBrainzIdRaw, ReleaseType, ReleaseDate, ContributorIds)
                SELECT 
                    COALESCE(acn_artist.MusicBrainzArtistId, credit_artist.MusicBrainzArtistId),
                    r.MusicBrainzIdRaw,
                    r.Name,
                    r.NameNormalized,
                    r.SortName,
                    rg.MusicBrainzIdRaw,
                    rg.ReleaseType,
                    COALESCE(
                        CASE WHEN rc.DateYear > 0 AND rc.DateMonth > 0 AND rc.DateDay > 0 
                             THEN printf('%04d-%02d-%02dT00:00:00', 
                                        CASE WHEN rc.DateYear BETWEEN 1 AND 9999 THEN rc.DateYear ELSE 1 END,
                                        CASE WHEN rc.DateMonth BETWEEN 1 AND 12 THEN rc.DateMonth ELSE 1 END,
                                        CASE WHEN rc.DateDay BETWEEN 1 AND 31 THEN rc.DateDay ELSE 1 END)
                             ELSE NULL END,
                        CASE WHEN rgm.DateYear > 0 AND rgm.DateMonth > 0 AND rgm.DateDay > 0 
                             THEN printf('%04d-%02d-%02dT00:00:00', 
                                        CASE WHEN rgm.DateYear BETWEEN 1 AND 9999 THEN rgm.DateYear ELSE 1 END,
                                        CASE WHEN rgm.DateMonth BETWEEN 1 AND 12 THEN rgm.DateMonth ELSE 1 END,
                                        CASE WHEN rgm.DateDay BETWEEN 1 AND 31 THEN rgm.DateDay ELSE 1 END)
                             ELSE NULL END
                    ),
                    NULL
                FROM ReleaseStaging r
                INNER JOIN ReleaseGroupStaging rg ON rg.ReleaseGroupId = r.ReleaseGroupId
                LEFT JOIN ReleaseCountryStaging rc ON rc.ReleaseId = r.ReleaseId
                LEFT JOIN ReleaseGroupMetaStaging rgm ON rgm.ReleaseGroupId = r.ReleaseGroupId
                LEFT JOIN ArtistCreditStaging ac ON ac.ArtistCreditId = r.ArtistCreditId
                LEFT JOIN ArtistCreditNameStaging acn ON acn.ArtistCreditId = ac.ArtistCreditId AND acn.Position = 0
                LEFT JOIN Artist acn_artist ON acn_artist.MusicBrainzArtistId = acn.ArtistId
                LEFT JOIN Artist credit_artist ON credit_artist.MusicBrainzArtistId = r.ArtistCreditId
                WHERE r.Name IS NOT NULL 
                  AND r.Name != ''
                  AND rg.MusicBrainzIdRaw IS NOT NULL
                  AND (acn_artist.MusicBrainzArtistId IS NOT NULL OR credit_artist.MusicBrainzArtistId IS NOT NULL)
                  AND (
                      (rc.DateYear > 0 AND rc.DateMonth > 0 AND rc.DateDay > 0) OR
                      (rgm.DateYear > 0 AND rgm.DateMonth > 0 AND rgm.DateDay > 0)
                  )";

            var rowsAffected = await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.Debug("DecentDbStreamingImporter: Materialized {Count} albums", rowsAffected);

            progressCallback?.Invoke("Materializing Albums", 1, 1, $"Materialized {rowsAffected:N0} albums");
        }
    }

    #endregion

    #region Phase 7: Drop Album Staging Tables

    private async Task DropAlbumStagingTablesAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Cleanup", 0, 1, "Dropping album staging tables...");

        await context.Database.ExecuteSqlRawAsync("DELETE FROM ArtistCreditStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ArtistCreditNameStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ReleaseCountryStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ReleaseGroupStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ReleaseGroupMetaStaging", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("DELETE FROM ReleaseStaging", cancellationToken);

        progressCallback?.Invoke("Cleanup", 1, 1, "Album staging tables cleared");
    }

    #endregion

    #region Helper Methods

    private async Task<int> StreamFileToStagingRawAsync(
        MusicBrainzDbContext context,
        string filePath,
        string tableName,
        string[] columns,
        Func<ReadOnlySpan<char>, object?[]> parser,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            logger.Warning("DecentDbStreamingImporter: File not found: {FilePath}", filePath);
            return 0;
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var totalCount = 0;

        var commandText = new StringBuilder();
        commandText.Append($"INSERT INTO {tableName} (");
        commandText.Append(string.Join(", ", columns));
        commandText.Append(") VALUES (");
        for (var i = 0; i < columns.Length; i++)
        {
            commandText.Append($"$p{i}");
            if (i < columns.Length - 1) commandText.Append(", ");
        }
        commandText.Append(')');

        using var command = connection.CreateCommand();
        command.CommandText = commandText.ToString();

        for (var i = 0; i < columns.Length; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"$p{i}";
            command.Parameters.Add(param);
        }

        var transaction = connection.BeginTransaction();
        command.Transaction = transaction;

        try
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                try
                {
                    var values = parser(line.AsSpan());

                    for (var i = 0; i < values.Length; i++)
                    {
                        var val = values[i];
                        if (val is string s && string.IsNullOrEmpty(s))
                        {
                            command.Parameters[i].Value = DBNull.Value;
                        }
                        else
                        {
                            command.Parameters[i].Value = val ?? DBNull.Value;
                        }
                    }

                    await command.ExecuteNonQueryAsync(cancellationToken);
                    totalCount++;

                    if (totalCount % BatchSize == 0)
                    {
                        transaction.Commit();
                        transaction.Dispose();
                        transaction = connection.BeginTransaction();
                        command.Transaction = transaction;
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug("DecentDbStreamingImporter: Skipped malformed line in {File}: {Error}",
                        Path.GetFileName(filePath), ex.Message);
                }
            }

            transaction.Commit();
        }
        catch (Exception)
        {
            try { transaction.Rollback(); } catch { /* ignored */ }
            throw;
        }
        finally
        {
            transaction.Dispose();
        }

        return totalCount;
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

    private static object? ToDate(ReadOnlySpan<char> year, ReadOnlySpan<char> month, ReadOnlySpan<char> day)
    {
        var y = int.TryParse(year, out var vy) ? (int?)vy : null;
        var m = int.TryParse(month, out var vm) ? (int?)vm : null;
        var d = int.TryParse(day, out var vd) ? (int?)vd : null;

        if (y is > 0 and < 9999)
        {
            var actualMonth = m is > 0 and <= 12 ? m.Value : 1;
            var actualDay = d is > 0 and <= 31 ? d.Value : 1;
            return $"{y:0000}-{actualMonth:00}-{actualDay:00} 00:00:00";
        }
        return DBNull.Value;
    }

    #endregion
}
