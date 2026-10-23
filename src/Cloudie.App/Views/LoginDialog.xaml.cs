using Cloudie.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

public sealed partial class LoginDialog : ContentDialog
{
    public LoginViewModel ViewModel { get; } = new();

    public LoginDialog()
    {
        InitializeComponent();
        Opened += (_, _) => _ = ViewModel.StartAsync();
        Closed += (_, _) => ViewModel.Stop();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginViewModel.IsDone) && ViewModel.IsDone) Hide();
        };
    }
}
