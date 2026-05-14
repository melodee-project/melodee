using System.Text.RegularExpressions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Validation;
using Melodee.Common.Plugins.Validation.Models;

namespace Melodee.Common.Plugins.Validation;

public sealed class ImageValidator(IImageProcessor imageProcessor, IMelodeeConfiguration configuration) : IImageValidator
{
    private static readonly Regex ImageNameIsProofRegex = new(@"(proof|foto)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private readonly List<ValidationResultMessage> _validationMessages = [];

    public async Task<OperationResult<ValidationResult>> ValidateImage(FileInfo? fileInfo,
        PictureIdentifier pictureIdentifier, CancellationToken cancellationToken = default)
    {
        _validationMessages.Clear();
        if (fileInfo == null)
        {
            _validationMessages.Add(new ValidationResultMessage
            {
                Severity = ValidationResultMessageSeverity.Critical,
                Message = "Image cannot be null."
            });
        }

        if (!fileInfo!.Exists)
        {
            _validationMessages.Add(new ValidationResultMessage
            {
                Severity = ValidationResultMessageSeverity.Critical,
                Message = "Image not found."
            });
        }

        if (fileInfo.Length == 0)
        {
            _validationMessages.Add(new ValidationResultMessage
            {
                Severity = ValidationResultMessageSeverity.Critical,
                Message = "Image is empty."
            });
        }

        try
        {
            var imageInfo = await imageProcessor.IdentifyAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);
            if (imageInfo == null)
            {
                _validationMessages.Add(new ValidationResultMessage
                {
                    Severity = ValidationResultMessageSeverity.Critical,
                    Message = "Unable to identify image format."
                });
            }
            else
            {
                if (pictureIdentifier.ValidateIsSquare())
                {
                    if (imageInfo.Width != imageInfo.Height)
                    {
                        _validationMessages.Add(new ValidationResultMessage
                        {
                            Severity = ValidationResultMessageSeverity.Critical,
                            Message = $"Image is not square [{imageInfo.Width}x{imageInfo.Height}]."
                        });
                    }
                }

                var minSize = configuration.GetValue<int>(SettingRegistry.ImagingMinimumImageSize);
                var smallImageSize = configuration.GetValue<int>(SettingRegistry.ImagingSmallSize);
                if (minSize > 0 && minSize >= smallImageSize)
                {
                    if (imageInfo.Width < minSize)
                    {
                        _validationMessages.Add(new ValidationResultMessage
                        {
                            Severity = ValidationResultMessageSeverity.Critical,
                            Message =
                                $"Image size [{imageInfo.Width}] is less than configured minimum image size [{minSize}]."
                        });
                    }
                }
            }
        }
        catch (Exception e)
        {
            _validationMessages.Add(new ValidationResultMessage
            {
                Severity = ValidationResultMessageSeverity.Critical,
                Message = $"Unable to load image. [{e}]"
            });
        }

        if (IsImageAProofType(fileInfo.FullName))
        {
            _validationMessages.Add(new ValidationResultMessage
            {
                Severity = ValidationResultMessageSeverity.Critical,
                Message = "Image is a proof type image."
            });
        }

        var isValid = _validationMessages.All(x => x.Severity != ValidationResultMessageSeverity.Critical);
        return new OperationResult<ValidationResult>
        {
            Data = new ValidationResult
            {
                IsValid = isValid,
                Messages = _validationMessages
            }
        };
    }

    public static bool IsImageAProofType(string? imageName)
    {
        return !string.IsNullOrWhiteSpace(imageName) && ImageNameIsProofRegex.IsMatch(imageName);
    }
}
