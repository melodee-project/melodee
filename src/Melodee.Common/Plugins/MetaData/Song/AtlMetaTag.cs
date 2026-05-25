using System.Diagnostics;
using ATL;
using Commons;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Directory;
using Melodee.Common.Plugins.MetaData.Song.Extensions;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using ImageInfo = Melodee.Common.Models.ImageInfo;

namespace Melodee.Common.Plugins.MetaData.Song;

/// <summary>
///     Implementation of Song Plugin using ATL Library
///     <remarks>https://github.com/Zeugma440/atldotnet</remarks>
/// </summary>
public sealed class AtlMetaTag(
    IMetaTagsProcessorPlugin metaTagsProcessorPlugin,
    IImageProcessor imageProcessor,
    ImageConvertor imageConverter,
    IImageValidator imageValidator,
    IMelodeeConfiguration configuration) : MetaDataBase(configuration),
    ISongPlugin, ISongFileUpdatePlugin, IAlbumNamesInDirectoryPlugin
{
    public OperationResult<string[]> AlbumNamesInDirectory(FileSystemDirectoryInfo directoryInfo)
    {
        var result = new List<string>();

        if (directoryInfo.Exists())
        {
            foreach (var file in directoryInfo.AllMediaTypeFileInfos())
            {
                try
                {
                    var fileAtl = new Track(file.FullName);
                    var album = fileAtl.Album;
                    if (!string.IsNullOrWhiteSpace(album) && !result.Contains(album))
                    {
                        result.Add(album);
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(e, "[{Plugin}] Skipping unreadable file [{FileName}] while collecting album names.",
                        DisplayName, file.FullName);
                }
            }
        }

        return new OperationResult<string[]>
        {
            Data = result.ToArray()
        };
    }

    public OperationResult<bool> UpdateFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo file,
        MetaTagIdentifier identifier, object? value)
    {
        var result = false;
        if (!file.Exists(directoryInfo))
        {
            return new OperationResult<bool>
            {
                Data = false
            };
        }

        var songFileName = file.FullName(directoryInfo);
        if (!File.Exists(songFileName))
        {
            Log.Warning(new Exception($"File not found [{songFileName}]"),
                "[{PlugInName}] UpdateFileAsync called File [{FileName}] does not exist", nameof(AtlMetaTag),
                songFileName);
            return new OperationResult<bool>
            {
                Data = false
            };
        }

        try
        {
            var doDeleteComment = MelodeeConfiguration.GetValue<bool?>(SettingRegistry.ProcessingDoDeleteComments) ??
                                  true;

            var fileAtl = new Track(songFileName);
            switch (identifier)
            {
                case MetaTagIdentifier.Album:
                    fileAtl.Album = value?.ToString().Nullify() ?? string.Empty;
                    break;

                case MetaTagIdentifier.AlbumArtist:
                    fileAtl.AlbumArtist = value?.ToString().Nullify() ?? string.Empty;
                    break;

                case MetaTagIdentifier.Artist:
                    fileAtl.Artist = value?.ToString().Nullify() ?? string.Empty;
                    break;

                case MetaTagIdentifier.Comment:
                    fileAtl.Comment = doDeleteComment ? string.Empty : value?.ToString().Nullify() ?? string.Empty;
                    break;

                case MetaTagIdentifier.DiscNumber:
                    fileAtl.DiscNumber = SafeParser.ToNumber<short>(value);
                    break;

                case MetaTagIdentifier.DiscTotal:
                    fileAtl.DiscTotal = SafeParser.ToNumber<short>(value);
                    break;

                case MetaTagIdentifier.Genre:
                    fileAtl.Genre = value?.ToString().Nullify() ?? string.Empty;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(identifier), identifier, null);
            }

            result = fileAtl.Save();
        }
        catch (Exception e)
        {
            Log.Error(e, "FileSystemFileInfo [{FileSystemFileInfo}]", directoryInfo);
        }

        return new OperationResult<bool>
        {
            Data = result
        };
    }

    public override string Id => "0F622E4B-64CD-4033-8B23-BA2001F045FA";

    public override string DisplayName => nameof(AtlMetaTag);

    public override bool IsEnabled { get; set; } = true;

    public override int SortOrder { get; } = 0;

    public override bool DoesHandleFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo fileSystemInfo)
    {
        if (!IsEnabled || !fileSystemInfo.Exists(directoryInfo))
        {
            return false;
        }

        return FileHelper.IsFileMediaType(fileSystemInfo.Extension(directoryInfo));
    }

    public async Task<OperationResult<Models.Song>> ProcessFileAsync(
        FileSystemDirectoryInfo directoryInfo,
        FileSystemFileInfo fileSystemFileInfo,
        CancellationToken cancellationToken = default)
    {
        var tags = new List<MetaTag<object?>>();
        var mediaAudios = new List<MediaAudio<object?>>();
        var images = new List<ImageInfo>();

        try
        {
            if (fileSystemFileInfo.Exists(directoryInfo))
            {
                var songPath = fileSystemFileInfo.FullName(directoryInfo);
                if (!await OptimizedFileOperations.WaitForFileStabilityAsync(songPath, cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                {
                    Log.Warning("[{Plugin}] File [{File}] not stable for read, skipping.", DisplayName, songPath);
                    return new OperationResult<Models.Song>(["File not stable for read"])
                    {
                        Data = new Models.Song
                        {
                            CrcHash = string.Empty,
                            File = fileSystemFileInfo
                        }
                    };
                }

                var fileAtl = new Track(songPath);
                if (!fileAtl.MetadataFormats.Any(x => x.ID < 0) && IsAtlTrackForAudioFile(fileAtl))
                {
                    var atlDictionary = fileAtl.ToDictionary()
                        .ToDictionary(x => x.Key.ToNormalizedString() ?? x.Key, x => x.Value);

                    var metaAudioIdentifierDictionary = MediaAudioIdentifier.NotSet.ToNormalizedDictionary();
                    foreach (var metaTagIdentifier in metaAudioIdentifierDictionary)
                    {
                        if (atlDictionary.TryGetValue(metaTagIdentifier.Value, out var v))
                        {
                            if (v is string s)
                            {
                                v = s.Nullify();
                            }

                            if (v != null)
                            {
                                var identifier = SafeParser.ToEnum<MediaAudioIdentifier>(metaTagIdentifier.Key);
                                mediaAudios.Add(new MediaAudio<object?>
                                {
                                    Identifier = identifier,
                                    Value = v
                                });
                            }
                        }
                    }

                    mediaAudios.Add(new MediaAudio<object?>
                    {
                        Identifier = MediaAudioIdentifier.CodecLongName,
                        Value = fileAtl.AudioFormat.Name
                    });
                    mediaAudios.Add(new MediaAudio<object?>
                    {
                        Identifier = MediaAudioIdentifier.Channels,
                        Value = fileAtl.ChannelsArrangement.Description
                    });
                    mediaAudios.Add(new MediaAudio<object?>
                    {
                        Identifier = MediaAudioIdentifier.ChannelLayout,
                        Value = fileAtl.ChannelsArrangement.ToString()
                    });


                    if (fileAtl.IsVBR)
                    {
                        mediaAudios.Add(new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.IsVbr,
                            Value = true
                        });
                    }

                    if (fileAtl.ChannelsArrangement != null)
                    {
                        mediaAudios.Add(new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.ChannelsArrangementDescription,
                            Value = fileAtl.ChannelsArrangement.Description
                        });
                        mediaAudios.Add(new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.ChannelsArrangementNumberChannels,
                            Value = fileAtl.ChannelsArrangement.NbChannels
                        });
                    }

                    if (fileAtl.TechnicalInformation != null)
                    {
                        mediaAudios.Add(new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.AudioDataOffset,
                            Value = fileAtl.TechnicalInformation.AudioDataOffset
                        });
                        mediaAudios.Add(new MediaAudio<object?>
                        {
                            Identifier = MediaAudioIdentifier.AudioDataSize,
                            Value = fileAtl.TechnicalInformation.AudioDataSize
                        });
                    }

                    var metaTagIdentifierDictionary = MetaTagIdentifier.NotSet.ToNormalizedDictionary();
                    foreach (var metaTagIdentifier in metaTagIdentifierDictionary)
                    {
                        if (atlDictionary.TryGetValue(metaTagIdentifier.Value, out var v))
                        {
                            if (v is string s)
                            {
                                v = s.Nullify();
                            }

                            if (v is DateTime vDt)
                            {
                                v = vDt > DateTime.MinValue && vDt < DateTime.MaxValue ? v : null;
                            }

                            if (v != null)
                            {
                                var identifier = SafeParser.ToEnum<MetaTagIdentifier>(metaTagIdentifier.Key);
                                if (metaTagIdentifier.Key is (int)MetaTagIdentifier.Date
                                    or (int)MetaTagIdentifier.AlbumDate or (int)MetaTagIdentifier.RecordingDate)
                                {
                                    var dt = SafeParser.ToDateTime(v);
                                    if (dt.HasValue)
                                    {
                                        tags.Add(new MetaTag<object?>
                                        {
                                            Identifier = MetaTagIdentifier.OrigAlbumYear,
                                            Value = dt.Value.Year
                                        });
                                        tags.Add(new MetaTag<object?>
                                        {
                                            Identifier = MetaTagIdentifier.AlbumDate,
                                            Value = dt.Value.ToString("O")
                                        });
                                    }
                                }

                                if (identifier is MetaTagIdentifier.DiscNumber or MetaTagIdentifier.DiscTotal)
                                {
                                    // Every album with songs has at least one disc
                                    if (SafeParser.ToNumber<short>(v) < 1)
                                    {
                                        v = 1;
                                    }
                                }

                                tags.Add(new MetaTag<object?>
                                {
                                    Identifier = identifier,
                                    Value = v
                                });
                            }
                        }
                    }

                    var adData1 = fileAtl.AdditionalFields.ToDictionary();
                    var additionalTags = MetaTagsForTagDictionary(adData1);
                    foreach (var additionalTag in additionalTags)
                    {
                        // Additional tag values override any in place
                        var existing = tags.FirstOrDefault(x => x.Identifier == additionalTag.Identifier);
                        if (existing != null)
                        {
                            tags.Remove(existing);
                        }

                        tags.Add(additionalTag);
                    }


                    if (fileAtl.EmbeddedPictures.Any() &&
                        SafeParser.ToBoolean(Configuration[SettingRegistry.ImagingDoLoadEmbeddedImages]))
                    {
                        try
                        {
                            var pictureIndex = 0;
                            foreach (var embeddedPicture in fileAtl.EmbeddedPictures)
                            {
                                if (embeddedPicture.NativeFormat is ImageFormat.Unsupported or ImageFormat.Undefined)
                                {
                                    Log.Warning("[{PluginName}] embedded image format [{Format}] is not supported.",
                                        nameof(AtlMetaTag), embeddedPicture.NativeFormat);
                                    continue;
                                }

                                var embeddedPictureDimensions = imageProcessor.Identify(embeddedPicture.PictureData);
                                var imageCrcHash = Crc32.Calculate(embeddedPicture.PictureData);
                                if (directoryInfo.GetFileForCrcHash("jpg", imageCrcHash) == null)
                                {
                                    var doSaveEmbeddedImage = true;
                                    var pictureIdentifier =
                                        SafeParser.ToEnum<PictureIdentifier>(embeddedPicture.PicType);
                                    var newImageFileName = Path.Combine(directoryInfo.Path,
                                        $"{ImageInfo.ImageFilePrefix}{(pictureIndex + 1).ToStringPadLeft(Common.Configuration.MelodeeConfiguration.ImageNameNumberPadding)}-{embeddedPicture.PicType.ToString()}.jpg");
                                    if (File.Exists(newImageFileName))
                                    {
                                        var embeddedPictureDataInfo = imageProcessor.Identify(embeddedPicture.PictureData);
                                        var exitingImageInfo =
                                            await imageProcessor.IdentifyAsync(newImageFileName, cancellationToken).ConfigureAwait(false);
                                        doSaveEmbeddedImage = embeddedPictureDataInfo?.Width > exitingImageInfo?.Width;
                                    }

                                    if (doSaveEmbeddedImage)
                                    {
                                        await File.WriteAllBytesAsync(newImageFileName, embeddedPicture.PictureData,
                                            cancellationToken).ConfigureAwait(false);
                                    }

                                    var newImageFileInfo = new FileInfo(newImageFileName).ToFileSystemInfo();
                                    newImageFileInfo = (await imageConverter
                                        .ProcessFileAsync(directoryInfo, newImageFileInfo, cancellationToken)
                                        .ConfigureAwait(false)).Data;
                                    if ((await imageValidator.ValidateImage(newImageFileInfo.ToFileInfo(directoryInfo),
                                            pictureIdentifier, cancellationToken).ConfigureAwait(false)).Data.IsValid)
                                    {
                                        images.Add(new ImageInfo
                                        {
                                            CrcHash = imageCrcHash,
                                            PictureIdentifier = pictureIdentifier,
                                            FileInfo = new FileInfo(newImageFileName).ToFileSystemInfo(),
                                            Width = embeddedPictureDimensions?.Width ?? 0,
                                            Height = embeddedPictureDimensions?.Height ?? 0,
                                            SortOrder = embeddedPicture.Position,
                                            WasEmbeddedInSong = true
                                        });
                                    }
                                    else
                                    {
                                        Log.Warning("[{PluginName}] embedded image did not pass validation.",
                                            nameof(AtlMetaTag));
                                    }
                                }

                                pictureIndex++;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Warning(e, "Could not load embedded pictures [{FileInfo}]", fileSystemFileInfo);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "FileSystemFileInfo [{FileSystemFileInfo}]", fileSystemFileInfo);
        }


        // Ensure that OrigAlbumYear exists and if not add with invalid date (will get set later by MetaTagProcessor.)
        if (tags.All(x => x.Identifier != MetaTagIdentifier.OrigAlbumYear))
        {
            tags.Add(new MetaTag<object?>
            {
                Identifier = MetaTagIdentifier.OrigAlbumYear,
                Value = 0
            });
        }

        // Set SortOrder necessary for processors
        var tagCount = tags.Count;
        tags.ForEach(x => x.SortOrder = tagCount);

        // If the album title isn't set try to parse it from the directory name
        if (tags.All(x => x.Identifier != MetaTagIdentifier.Album))
        {
            tags.Add(new MetaTag<object?>
            {
                Identifier = MetaTagIdentifier.Album,
                Value = directoryInfo.Name.TryToGetAlbumTitle()
            });
        }

        var albumTag = tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.Album);
        var artistTag = tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.Artist);
        if (artistTag?.Value?.ToString().Nullify() == null)
        {
            // If [TP1,TPE1] is not set and [TP2,TPE2] is set, and only one, then use [TP2,TPE2] for [TP1,TPE1]
            var isSingleArtist = tags.Count(x => x.Identifier == MetaTagIdentifier.AlbumArtist) == 1;
            var albumArtistTag = tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.AlbumArtist);
            if (isSingleArtist && albumArtistTag?.Value?.ToString().Nullify() != null)
            {
                tags.Add(new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.Artist,
                    Value = albumArtistTag.Value
                });
                artistTag = tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.Artist);
            }
        }

        if (albumTag?.Value?.ToString().Nullify() == null || artistTag?.Value?.ToString().Nullify() == null)
        {
            return new OperationResult<Models.Song>(
                $"[{DisplayName}] Song [{fileSystemFileInfo.Name}] is invalid, missing Album and/or Artist tags.")
            {
                Data = new Models.Song
                {
                    CrcHash = string.Empty,
                    File = fileSystemFileInfo
                },
                Type = OperationResponseType.ValidationFailure
            };
        }

        tags.First(x => x.Identifier == MetaTagIdentifier.Album).SortOrder = 1;
        tags.First(x => x.Identifier == MetaTagIdentifier.Artist).SortOrder = 2;

        // Ensure that AlbumArtist is set, if it has fragments will get cleaned up by MetaTag Processor
        if (tags.All(x => x.Identifier != MetaTagIdentifier.AlbumArtist))
        {
            tags.Add(new MetaTag<object?>
            {
                Identifier = MetaTagIdentifier.AlbumArtist,
                Value = tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.Artist)?.Value,
                SortOrder = 3
            });
        }

        var metaTagsProcessorResult = await metaTagsProcessorPlugin
            .ProcessMetaTagAsync(directoryInfo, fileSystemFileInfo, tags, cancellationToken).ConfigureAwait(false);
        if (!metaTagsProcessorResult.IsSuccess)
        {
            return new OperationResult<Models.Song>(metaTagsProcessorResult.Messages)
            {
                Errors = metaTagsProcessorResult.Errors,
                Data = new Models.Song
                {
                    CrcHash = string.Empty,
                    File = fileSystemFileInfo
                }
            };
        }

        var song = new Models.Song
        {
            CrcHash = Crc32.Calculate(new FileInfo(fileSystemFileInfo.FullName(directoryInfo))),
            File = fileSystemFileInfo,
            Images = images,
            Tags = metaTagsProcessorResult.Data.DistinctBy(x => x.Identifier).ToArray(),
            MediaAudios = mediaAudios.DistinctBy(x => x.Identifier).ToArray(),
            SortOrder = tags.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.TrackNumber)?.Value as int? ?? 0
        };
        return new OperationResult<Models.Song>
        {
            Data = song
        };
    }


    public async Task<OperationResult<bool>> UpdateSongAsync(FileSystemDirectoryInfo directoryInfo, Models.Song song,
        CancellationToken cancellationToken = default)
    {
        var result = false;
        if (song.Tags?.Any() ?? false)
        {
            var songFileName = song.File.FullName(directoryInfo);

            // Check file existence first before attempting any operations
            if (!File.Exists(songFileName))
            {
                Log.Warning(new Exception($"File not found [{songFileName}]"),
                    "[{PlugInName}] UpdateSongAsync called File [{FileName}] does not exist", nameof(AtlMetaTag),
                    songFileName);
                return new OperationResult<bool>([$"File not found: {songFileName}"])
                {
                    Data = false
                };
            }

            try
            {
                // Wait for file stability with retry logic
                if (!await OptimizedFileOperations.WaitForFileStabilityAsync(songFileName, checks: 3, delayMs: 150, cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                {
                    Log.Warning("[{Plugin}] File [{File}] not stable for write, skipping.", DisplayName, songFileName);
                    return new OperationResult<bool>(["File not stable for write"])
                    {
                        Data = false
                    };
                }

                // Double-check file still exists after stability wait
                if (!File.Exists(songFileName))
                {
                    Log.Warning("[{Plugin}] File [{File}] disappeared during stability check.", DisplayName, songFileName);
                    return new OperationResult<bool>(["File disappeared during processing"])
                    {
                        Data = false
                    };
                }

                var doDeleteComment = MelodeeConfiguration.GetValue<bool?>(SettingRegistry.ProcessingDoDeleteComments) ?? true;

                var fileAtl = new Track(songFileName)
                {
                    Album = song.AlbumTitle(),
                    AlbumArtist = song.AlbumArtist(),
                    Artist = song.SongArtist(),
                    Comment = doDeleteComment ? string.Empty : song.Comment(),
                    DiscNumber = 1,
                    DiscTotal = 1,
                    Genre = song.Genre(),
                    OriginalReleaseDate = song.AlbumDateValue(),
                    Title = song.Title(),
                    TrackNumber = song.SongNumber(),
                    TrackTotal = song.SongTotalNumber(),
                    Year = song.AlbumYear()
                };
                if (song.Images?.Any() ?? false)
                {
                    var coverImage = song.Images.FirstOrDefault(x =>
                        x.PictureIdentifier is PictureIdentifier.Front or PictureIdentifier.SecondaryFront);
                    if (coverImage != null && (coverImage.FileInfo?.Exists(directoryInfo) ?? false))
                    {
                        fileAtl.EmbeddedPictures.Clear();
                        fileAtl.EmbeddedPictures.Add(PictureInfo.fromBinaryData(
                            await File.ReadAllBytesAsync(coverImage.FileInfo!.FullName(directoryInfo),
                                cancellationToken).ConfigureAwait(false),
                            PictureInfo.PIC_TYPE.Front));
                    }
                }
                else
                {
                    fileAtl.EmbeddedPictures.Clear();
                }

                result = fileAtl.Save();
            }
            catch (Exception e)
            {
                Log.Error(e, "FileSystemFileInfo [{FileSystemFileInfo}]", directoryInfo);
            }
        }

        return new OperationResult<bool>
        {
            Data = result
        };
    }

    public OperationResult<bool> RemoveImages(FileSystemDirectoryInfo directoryInfo, Models.Song song)
    {
        var result = false;
        if (song.Tags?.Any() ?? false)
        {
            var songFileName = song.File.FullName(directoryInfo);
            using (Operation.At(LogEventLevel.Debug)
                       .Time("[{Plugin}] Removing images from [{FileName}]", DisplayName, songFileName))
            {
                try
                {
                    var fileAtl = new Track(songFileName);
                    fileAtl.EmbeddedPictures.Clear();
                    result = fileAtl.Save();
                }
                catch (Exception e)
                {
                    Log.Error(e, "FileSystemFileInfo [{FileSystemFileInfo}]", directoryInfo);
                }
            }
        }

        return new OperationResult<bool>
        {
            Data = result
        };
    }

    private IEnumerable<MetaTag<object?>> MetaTagsForTagDictionary(Dictionary<string, string> tagsDictionary)
    {
        var result = new List<MetaTag<object?>>();
        if (tagsDictionary.Count == 0)
        {
            return result;
        }

        foreach (var kp in tagsDictionary)
        {
            var k = kp.Key.CleanString()?.Replace(" ", string.Empty).ToUpperInvariant();
            switch (k)
            {
                case "ARTISTS":
                    if (result.All(x => x.Identifier != MetaTagIdentifier.Artist))
                    {
                        var artists = kp.Value.ToCleanedMultipleArtistsValue();
                        if (artists != null)
                        {
                            result.Add(new MetaTag<object?>
                            {
                                Identifier = MetaTagIdentifier.Artists,
                                Value = artists
                            });
                        }
                    }

                    break;

                case "LENGTH":
                    if (result.All(x => x.Identifier != MetaTagIdentifier.Length))
                    {
                        result.Add(new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.Length,
                            Value = kp.Value
                        });
                    }

                    break;

                case "DATE":
                    if (result.All(x => x.Identifier != MetaTagIdentifier.RecordingYear))
                    {
                        if (SafeParser.ToNumber<int>(kp.Value) >
                            MelodeeConfiguration.GetValue<int>(SettingRegistry.ValidationMinimumAlbumYear))
                        {
                            result.Add(new MetaTag<object?>
                            {
                                Identifier = MetaTagIdentifier.RecordingYear,
                                Value = kp.Value
                            });
                        }
                        else if (SafeParser.ToDateTime(kp.Value) != null)
                        {
                            result.Add(new MetaTag<object?>
                            {
                                Identifier = MetaTagIdentifier.AlbumDate,
                                Value = SafeParser.ToDateTime(kp.Value)!.Value.ToString("O")
                            });
                        }
                    }

                    break;

                case "Song":
                    if (result.All(x => x.Identifier != MetaTagIdentifier.SongNumberTotal))
                    {
                        if (kp.Value.Contains('/'))
                        {
                            result.Add(new MetaTag<object?>
                            {
                                Identifier = MetaTagIdentifier.SongNumberTotal,
                                Value = kp.Value
                            });
                        }
                    }

                    break;

                // ReSharper disable once StringLiteralTypo
                case "WXXX":
                    if (result.All(x => x.Identifier != MetaTagIdentifier.UserDefinedUrlLink))
                    {
                        result.Add(new MetaTag<object?>
                        {
                            Identifier = MetaTagIdentifier.UserDefinedUrlLink,
                            Value = kp.Value
                        });
                    }

                    break;
            }
        }

        return result;
    }


    private static bool IsAtlTrackForAudioFile(Track track)
    {
        if (track.AudioFormat?.ShortName == null)
        {
            return false;
        }

        if (string.Equals(track.AudioFormat?.ShortName, "mpeg-4", StringComparison.OrdinalIgnoreCase))
        {
            var ext = track.FileInfo().Extension;
            if (!ext.ToLower().EndsWith("m4a")) // M4A is an audio file using the MP4 encoding
            {
                Trace.WriteLine($"Video file found in Scanning. File [{track.FileInfo().FullName}]");
                return false;
            }
        }

        return track is { AudioFormat: { ID: > -1 }, Duration: > 0 };
    }
}
