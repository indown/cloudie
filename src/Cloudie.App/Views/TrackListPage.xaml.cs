using Cloudie.App.ViewModels;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Cloudie.App.Views;

/// <summary>Playlist, album, artist and daily-recommendation pages share this layout.</summary>
public sealed partial class TrackListPage : Page
{
    public TrackListViewModel ViewModel { get; } = new();

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
    }

    /// <summary>
    /// The album strip swallows the mouse wheel (a nested ScrollViewer does not chain it to the page), so a plain
    /// wheel over it is passed on to the song list. Shift+wheel and sideways wheels stay with the strip.
    /// </summary>
    private void Albums_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsHorizontalMouseWheel || e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)) return;
        List.ScrollByWheel(properties.MouseWheelDelta);
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
