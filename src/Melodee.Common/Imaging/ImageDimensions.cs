namespace Melodee.Common.Imaging;

/// <summary>
///     Simple DTO representing the dimensions of an image.
/// </summary>
public sealed record ImageDimensions(int Width, int Height, string? Format = null);
