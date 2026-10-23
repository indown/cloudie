using System.Collections.ObjectModel;
using Cloudie.App.ViewModels;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Cloudie.App.Controls;

/// <summary>The song table used by playlist, album, artist, daily and search pages.</summary>
public sealed partial class TrackList : UserControl
{
    private const string RemoveMenuText = "从歌单中移除";

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(ObservableCollection<TrackRow>), typeof(TrackList), new PropertyMetadata(null));

    public static readonly DependencyProperty CanRemoveProperty = DependencyProperty.Register(
        nameof(CanRemove), typeof(bool), typeof(TrackList), new PropertyMetadata(false));

    public ObservableCollection<TrackRow>? Rows
    {
        get => (ObservableCollection<TrackRow>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>True on one of the account's own playlists, where rows may be removed.</summary>
    public bool CanRemove
    {
        get => (bool)GetValue(CanRemoveProperty);
        set => SetValue(CanRemoveProperty, value);
    }

    /// <summary>Raised when the user wants a row played (double-click, menu).</summary>
    public event Action<TrackRow>? PlayRequested;
    public event Action<TrackRow>? PlayNextRequested;
    public event Action<TrackRow>? LikeRequested;
    public event Action<TrackRow>? RemoveRequested;
    /// <summary>Raised from the context menu to open the row's artist or album page.</summary>
    public event Action<TrackListRequest>? NavigateRequested;

    public TrackList() => InitializeComponent();

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        // Single click selects; playback starts on double-click.
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TrackRow row) PlayRequested?.Invoke(row);
    }

    private void PlayMenu_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) PlayRequested?.Invoke(row);
    }

    private void PlayNextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) PlayNextRequested?.Invoke(row);
    }

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) LikeRequested?.Invoke(row);
    }

    private void ArtistMenu_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && TrackListRequest.ForArtistOf(row.Track) is { } request) NavigateRequested?.Invoke(request);
    }

    private void AlbumMenu_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && TrackListRequest.ForAlbumOf(row.Track) is { } request) NavigateRequested?.Invoke(request);
    }

    private void RemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) RemoveRequested?.Invoke(row);
    }

    /// <summary>Fills the "添加到歌单" submenu with the account's playlists and shows "移除" only where allowed.</summary>
    private void RowMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout) return;
        var row = (flyout.Target as FrameworkElement)?.DataContext as TrackRow;
        foreach (var item in flyout.Items)
        {
            switch (item)
            {
                case MenuFlyoutSubItem { Tag: "add-to-playlist" } submenu:
                    FillAddToPlaylist(submenu, row);
                    break;
                case MenuFlyoutItem { Text: RemoveMenuText } remove:
                    remove.Visibility = CanRemove ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }
    }

    private static void FillAddToPlaylist(MenuFlyoutSubItem submenu, TrackRow? row)
    {
        submenu.Items.Clear();
        var account = App.Services.Account;
        if (!account.IsLoggedIn)
        {
            submenu.Items.Add(new MenuFlyoutItem { Text = "登录后可用", IsEnabled = false });
            return;
        }
        var playlists = account.OwnPlaylists;
        if (playlists.Count == 0)
        {
            submenu.Items.Add(new MenuFlyoutItem { Text = "还没有自己的歌单", IsEnabled = false });
            return;
        }
        foreach (var playlist in playlists)
        {
            var target = playlist;
            var item = new MenuFlyoutItem { Text = playlist.Name };
            item.Click += (_, _) =>
            {
                if (row is not null) _ = AddToPlaylistAsync(row, target);
            };
            submenu.Items.Add(item);
        }
    }

    private static async Task AddToPlaylistAsync(TrackRow row, PlaylistSummary playlist)
    {
        try
        {
            await App.Services.Account.AddToPlaylistAsync(playlist.Id, row.Track.Id);
            App.Services.Status.Show($"已添加到《{playlist.Name}》");
        }
        catch (NeteaseApiException error) when (error.Code == 502)
        {
            App.Services.Status.Show($"《{row.Track.Name}》已经在《{playlist.Name}》里了");
        }
        catch (Exception error)
        {
            App.Services.Status.Show("添加失败：" + error.Message);
        }
    }

    private static TrackRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as TrackRow;
}
