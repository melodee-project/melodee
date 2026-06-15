using System.Diagnostics;
using ATL;
using Melodee.Common.Configuration;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Metadata.AudioTags.Models;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Serilog;
using Serilog.Events;
using SerilogTimings;

namespace Melodee.Common.Plugins.MetaData.Song;

public sealed class NativeId3MetaTag(
    IMetaTagsProcessorPlugin metaTagsProcessorPlugin,
    IMelodeeConfiguration configuration) : MetaDataBase(configuration), ISongPlugin
{
    private readonly IMetaTagsProcessorPlugin _metaTagsProcessorPlugin = metaTagsProcessorPlugin;

    public override string Id => "0AE16462-6924-496B-AC5E-C9CD70EA078D";

    public override string DisplayName => nameof(NativeId3MetaTag);

    public override bool IsEnabled { get; set; } = true;

    public override int SortOrder { get; } = 1;

    public override bool DoesHandleFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo fileSystemInfo)
    {
        if (!IsEnabled || !fileSystemInfo.Exists(directoryInfo))
        {
            return false;
        }

        return string.Equals(fileSystemInfo.Extension(directoryInfo), ".mp3", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OperationResult<Models.Song>> ProcessFileAsync(FileSystemDirectoryInfo directoryInfo,
        FileSystemFileInfo fileSystemInfo, CancellationToken cancellationToken = default)
    {
        using (Operation.At(LogEventLevel.Debug)
                   .Time("[{PluginName}] Processing [{fileSystemInfo}]", DisplayName, fileSystemInfo.Name))
        {
            var tags = new List<MetaTag<object?>>();
            var mediaAudios = new List<MediaAudio<object?>>();
            var images = new List<ImageInfo>();
            var fullName = fileSystemInfo.FullName(directoryInfo);

            try
            {
                if (fileSystemInfo.Exists(directoryInfo))
                {
                    if (!await OptimizedFileOperations.WaitForFileStabilityAsync(fullName, cancellationToken: cancellationToken)
                            .ConfigureAwait(false))
                    {
                        Log.Warning("[{Plugin}] File [{File}] not stable for read, skipping.", DisplayName, fullName);
                        return new OperationResult<Models.Song>(["File not stable for read"])
                        {
                            Data = new Models.Song
                            {
                                CrcHash = string.Empty,
                                File = fileSystemInfo
                            }
                        };
                    }

                    var tagData = await ReadTagDataAsync(fullName, cancellationToken).ConfigureAwait(false);
                    tags.AddRange(tagData.Select(ToMetaTag));

                    var duration = ReadDurationMs(fullName);
                    if (duration is > 0)
                    {
                        mediaAudios.Add(new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.DurationMs,
                            Value = duration
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "fileSystemInfo [{fileSystemInfo}]", fileSystemInfo);
            }

            if (tags.All(x => x.Identifier != MetaTagIdentifier.RecordingYear))
            {
                tags.Add(new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.RecordingYear,
                    Value = 0
                });
            }

            var metaTagsProcessorResult =
                await _metaTagsProcessorPlugin.ProcessMetaTagAsync(directoryInfo, fileSystemInfo, tags,
                    cancellationToken);
            if (!metaTagsProcessorResult.IsSuccess)
            {
                return new OperationResult<Models.Song>(metaTagsProcessorResult.Messages)
                {
                    Errors = metaTagsProcessorResult.Errors,
                    Data = new Models.Song
                    {
                        CrcHash = string.Empty,
                        File = fileSystemInfo
                    }
                };
            }

            var song = new Models.Song
            {
                CrcHash = Crc32.Calculate(new FileInfo(fullName)),
                File = fileSystemInfo,
                Images = images,
                Tags = metaTagsProcessorResult.Data,
                MediaAudios = mediaAudios,
                SortOrder = SafeParser.ToNumber<int>(tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.TrackNumber)?.Value)
            };
            if (!song.IsValid(Configuration))
            {
                Trace.WriteLine("Song is invalid");
            }

            return new OperationResult<Models.Song>
            {
                Data = song
            };
        }
    }

    public Task<OperationResult<bool>> UpdateSongAsync(FileSystemDirectoryInfo directoryInfo, Models.Song song,
        CancellationToken cancellationToken = default)
    {
        var fullPath = song.File.FullName(directoryInfo);
        if (!OptimizedFileOperations.WaitForFileStabilityAsync(fullPath, cancellationToken: cancellationToken)
                .GetAwaiter()
                .GetResult())
        {
            Log.Warning("[{Plugin}] File [{File}] not stable for write, skipping.", DisplayName, fullPath);
            return Task.FromResult(new OperationResult<bool>(["File not stable for write"])
            {
                Data = false
            });
        }

        var track = new Track(fullPath);

        try
        {
            track.Title = song.Title();
            track.Album = song.AlbumTitle();
            track.Artist = song.SongArtist();
            track.AlbumArtist = song.AlbumArtist();
            track.TrackNumber = song.SongNumber();

            var year = song.AlbumYear();
            if (year.HasValue)
            {
                track.Date = new DateTime(year.Value, 1, 1);
            }

            var comment = song.Comment();
            if (!string.IsNullOrWhiteSpace(comment))
            {
                track.Comment = comment;
            }

            var genres = song.Tags?.Where(t => t.Identifier == MetaTagIdentifier.Genre)
                .Select(t => t.Value?.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .ToArray() ?? [];
            if (genres.Length > 0)
            {
                track.Genre = string.Join(';', genres);
            }

            track.Save();

            return Task.FromResult(new OperationResult<bool> { Data = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update tags for file [{File}]", fullPath);
            return Task.FromResult(new OperationResult<bool>([$"Failed to update tags: {ex.Message}"]) { Data = false });
        }
    }

    private static async Task<IEnumerable<KeyValuePair<MetaTagIdentifier, object>>> ReadTagDataAsync(string fullName, CancellationToken cancellationToken)
    {
        try
        {
            var tagData = await AudioTagManager.ReadAllTagsAsync(fullName, cancellationToken).ConfigureAwait(false);
            return tagData.Tags
                .Where(x => x.Value.ToString().Nullify() is not null)
                .Where(x => x.Value is not byte[] && x.Value is not IEnumerable<AudioImage>);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug(ex, "Native ID3 tag reader could not read [{File}]", fullName);
            return [];
        }
    }

    private static MetaTag<object?> ToMetaTag(KeyValuePair<MetaTagIdentifier, object> tag)
    {
        return new MetaTag<object?>
        {
            Identifier = tag.Key,
            Value = tag.Value
        };
    }

    private static double? ReadDurationMs(string fullName)
    {
        try
        {
            var atlTag = new Track(fullName);
            return atlTag.DurationMs > 0 ? atlTag.DurationMs : null;
        }
        catch
        {
            return null;
        }
    }
}
