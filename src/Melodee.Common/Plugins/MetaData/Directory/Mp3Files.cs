using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Artist = Melodee.Common.Models.Artist;

namespace Melodee.Common.Plugins.MetaData.Directory;

/// <summary>
///     Creates an album in a directory for MP3 files grouped by Album Title
/// </summary>
public class Mp3Files(
    IEnumerable<ISongPlugin> songPlugins,
    IAlbumValidator albumValidator,
    ISerializer serializer,
    ILogger logger,
    IMelodeeConfiguration configuration) : AlbumMetaDataBase(configuration), IDirectoryPlugin
{
    private const string HandlesExtension = "MP3";

    public override string Id => "4015E7C8-240F-4FC2-A40D-372168C78C98";

    public override string DisplayName => nameof(Mp3Files);

    public override bool IsEnabled { get; set; } = true;

    public override int SortOrder { get; } = 0;

    public async Task<OperationResult<int>> ProcessDirectoryAsync(FileSystemDirectoryInfo fileSystemDirectoryInfo,
        CancellationToken cancellationToken = default)
    {
        var processedFileCount = 0;

        var albums = new List<Album>();
        var messages = new List<string>();
        var errors = new List<Exception>();
        var viaPlugins = new List<string>
        {
            DisplayName
        };
        var songs = new List<Common.Models.Song>();

        var maxAlbumProcessingCount =
            MelodeeConfiguration.GetValue<int>(SettingRegistry.ProcessingMaximumProcessingCount,
                value => value < 1 ? int.MaxValue : value);

        if (fileSystemDirectoryInfo.Exists())
        {
            using (Operation.At(LogEventLevel.Debug).Time("[{PluginName}] ProcessDirectoryAsync [{directoryInfo}]",
                       DisplayName, fileSystemDirectoryInfo.Name))
            {
                HandleMelodeeTagFiles(fileSystemDirectoryInfo);

                // Get all media files upfront to enable parallel processing
                var mediaFiles = fileSystemDirectoryInfo.AllMediaTypeFileInfos(SearchOption.TopDirectoryOnly).ToArray();

                // Process files in parallel for better I/O throughput
                var maxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4);
                var songResults = new System.Collections.Concurrent.ConcurrentBag<(Common.Models.Song? Song, string? ViaPlugin, List<string> Messages, List<Exception> Errors)>();

                await Parallel.ForEachAsync(mediaFiles, new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                }, async (fileSystemInfo, token) =>
                {
                    var fsi = fileSystemInfo.ToFileSystemInfo();
                    var localMessages = new List<string>();
                    var localErrors = new List<Exception>();

                    foreach (var plugin in songPlugins.OrderBy(x => x.SortOrder))
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        if (plugin.DoesHandleFile(fileSystemDirectoryInfo, fsi))
                        {
                            var pluginResult = await plugin.ProcessFileAsync(fileSystemDirectoryInfo, fsi, token);
                            localErrors.AddRange(pluginResult.Errors ?? []);
                            localMessages.AddRange(pluginResult.Messages ?? []);
                            if (pluginResult.IsSuccess)
                            {
                                songResults.Add((pluginResult.Data, $"{nameof(Mp3Files)}:{plugin.DisplayName}", localMessages, localErrors));
                                break;
                            }

                            logger.Debug("[{Plugin}] failed to process file: [{File}] result [{Result}]",
                                plugin.DisplayName, fsi, serializer.Serialize(pluginResult));
                        }
                    }
                });

                // Collect results from parallel processing
                foreach (var result in songResults)
                {
                    if (result.Song != null)
                    {
                        songs.Add(result.Song);
                        processedFileCount++;
                    }
                    if (result.ViaPlugin != null)
                    {
                        viaPlugins.Add(result.ViaPlugin);
                    }
                    messages.AddRange(result.Messages);
                    errors.AddRange(result.Errors);
                }

                await HandleDuplicates(fileSystemDirectoryInfo, songs.ToArray(), cancellationToken);
                EnsureSortOrderSet(songs.ToArray());

                foreach (var songsGroupedByAlbum in songs.GroupBy(x => x.SongArtistAlbumUniqueId()))
                {
                    foreach (var song in songsGroupedByAlbum)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        var foundAlbum = albums.FirstOrDefault(x =>
                            x.Artist.NameNormalized ==
                            (song.AlbumArtist().ToNormalizedString() ?? song.AlbumArtist()) &&
                            x.AlbumTitle() == song.AlbumTitle());
                        if (foundAlbum != null)
                        {
                            albums.Remove(foundAlbum);
                            albums.Add(foundAlbum.MergeSongs([song]));
                        }
                        else
                        {
                            var songTotal = SafeParser.ToNumber<short>(songsGroupedByAlbum.Count());
                            var newAlbumTags = new List<MetaTag<object?>>
                            {
                                new()
                                {
                                    Identifier = MetaTagIdentifier.Album, Value = song.AlbumTitle(), SortOrder = 1
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.AlbumArtist, Value = song.AlbumArtist(),
                                    SortOrder = 2
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.DiscTotal, Value = 1, SortOrder = 4
                                },
                                new()
                                {
                                    Identifier = MetaTagIdentifier.RecordingYear, Value = song.AlbumYear(),
                                    SortOrder = 100
                                },
                                new() { Identifier = MetaTagIdentifier.SongTotal, Value = songTotal, SortOrder = 101 }
                            };
                            var albumDate = song.AlbumDate();
                            if (albumDate != null)
                            {
                                newAlbumTags.Add(new MetaTag<object?>
                                {
                                    Identifier = MetaTagIdentifier.AlbumDate,
                                    Value = albumDate,
                                    SortOrder = 100
                                });
                            }

                            var genres = songsGroupedByAlbum
                                .SelectMany(x => x.Tags ?? [])
                                .Where(x => x.Identifier == MetaTagIdentifier.Genre);
                            newAlbumTags.AddRange(genres
                                .GroupBy(x => x.Value)
                                .Select((genre, i) => new MetaTag<object?>
                                {
                                    Identifier = MetaTagIdentifier.Genre,
                                    Value = genre.Key?.ToString()?.CleanStringAsIs(),
                                    SortOrder = 5 + i
                                }));
                            var artistName = ResolveArtistName(newAlbumTags, songsGroupedByAlbum, fileSystemDirectoryInfo);
                            var newAlbum = new Album
                            {
                                Artist = Artist.NewArtistFromName(artistName),
                                AlbumType = song.AlbumTitle().TryToDetectAlbumType(),
                                Images = songsGroupedByAlbum.Where(x => x.Images != null)
                                    .SelectMany(x => x.Images!)
                                    .DistinctBy(x => x.CrcHash).ToArray(),
                                Directory = fileSystemDirectoryInfo,
                                OriginalDirectory = fileSystemDirectoryInfo,
                                Tags = newAlbumTags,
                                Songs = songsGroupedByAlbum.OrderBy(x => x.SortOrder).ToArray(),
                                ViaPlugins = viaPlugins.Distinct().ToArray()
                            };
                            albums.Add(newAlbum);
                            if (albums.Count(x => x.IsValid) > maxAlbumProcessingCount)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        // Save all album files to given directory
        var serialized = string.Empty;
        foreach (var album in albums)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var validationResult = albumValidator.ValidateAlbum(album);
            album.ValidationMessages = validationResult.Data.Messages ?? [];
            album.Status = validationResult.Data.AlbumStatus;
            album.StatusReasons = validationResult.Data.AlbumStatusReasons;
            try
            {
                serialized = serializer.Serialize(album);
            }
            catch (Exception e)
            {
                logger.Error(e, "Error serializing album [{Album}]", album.ToString());
            }

            await File.WriteAllTextAsync(
                Path.Combine(fileSystemDirectoryInfo.FullName(), album.ToMelodeeJsonName(MelodeeConfiguration, true)),
                serialized, cancellationToken);
        }

        return new OperationResult<int>(messages)
        {
            Errors = errors.ToArray(),
            Data = processedFileCount
        };
    }

    private void EnsureSortOrderSet(Common.Models.Song[] songs)
    {
        logger.Debug("[{Plugin}] Ensuring sort order is set on songs...", DisplayName);
        foreach (var song in songs)
        {
            song.SortOrder = song.SongNumber() + song.MediaNumber() * MediaEditService.SortOrderMediaMultiplier -
                             MediaEditService.SortOrderMediaMultiplier;
        }
    }

    /// <summary>
    ///     If MelodeeTag files are present (*.mtg), if so then process them.
    /// </summary>
    private void HandleMelodeeTagFiles(FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var melodeeTagFiles = fileSystemDirectoryInfo.AllFileInfos($"*{FileHelper.MelodeeTagFileExtension}").ToArray();
        if (melodeeTagFiles.Length == 0)
        {
            return;
        }

        var editorSongPlugin = songPlugins.FirstOrDefault(x => x is ISongFileUpdatePlugin) as ISongFileUpdatePlugin;
        if (editorSongPlugin == null)
        {
            return;
        }

        foreach (var mediaFile in fileSystemDirectoryInfo.AllMediaTypeFileInfos())
        {
            foreach (var melodeeTagFile in melodeeTagFiles)
            {
                var mediaTagNameParts = melodeeTagFile.Name.Split("__");
                if (mediaTagNameParts.Length != 2)
                {
                    continue;
                }

                var tagIdentifier = SafeParser.ToEnum<MetaTagIdentifier>(mediaTagNameParts[0]);
                var tagValue = mediaTagNameParts[1].Replace(FileHelper.MelodeeTagFileExtension, string.Empty);
                var updateResult = editorSongPlugin.UpdateFile(
                    fileSystemDirectoryInfo,
                    mediaFile.ToFileSystemInfo(),
                    tagIdentifier,
                    tagValue);
                if (!updateResult.IsSuccess)
                {
                    return;
                }

                melodeeTagFile.Delete();
            }
        }
    }

    private async Task HandleDuplicates(FileSystemDirectoryInfo fileSystemDirectoryInfo, Common.Models.Song[] seenSongs,
        CancellationToken cancellationToken = default)
    {
        if (seenSongs.Length < 2)
        {
            return;
        }

        // First check for duplicate songs by their hash (much faster than file-level duplicate detection)
        var ss = seenSongs.ToList();
        var duplicateSongs = ss.GroupBy(x => x.DuplicateHashCheck).Where(x => x.Count() > 1).ToArray();
        if (duplicateSongs.Any())
        {
            foreach (var duplicateGroup in duplicateSongs)
            {
                var bestSong = Common.Models.Song.IdentityBestAndMergeOthers(duplicateGroup.ToArray());
                var duplicateSong = duplicateGroup.Where(x => x.Id != bestSong.Id).ToArray();
                foreach (var ds in duplicateSong)
                {
                    var duplicateSongFile = ds.File.FullName(fileSystemDirectoryInfo);
                    try
                    {
                        File.Delete(duplicateSongFile);
                        // Only prune the in-memory list once the file is actually deleted, so a
                        // locked duplicate does not leave the album JSON referencing a song that
                        // was never removed from disk.
                        ss.RemoveAll(x => x.File.FullName(fileSystemDirectoryInfo) == duplicateSongFile);
                        logger.Debug("[{Plugin}] Deleted duplicate song: {DuplicateSongFile}", DisplayName,
                            duplicateSongFile);
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "[{Plugin}] Could not delete duplicate song [{DuplicateSongFile}]",
                            DisplayName, duplicateSongFile);
                    }
                }
            }
        }

        // Only do expensive file-level duplicate detection if we still have potential duplicates
        // (files with same size that weren't caught by song hash check)
        var filesBySize = ss.GroupBy(s => new FileInfo(s.File.FullName(fileSystemDirectoryInfo)).Length)
            .Where(g => g.Count() > 1)
            .ToArray();

        if (filesBySize.Length > 0)
        {
            logger.Debug("[{Plugin}] Checking for file-level duplicates in [{Directory}]...", DisplayName,
                fileSystemDirectoryInfo.FullName());
            var foundDuplicates = await fileSystemDirectoryInfo.FindDuplicatesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var dup in foundDuplicates.SelectMany(x => x.Value))
            {
                try
                {
                    dup.Delete();
                    logger.Debug("[{Plugin}] Deleted duplicate: {Duplicate}", DisplayName, dup.FullName);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "[{Plugin}] Could not delete duplicate file [{Duplicate}]", DisplayName,
                        dup.FullName);
                }
            }
        }
    }

    public override bool DoesHandleFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo fileSystemInfo)
    {
        return fileSystemInfo.Extension(directoryInfo).DoStringsMatch(HandlesExtension);
    }

    /// <summary>
    ///     Resolves the album artist name using a graceful fallback chain so a missing tag never aborts processing of
    ///     the whole directory. Order: AlbumArtist/Artist tags from the album, then the per-song Artist tag, then the
    ///     artist segment of the directory name, finally "Unknown Artist". A resolved-but-unknown name lets the
    ///     AlbumValidator flag the album as needing attention instead of throwing and dropping the directory.
    /// </summary>
    internal static string ResolveArtistName(
        List<MetaTag<object?>> albumTags,
        IGrouping<long?, Common.Models.Song> songsGroupedByAlbum,
        FileSystemDirectoryInfo directoryInfo)
    {
        var fromTags = albumTags
            .FirstOrDefault(x => x.Identifier is MetaTagIdentifier.Artist or MetaTagIdentifier.AlbumArtist)
            ?.Value?.ToString().Nullify();
        if (fromTags != null)
        {
            return fromTags;
        }

        var fromSongArtist = songsGroupedByAlbum
            .Select(song => song.SongArtist().Nullify())
            .FirstOrDefault(artist => artist != null);
        if (fromSongArtist != null)
        {
            return fromSongArtist;
        }

        // Directory names conventionally follow "Artist - Album (Year)"; derive the artist from the leading segment,
        // but only when a separator is present so bracket/year-only folders don't get mistaken for artist names.
        var directoryName = directoryInfo.Name.Nullify();
        if (directoryName != null &&
            (directoryName.Contains('-') || directoryName.Contains(StringExtensions.TagsSeparator)))
        {
            var directoryArtist = directoryName
                .Split('-', StringExtensions.TagsSeparator)
                .FirstOrDefault()?
                .CleanString()
                .Nullify();
            if (directoryArtist != null)
            {
                return directoryArtist;
            }
        }

        return "Unknown Artist";
    }
}
