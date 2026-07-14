using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services.Scanning;

/// <summary>
///     Service that returns Albums found from scanning media.
/// </summary>
public sealed class AlbumDiscoveryService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    IFileSystemService fileSystemService,
    TimeSpan? directoryCacheEntryMaxAge = null,
    int? directoryCacheCapacity = null)
    : ServiceBase(logger, cacheManager, contextFactory), IDisposable
{
    private static readonly ParallelOptions DefaultParallelOptions = new()
    {
        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
    };

    private readonly SemaphoreSlim _cacheUpdateSemaphore = new(1, 1);

    // Performance optimizations
    // The tuple stores the directory's actual LastWriteTimeUtc (captured at cache population) so a
    // cache hit can be invalidated immediately when the directory changes externally, rather than
    // waiting for the TTL to expire and serving stale albums.
    private readonly ConcurrentDictionary<string, (DateTime DirectoryLastWriteTimeUtc, Album[] Albums)> _directoryCache = new();
    private readonly TimeSpan _directoryCacheEntryMaxAge = directoryCacheEntryMaxAge ?? TimeSpan.FromSeconds(30);
    private readonly int _directoryCacheCapacity = directoryCacheCapacity is > 0 ? directoryCacheCapacity.Value : 1000;
    private long _directoryCacheHits;
    private long _directoryCacheMisses;
    private IAlbumValidator _albumValidator = null!;
    private IMelodeeConfiguration _configuration = new MelodeeConfiguration([]);
    private bool _initialized;

    public void Dispose()
    {
        _cacheUpdateSemaphore.Dispose();
        _directoryCache.Clear();
    }

    public async Task InitializeAsync(IMelodeeConfiguration? configuration = null, CancellationToken token = default)
    {
        _configuration = configuration ?? await configurationFactory.GetConfigurationAsync(token).ConfigureAwait(false);
        _albumValidator = new AlbumValidator(_configuration);
        _initialized = true;
    }

    private void CheckInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Album discovery service is not initialized.");
        }
    }

    public async Task<Album?> AlbumByDbIdAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        int albumId,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();
        var result =
            (await AllMelodeeAlbumDataFilesForDirectoryAsync(fileSystemDirectoryInfo, cancellationToken)).Data
            ?.FirstOrDefault(x => x.AlbumDbId == albumId);

        return result;
    }

    public async Task<Album> AlbumByUniqueIdAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();
        var result =
            (await AllMelodeeAlbumDataFilesForDirectoryAsync(fileSystemDirectoryInfo, cancellationToken)).Data
            ?.FirstOrDefault(x => x.Id == id);
        if (result == null)
        {
            Log.Error("Unable to find Album by id [{Id}] in [{DirectoryName}]", id, fileSystemDirectoryInfo.FullName());
            return new Album
            {
                Artist = new Artist(string.Empty, string.Empty, null),
                Directory = fileSystemDirectoryInfo,
                ViaPlugins = [],
                OriginalDirectory = fileSystemDirectoryInfo
            };
        }

        return result;
    }

    private async Task<PagedResult<Album>> AlbumsForDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        // Early cancellation check
        if (cancellationToken.IsCancellationRequested)
        {
            return new PagedResult<Album>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        // Use HashSet for O(1) duplicate detection instead of O(n) All() checks
        var albumIds = new HashSet<Guid>();
        var albums = new List<Album>();

        var dataForDirectoryInfoResult =
            await AllMelodeeAlbumDataFilesForDirectoryAsync(fileSystemDirectoryInfo, cancellationToken);
        if (dataForDirectoryInfoResult is { IsSuccess: true, Data: not null })
        {
            foreach (var album in dataForDirectoryInfoResult.Data)
            {
                if (albumIds.Add(album.Id))
                {
                    albums.Add(album);
                }
            }
        }

        // Check cancellation before starting parallel operations
        if (cancellationToken.IsCancellationRequested)
        {
            return new PagedResult<Album>
            {
                TotalCount = albums.Count,
                TotalPages = 1,
                Data = albums.Skip(pagedRequest.SkipValue).Take(pagedRequest.PageSizeValue)
            };
        }

        // Use parallel processing for directory enumeration with controlled concurrency
        var childDirectories = fileSystemService.EnumerateDirectories(fileSystemDirectoryInfo.Path, "*.*", SearchOption.AllDirectories);
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 4) // Conservative for I/O operations
        };

        var additionalAlbums = new ConcurrentBag<Album>();

        try
        {
            await Parallel.ForEachAsync(childDirectories, parallelOptions, async (childDir, token) =>
            {
                // Check cancellation at start of each iteration
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var dataForChildDirResult = await AllMelodeeAlbumDataFilesForDirectoryAsync(new FileSystemDirectoryInfo
                {
                    Path = childDir.FullName,
                    Name = childDir.Name
                }, token);

                if (dataForChildDirResult is { IsSuccess: true, Data: not null })
                {
                    foreach (var album in dataForChildDirResult.Data)
                    {
                        additionalAlbums.Add(album);
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Handle cancellation gracefully - just continue with what we have
        }

        // Merge results with efficient duplicate detection
        foreach (var album in additionalAlbums)
        {
            if (albumIds.Add(album.Id))
            {
                albums.Add(album);
            }
        }

        // Apply filters early to reduce memory pressure
        albums = ApplyFilters(albums, pagedRequest);

        // Apply sorting with optimized comparisons
        albums = ApplySorting(albums, pagedRequest);

        var albumsCount = albums.Count;
        return new PagedResult<Album>
        {
            TotalCount = albumsCount,
            TotalPages = (albumsCount + pagedRequest.PageSizeValue - 1) / pagedRequest.PageSizeValue,
            Data = albums
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.PageSizeValue)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> ApplyFilters(List<Album> albums, PagedRequest pagedRequest)
    {
        if (pagedRequest.AlbumResultFilter != AlbumResultFilter.All && albums.Count != 0)
        {
            albums = pagedRequest.AlbumResultFilter switch
            {
                AlbumResultFilter.Duplicates => albums
                    .GroupBy(x => x.Id)
                    .Where(x => x.Count() > 1)
                    .SelectMany(x => x)
                    .ToList(),

                AlbumResultFilter.Incomplete or AlbumResultFilter.NeedsAttention =>
                    albums.Where(x => x.Status == AlbumStatus.Invalid).ToList(),

                AlbumResultFilter.LessThanConfiguredSongs => FilterByMinSongs(albums),

                AlbumResultFilter.New => albums.Where(x => x.Status == AlbumStatus.New).ToList(),

                AlbumResultFilter.ReadyToMove => albums.Where(x => x.Status is AlbumStatus.Ok).ToList(),

                AlbumResultFilter.Selected when pagedRequest.SelectedAlbumIds.Length > 0 =>
                    FilterBySelectedIds(albums, pagedRequest.SelectedAlbumIds),

                AlbumResultFilter.LessThanConfiguredDuration => FilterByMinDuration(albums),

                _ => albums
            };
        }

        if (pagedRequest.FilterBy != null)
        {
            foreach (var filterBy in pagedRequest.FilterBy)
            {
                albums = ApplyPropertyFilter(albums, filterBy);
            }
        }

        return albums;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> FilterByMinSongs(List<Album> albums)
    {
        var filterLessThanSongs = SafeParser.ToNumber<int>(
            _configuration.Configuration[SettingRegistry.FilteringLessThanSongCount]);
        return albums.Where(x => x.Songs?.Count() < filterLessThanSongs || x.SongTotalValue() < filterLessThanSongs).ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> FilterBySelectedIds(List<Album> albums, Guid[] selectedIds)
    {
        var selectedSet = new HashSet<Guid>(selectedIds);
        return albums.Where(x => selectedSet.Contains(x.Id)).ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> FilterByMinDuration(List<Album> albums)
    {
        var filterLessDuration = SafeParser.ToNumber<int>(
            _configuration.Configuration[SettingRegistry.FilteringLessThanDuration]);
        return albums.Where(x => x.TotalDuration() < filterLessDuration).ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> ApplyPropertyFilter(List<Album> albums, FilterOperatorInfo filterBy)
    {
        return filterBy.PropertyName switch
        {
            "ArtistName" => albums.Where(x =>
                x.Artist.NameNormalized.Contains(filterBy.Value.ToString()?.ToNormalizedString() ?? string.Empty)).ToList(),

            "AlbumStatus" => albums.Where(x =>
                x.Status == SafeParser.ToEnum<AlbumStatus>(filterBy.Value)).ToList(),

            "NeedsAttentionReasons" => albums.Where(x =>
                x.StatusReasons.HasFlag(SafeParser.ToEnum<AlbumNeedsAttentionReasons>(filterBy.Value))).ToList(),

            "NameNormalized" => FilterByNameNormalized(albums, filterBy.Value.ToString() ?? string.Empty),

            "ReleaseDate" => albums.Where(x =>
                x.AlbumYear() == SafeParser.ToNumber<int>(filterBy.Value)).ToList(),

            _ => albums
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> FilterByNameNormalized(List<Album> albums, string filterValue)
    {
        return albums.Where(x =>
            x.AlbumTitle()?.Contains(filterValue, StringComparison.CurrentCultureIgnoreCase) == true ||
            x.Artist.Name.Contains(filterValue, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<Album> ApplySorting(List<Album> albums, PagedRequest pagedRequest)
    {
        return pagedRequest.OrderByValue("SortOrder") switch
        {
            "\"Artist\" ASC" => albums.OrderBy(x => x.Artist.SortName).ToList(),
            "\"Artist\" DESC" => albums.OrderByDescending(x => x.Artist.SortName).ToList(),
            "\"CreatedAt\" ASC" => albums.OrderBy(x => x.Created).ToList(),
            "\"CreatedAt\" DESC" => albums.OrderByDescending(x => x.Created).ToList(),
            "\"Duration\" ASC" => albums.OrderBy(x => x.Duration()).ToList(),
            "\"Duration\" DESC" => albums.OrderByDescending(x => x.Duration()).ToList(),
            "\"NeedsAttentionReasonsValue\" ASC" => albums.OrderBy(x => x.StatusReasons).ToList(),
            "\"NeedsAttentionReasonsValue\" DESC" => albums.OrderByDescending(x => x.StatusReasons).ToList(),
            "\"Title\" ASC" => albums.OrderBy(x => x.AlbumTitle()).ToList(),
            "\"Title\" DESC" => albums.OrderByDescending(x => x.AlbumTitle()).ToList(),
            "\"Year\" ASC" => albums.OrderBy(x => x.AlbumYear()).ToList(),
            "\"Year\" DESC" => albums.OrderByDescending(x => x.AlbumYear()).ToList(),
            "\"Status\" ASC" => albums.OrderBy(x => x.Status).ToList(),
            "\"Status\" DESC" => albums.OrderByDescending(x => x.Status).ToList(),
            "\"SongCount\" ASC" => albums.OrderBy(x => x.SongTotalValue()).ToList(),
            "\"SongCount\" DESC" => albums.OrderByDescending(x => x.SongTotalValue()).ToList(),
            _ => albums
        };
    }

    public async Task<bool> DeleteAlbumsAsync(FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Func<Album, bool> condition, CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        var result = false;
        var albumsForDirectoryInfo = await AlbumsForDirectoryAsync(fileSystemDirectoryInfo,
            new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
        if (albumsForDirectoryInfo.Data.Any())
        {
            foreach (var album in albumsForDirectoryInfo.Data)
            {
                if (!condition(album))
                {
                    continue;
                }

                var directoryName = fileSystemService.GetDirectoryName(album.MelodeeDataFileName ?? string.Empty);
                if (string.IsNullOrEmpty(directoryName))
                {
                    continue;
                }

                fileSystemService.DeleteDirectory(directoryName, true);
                result = true;
            }
        }

        return result;
    }

    public async Task<OperationResult<Dictionary<AlbumNeedsAttentionReasons, int>>> AlbumsCountByStatusAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo, CancellationToken cancellationToken = default)
    {
        CheckInitialized();
        var albumsForDirectoryInfo = await AlbumsForDirectoryAsync(fileSystemDirectoryInfo,
            new PagedRequest { PageSize = short.MaxValue }, cancellationToken);

        return new OperationResult<Dictionary<AlbumNeedsAttentionReasons, int>>
        {
            Data = albumsForDirectoryInfo.Data.GroupBy(x => x.StatusReasons).ToDictionary(x => x.Key, x => x.Count())
        };
    }

    public async Task<PagedResult<AlbumDataInfo>> AlbumsDataInfosForDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();
        var albumsForDirectoryInfo =
            await AlbumsForDirectoryAsync(fileSystemDirectoryInfo, pagedRequest, cancellationToken);
        var data = albumsForDirectoryInfo.Data.ToArray().Select(async x => new AlbumDataInfo(
            0,
            x.Id,
            false,
            x.AlbumTitle() ?? string.Empty,
            x.AlbumTitle().ToNormalizedString() ?? x.AlbumTitle() ?? string.Empty,
            null,
            Guid.Empty,
            x.Artist.Name,
            x.SongTotalValue(),
            x.TotalDuration(),
            Instant.FromDateTimeOffset(x.Created),
            null,
            SafeParser.ToLocalDate(x.AlbumYear() ?? 0),
            SafeParser.ToNumber<short>(_albumValidator.ValidateAlbum(x).Data.AlbumStatus)
        )
        {
            ImageBytes = await x.CoverImageBytesAsync(cancellationToken),
            MelodeeDataFileName = fileSystemService.CombinePath(x.Directory.FullName(), Album.JsonFileName),
            NeedsAttentionReasons = (int)x.StatusReasons
        });

        var d = await Task.WhenAll(data);

        return new PagedResult<AlbumDataInfo>
        {
            TotalCount = albumsForDirectoryInfo.TotalCount,
            TotalPages = albumsForDirectoryInfo.TotalPages,
            Data = d
        };
    }

    public async Task<int> NumberOfOkAlbumsAsync(FileSystemDirectoryInfo fileSystemDirectoryInfo, CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        if (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        var albumsForDirectoryInfo = await AlbumsForDirectoryAsync(
            fileSystemDirectoryInfo,
            new PagedRequest
            {
                PageSize = short.MaxValue
            },
            cancellationToken);

        return albumsForDirectoryInfo.Data.Count(x => x.Status == AlbumStatus.Ok);
    }

    public async Task<OperationResult<IEnumerable<Album>?>> AllMelodeeAlbumDataFilesForDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo, CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        // Early cancellation check
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationResult<IEnumerable<Album>?>
            {
                Data = []
            };
        }

        // Check cache first with simple directory existence validation
        var cacheKey = fileSystemDirectoryInfo.Path;
        if (_directoryCache.TryGetValue(cacheKey, out var cached))
        {
            try
            {
                // A cache entry is valid only when the directory still exists, has not been
                // modified since the entry was populated, and is within the TTL window. Comparing
                // the directory's actual LastWriteTimeUtc against the cached value invalidates the
                // entry immediately on external modification instead of serving stale albums.
                if (fileSystemService.DirectoryExists(fileSystemDirectoryInfo.Path) &&
                    fileSystemService.GetDirectoryLastWriteTimeUtc(fileSystemDirectoryInfo.Path) ==
                    cached.DirectoryLastWriteTimeUtc &&
                    DateTime.UtcNow - cached.DirectoryLastWriteTimeUtc < _directoryCacheEntryMaxAge)
                {
                    Interlocked.Increment(ref _directoryCacheHits);
                    return new OperationResult<IEnumerable<Album>?>
                    {
                        Data = cached.Albums
                    };
                }
            }
            catch (Exception ex)
            {
                // If we can't check timestamps, proceed with fresh scan rather than silently
                // serving potentially stale data.
                Logger.Debug(ex, "[{ServiceName}] Cache freshness check failed for [{Path}], rescanning",
                    nameof(AlbumDiscoveryService), fileSystemDirectoryInfo.Path);
            }
            Interlocked.Increment(ref _directoryCacheMisses);
        }

        var albums = new ConcurrentBag<Album>();
        var errors = new ConcurrentBag<Exception>();
        var messages = new ConcurrentBag<string>();

        try
        {
            if (fileSystemService.DirectoryExists(fileSystemDirectoryInfo.Path))
            {
                var jsonFiles = fileSystemService.EnumerateFiles(fileSystemDirectoryInfo.Path, $"*{Album.JsonFileName}", SearchOption.AllDirectories);

                // Use optimized parallel processing with bounded concurrency
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = DefaultParallelOptions.MaxDegreeOfParallelism
                };

                try
                {
                    await Parallel.ForEachAsync(jsonFiles, parallelOptions,
                        async (jsonFilePath, token) =>
                        {
                            // Check cancellation at start of each iteration
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            try
                            {
                                var album = await fileSystemService.DeserializeAlbumAsync(jsonFilePath, token).ConfigureAwait(false);
                                if (album != null)
                                {
                                    album.Directory = new FileSystemDirectoryInfo
                                    {
                                        Path = fileSystemService.GetDirectoryName(jsonFilePath),
                                        Name = fileSystemService.GetFileName(fileSystemService.GetDirectoryName(jsonFilePath))
                                    };
                                    album.Created = fileSystemService.GetFileCreationTimeUtc(jsonFilePath);
                                    albums.Add(album);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Warning(e, "Error processing Melodee Data file [{FileName}]", jsonFilePath);
                                messages.Add($"Error processing Melodee Data file [{fileSystemDirectoryInfo.FullName()}]");
                                errors.Add(e);
                            }
                        });
                }
                catch (OperationCanceledException)
                {
                    // Handle cancellation gracefully - just continue with what we have
                }

                // Update cache with results if not canceled
                if (!cancellationToken.IsCancellationRequested)
                {
                    await UpdateCacheAsync(cacheKey, albums.ToArray(), cancellationToken);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Unable to load Albums for [{DirInfo}]", fileSystemDirectoryInfo.FullName);
            errors.Add(e);
        }

        return new OperationResult<IEnumerable<Album>?>(messages)
        {
            Errors = errors,
            Data = albums.IsEmpty ? null : albums.ToArray()
        };
    }

    private async Task UpdateCacheAsync(string cacheKey, Album[] albums, CancellationToken cancellationToken)
    {
        try
        {
            await _cacheUpdateSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Capture the directory's actual LastWriteTimeUtc so a later external modification
                // is detected on lookup and invalidates the entry immediately.
                var directoryLastWriteTimeUtc = fileSystemService.GetDirectoryLastWriteTimeUtc(cacheKey);

                _directoryCache.AddOrUpdate(cacheKey,
                    (directoryLastWriteTimeUtc, albums),
                    (_, _) => (directoryLastWriteTimeUtc, albums));

                // Implement cache size management to prevent memory bloat
                if (_directoryCache.Count > _directoryCacheCapacity) // Configurable threshold
                {
                    var oldestEntries = _directoryCache
                        .OrderBy(kvp => kvp.Value.DirectoryLastWriteTimeUtc)
                        .Take(_directoryCache.Count - (int)Math.Floor(_directoryCacheCapacity * 0.8)) // Keep 80% most recent
                        .Select(kvp => kvp.Key)
                        .ToArray();

                    foreach (var oldKey in oldestEntries)
                    {
                        _directoryCache.TryRemove(oldKey, out _);
                    }
                }
            }
            finally
            {
                _cacheUpdateSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during cache update
        }
    }

    public void ClearCache()
    {
        _directoryCache.Clear();
    }

    public (long Hits, long Misses, int Count) GetDirectoryCacheStats()
        => (Interlocked.Read(ref _directoryCacheHits), Interlocked.Read(ref _directoryCacheMisses), _directoryCache.Count);

    /// <summary>
    /// Diagnostic method to analyze a directory and identify why albums may not be discovered.
    /// Returns detailed information about the directory structure and melodee.json file status.
    /// </summary>
    public async Task<DirectoryDiagnosticResult> DiagnoseDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        CancellationToken cancellationToken = default)
    {
        var result = new DirectoryDiagnosticResult
        {
            DirectoryPath = fileSystemDirectoryInfo.Path,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            if (!fileSystemService.DirectoryExists(fileSystemDirectoryInfo.Path))
            {
                result.Errors.Add($"Directory does not exist: {fileSystemDirectoryInfo.Path}");
                return result;
            }

            // Count all subdirectories
            var allDirectories = fileSystemService.EnumerateDirectories(
                fileSystemDirectoryInfo.Path,
                "*",
                SearchOption.AllDirectories).ToList();
            result.TotalSubdirectories = allDirectories.Count;

            // Find all melodee.json files
            var melodeeJsonFiles = fileSystemService.EnumerateFiles(
                fileSystemDirectoryInfo.Path,
                $"*{Album.JsonFileName}",
                SearchOption.AllDirectories).ToList();
            result.DirectoriesWithMelodeeJson = melodeeJsonFiles.Count;

            // Find directories with media files but no melodee.json
            var mediaExtensions = new[] { ".mp3", ".flac", ".ogg", ".m4a", ".wav", ".wma", ".aac", ".opus" };
            var directoriesWithMelodeeJson = new HashSet<string>(
                melodeeJsonFiles.Select(f => fileSystemService.GetDirectoryName(f) ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            var directoriesWithMediaButNoJson = new List<string>();
            var directoriesWithMediaCount = 0;

            foreach (var dir in allDirectories)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var dirPath = dir.FullName;
                    var hasMediaFiles = fileSystemService.EnumerateFiles(dirPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Any(f => mediaExtensions.Contains(
                            Path.GetExtension(f).ToLowerInvariant()));

                    if (hasMediaFiles)
                    {
                        directoriesWithMediaCount++;
                        if (!directoriesWithMelodeeJson.Contains(dirPath))
                        {
                            directoriesWithMediaButNoJson.Add(dirPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error scanning directory {dir.FullName}: {ex.Message}");
                }
            }

            result.DirectoriesWithMediaFiles = directoriesWithMediaCount;
            result.DirectoriesWithMediaButNoMelodeeJson = directoriesWithMediaButNoJson.Count;

            // Sample some directories without melodee.json for diagnostic purposes
            result.SampleUnprocessedDirectories = directoriesWithMediaButNoJson
                .Take(20)
                .ToList();

            // Check for common issues in melodee.json files
            var jsonErrors = new List<string>();
            var validAlbums = 0;
            var invalidAlbums = 0;

            foreach (var jsonFile in melodeeJsonFiles.Take(100)) // Sample first 100
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var album = await fileSystemService.DeserializeAlbumAsync(jsonFile, cancellationToken);
                    if (album != null)
                    {
                        validAlbums++;
                    }
                    else
                    {
                        invalidAlbums++;
                        jsonErrors.Add($"Null album from: {jsonFile}");
                    }
                }
                catch (Exception ex)
                {
                    invalidAlbums++;
                    jsonErrors.Add($"Error deserializing {jsonFile}: {ex.Message}");
                }
            }

            result.ValidMelodeeJsonFiles = validAlbums;
            result.InvalidMelodeeJsonFiles = invalidAlbums;
            result.JsonDeserializationErrors = jsonErrors.Take(10).ToList();

            // Log summary
            Log.Information(
                "Directory Diagnostic for [{Path}]: " +
                "TotalSubdirs={TotalSubdirs}, WithMelodeeJson={WithJson}, WithMedia={WithMedia}, " +
                "MediaButNoJson={MediaNoJson}, ValidJson={ValidJson}, InvalidJson={InvalidJson}",
                fileSystemDirectoryInfo.Path,
                result.TotalSubdirectories,
                result.DirectoriesWithMelodeeJson,
                result.DirectoriesWithMediaFiles,
                result.DirectoriesWithMediaButNoMelodeeJson,
                result.ValidMelodeeJsonFiles,
                result.InvalidMelodeeJsonFiles);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Fatal error during diagnosis: {ex.Message}");
            Log.Error(ex, "Error during directory diagnosis for [{Path}]", fileSystemDirectoryInfo.Path);
        }

        return result;
    }
}

/// <summary>
/// Result of a directory diagnostic analysis.
/// </summary>
public sealed class DirectoryDiagnosticResult
{
    public string DirectoryPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }

    /// <summary>Total number of subdirectories in the path.</summary>
    public int TotalSubdirectories { get; set; }

    /// <summary>Number of directories containing a melodee.json file.</summary>
    public int DirectoriesWithMelodeeJson { get; set; }

    /// <summary>Number of directories containing media files (mp3, flac, etc.).</summary>
    public int DirectoriesWithMediaFiles { get; set; }

    /// <summary>Number of directories with media files but no melodee.json (unprocessed).</summary>
    public int DirectoriesWithMediaButNoMelodeeJson { get; set; }

    /// <summary>Number of melodee.json files that deserialized successfully (sampled).</summary>
    public int ValidMelodeeJsonFiles { get; set; }

    /// <summary>Number of melodee.json files that failed to deserialize (sampled).</summary>
    public int InvalidMelodeeJsonFiles { get; set; }

    /// <summary>Sample of directories with media files but no melodee.json.</summary>
    public List<string> SampleUnprocessedDirectories { get; set; } = [];

    /// <summary>Sample of JSON deserialization errors.</summary>
    public List<string> JsonDeserializationErrors { get; set; } = [];

    /// <summary>Any errors encountered during diagnosis.</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>Summary of the diagnostic findings.</summary>
    public string Summary =>
        $"Directory: {DirectoryPath}\n" +
        $"Analyzed: {AnalyzedAt:u}\n" +
        $"Total Subdirectories: {TotalSubdirectories:N0}\n" +
        $"Directories with melodee.json: {DirectoriesWithMelodeeJson:N0}\n" +
        $"Directories with media files: {DirectoriesWithMediaFiles:N0}\n" +
        $"Unprocessed (media but no melodee.json): {DirectoriesWithMediaButNoMelodeeJson:N0}\n" +
        $"Valid melodee.json (sampled): {ValidMelodeeJsonFiles:N0}\n" +
        $"Invalid melodee.json (sampled): {InvalidMelodeeJsonFiles:N0}\n" +
        $"Processing Gap: {GetProcessingGapPercentage()}% unprocessed";

    private string GetProcessingGapPercentage()
    {
        if (DirectoriesWithMediaFiles <= 0) return "0.0";
        var percentage = (double)DirectoriesWithMediaButNoMelodeeJson / DirectoriesWithMediaFiles * 100;
        return percentage.ToString("F1");
    }
}
