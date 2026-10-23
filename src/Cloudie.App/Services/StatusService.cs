namespace Cloudie.App.Services;

/// <summary>One-line notices for the window's status bar, raised from anywhere on the UI thread.</summary>
public sealed class StatusService
{
    public event Action<string>? Shown;

    public void Show(string message) => Shown?.Invoke(message);
}
