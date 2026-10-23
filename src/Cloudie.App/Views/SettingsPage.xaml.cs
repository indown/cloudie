using Cloudie.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage() => InitializeComponent();

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        ViewModel.Refresh();
    }
}
