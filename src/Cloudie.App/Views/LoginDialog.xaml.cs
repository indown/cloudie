using Cloudie.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

public sealed partial class LoginDialog : ContentDialog
{
    public LoginViewModel ViewModel { get; }

    /// <summary>Signs in the main account.</summary>
    public LoginDialog()
        : this(new LoginViewModel(App.Services.Api, () => App.Services.Account.ReloadAsync(), "用网易云音乐 App 扫码登录"), "登录网易云音乐")
    {
    }

    /// <summary>Signs in 音源账号, the second account that only fetches song URLs.</summary>
    public static LoginDialog ForSourceAccount() =>
        new(new LoginViewModel(App.Services.SourceAccount.Api, () => App.Services.SourceAccount.ReloadAsync(), "请用有黑胶 VIP 的那个账号扫码"), "登录音源账号");

    private LoginDialog(LoginViewModel viewModel, string title)
    {
        ViewModel = viewModel;   // before InitializeComponent, so the bindings find it
        InitializeComponent();
        Title = title;
        Opened += (_, _) => _ = ViewModel.StartAsync();
        Closed += (_, _) => ViewModel.Stop();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginViewModel.IsDone) && ViewModel.IsDone) Hide();
        };
    }
}
