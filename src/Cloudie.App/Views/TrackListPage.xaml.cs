using Cloudie.App.ViewModels;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml.Controls;
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
}
