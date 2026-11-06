using Cloudie.App.ViewModels;
using Cloudie.Core.Unblock;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ViewModel.Detach();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        ViewModel.Refresh();
    }

    private async void AddServer_Click(object sender, RoutedEventArgs e)
    {
        if (await SubsonicServerDialog.PromptAsync(XamlRoot) is { } added) ViewModel.SaveServer(added);
    }

    /// <summary>
    /// The 编辑 button of a server row. The entry rides in the button's Tag: the expander's ItemsRepeater does not
    /// set DataContext on an x:Bind template, so the data context is empty here.
    /// </summary>
    private async void EditServer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SubsonicServer server) return;
        if (await SubsonicServerDialog.PromptAsync(XamlRoot, server) is { } edited) ViewModel.SaveServer(edited);
    }

    private async void SourceLogin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = LoginDialog.ForSourceAccount();
        dialog.XamlRoot = XamlRoot;
        await dialog.ShowAsync();
        ViewModel.RefreshSourceAccount();
    }
}
