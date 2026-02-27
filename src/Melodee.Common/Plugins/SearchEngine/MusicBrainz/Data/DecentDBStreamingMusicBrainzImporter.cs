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
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Materialize artists using SQL
        await MaterializeArtistsAsync(context, progressCallback, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3: Materialize artist relations using SQL
        await MaterializeArtistRelationsAsync(context, progressCallback, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 4: Drop artist staging tables
        await DropArtistStagingTablesAsync(context, progressCallback, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 5: Stream album support data to staging tables
        await ImportAlbumStagingDataAsync(context, mbDumpPath, progressCallback, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 6: Materialize albums using SQL
        await MaterializeAlbumsAsync(context, progressCallback, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

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
            var artistCount = StreamFileToStaging<ArtistStaging>(
                context,
                Path.Combine(mbDumpPath, "artist"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);

                    var name = ToString(p2);
                    var sortName = ToString(p3);

                    return new ArtistStaging
                    {
                        ArtistId = ToLong(p0),
                        MusicBrainzIdRaw = (Guid.TryParse(p1, out var g) ? g : Guid.Empty).ToString(),
                        Name = name.CleanString().TruncateLongString(MaxIndexSize) ?? string.Empty,
                        NameNormalized = name.CleanString().TruncateLongString(MaxIndexSize)?.ToNormalizedString() ?? name,
                        SortName = sortName.CleanString(true).TruncateLongString(MaxIndexSize) ?? name
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 1, 4, $"Streamed {artistCount:N0} artists to staging");

            progressCallback?.Invoke("Loading Artists", 1, 4, "Streaming artist aliases to staging...");
            var aliasCount = StreamFileToStaging<ArtistAliasStaging>(
                context,
                Path.Combine(mbDumpPath, "artist_alias"),
                span =>
                {
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var name = ToString(p2);

                    return new ArtistAliasStaging
                    {
                        ArtistId = ToLong(p1),
                        NameNormalized = name.CleanString().TruncateLongString(MaxIndexSize)?.ToNormalizedString() ?? name
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 2, 4, $"Streamed {aliasCount:N0} artist aliases to staging");

            progressCallback?.Invoke("Loading Artists", 2, 4, "Streaming links to staging...");
            var linkCount = StreamFileToStaging<LinkStaging>(
                context,
                Path.Combine(mbDumpPath, "link"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var pBeginY = GetColumn(span, 2);
                    var pBeginM = GetColumn(span, 3);
                    var pBeginD = GetColumn(span, 4);
                    var pEndY = GetColumn(span, 5);
                    var pEndM = GetColumn(span, 6);
                    var pEndD = GetColumn(span, 7);

                    return new LinkStaging
                    {
                        LinkId = ToLong(p0),
                        BeginDate = ToDateValue(pBeginY, pBeginM, pBeginD),
                        EndDate = ToDateValue(pEndY, pEndM, pEndD)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 3, 4, $"Streamed {linkCount:N0} links to staging");

            progressCallback?.Invoke("Loading Artists", 3, 4, "Streaming artist links to staging...");
            var artistLinkCount = StreamFileToStaging<LinkArtistToArtistStaging>(
                context,
                Path.Combine(mbDumpPath, "l_artist_artist"),
                span =>
                {
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p6 = GetColumn(span, 6);

                    return new LinkArtistToArtistStaging
                    {
                        LinkId = ToLong(p1),
                        Artist0 = ToLong(p2),
                        Artist1 = ToLong(p3),
                        LinkOrder = ToInt(p6)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Artists", 4, 4, $"Streamed {artistLinkCount:N0} artist links to staging");

            progressCallback?.Invoke("Loading Artists", 4, 4, "Artist staging data loaded");
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
            progressCallback?.Invoke("Materializing Artists", 0, 2, "Creating materialized artists from staging...");

            var insertSql = @"
                INSERT INTO ""Artist"" (""MusicBrainzArtistId"", ""MusicBrainzIdRaw"", ""Name"", ""NameNormalized"", ""SortName"", ""AlternateNames"")
                SELECT 
                    a.""ArtistId"",
                    a.""MusicBrainzIdRaw"",
                    a.""Name"",
                    a.""NameNormalized"",
                    a.""SortName"",
                    NULL
                FROM ""ArtistStaging"" a";

            var rowsAffected = await context.Database.ExecuteSqlRawAsync(insertSql, cancellationToken);
            progressCallback?.Invoke("Materializing Artists", 1, 2, $"Inserted {rowsAffected:N0} artists, updating alternate names...");

            // Build alternate names in C# to avoid correlated subquery (not supported by DecentDB)
            var aliasGroups = await context.ArtistAliasesStaging
                .GroupBy(aa => aa.ArtistId)
                .Select(g => new { ArtistId = g.Key, Names = g.Select(x => x.NameNormalized).ToList() })
                .ToListAsync(cancellationToken);

            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = @"UPDATE ""Artist"" SET ""AlternateNames"" = @p0 WHERE ""MusicBrainzArtistId"" = @p1";
            var namesParam = updateCmd.CreateParameter();
            namesParam.ParameterName = "@p0";
            updateCmd.Parameters.Add(namesParam);
            var idParam = updateCmd.CreateParameter();
            idParam.ParameterName = "@p1";
            updateCmd.Parameters.Add(idParam);

            foreach (var group in aliasGroups)
            {
                namesParam.Value = string.Join("|", group.Names);
                idParam.Value = group.ArtistId;
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            logger.Debug("DecentDbStreamingImporter: Materialized {Count} artists with {AliasGroups} alias groups", rowsAffected, aliasGroups.Count);
            progressCallback?.Invoke("Materializing Artists", 2, 2, $"Materialized {rowsAffected:N0} artists");
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

        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistAliasStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""LinkStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""LinkArtistToArtistStaging""", cancellationToken);

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
            var creditCount = StreamFileToStaging<ArtistCreditStaging>(
                context,
                Path.Combine(mbDumpPath, "artist_credit"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p2 = GetColumn(span, 2);
                    return new ArtistCreditStaging
                    {
                        ArtistCreditId = ToLong(p0),
                        ArtistCount = ToInt(p2)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 1, 6, $"Streamed {creditCount:N0} artist credits");

            progressCallback?.Invoke("Loading Albums", 1, 6, "Streaming artist credit names to staging...");
            var creditNameCount = StreamFileToStaging<ArtistCreditNameStaging>(
                context,
                Path.Combine(mbDumpPath, "artist_credit_name"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    return new ArtistCreditNameStaging
                    {
                        ArtistCreditId = ToLong(p0),
                        Position = ToInt(p1),
                        ArtistId = ToLong(p2)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 2, 6, $"Streamed {creditNameCount:N0} artist credit names");

            progressCallback?.Invoke("Loading Albums", 2, 6, "Streaming release countries to staging...");
            var countryCount = StreamFileToStaging<ReleaseCountryStaging>(
                context,
                Path.Combine(mbDumpPath, "release_country"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);
                    return new ReleaseCountryStaging
                    {
                        ReleaseId = ToLong(p0),
                        DateYear = ToInt(p2),
                        DateMonth = ToInt(p3),
                        DateDay = ToInt(p4)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 3, 6, $"Streamed {countryCount:N0} release countries");

            progressCallback?.Invoke("Loading Albums", 3, 6, "Streaming release groups to staging...");
            var groupCount = StreamFileToStaging<ReleaseGroupStaging>(
                context,
                Path.Combine(mbDumpPath, "release_group"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);
                    return new ReleaseGroupStaging
                    {
                        ReleaseGroupId = ToLong(p0),
                        MusicBrainzIdRaw = ToString(p1),
                        ArtistCreditId = ToLong(p3),
                        ReleaseType = ToInt(p4)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 4, 6, $"Streamed {groupCount:N0} release groups");

            progressCallback?.Invoke("Loading Albums", 4, 6, "Streaming release group meta to staging...");
            var metaCount = StreamFileToStaging<ReleaseGroupMetaStaging>(
                context,
                Path.Combine(mbDumpPath, "release_group_meta"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);
                    return new ReleaseGroupMetaStaging
                    {
                        ReleaseGroupId = ToLong(p0),
                        DateYear = ToInt(p2),
                        DateMonth = ToInt(p3),
                        DateDay = ToInt(p4)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 5, 6, $"Streamed {metaCount:N0} release group meta");

            progressCallback?.Invoke("Loading Albums", 5, 6, "Streaming releases to staging...");
            var releaseCount = StreamFileToStaging<ReleaseStaging>(
                context,
                Path.Combine(mbDumpPath, "release"),
                span =>
                {
                    var p0 = GetColumn(span, 0);
                    var p1 = GetColumn(span, 1);
                    var p2 = GetColumn(span, 2);
                    var p3 = GetColumn(span, 3);
                    var p4 = GetColumn(span, 4);

                    var name = ToString(p2);

                    return new ReleaseStaging
                    {
                        ReleaseId = ToLong(p0),
                        MusicBrainzIdRaw = ToString(p1),
                        Name = name.CleanString().TruncateLongString(MaxIndexSize) ?? string.Empty,
                        NameNormalized = name.CleanString().TruncateLongString(MaxIndexSize)?.ToNormalizedString() ?? name,
                        SortName = name.CleanString(true).TruncateLongString(MaxIndexSize) ?? name,
                        ReleaseGroupId = ToLong(p4),
                        ArtistCreditId = ToLong(p3)
                    };
                },
                cancellationToken);
            progressCallback?.Invoke("Loading Albums", 6, 6, $"Streamed {releaseCount:N0} releases");

            progressCallback?.Invoke("Loading Albums", 6, 6, "Album staging data loaded");
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

            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            var querySql = @"
                SELECT 
                    COALESCE(acn_artist.""MusicBrainzArtistId"", credit_artist.""MusicBrainzArtistId"") AS ""ArtistId"",
                    r.""MusicBrainzIdRaw"",
                    r.""Name"",
                    r.""NameNormalized"",
                    r.""SortName"",
                    rg.""MusicBrainzIdRaw"" AS ""RgMbId"",
                    rg.""ReleaseType"",
                    rc.""DateYear"" AS ""RcYear"", rc.""DateMonth"" AS ""RcMonth"", rc.""DateDay"" AS ""RcDay"",
                    rgm.""DateYear"" AS ""RgmYear"", rgm.""DateMonth"" AS ""RgmMonth"", rgm.""DateDay"" AS ""RgmDay""
                FROM ""ReleaseStaging"" r
                INNER JOIN ""ReleaseGroupStaging"" rg ON rg.""ReleaseGroupId"" = r.""ReleaseGroupId""
                LEFT JOIN ""ReleaseCountryStaging"" rc ON rc.""ReleaseId"" = r.""ReleaseId""
                LEFT JOIN ""ReleaseGroupMetaStaging"" rgm ON rgm.""ReleaseGroupId"" = r.""ReleaseGroupId""
                LEFT JOIN ""ArtistCreditStaging"" ac ON ac.""ArtistCreditId"" = r.""ArtistCreditId""
                LEFT JOIN ""ArtistCreditNameStaging"" acn ON acn.""ArtistCreditId"" = ac.""ArtistCreditId"" AND acn.""Position"" = 0
                LEFT JOIN ""Artist"" acn_artist ON acn_artist.""MusicBrainzArtistId"" = acn.""ArtistId""
                LEFT JOIN ""Artist"" credit_artist ON credit_artist.""MusicBrainzArtistId"" = r.""ArtistCreditId""
                WHERE r.""Name"" IS NOT NULL 
                  AND r.""Name"" != ''
                  AND rg.""MusicBrainzIdRaw"" IS NOT NULL
                  AND (acn_artist.""MusicBrainzArtistId"" IS NOT NULL OR credit_artist.""MusicBrainzArtistId"" IS NOT NULL)
                  AND (
                      (rc.""DateYear"" > 0 AND rc.""DateMonth"" > 0 AND rc.""DateDay"" > 0) OR
                      (rgm.""DateYear"" > 0 AND rgm.""DateMonth"" > 0 AND rgm.""DateDay"" > 0)
                  )";

            // Read all query results into Album entities first
            var albums = new List<Album>();
            using (var queryCmd = connection.CreateCommand())
            {
                queryCmd.CommandText = querySql;
                using var reader = await queryCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var rcYear = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                    var rcMonth = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
                    var rcDay = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);
                    var rgmYear = reader.IsDBNull(10) ? 0 : reader.GetInt32(10);
                    var rgmMonth = reader.IsDBNull(11) ? 0 : reader.GetInt32(11);
                    var rgmDay = reader.IsDBNull(12) ? 0 : reader.GetInt32(12);

                    DateTime? releaseDate = null;
                    if (rcYear > 0 && rcMonth > 0 && rcDay > 0)
                    {
                        releaseDate = SafeDate(rcYear, rcMonth, rcDay);
                    }
                    else if (rgmYear > 0 && rgmMonth > 0 && rgmDay > 0)
                    {
                        releaseDate = SafeDate(rgmYear, rgmMonth, rgmDay);
                    }

                    albums.Add(new Album
                    {
                        MusicBrainzArtistId = reader.GetInt64(0),
                        MusicBrainzIdRaw = reader.GetString(1),
                        Name = reader.GetString(2),
                        NameNormalized = reader.GetString(3),
                        SortName = reader.GetString(4),
                        ReleaseGroupMusicBrainzIdRaw = reader.GetString(5),
                        ReleaseType = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        ReleaseDate = releaseDate ?? DateTime.MinValue
                    });
                }
            }

            // Batch insert via EF Core to leverage prepared-statement reuse
            var previousAutoDetect = context.ChangeTracker.AutoDetectChangesEnabled;
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            try
            {
                for (var i = 0; i < albums.Count; i += BatchSize)
                {
                    var batchEnd = Math.Min(i + BatchSize, albums.Count);
                    context.Albums.AddRange(albums.GetRange(i, batchEnd - i));
                    context.SaveChanges();
                    context.ChangeTracker.Clear();
                }
            }
            finally
            {
                context.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
            }

            logger.Debug("DecentDbStreamingImporter: Materialized {Count} albums", albums.Count);
            progressCallback?.Invoke("Materializing Albums", 1, 1, $"Materialized {albums.Count:N0} albums");
        }
    }

    private static DateTime? SafeDate(int year, int month, int day)
    {
        year = Math.Clamp(year, 1, 9999);
        month = Math.Clamp(month, 1, 12);
        day = Math.Clamp(day, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    #endregion

    #region Phase 7: Drop Album Staging Tables

    private async Task DropAlbumStagingTablesAsync(
        MusicBrainzDbContext context,
        ImportProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Cleanup", 0, 1, "Dropping album staging tables...");

        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistCreditStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ArtistCreditNameStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseCountryStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseGroupStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseGroupMetaStaging""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync(@"DELETE FROM ""ReleaseStaging""", cancellationToken);

        progressCallback?.Invoke("Cleanup", 1, 1, "Album staging tables cleared");
    }

    #endregion

    #region Helper Methods

    private int StreamFileToStaging<T>(
        MusicBrainzDbContext context,
        string filePath,
        Func<ReadOnlySpan<char>, T?> entityFactory,
        CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(filePath))
        {
            logger.Warning("DecentDbStreamingImporter: File not found: {FilePath}", filePath);
            return 0;
        }

        var totalCount = 0;
        var batch = new List<T>(BatchSize);
        var previousAutoDetect = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var entity = entityFactory(line.AsSpan());
                    if (entity is not null)
                    {
                        batch.Add(entity);
                        totalCount++;

                        if (batch.Count >= BatchSize)
                        {
                            context.Set<T>().AddRange(batch);
                            context.SaveChanges();
                            context.ChangeTracker.Clear();
                            batch.Clear();
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.Warning("DecentDbStreamingImporter: Skipped malformed line in {File}: {Error}",
                        Path.GetFileName(filePath), ex.Message);
                }
            }

            if (batch.Count > 0)
            {
                context.Set<T>().AddRange(batch);
                context.SaveChanges();
                context.ChangeTracker.Clear();
            }
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
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
