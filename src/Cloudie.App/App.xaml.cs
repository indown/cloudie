using Cloudie.App.Services;
using Microsoft.UI.Xaml;

namespace Cloudie.App;

public partial class App : Application
{
    /// <summary>The single service container; views and view-models resolve their dependencies here.</summary>
    public static AppServices Services { get; private set; } = null!;

    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        Services = AppServices.Create();
        UnhandledException += (_, e) =>
        {
            Services.Log.Error($"Unhandled: {e.Exception}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services.Log.Info($"Cloudie {AppInfo.VersionText} starting");
        var theme = Services.Settings.Theme;
        Services.Player.RestoreQueue();
        // Started before the window so the home page can wait for it and load signed in the first time.
        _ = Services.Account.RestoreAsync();
        _ = RestoreSourceAccountAsync();
        MainWindow = new MainWindow();
        MainWindow.ApplyTheme(theme);
        MainWindow.Activate();
        if (Services.AudioCacheNotice is { } notice) Services.Status.Show(notice);
        if (Services.Settings.CheckUpdates) CheckForUpdatesLater(MainWindow);
    }

    /// <summary>音源账号 is restored alongside; nothing waits for it, since a stored cookie is usable right away.</summary>
    private static async Task RestoreSourceAccountAsync()
    {
        try
        {
            await Services.SourceAccount.RestoreAsync();
        }
        catch (Exception error)
        {
            Services.Log.Warn($"恢复音源账号失败: {error.Message}");
        }
    }

    /// <summary>A few seconds after launch, so the first screen is not competing with a network call.</summary>
    private static async void CheckForUpdatesLater(MainWindow window)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        await window.ViewModel.CheckForUpdatesAsync();
    }
}
