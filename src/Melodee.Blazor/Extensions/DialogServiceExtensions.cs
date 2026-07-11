using Melodee.Blazor.Components.Dialogs;
using Melodee.Common.Services.Doctor;
using Radzen;

namespace Melodee.Blazor.Extensions;

/// <summary>
/// Extensions for the Radzen DialogService
/// </summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Creates a confirmation dialog with HTML content from a string
    /// </summary>
    /// <param name="dialogService">The dialog service</param>
    /// <param name="htmlContent">HTML content as string</param>
    /// <param name="title">Title of dialog</param>
    /// <param name="options">Dialog options</param>
    /// <returns>True if confirmed, otherwise false</returns>
    public static async Task<bool?> ConfirmHtml(this DialogService dialogService,
        string htmlContent,
        string title = "Confirm",
        ConfirmOptions? options = null)
    {
        return await dialogService.Confirm(builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddMarkupContent(1, htmlContent);
            builder.CloseElement();
        }, title, options ?? new ConfirmOptions());
    }

    /// <summary>
    /// Opens the DecentDB unsupported-format remediation dialog.
    /// </summary>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="title">Localized dialog title.</param>
    /// <param name="issues">Unsupported-format Doctor results for affected DecentDB databases.</param>
    public static async Task OpenDecentDbMigrationDialogAsync(
        this DialogService dialogService,
        string title,
        IReadOnlyList<DoctorCheckResult> issues)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(issues);

        await dialogService.OpenAsync<DecentDbMigrationDialog>(
            title,
            new Dictionary<string, object?>
            {
                { nameof(DecentDbMigrationDialog.Issues), issues.ToArray() }
            },
            new DialogOptions
            {
                Width = "900px",
                Height = "auto",
                Resizable = true,
                Draggable = true,
                ShowClose = true,
                CloseDialogOnOverlayClick = false,
                AriaLabel = title
            });
    }
}
