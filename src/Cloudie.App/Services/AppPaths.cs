namespace Cloudie.App.Services;

/// <summary>
/// Where the app keeps its data: <c>%LOCALAPPDATA%\Cloudie</c>. A plain folder rather than
/// ApplicationData so the same build runs packaged (MSIX) and unpackaged (zip).
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloudie");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string CookiesFile => Path.Combine(DataDirectory, "cookies.json");
    public static string LogFile => Path.Combine(DataDirectory, "cloudie.log");
    public static string QueueFile => Path.Combine(DataDirectory, "queue.json");
    public static string PositionFile => Path.Combine(DataDirectory, "playback.json");

    public static void Ensure() => Directory.CreateDirectory(DataDirectory);
}
