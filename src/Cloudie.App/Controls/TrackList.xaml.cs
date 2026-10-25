using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Cloudie.App.Services;
using Cloudie.App.ViewModels;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Cloudie.App.Controls;

/// <summary>The song table used by playlist, album, artist, daily and search pages.</summary>
public sealed partial class TrackList : UserControl
{
    private const string RemoveMenuText = "从歌单中移除";
    private const string HeartModeMenuText = "心动模式";

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(ObservableCollection<TrackRow>), typeof(TrackList), new PropertyMetadata(null, (d, e) => ((TrackList)d).OnRowsChanged(e)));

    /// <summary>
    /// Shows the "定位到正在播放" button when the playing song's row has scrolled out of view. Only for pages
    /// where this list is the scrolling element (the search page scrolls as a whole, so it leaves this off).
    /// </summary>
    public static readonly DependencyProperty CanLocateProperty = DependencyProperty.Register(
        nameof(CanLocate), typeof(bool), typeof(TrackList), new PropertyMetadata(false, (d, _) => ((TrackList)d).UpdateLocate()));

    public static readonly DependencyProperty CanRemoveProperty = DependencyProperty.Register(
        nameof(CanRemove), typeof(bool), typeof(TrackList), new PropertyMetadata(false));

    public static readonly DependencyProperty CanHeartModeProperty = DependencyProperty.Register(
        nameof(CanHeartMode), typeof(bool), typeof(TrackList), new PropertyMetadata(false));

    /// <summary>
    /// Content above the first row that scrolls with it (the page header: cover, title, actions), so a long
    /// list gets the whole height once the user scrolls. Fixed above the list, it left two rows on an artist page.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(TrackList), new PropertyMetadata(null));

    public ObservableCollection<TrackRow>? Rows
    {
        get => (ObservableCollection<TrackRow>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public bool CanLocate
    {
        get => (bool)GetValue(CanLocateProperty);
        set => SetValue(CanLocateProperty, value);
    }

    /// <summary>True on one of the account's own playlists, where rows may be removed.</summary>
    public bool CanRemove
    {
        get => (bool)GetValue(CanRemoveProperty);
        set => SetValue(CanRemoveProperty, value);
    }

    /// <summary>True on "我喜欢的音乐", where 心动模式 can start from a row.</summary>
    public bool CanHeartMode
    {
        get => (bool)GetValue(CanHeartModeProperty);
        set => SetValue(CanHeartModeProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Raised when the user wants a row played (double-click, menu).</summary>
    public event Action<TrackRow>? PlayRequested;
    public event Action<TrackRow>? PlayNextRequested;
    public event Action<TrackRow>? LikeRequested;
    public event Action<TrackRow>? RemoveRequested;
    public event Action<TrackRow>? HeartModeRequested;
    /// <summary>Raised from the context menu to open the row's artist or album page.</summary>
    public event Action<TrackListRequest>? NavigateRequested;
    /// <summary>The list scrolled (the value is the vertical offset); also raised once when the list is ready.</summary>
    public event Action<double>? Scrolled;

    private ScrollViewer? _scroller;
    private bool _locateShown;
    /// <summary>Where wheel scrolling handed over from the header is heading, so quick notches add up while the view animates.</summary>
    private double? _wheelTarget;
    /// <summary>What one wheel notch moves the list, measured against the list's own wheel scrolling (Windows' default three lines).</summary>
    private const double WheelStep = 92;
    /// <summary>The row whose context menu is open; the menu is shared by all rows.</summary>
    private TrackRow? _menuRow;
    private TrackRow? _currentRow;

    public TrackList()
    {
        InitializeComponent();
        List.Loaded += (_, _) =>
        {
            _scroller ??= List.FindDescendant<ScrollViewer>();
            if (_scroller is { } scroller)
            {
                scroller.ViewChanged += (_, e) =>
                {
                    if (!e.IsIntermediate) _wheelTarget = null;
                    UpdateLocate();
                    Scrolled?.Invoke(scroller.VerticalOffset);
                };
                Scrolled?.Invoke(scroller.VerticalOffset);
            }
            UpdateLocate();
        };
        // MarkCurrent in the view-model runs on the same notification, before this (it subscribed earlier);
        // deferring once more makes sure the rows carry the new IsCurrent flags.
        App.Services.Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerService.CurrentTrack)) DispatcherQueue.TryEnqueue(RefreshCurrentRow);
        };
    }

    /// <summary>
    /// Scrolls the list by a mouse-wheel delta (120 per notch, positive = towards the top) on behalf of a part
    /// of the header that swallows the wheel itself: a nested ScrollViewer, like the album strip, takes the
    /// event and does not pass it on to the page even when it has nothing to scroll that way.
    /// </summary>
    public void ScrollByWheel(int delta)
    {
        if (_scroller is null) return;
        var from = _wheelTarget ?? _scroller.VerticalOffset;
        var target = Math.Clamp(from - delta / 120.0 * WheelStep, 0, _scroller.ScrollableHeight);
        _wheelTarget = target;
        _scroller.ChangeView(null, target, null, disableAnimation: false);
    }

    // MARK: 定位到正在播放

    private void OnRowsChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObservableCollection<TrackRow> old) old.CollectionChanged -= Rows_CollectionChanged;
        if (e.NewValue is ObservableCollection<TrackRow> rows) rows.CollectionChanged += Rows_CollectionChanged;
        DispatcherQueue.TryEnqueue(RefreshCurrentRow);
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => DispatcherQueue.TryEnqueue(RefreshCurrentRow);

    /// <summary>Scans the rows once per change of song or rows; the scroll handler then only looks at this one.</summary>
    private void RefreshCurrentRow()
    {
        _currentRow = Rows?.FirstOrDefault(static r => r.IsCurrent);
        UpdateLocate();
    }

    /// <summary>The button is wanted when the playing song is in this list but its row is not on screen.</summary>
    private void UpdateLocate()
    {
        var row = CanLocate ? _currentRow : null;
        var show = row is not null && !IsInView(row);
        if (show == _locateShown) return;
        _locateShown = show;
        LocateButton.IsHitTestVisible = show;
        var fade = CompositionTarget.GetCompositorForCurrentThread().CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(1f, show ? 1f : 0f);
        fade.Duration = TimeSpan.FromMilliseconds(160);
        LocateButton.StartAnimation(fade);
    }

    private bool IsInView(TrackRow row)
    {
        if (_scroller is null) return true;
        // A virtualised-away row has no container: it is certainly off screen.
        if (List.ContainerFromItem(row) is not FrameworkElement container) return false;
        var top = container.TransformToVisual(_scroller).TransformPoint(new Point(0, 0)).Y;
        return top + container.ActualHeight > 0 && top < _scroller.ViewportHeight;
    }

    /// <summary>Brings the playing song's row to the middle of the view.</summary>
    private async void Locate_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRow is not { } row || _scroller is null) return;
        List.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
        await Task.Delay(60);   // the container exists once the jump has been laid out
        if (List.ContainerFromItem(row) is FrameworkElement container)
        {
            var top = container.TransformToVisual(_scroller).TransformPoint(new Point(0, 0)).Y + _scroller.VerticalOffset;
            var centred = top - (_scroller.ViewportHeight - container.ActualHeight) / 2;
            _scroller.ChangeView(null, Math.Max(0, centred), null, disableAnimation: false);
        }
    }

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
        if (_menuRow is { } row) PlayRequested?.Invoke(row);
    }

    private void PlayNextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) PlayNextRequested?.Invoke(row);
    }

    /// <summary>The heart button in the row (its Tag is the row).</summary>
    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) LikeRequested?.Invoke(row);
    }

    private void LikeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) LikeRequested?.Invoke(row);
    }

    private void ArtistMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row && TrackListRequest.ForArtistOf(row.Track) is { } request) NavigateRequested?.Invoke(request);
    }

    private void AlbumMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row && TrackListRequest.ForAlbumOf(row.Track) is { } request) NavigateRequested?.Invoke(request);
    }

    private void RemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) RemoveRequested?.Invoke(row);
    }

    private void HeartModeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_menuRow is { } row) HeartModeRequested?.Invoke(row);
    }

    /// <summary>
    /// Remembers which row opened the shared menu, fills the "添加到歌单" submenu with the account's playlists
    /// and shows "移除" / "心动模式" only where allowed.
    /// </summary>
    private void RowMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout) return;
        var row = _menuRow = (flyout.Target as FrameworkElement)?.DataContext as TrackRow;
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
                case MenuFlyoutItem { Text: HeartModeMenuText } heart:
                    heart.Visibility = CanHeartMode ? Visibility.Visible : Visibility.Collapsed;
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
