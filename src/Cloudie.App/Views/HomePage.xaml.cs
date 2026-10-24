using Cloudie.App.ViewModels;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Cloudie.App.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = new();
    private bool _loaded;

    public HomePage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_loaded) return;
        _loaded = true;
        await ViewModel.LoadInitialAsync();
        // From here on a sign-in or sign-out rebuilds the page.
        App.Services.Account.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(Services.AccountService.Profile)) _ = ViewModel.LoadAsync();
        };
    }

    private void Daily_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track) _ = ViewModel.PlayDailySongsAsync(track);
    }

    private void AllDaily_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(TrackListPage), TrackListRequest.DailySongs());

    private void Playlist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistSummary playlist) Frame.Navigate(typeof(TrackListPage), TrackListRequest.Playlist(playlist.Id, playlist.Name));
    }

    private void Toplist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToplistItem toplist) Frame.Navigate(typeof(TrackListPage), TrackListRequest.Playlist(toplist.Id, toplist.Name));
    }
}
