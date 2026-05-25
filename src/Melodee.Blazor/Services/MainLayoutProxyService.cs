namespace Melodee.Blazor.Services;

/// <summary>
///     This is used by child pages to notify the MainLayout that it should show text or the activity spinner.
/// </summary>
public sealed class MainLayoutProxyService
{
    public bool ShowSpinner { get; private set; }

    public string Header { get; set; } = "Loading...";

    public event EventHandler? HeaderChanged;
    public event EventHandler? SpinnerVisibleChanged;

    public event EventHandler<string>? SearchTextChanged;

    public void SetHeader(string header)
    {
        Header = header;
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleSpinnerVisible(bool? forceState = null)
    {
        SetSpinnerVisible(forceState ?? !ShowSpinner);
    }

    public void SetSpinnerVisible(bool isVisible)
    {
        if (ShowSpinner == isVisible)
        {
            return;
        }

        ShowSpinner = isVisible;
        SpinnerVisibleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DoSearch(string query)
    {
        SearchTextChanged?.Invoke(this, query);
    }

    public void NotifyHeaderChanged()
    {
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifySpinnerVisibleChangedChanged()
    {
        SpinnerVisibleChanged?.Invoke(this, EventArgs.Empty);
    }
}
