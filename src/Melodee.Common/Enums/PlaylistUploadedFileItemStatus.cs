namespace Melodee.Common.Enums;

/// <summary>
/// Status of a playlist uploaded file item (song reference).
/// </summary>
public enum PlaylistUploadedFileItemStatus
{
    /// <summary>
    /// The song reference has been matched to a Song in the library.
    /// </summary>
    Resolved = 1,

    /// <summary>
    /// The song reference has not been matched to a Song in the library.
    /// </summary>
    Missing = 2
}
