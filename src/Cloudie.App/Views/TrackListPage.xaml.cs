using Cloudie.App.Controls;
using Cloudie.App.ViewModels;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Cloudie.App.Views;

/// <summary>Playlist, album, artist and daily-recommendation pages share this layout.</summary>
public sealed partial class TrackListPage : Page
{
    public TrackListViewModel ViewModel { get; } = new();

    private bool _compactShown;
    private ScrollViewer? _stripScroller;
    private double? _stripTarget;
    /// <summary>What Shift+wheel moves the album strip per notch: one card (CoverCard Size 120 plus its 8 px host margin).</summary>
    private const double AlbumCardWidth = 128;

    public TrackListPage()
    {
        InitializeComponent();
        List.PlayRequested += row => ViewModel.PlayRowCommand.Execute(row);
        List.PlayNextRequested += row => ViewModel.PlayNextCommand.Execute(row);
        List.LikeRequested += row => ViewModel.ToggleLikeCommand.Execute(row);
        List.RemoveRequested += row => ViewModel.RemoveRowCommand.Execute(row);
        List.HeartModeRequested += row => ViewModel.StartHeartModeFromCommand.Execute(row);
        List.NavigateRequested += request => Frame.Navigate(typeof(TrackListPage), request);
        // handledEventsToo: the strip's own ScrollViewer has already marked the wheel handled by the time it gets here.
        Albums.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Albums_PointerWheelChanged), handledEventsToo: true);
        List.Scrolled += offset => ShowCompactBar(offset > TitleBlock.ActualHeight + List.Padding.Top);
    }

    /// <summary>
    /// 紧凑栏: the title and 播放全部 fade in over the rows once the title block has scrolled out of view, so a
    /// long list keeps its name and its play button, and fade out again at the top.
    /// </summary>
    private void ShowCompactBar(bool show)
    {
        if (show == _compactShown) return;
        _compactShown = show;
        CompactBar.IsHitTestVisible = show;
        var fade = CompositionTarget.GetCompositorForCurrentThread().CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(1f, show ? 1f : 0f);
        fade.Duration = TimeSpan.FromMilliseconds(160);
        CompactBar.StartAnimation(fade);
    }

    /// <summary>
    /// The album strip swallows the mouse wheel (a nested ScrollViewer does not chain it to the page, and the
    /// classic ScrollViewer knows no Shift+wheel), so the page deals with it: a plain wheel scrolls the song
    /// list, Shift+wheel scrolls the strip sideways. A sideways (tilt) wheel is left to the strip.
    /// </summary>
    private void Albums_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsHorizontalMouseWheel) return;
        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)) ScrollStripByWheel(properties.MouseWheelDelta);
        else List.ScrollByWheel(properties.MouseWheelDelta);
    }

    /// <summary>One card per notch; wheel up goes left, as Shift+wheel does elsewhere in Windows. Quick notches add up.</summary>
    private void ScrollStripByWheel(int delta)
    {
        if (_stripScroller is null)
        {
            _stripScroller = Albums.FindDescendant<ScrollViewer>();
            if (_stripScroller is null) return;
            _stripScroller.ViewChanged += (_, e) =>
            {
                if (!e.IsIntermediate) _stripTarget = null;
            };
        }
        var from = _stripTarget ?? _stripScroller.HorizontalOffset;
        var target = Math.Clamp(from - delta / 120.0 * AlbumCardWidth, 0, _stripScroller.ScrollableWidth);
        _stripTarget = target;
        _stripScroller.ChangeView(target, null, null, disableAnimation: false);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is TrackListRequest request) await ViewModel.LoadAsync(request);
    }

    private void Album_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlbumSummary album) Frame.Navigate(typeof(TrackListPage), TrackListRequest.Album(album.Id, album.Name));
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var name = await PlaylistDialogs.RenameAsync(XamlRoot, ViewModel.PlaylistId, ViewModel.Title);
        if (name is not null) ViewModel.ApplyName(name);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!await PlaylistDialogs.DeleteAsync(XamlRoot, ViewModel.PlaylistId, ViewModel.Title)) return;
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(HomePage));
    }
}
