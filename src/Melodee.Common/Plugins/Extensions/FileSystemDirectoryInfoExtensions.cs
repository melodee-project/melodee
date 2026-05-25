using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Utility;
using ImageInfo = Melodee.Common.Models.ImageInfo;

namespace Melodee.Common.Plugins.Extensions;

public static class FileSystemDirectoryInfoExtensions
{
    public static async Task<ImageInfo[]> ImagesForTypeAsync(this FileSystemDirectoryInfo directory,
        IImageProcessor imageProcessor,
        int maxNumberOfImagesAllowed, PictureIdentifier[] forPictureIdentifiers, IImageValidator imageValidator,
        CancellationToken cancellationToken = default)
    {
        var imageInfos = new List<ImageInfo>();
        var imageFiles = ImageHelper.ImageFilesInDirectory(directory.FullName(), SearchOption.TopDirectoryOnly)
            .ToArray();
        var index = 1;
        var maxNumberOfImagesLength = SafeParser.ToNumber<short>(maxNumberOfImagesAllowed.ToString().Length);
        foreach (var imageFile in imageFiles.Order())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var fileInfo = new FileInfo(imageFile);
            var fileNameNormalized = fileInfo.Name.ToNormalizedString() ?? fileInfo.Name;
            if (ImageHelper.IsArtistImage(fileInfo) || ImageHelper.IsArtistSecondaryImage(fileInfo))
            {
                if (!(await imageValidator.ValidateImage(fileInfo,
                        ImageHelper.IsArtistImage(fileInfo)
                            ? PictureIdentifier.Artist
                            : PictureIdentifier.ArtistSecondary, cancellationToken)).Data.IsValid)
                {
                    continue;
                }

                var pictureIdentifier = PictureIdentifier.NotSet;
                if (ImageHelper.IsArtistImage(fileInfo))
                {
                    pictureIdentifier = PictureIdentifier.Band;
                }
                else if (ImageHelper.IsArtistSecondaryImage(fileInfo))
                {
                    pictureIdentifier = PictureIdentifier.BandSecondary;
                }

                if (forPictureIdentifiers.Contains(pictureIdentifier))
                {
                    var imageDimensions = await imageProcessor.IdentifyAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
                    var fileInfoFileSystemInfo = fileInfo.ToFileSystemInfo();
                    imageInfos.Add(new ImageInfo
                    {
                        CrcHash = Crc32.Calculate(fileInfo),
                        FileInfo = new FileSystemFileInfo
                        {
                            Name =
                                $"{ImageInfo.ImageFilePrefix}{index.ToStringPadLeft(maxNumberOfImagesLength)}-{pictureIdentifier}.jpg",
                            Size = fileInfoFileSystemInfo.Size,
                            OriginalName = fileInfo.Name
                        },
                        OriginalFilename = fileInfo.Name,
                        PictureIdentifier = pictureIdentifier,
                        Width = imageDimensions?.Width ?? 0,
                        Height = imageDimensions?.Height ?? 0,
                        SortOrder = index
                    });
                    index++;
                }
            }
        }

        return imageInfos.ToArray();
    }
}
