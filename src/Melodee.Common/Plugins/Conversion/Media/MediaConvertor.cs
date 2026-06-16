using System.Text.RegularExpressions;
using FFMpegCore;
using FFMpegCore.Enums;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Metadata.AudioTags;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.MetaData;
using Melodee.Common.Plugins.MetaData.Song.Extensions;
using Melodee.Common.Utility;
using Serilog.Events;
using SerilogTimings;
using Track = ATL.Track;

namespace Melodee.Common.Plugins.Conversion.Media;

/// <summary>
///     This converts all Media files into MP3 files.
/// </summary>
public sealed partial class MediaConvertor(IMelodeeConfiguration configuration)
    : MetaDataBase(configuration), IConversionPlugin
{
    public override string Id => "61995E53-D998-4BD4-BC83-2AB2F9D9B931";

    public override string DisplayName => nameof(MediaConvertor);

    public override bool IsEnabled { get; set; } = true;

    public override int SortOrder { get; } = 0;

    public override bool DoesHandleFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo fileSystemInfo)
    {
        if (!IsEnabled || !fileSystemInfo.Exists(directoryInfo) ||
            !MelodeeConfiguration.GetValue<bool>(SettingRegistry.ConversionEnabled))
        {
            return false;
        }

        return FileHelper.IsFileMediaType(fileSystemInfo.Extension(directoryInfo));
    }

    public async Task<OperationResult<FileSystemFileInfo>> ProcessFileAsync(FileSystemDirectoryInfo directoryInfo,
        FileSystemFileInfo fileSystemInfo, CancellationToken cancellationToken = default)
    {
        if (!MelodeeConfiguration.GetValue<bool>(SettingRegistry.ConversionEnabled))
        {
            return new OperationResult<FileSystemFileInfo>(
                $"Configuration value '{SettingRegistry.ConversionEnabled}' has disabled media conversion.")
            {
                Data = fileSystemInfo,
                Type = OperationResponseType.NotImplementedOrDisabled
            };
        }

        if (!FileHelper.IsFileMediaType(fileSystemInfo.Extension(directoryInfo)))
        {
            return new OperationResult<FileSystemFileInfo>
            {
                Errors =
                [
                    new Exception("Invalid file type. This convertor only processes Media type files.")
                ],
                Data = fileSystemInfo
            };
        }

        var fileInfo = new FileInfo(fileSystemInfo.FullName(directoryInfo));
        if (fileInfo.Exists && SafeParser.ToBoolean(Configuration[SettingRegistry.ConversionEnabled]))
        {
            if (await AudioTagManager.NeedsConversionToMp3Async(fileInfo, cancellationToken).ConfigureAwait(false))
            {
                using (Operation.At(LogEventLevel.Debug).Time("Converted [{directoryInfo}] to MP3", fileInfo.FullName))
                {
                    var songFileInfo = fileInfo;
                    var songDirectory = songFileInfo.Directory?.FullName ??
                                        throw new Exception("Invalid FileInfo For Song");
                    var newFileName = Path.Combine(songDirectory,
                        $"{Path.GetFileNameWithoutExtension(songFileInfo.Name)}.mp3");

                    try
                    {
                        FFMpegArguments.FromFileInput(songFileInfo)
                            .OutputToFile(newFileName, true, options =>
                            {
                                options.WithAudioBitrate(
                                    SafeParser.ToEnum<AudioQuality>(Configuration[SettingRegistry.ConversionBitrate]));
                                options.WithAudioSamplingRate(
                                    SafeParser.ToNumber<int>(Configuration[SettingRegistry.ConversionSamplingRate]));
                                options.WithVariableBitrate(
                                    SafeParser.ToNumber<int>(Configuration[SettingRegistry.ConversionVbrLevel]));
                                options.WithAudioCodec(AudioCodec.LibMp3Lame).ForceFormat("mp3");
                            }).ProcessSynchronously();
                    }
                    catch (Exception ex)
                    {
                        var fallbackFileInfo = new FileInfo(newFileName);
                        if (await IsUsableConvertedMp3Async(fallbackFileInfo, cancellationToken).ConfigureAwait(false))
                        {
                            fileInfo = FinalizeConvertedFile(songFileInfo, fallbackFileInfo);
                            return new OperationResult<FileSystemFileInfo>
                            {
                                Data = fileInfo.ToFileSystemInfo()
                            };
                        }

                        throw new Exception($"Unable to convert [{songFileInfo.FullName}] to MP3", ex);
                    }

                    var newFileInfo = new FileInfo(newFileName);
                    while (!newFileInfo.CanWriteTo())
                    {
                        await Task.Delay(100, cancellationToken);
                    }

                    if (await AudioTagManager.NeedsConversionToMp3Async(newFileInfo, cancellationToken).ConfigureAwait(false))
                    {
                        throw new Exception($"Unable to convert [{songFileInfo.FullName}] to MP3");
                    }

                    if (await IsUsableConvertedMp3Async(newFileInfo, cancellationToken).ConfigureAwait(false))
                    {
                        fileInfo = FinalizeConvertedFile(songFileInfo, newFileInfo);
                    }
                    else
                    {
                        throw new Exception($"Unable to convert [{songFileInfo.FullName}] to MP3");
                    }
                }
            }
        }

        return new OperationResult<FileSystemFileInfo>
        {
            Data = fileInfo.ToFileSystemInfo()
        };
    }

    public static async Task<bool> IsUsableConvertedMp3Async(FileInfo fileInfo, CancellationToken cancellationToken = default)
    {
        if (!fileInfo.Exists || fileInfo.Length == 0 ||
            !fileInfo.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return await FileFormatDetector.DetectFormatAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false) == AudioFormat.MP3;
        }
        catch
        {
            return false;
        }
    }

    private FileInfo FinalizeConvertedFile(FileInfo sourceFileInfo, FileInfo convertedFileInfo)
    {
        var convertedRenamedExtension =
            SafeParser.ToString(Configuration[SettingRegistry.ProcessingConvertedExtension]);

        if (SafeParser.ToBoolean(Configuration[SettingRegistry.ProcessingDoDeleteOriginal]) &&
            sourceFileInfo.Exists)
        {
            sourceFileInfo.Delete();
        }
        else if (convertedRenamedExtension.Nullify() != null && sourceFileInfo.Exists)
        {
            var movedFileName = Path.Combine(sourceFileInfo.DirectoryName!,
                $"{sourceFileInfo.Name}.{convertedRenamedExtension}");
            sourceFileInfo.MoveTo(movedFileName);
        }

        return convertedFileInfo;
    }

    private static bool ShouldMediaSongBeConverted(Track song)
    {
        if (song.AudioFormat == null || (song.AudioFormat?.MimeList?.Contains("image") ?? false))
        {
            return false;
        }

        var shortName = song.AudioFormat?.ShortName ?? string.Empty;

        if (MpegRegex().IsMatch(shortName))
        {
            var ext = song.FileInfo().Extension;
            if (ext.ToLower().EndsWith("m4a")) // M4A is an audio file using the MP4 encoding
            {
                return true;
            }

            return false;
        }

        return true;
    }

    [GeneratedRegex("mpeg([0-9]*)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MpegRegex();
}
