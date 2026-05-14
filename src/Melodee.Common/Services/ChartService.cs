using System.Text;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using SmartFormat;

namespace Melodee.Common.Services;

public sealed class ChartService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    LibraryService libraryService,
    IImageProcessor imageProcessor)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailBySlugTemplate = "urn:chart:slug:{0}";
    private const string CacheKeyDetailTemplate = "urn:chart:{0}";

    public async Task<OperationResult<Chart?>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyDetailTemplate.FormatSmart(id);
        var chart = await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext.Charts
                .Include(x => x.Items.OrderBy(i => i.Rank))
                .ThenInclude(i => i.LinkedArtist)
                .Include(x => x.Items)
                .ThenInclude(i => i.LinkedAlbum)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken, DefaultCacheDuration, Chart.CacheRegion).ConfigureAwait(false);

        return new OperationResult<Chart?> { Data = chart };
    }

    public async Task<OperationResult<Chart?>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyDetailBySlugTemplate.FormatSmart(slug.ToUpperInvariant());
        var chart = await CacheManager.GetAsync(cacheKey, async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await scopedContext.Charts
                .Include(x => x.Items.OrderBy(i => i.Rank))
                .ThenInclude(i => i.LinkedArtist)
                .Include(x => x.Items)
                .ThenInclude(i => i.LinkedAlbum)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken, DefaultCacheDuration, Chart.CacheRegion).ConfigureAwait(false);

        return new OperationResult<Chart?> { Data = chart };
    }

    public async Task<PagedResult<Chart>> ListAsync(
        PagedRequest pagedRequest,
        bool includeHidden = false,
        string[]? filterByTags = null,
        int? filterByYear = null,
        string? filterBySource = null,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = scopedContext.Charts
            .Include(c => c.Items)
                .ThenInclude(i => i.LinkedAlbum)
            .Include(c => c.Items)
                .ThenInclude(i => i.LinkedArtist)
            .AsNoTracking();

        if (!includeHidden)
        {
            query = query.Where(x => x.IsVisible);
        }

        if (filterByYear.HasValue)
        {
            query = query.Where(x => x.Year == filterByYear.Value);
        }

        if (!string.IsNullOrWhiteSpace(filterBySource))
        {
            query = query.Where(x => x.SourceName != null && x.SourceName.ToLower() == filterBySource.ToLower());
        }

        if (filterByTags is { Length: > 0 })
        {
            foreach (var tag in filterByTags)
            {
                var lowerTag = tag.ToLowerInvariant();
                query = query.Where(x => x.Tags != null && x.Tags.ToLower().Contains(lowerTag));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var charts = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pagedRequest.PageValue - 1) * pagedRequest.PageSizeValue)
            .Take(pagedRequest.PageSizeValue)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<Chart>
        {
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pagedRequest.PageSizeValue),
            CurrentPage = pagedRequest.PageValue,
            Data = charts
        };
    }

    public async Task<OperationResult<string>> GenerateSlugPreviewAsync(
        string title,
        int? excludeChartId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new OperationResult<string>
            {
                Data = string.Empty
            };
        }

        var slug = await GenerateUniqueSlugAsync(title, excludeChartId, cancellationToken).ConfigureAwait(false);
        return new OperationResult<string>
        {
            Data = slug
        };
    }

    public async Task<OperationResult<Chart?>> CreateAsync(
        string title,
        string? sourceName = null,
        string? sourceUrl = null,
        int? year = null,
        string? description = null,
        string[]? tags = null,
        bool isVisible = false,
        bool isGeneratedPlaylistEnabled = false,
        CancellationToken cancellationToken = default)
    {
        var slug = await GenerateUniqueSlugAsync(title, null, cancellationToken).ConfigureAwait(false);

        var chart = new Chart
        {
            Slug = slug,
            Title = title,
            SourceName = sourceName,
            SourceUrl = sourceUrl,
            Year = year,
            Description = description,
            Tags = tags?.Length > 0 ? string.Join(StringExtensions.TagsSeparator, tags) : null,
            IsVisible = isVisible,
            IsGeneratedPlaylistEnabled = isGeneratedPlaylistEnabled,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        scopedContext.Charts.Add(chart);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information("Created chart [{ChartId}] with slug [{Slug}]", chart.Id, chart.Slug);

        return new OperationResult<Chart?> { Data = chart };
    }

    public async Task<OperationResult<bool>> UpdateAsync(
        int chartId,
        string title,
        string? sourceName = null,
        string? sourceUrl = null,
        int? year = null,
        string? description = null,
        string[]? tags = null,
        bool? isVisible = null,
        bool? isGeneratedPlaylistEnabled = null,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var chart = await scopedContext.Charts.FindAsync([chartId], cancellationToken).ConfigureAwait(false);
        if (chart == null)
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        if (chart.Title != title)
        {
            chart.Slug = await GenerateUniqueSlugAsync(title, chartId, cancellationToken).ConfigureAwait(false);
        }

        chart.Title = title;
        chart.SourceName = sourceName;
        chart.SourceUrl = sourceUrl;
        chart.Year = year;
        chart.Description = description;
        chart.Tags = tags?.Length > 0 ? string.Join(StringExtensions.TagsSeparator, tags) : null;
        if (isVisible.HasValue)
        {
            chart.IsVisible = isVisible.Value;
        }

        if (isGeneratedPlaylistEnabled.HasValue)
        {
            chart.IsGeneratedPlaylistEnabled = isGeneratedPlaylistEnabled.Value;
        }

        chart.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        ClearCache(chart);

        Logger.Information("Updated chart [{ChartId}]", chartId);

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> DeleteAsync(int chartId, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var chart = await scopedContext.Charts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(x => x.Id == chartId, cancellationToken)
            .ConfigureAwait(false);

        if (chart == null)
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        scopedContext.ChartItems.RemoveRange(chart.Items);
        scopedContext.Charts.Remove(chart);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        ClearCache(chart);

        Logger.Information("Deleted chart [{ChartId}]", chartId);

        return new OperationResult<bool> { Data = true };
    }

    public Task<OperationResult<ChartCsvPreviewResult>> ParseCsvAsync(
        string csvContent,
        CancellationToken cancellationToken = default)
    {
        var result = new ChartCsvPreviewResult();

        if (string.IsNullOrWhiteSpace(csvContent))
        {
            result.Errors.Add(new ChartCsvError(0, "CSV content is empty"));
            return Task.FromResult(new OperationResult<ChartCsvPreviewResult>
            {
                Data = result,
                Type = OperationResponseType.ValidationFailure
            });
        }

        var lines = csvContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var seenRanks = new HashSet<int>();
        var rowNumber = 0;

        foreach (var line in lines)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseCsvLine(line);
            if (columns.Length < 3)
            {
                result.Errors.Add(new ChartCsvError(rowNumber, "Row must have at least 3 columns: Rank, ArtistName, AlbumTitle"));
                continue;
            }

            if (!int.TryParse(columns[0].Trim(), out var rank) || rank < 1)
            {
                result.Errors.Add(new ChartCsvError(rowNumber, $"Invalid rank value: '{columns[0]}' (must be a positive integer)"));
                continue;
            }

            if (seenRanks.Contains(rank))
            {
                result.Errors.Add(new ChartCsvError(rowNumber, $"Duplicate rank: {rank}"));
                continue;
            }

            seenRanks.Add(rank);

            var artistName = columns[1].Trim();
            var albumTitle = columns[2].Trim();

            if (string.IsNullOrWhiteSpace(artistName))
            {
                result.Errors.Add(new ChartCsvError(rowNumber, "Artist name is required"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(albumTitle))
            {
                result.Errors.Add(new ChartCsvError(rowNumber, "Album title is required"));
                continue;
            }

            int? releaseYear = null;
            if (columns.Length > 3 && !string.IsNullOrWhiteSpace(columns[3]))
            {
                if (int.TryParse(columns[3].Trim(), out var year))
                {
                    releaseYear = year;
                }
            }

            result.Items.Add(new ChartCsvPreviewItem
            {
                RowNumber = rowNumber,
                Rank = rank,
                ArtistName = artistName,
                AlbumTitle = albumTitle,
                ReleaseYear = releaseYear
            });
        }

        return Task.FromResult(new OperationResult<ChartCsvPreviewResult> { Data = result });
    }

    public async Task<OperationResult<bool>> SaveItemsAsync(
        int chartId,
        IEnumerable<ChartCsvPreviewItem> items,
        bool doAutoLink = true,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await scopedContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var chart = await scopedContext.Charts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(x => x.Id == chartId, cancellationToken)
                .ConfigureAwait(false);

            if (chart == null)
            {
                return new OperationResult<bool>
                {
                    Data = false,
                    Type = OperationResponseType.NotFound
                };
            }

            scopedContext.ChartItems.RemoveRange(chart.Items);

            var now = SystemClock.Instance.GetCurrentInstant();
            var newItems = items.Select(item => new ChartItem
            {
                ChartId = chartId,
                Rank = item.Rank,
                ArtistName = item.ArtistName,
                AlbumTitle = item.AlbumTitle,
                ReleaseYear = item.ReleaseYear,
                LinkStatus = (short)ChartItemLinkStatus.Unlinked,
                CreatedAt = now
            }).ToList();

            await scopedContext.ChartItems.AddRangeAsync(newItems, cancellationToken).ConfigureAwait(false);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (doAutoLink)
            {
                await LinkItemsInternalAsync(scopedContext, chartId, false, cancellationToken).ConfigureAwait(false);
            }

            chart.LastUpdatedAt = now;
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            ClearCache(chart);

            Logger.Information("Saved {ItemCount} items to chart [{ChartId}]", newItems.Count, chartId);

            return new OperationResult<bool> { Data = true };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            Logger.Error(ex, "Failed to save items to chart [{ChartId}]", chartId);
            throw;
        }
    }

    public async Task<OperationResult<ChartLinkingResult>> LinkItemsAsync(
        int chartId,
        bool overwriteManualLinks = false,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var result = await LinkItemsInternalAsync(scopedContext, chartId, overwriteManualLinks, cancellationToken).ConfigureAwait(false);

        var chart = await scopedContext.Charts.FindAsync([chartId], cancellationToken).ConfigureAwait(false);
        if (chart != null)
        {
            ClearCache(chart);
        }

        return result;
    }

    private async Task<OperationResult<ChartLinkingResult>> LinkItemsInternalAsync(
        MelodeeDbContext context,
        int chartId,
        bool overwriteManualLinks,
        CancellationToken cancellationToken)
    {
        var result = new ChartLinkingResult();

        var items = await context.ChartItems
            .Where(x => x.ChartId == chartId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            return new OperationResult<ChartLinkingResult> { Data = result };
        }

        var artistNames = items.Select(i => i.ArtistName.ToNormalizedString() ?? string.Empty).Distinct().ToArray();
        var artists = await context.Artists
            .Where(a => artistNames.Contains(a.NameNormalized))
            .AsNoTracking()
            .ToDictionaryAsync(a => a.NameNormalized, cancellationToken)
            .ConfigureAwait(false);

        var albums = await context.Albums
            .Include(a => a.Artist)
            .Where(a => artistNames.Contains(a.Artist.NameNormalized))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var albumLookup = albums
            .GroupBy(a => a.Artist.NameNormalized)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = SystemClock.Instance.GetCurrentInstant();

        foreach (var item in items)
        {
            if (!overwriteManualLinks && item.LinkStatusValue == ChartItemLinkStatus.Linked && item.LinkedAlbumId.HasValue)
            {
                result.SkippedCount++;
                continue;
            }

            var normalizedArtist = item.ArtistName.ToNormalizedString() ?? string.Empty;
            var normalizedAlbum = item.AlbumTitle.ToNormalizedString() ?? string.Empty;

            if (!artists.TryGetValue(normalizedArtist, out var matchedArtist))
            {
                item.LinkedArtistId = null;
                item.LinkedAlbumId = null;
                item.LinkStatus = (short)ChartItemLinkStatus.Unlinked;
                item.LinkConfidence = null;
                item.LastUpdatedAt = now;
                result.UnlinkedCount++;
                continue;
            }

            item.LinkedArtistId = matchedArtist.Id;

            if (!albumLookup.TryGetValue(normalizedArtist, out var artistAlbums))
            {
                item.LinkedAlbumId = null;
                item.LinkStatus = (short)ChartItemLinkStatus.Unlinked;
                item.LinkConfidence = 0.5m;
                item.LinkNotes = "Artist found but no albums";
                item.LastUpdatedAt = now;
                result.UnlinkedCount++;
                continue;
            }

            var exactMatches = artistAlbums
                .Where(a => a.NameNormalized == normalizedAlbum)
                .ToList();

            if (exactMatches.Count == 1)
            {
                var album = exactMatches[0];
                item.LinkedAlbumId = album.Id;
                item.LinkedArtistId = album.ArtistId;
                item.LinkStatus = (short)ChartItemLinkStatus.Linked;
                item.LinkConfidence = 1.0m;
                item.LastUpdatedAt = now;
                result.LinkedCount++;
            }
            else if (exactMatches.Count > 1)
            {
                item.LinkedAlbumId = null;
                item.LinkStatus = (short)ChartItemLinkStatus.Ambiguous;
                item.LinkConfidence = 0.8m;
                item.LinkNotes = $"Multiple exact matches found: {exactMatches.Count}";
                item.LastUpdatedAt = now;
                result.AmbiguousCount++;
            }
            else
            {
                var fuzzyMatches = artistAlbums
                    .Where(a => a.NameNormalized.Contains(normalizedAlbum) || normalizedAlbum.Contains(a.NameNormalized))
                    .ToList();

                if (fuzzyMatches.Count == 1)
                {
                    var album = fuzzyMatches[0];
                    item.LinkedAlbumId = album.Id;
                    item.LinkedArtistId = album.ArtistId;
                    item.LinkStatus = (short)ChartItemLinkStatus.Linked;
                    item.LinkConfidence = 0.7m;
                    item.LinkNotes = "Fuzzy match";
                    item.LastUpdatedAt = now;
                    result.LinkedCount++;
                }
                else if (fuzzyMatches.Count > 1)
                {
                    item.LinkedAlbumId = null;
                    item.LinkStatus = (short)ChartItemLinkStatus.Ambiguous;
                    item.LinkConfidence = 0.5m;
                    item.LinkNotes = $"Multiple fuzzy matches: {fuzzyMatches.Count}";
                    item.LastUpdatedAt = now;
                    result.AmbiguousCount++;
                }
                else
                {
                    item.LinkedAlbumId = null;
                    item.LinkStatus = (short)ChartItemLinkStatus.Unlinked;
                    item.LinkConfidence = 0.5m;
                    item.LinkNotes = "Artist found but album not matched";
                    item.LastUpdatedAt = now;
                    result.UnlinkedCount++;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information(
            "Linking completed for chart [{ChartId}]: Linked={Linked}, Ambiguous={Ambiguous}, Unlinked={Unlinked}, Skipped={Skipped}",
            chartId, result.LinkedCount, result.AmbiguousCount, result.UnlinkedCount, result.SkippedCount);

        return new OperationResult<ChartLinkingResult> { Data = result };
    }

    public async Task<OperationResult<bool>> ResolveItemAsync(
        int chartItemId,
        int albumId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var item = await scopedContext.ChartItems
            .Include(i => i.Chart)
            .FirstOrDefaultAsync(i => i.Id == chartItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item == null)
        {
            return new OperationResult<bool>(["Chart item not found"])
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        var album = await scopedContext.Albums
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album == null)
        {
            return new OperationResult<bool>(["Album not found"])
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        item.LinkedAlbumId = album.Id;
        item.LinkedArtistId = album.ArtistId;
        item.LinkStatus = (short)ChartItemLinkStatus.Linked;
        item.LinkConfidence = 1.0m;
        item.LinkNotes = "Manually resolved";
        item.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        ClearCache(item.Chart);

        Logger.Information("Manually resolved chart item [{ItemId}] to album [{AlbumId}]", chartItemId, albumId);

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<bool>> IgnoreItemAsync(
        int chartItemId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var item = await scopedContext.ChartItems
            .Include(i => i.Chart)
            .FirstOrDefaultAsync(i => i.Id == chartItemId, cancellationToken)
            .ConfigureAwait(false);

        if (item == null)
        {
            return new OperationResult<bool>
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        item.LinkedAlbumId = null;
        item.LinkedArtistId = null;
        item.LinkStatus = (short)ChartItemLinkStatus.Ignored;
        item.LinkNotes = "Marked as ignored";
        item.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        ClearCache(item.Chart);

        return new OperationResult<bool> { Data = true };
    }

    public async Task<OperationResult<IEnumerable<ChartPlaylistTrack>>> GetGeneratedPlaylistTracksAsync(
        int chartId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var chart = await scopedContext.Charts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == chartId, cancellationToken)
            .ConfigureAwait(false);

        if (chart == null)
        {
            return new OperationResult<IEnumerable<ChartPlaylistTrack>>
            {
                Data = [],
                Type = OperationResponseType.NotFound
            };
        }

        if (!chart.IsGeneratedPlaylistEnabled)
        {
            return new OperationResult<IEnumerable<ChartPlaylistTrack>>(["Generated playlist is not enabled for this chart"])
            {
                Data = []
            };
        }

        var items = await scopedContext.ChartItems
            .Where(i => i.ChartId == chartId && i.LinkedAlbumId != null)
            .OrderBy(i => i.Rank)
            .Select(i => new { i.Rank, i.LinkedAlbumId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            return new OperationResult<IEnumerable<ChartPlaylistTrack>> { Data = [] };
        }

        var albumIds = items.Select(i => i.LinkedAlbumId!.Value).Distinct().ToList();

        var songs = await scopedContext.Songs
            .Where(s => albumIds.Contains(s.AlbumId))
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var songsByAlbum = songs.GroupBy(s => s.AlbumId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.SongNumber).ThenBy(s => s.Id).ToList());

        var tracks = new List<ChartPlaylistTrack>();
        foreach (var item in items)
        {
            if (item.LinkedAlbumId.HasValue && songsByAlbum.TryGetValue(item.LinkedAlbumId.Value, out var albumSongs))
            {
                foreach (var song in albumSongs)
                {
                    tracks.Add(new ChartPlaylistTrack
                    {
                        ChartRank = item.Rank,
                        SongId = song.Id,
                        SongApiKey = song.ApiKey,
                        SongTitle = song.Title,
                        AlbumId = song.AlbumId,
                        AlbumApiKey = song.Album.ApiKey,
                        AlbumName = song.Album.Name,
                        ArtistId = song.Album.ArtistId,
                        ArtistApiKey = song.Album.Artist.ApiKey,
                        ArtistName = song.Album.Artist.Name,
                        SongNumber = song.SongNumber,
                        Duration = song.Duration
                    });
                }
            }
        }

        return new OperationResult<IEnumerable<ChartPlaylistTrack>> { Data = tracks };
    }

    public async Task<OperationResult<IEnumerable<ChartAlbumSearchResult>>> SearchAlbumsAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new OperationResult<IEnumerable<ChartAlbumSearchResult>> { Data = [] };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var normalizedQuery = query.ToNormalizedString() ?? string.Empty;

        var albums = await scopedContext.Albums
            .Include(a => a.Artist)
            .Where(a => a.NameNormalized.Contains(normalizedQuery) || a.Artist.NameNormalized.Contains(normalizedQuery))
            .OrderBy(a => a.Artist.Name)
            .ThenBy(a => a.Name)
            .Take(limit)
            .AsNoTracking()
            .Select(a => new ChartAlbumSearchResult
            {
                AlbumId = a.Id,
                AlbumApiKey = a.ApiKey,
                AlbumName = a.Name,
                ArtistId = a.ArtistId,
                ArtistApiKey = a.Artist.ApiKey,
                ArtistName = a.Artist.Name,
                ReleaseYear = a.ReleaseDate.Year
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<IEnumerable<ChartAlbumSearchResult>> { Data = albums };
    }

    /// <summary>
    /// Identifies a missing album by linking all chart items with matching artist/album name
    /// and adding the chart album title as an alternate name to the album.
    /// </summary>
    public async Task<OperationResult<IdentifyMissingAlbumResult>> IdentifyMissingAlbumAsync(
        string artistName,
        string albumTitle,
        int albumId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
        {
            return new OperationResult<IdentifyMissingAlbumResult>(["Artist name and album title are required"])
            {
                Data = new IdentifyMissingAlbumResult(),
                Type = OperationResponseType.ValidationFailure
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var album = await scopedContext.Albums
            .Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album == null)
        {
            return new OperationResult<IdentifyMissingAlbumResult>(["Album not found"])
            {
                Data = new IdentifyMissingAlbumResult(),
                Type = OperationResponseType.NotFound
            };
        }

        var normalizedArtistName = artistName.ToUpperInvariant();
        var normalizedAlbumTitle = albumTitle.ToUpperInvariant();
        var linkedStatus = (short)ChartItemLinkStatus.Linked;

        var matchingItems = await scopedContext.ChartItems
            .Include(i => i.Chart)
            .Where(i => i.ArtistName.ToUpper() == normalizedArtistName &&
                        i.AlbumTitle.ToUpper() == normalizedAlbumTitle &&
                        i.LinkStatus != linkedStatus)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (matchingItems.Count == 0)
        {
            return new OperationResult<IdentifyMissingAlbumResult>(["No unlinked chart items found for the specified artist/album"])
            {
                Data = new IdentifyMissingAlbumResult(),
                Type = OperationResponseType.NotFound
            };
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var chartsUpdated = new HashSet<int>();

        foreach (var item in matchingItems)
        {
            item.LinkedAlbumId = album.Id;
            item.LinkedArtistId = album.ArtistId;
            item.LinkStatus = (short)ChartItemLinkStatus.Linked;
            item.LinkConfidence = 1.0m;
            item.LinkNotes = "Identified from missing report";
            item.LastUpdatedAt = now;
            chartsUpdated.Add(item.ChartId);
        }

        var normalizedAlternateNameToAdd = albumTitle.ToNormalizedString();
        if (!string.IsNullOrEmpty(normalizedAlternateNameToAdd))
        {
            album.AlternateNames = album.AlternateNames.AddTags([normalizedAlternateNameToAdd], doNormalize: true);
            album.LastUpdatedAt = now;
        }

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var chartId in chartsUpdated)
        {
            var chart = matchingItems.First(i => i.ChartId == chartId).Chart;
            ClearCache(chart);
        }

        Logger.Information(
            "Identified missing album [{ArtistName} - {AlbumTitle}] as [{AlbumId}], linked {ItemCount} chart items across {ChartCount} charts",
            artistName, albumTitle, albumId, matchingItems.Count, chartsUpdated.Count);

        return new OperationResult<IdentifyMissingAlbumResult>
        {
            Data = new IdentifyMissingAlbumResult
            {
                LinkedItemCount = matchingItems.Count,
                ChartsUpdatedCount = chartsUpdated.Count,
                AlbumId = album.Id,
                AlbumName = album.Name,
                ArtistName = album.Artist.Name
            }
        };
    }

    private async Task<string> GenerateUniqueSlugAsync(string title, int? excludeChartId, CancellationToken cancellationToken)
    {
        var baseSlug = title.ToSlug();
        var slug = baseSlug;
        var suffix = 1;

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var query = scopedContext.Charts.Where(c => c.Slug == slug);
            if (excludeChartId.HasValue)
            {
                query = query.Where(c => c.Id != excludeChartId.Value);
            }

            var exists = await query.AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!exists)
            {
                return slug;
            }

            suffix++;
            slug = $"{baseSlug}-{suffix}";
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    private void ClearCache(Chart chart)
    {
        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(chart.Id), Chart.CacheRegion);
        CacheManager.Remove(CacheKeyDetailBySlugTemplate.FormatSmart(chart.Slug.ToUpperInvariant()), Chart.CacheRegion);
    }

    public async Task<ImageBytesAndEtag> GetChartImageBytesAndEtagAsync(
        Guid chartApiKey,
        string? size,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var chart = await scopedContext.Charts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == chartApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (chart == null)
        {
            return new ImageBytesAndEtag(null, null);
        }

        var chartLibrary = await libraryService.GetChartLibraryAsync(cancellationToken).ConfigureAwait(false);
        if (!chartLibrary.IsSuccess || chartLibrary.Data == null)
        {
            return new ImageBytesAndEtag(null, null);
        }

        var chartImageFilename = chart.ToImageFileName(chartLibrary.Data.Path);
        var chartImageFileInfo = new FileInfo(chartImageFilename);

        if (chartImageFileInfo.Exists)
        {
            var imageBytes = await File.ReadAllBytesAsync(chartImageFileInfo.FullName, cancellationToken).ConfigureAwait(false);
            var etag = chartImageFileInfo.LastWriteTimeUtc.ToEtag();
            return new ImageBytesAndEtag(imageBytes, etag);
        }

        return new ImageBytesAndEtag(null, null);
    }

    public async Task<OperationResult<bool>> UploadChartImageAsync(
        Guid chartApiKey,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var chart = await scopedContext.Charts
            .FirstOrDefaultAsync(x => x.ApiKey == chartApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (chart == null)
        {
            return new OperationResult<bool>("Chart not found.")
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        var chartLibrary = await libraryService.GetChartLibraryAsync(cancellationToken).ConfigureAwait(false);
        if (!chartLibrary.IsSuccess || chartLibrary.Data == null)
        {
            return new OperationResult<bool>("Chart library not found.")
            {
                Data = false
            };
        }

        var imagesDirectory = Path.Combine(chartLibrary.Data.Path, Chart.ImagesDirectoryName);
        if (!Directory.Exists(imagesDirectory))
        {
            Directory.CreateDirectory(imagesDirectory);
        }

        var chartImageFilename = chart.ToImageFileName(chartLibrary.Data.Path);

        var gifImageBytes = await imageProcessor.ConvertToGifAsync(imageBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(chartImageFilename, gifImageBytes, cancellationToken).ConfigureAwait(false);

        chart.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ClearCache(chart);

        Logger.Information("Uploaded image for chart [{ChartTitle}]", chart.Title);

        return new OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<OperationResult<bool>> DeleteChartImageAsync(
        Guid chartApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var chart = await scopedContext.Charts
            .FirstOrDefaultAsync(x => x.ApiKey == chartApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (chart == null)
        {
            return new OperationResult<bool>("Chart not found.")
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        var chartLibrary = await libraryService.GetChartLibraryAsync(cancellationToken).ConfigureAwait(false);
        if (!chartLibrary.IsSuccess || chartLibrary.Data == null)
        {
            return new OperationResult<bool>("Chart library not found.")
            {
                Data = false
            };
        }

        var chartImageFilename = chart.ToImageFileName(chartLibrary.Data.Path);

        if (File.Exists(chartImageFilename))
        {
            File.Delete(chartImageFilename);
            chart.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            ClearCache(chart);
            Logger.Information("Deleted image for chart [{ChartTitle}]", chart.Title);
        }

        return new OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<OperationResult<Chart?>> GetByApiKeyAsync(Guid apiKey, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var chart = await scopedContext.Charts
            .Include(x => x.Items.OrderBy(i => i.Rank))
            .ThenInclude(i => i.LinkedArtist)
            .Include(x => x.Items)
            .ThenInclude(i => i.LinkedAlbum)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<Chart?> { Data = chart };
    }
}

public sealed record ChartCsvPreviewResult
{
    public List<ChartCsvPreviewItem> Items { get; init; } = [];
    public List<ChartCsvError> Errors { get; init; } = [];
    public bool HasErrors => Errors.Count > 0;
}

public sealed record ChartCsvPreviewItem
{
    public int RowNumber { get; init; }
    public int Rank { get; init; }
    public required string ArtistName { get; init; }
    public required string AlbumTitle { get; init; }
    public int? ReleaseYear { get; init; }
}

public sealed record ChartCsvError(int RowNumber, string Message);

public sealed record ChartLinkingResult
{
    public int LinkedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int UnlinkedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed record ChartPlaylistTrack
{
    public int ChartRank { get; init; }
    public int SongId { get; init; }
    public Guid SongApiKey { get; init; }
    public required string SongTitle { get; init; }
    public int AlbumId { get; init; }
    public Guid AlbumApiKey { get; init; }
    public required string AlbumName { get; init; }
    public int ArtistId { get; init; }
    public Guid ArtistApiKey { get; init; }
    public required string ArtistName { get; init; }
    public int SongNumber { get; init; }
    public double Duration { get; init; }
}

public sealed record ChartAlbumSearchResult
{
    public int AlbumId { get; init; }
    public Guid AlbumApiKey { get; init; }
    public required string AlbumName { get; init; }
    public int ArtistId { get; init; }
    public Guid ArtistApiKey { get; init; }
    public required string ArtistName { get; init; }
    public int ReleaseYear { get; init; }
}

public sealed record IdentifyMissingAlbumResult
{
    public int LinkedItemCount { get; init; }
    public int ChartsUpdatedCount { get; init; }
    public int AlbumId { get; init; }
    public string AlbumName { get; init; } = string.Empty;
    public string ArtistName { get; init; } = string.Empty;
}
