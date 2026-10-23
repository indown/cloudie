using Cloudie.App.Services;
using Cloudie.App.ViewModels;
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
        var theme = Services.Settings.Theme;
        MainWindow = new MainWindow();
        MainWindow.ApplyTheme(theme);
        MainWindow.Activate();
        _ = Services.Account.RestoreAsync();
    }
}
