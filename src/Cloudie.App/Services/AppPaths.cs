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
    /// <summary>音源账号: the second NetEase session, kept apart from the main account's cookies.</summary>
    public static string SourceCookiesFile => Path.Combine(DataDirectory, "cookies-source.json");
    public static string LogFile => Path.Combine(DataDirectory, "cloudie.log");
    public static string QueueFile => Path.Combine(DataDirectory, "queue.json");
    public static string PositionFile => Path.Combine(DataDirectory, "playback.json");
    /// <summary>歌曲缓存: downloaded songs (<c>*.cache</c>) and their <c>index.json</c>.</summary>
    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");
    /// <summary>List pages as last shown (one JSON file per playlist, album, artist or day), so they open before the network answers.</summary>
    public static string ListsDirectory => Path.Combine(DataDirectory, "lists");

    public static void Ensure() => Directory.CreateDirectory(DataDirectory);
}
