using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.MetaData;
using Melodee.Common.Utility;

namespace Melodee.Common.Plugins.Conversion.Image;

/// <summary>
///     This converts non-JPG image into a JPG image.
/// </summary>
public sealed class ImageConvertor(IImageProcessor imageProcessor, IMelodeeConfiguration configuration) : MetaDataBase(configuration), IConversionPlugin
{
    public override string Id => "8A169045-C650-4DE5-A564-F0E2D28EF07D";

    public override string DisplayName => nameof(ImageConvertor);

    public override bool IsEnabled { get; set; } = true;

    public override int SortOrder { get; } = 0;

    public override bool DoesHandleFile(FileSystemDirectoryInfo directoryInfo, FileSystemFileInfo fileSystemInfo)
    {
        if (!IsEnabled || !fileSystemInfo.Exists(directoryInfo))
        {
            return false;
        }

        return FileHelper.IsFileImageType(fileSystemInfo.Extension(directoryInfo));
    }

    public async Task<OperationResult<FileSystemFileInfo>> ProcessFileAsync(FileSystemDirectoryInfo directoryInfo,
        FileSystemFileInfo fileSystemInfo, CancellationToken cancellationToken = default)
    {
        if (!FileHelper.IsFileImageType(fileSystemInfo.Extension(directoryInfo)))
        {
            return new OperationResult<FileSystemFileInfo>
            {
                Errors =
                [
                    new Exception("Invalid file type. This convertor only processes Image type files.")
                ],
                Data = fileSystemInfo
            };
        }

        var fileInfo = new FileInfo(fileSystemInfo.FullName(directoryInfo));
        if (fileInfo.Exists)
        {
            var smallImageSize = MelodeeConfiguration.GetValue<int>(SettingRegistry.ImagingSmallSize);
            var mediumImageSize = MelodeeConfiguration.GetValue<int>(SettingRegistry.ImagingMediumSize);
            var largeImageSize = MelodeeConfiguration.GetValue<int>(SettingRegistry.ImagingLargeSize);

            var newName = Path.ChangeExtension(fileInfo.FullName, "jpg");

            ImageDimensions? imageDimensions = null;
            try
            {
                imageDimensions = await imageProcessor.IdentifyAsync(fileInfo.FullName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Deleting invalid image file [{fileInfo.FullName}] due to error: {e.Message}");
                fileInfo.Delete();
                return new OperationResult<FileSystemFileInfo>
                {
                    Errors =
                    [
                        new Exception($"Deleting invalid image file [{fileInfo.FullName}] due to error: {e.Message}")
                    ],
                    Data = fileSystemInfo
                };
            }

            if (imageDimensions == null)
            {
                Trace.WriteLine($"Deleting unidentifiable image file [{fileInfo.FullName}]");
                fileInfo.Delete();
                return new OperationResult<FileSystemFileInfo>
                {
                    Errors =
                    [
                        new Exception($"Deleting unidentifiable image file [{fileInfo.FullName}]")
                    ],
                    Data = fileSystemInfo
                };
            }

            var larger = imageDimensions.Width;
            if (larger < smallImageSize)
            {
                larger = smallImageSize;
            }

            if (larger < imageDimensions.Height)
            {
                larger = imageDimensions.Height;
            }

            var resizeWithPaddingSize = smallImageSize;
            if (larger > smallImageSize)
            {
                resizeWithPaddingSize = mediumImageSize;
            }

            if (larger > mediumImageSize)
            {
                resizeWithPaddingSize = largeImageSize;
            }

            var didModify = false;
            var imageBytes = await File.ReadAllBytesAsync(fileInfo.FullName, cancellationToken);
            var decodedFormat = imageDimensions.Format;
            if (!string.Equals(decodedFormat, "Jpeg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(decodedFormat, "Jpeg", StringComparison.OrdinalIgnoreCase))
            {
                imageBytes = imageProcessor.ConvertToJpeg(imageBytes);
                didModify = true;
            }

            if (imageDimensions.Width != imageDimensions.Height || imageDimensions.Height > largeImageSize)
            {
                imageBytes = imageProcessor.ResizeAndPadToSquare(imageBytes, resizeWithPaddingSize);
                didModify = true;
            }

            if (didModify || !string.Equals(fileInfo.FullName, newName, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newName))
                {
                    File.Delete(newName);
                }

                await File.WriteAllBytesAsync(newName, imageBytes, cancellationToken);
                if (newName != fileInfo.FullName)
                {
                    fileInfo.Delete();
                    fileInfo = new FileInfo(newName);
                }
            }
        }

        return new OperationResult<FileSystemFileInfo>
        {
            Data = fileInfo.ToFileSystemInfo()
        };
    }

    public byte[] ResizeImageIfNeeded(ReadOnlyMemory<byte> imageBytes, int maxWidth, int maxHeight,
        bool isForUserAvatar)
    {
        return imageProcessor.ResizeImageIfNeeded(imageBytes, maxWidth, maxHeight, isForUserAvatar);
    }

    public async Task<byte[]> ConvertToGifFormat(byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        return await imageProcessor.ConvertToGifAsync(imageBytes, cancellationToken).ConfigureAwait(false);
    }
}
