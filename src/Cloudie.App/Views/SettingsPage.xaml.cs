using Cloudie.App.ViewModels;
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

    private async void SourceLogin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = LoginDialog.ForSourceAccount();
        dialog.XamlRoot = XamlRoot;
        await dialog.ShowAsync();
        ViewModel.RefreshSourceAccount();
    }
}
