using Cloudie.App.Services;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

/// <summary>Create / rename / delete dialogs for the account's own playlists; outcomes go to the status bar.</summary>
public static class PlaylistDialogs
{
    public static async Task<PlaylistSummary?> CreateAsync(XamlRoot root)
    {
        var name = new TextBox { PlaceholderText = "歌单名称", MaxLength = 40 };
        var isPrivate = new CheckBox { Content = "设为隐私歌单" };
        var dialog = new ContentDialog
        {
            Title = "新建歌单",
            Content = new StackPanel { Spacing = 12, MinWidth = 320, Children = { name, isPrivate } },
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var text = name.Text.Trim();
        if (text.Length == 0) return null;
        try
        {
            var playlist = await App.Services.Account.CreatePlaylistAsync(text, isPrivate.IsChecked == true);
            Status.Show($"已创建《{text}》");
            return playlist;
        }
        catch (Exception error)
        {
            Status.Show("创建失败：" + error.Message);
            return null;
        }
    }

    /// <summary>Returns the new name, or null when cancelled, unchanged or failed.</summary>
    public static async Task<string?> RenameAsync(XamlRoot root, long playlistId, string currentName)
    {
        var name = new TextBox { Text = currentName, MaxLength = 40 };
        var dialog = new ContentDialog
        {
            Title = "重命名歌单",
            Content = name,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var text = name.Text.Trim();
        if (text.Length == 0 || text == currentName) return null;
        try
        {
            await App.Services.Account.RenamePlaylistAsync(playlistId, text);
            Status.Show($"已重命名为《{text}》");
            return text;
        }
        catch (Exception error)
        {
            Status.Show("重命名失败：" + error.Message);
            return null;
        }
    }

    public static async Task<bool> DeleteAsync(XamlRoot root, long playlistId, string name)
    {
        var dialog = new ContentDialog
        {
            Title = "删除歌单",
            Content = $"删除《{name}》？歌单里的歌曲本身不会被删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        try
        {
            await App.Services.Account.DeletePlaylistAsync(playlistId);
            Status.Show($"已删除《{name}》");
            return true;
        }
        catch (Exception error)
        {
            Status.Show("删除失败：" + error.Message);
            return false;
        }
    }

    private static StatusService Status => App.Services.Status;
}
