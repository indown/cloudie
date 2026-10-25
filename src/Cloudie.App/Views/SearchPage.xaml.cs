using System.ComponentModel;
using Cloudie.App.ViewModels;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Cloudie.App.Views;

public sealed partial class SearchPage : Page, INotifyPropertyChanged
{
    public SearchViewModel ViewModel { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasArtists => ViewModel.Artists.Count > 0;
    public bool HasAlbums => ViewModel.Albums.Count > 0;
    public bool HasPlaylists => ViewModel.Playlists.Count > 0;

    public SearchPage()
    {
        InitializeComponent();
        List.PlayRequested += row => _ = ViewModel.PlayAsync(row);
        List.PlayNextRequested += row => App.Services.Player.PlayNext(row.Track);
        List.LikeRequested += row => _ = ToggleLikeAsync(row);
        List.NavigateRequested += request => Frame.Navigate(typeof(TrackListPage), request);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SearchViewModel.IsLoading)) return;
            foreach (var name in new[] { nameof(HasArtists), nameof(HasAlbums), nameof(HasPlaylists) })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query) await ViewModel.SearchAsync(query);
    }

    private void Artist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistSummary artist) Frame.Navigate(typeof(TrackListPage), TrackListRequest.Artist(artist.Id, artist.Name));
    }

    private void Album_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlbumSummary album) Frame.Navigate(typeof(TrackListPage), TrackListRequest.Album(album.Id, album.Name));
    }

    private void Playlist_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistSummary playlist) Frame.Navigate(typeof(TrackListPage), TrackListRequest.Playlist(playlist.Id, playlist.Name));
    }

    /// <summary>The heart flips at once (the account service is optimistic); a failure flips it back and says so.</summary>
    private static async Task ToggleLikeAsync(Cloudie.App.ViewModels.TrackRow row)
    {
        try
        {
            await App.Services.Account.ToggleLikeAsync(row.Track.Id);
        }
        catch (Exception error)
        {
            App.Services.Status.Show("操作失败：" + Cloudie.App.Services.ErrorText.Describe(error));
        }
    }
}
