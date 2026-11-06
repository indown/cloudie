using Cloudie.App.ViewModels;
using Cloudie.Core.Unblock;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

/// <summary>Adds or edits one Navidrome server; 保存 stays disabled until address, user name and password are all there.</summary>
public sealed partial class SubsonicServerDialog : ContentDialog
{
    private readonly string _id;
    private readonly bool _enabled;

    /// <summary>What 保存 captured; null until then.</summary>
    public SubsonicServer? Result { get; private set; }

    private SubsonicServerDialog(SubsonicServer? existing)
    {
        InitializeComponent();
        _id = existing?.Id ?? SubsonicServer.NewId();
        _enabled = existing?.Enabled ?? true;
        if (existing is not null)
        {
            Title = "编辑 Navidrome 服务器";
            NameField.Text = existing.Name;
            UrlField.Text = existing.Url;
            UserField.Text = existing.User;
            PasswordField.Password = existing.Password;
        }
        Validate();
    }

    /// <summary>Shows the dialog over <paramref name="root"/>: the saved entry (same id when editing), or null when cancelled.</summary>
    public static async Task<SubsonicServer?> PromptAsync(XamlRoot root, SubsonicServer? existing = null)
    {
        var dialog = new SubsonicServerDialog(existing) { XamlRoot = root };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Result : null;
    }

    private SubsonicServer Draft => new(_id, NameField.Text.Trim(), UrlField.Text.Trim(), UserField.Text.Trim(), PasswordField.Password, _enabled);

    private void Validate() => IsPrimaryButtonEnabled = Draft.Configuration is not null;

    private void Text_Changed(object sender, TextChangedEventArgs e) => Validate();

    private void Password_Changed(object sender, RoutedEventArgs e) => Validate();

    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) => Result = Draft;

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestText.Text = "正在连接…";
        TestText.Text = await SubsonicServerViewModel.DescribeProbeAsync(Draft);
        TestButton.IsEnabled = true;
    }
}
