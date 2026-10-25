using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.App.ViewModels;

public sealed partial class SearchViewModel : ObservableObject
{
    private readonly NeteaseApi _api = App.Services.Api;
    private readonly AccountService _account = App.Services.Account;
    private readonly PlayerService _player = App.Services.Player;
    private readonly List<Track> _songs = [];

    public ObservableCollection<TrackRow> Songs { get; } = [];
    public ObservableCollection<AlbumSummary> Albums { get; } = [];
    public ObservableCollection<ArtistSummary> Artists { get; } = [];
    public ObservableCollection<PlaylistSummary> Playlists { get; } = [];

    [ObservableProperty] public partial string Query { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] public partial string? Error { get; private set; }
    public bool HasError => Error is { Length: > 0 };

    public async Task SearchAsync(string query)
    {
        Query = query;
        IsLoading = true;
        Error = null;
        try
        {
            var songs = _api.SearchAsync(query, SearchType.Song, 30);
            var albums = _api.SearchAsync(query, SearchType.Album, 8);
            var artists = _api.SearchAsync(query, SearchType.Artist, 8);
            var playlists = _api.SearchAsync(query, SearchType.Playlist, 8);
            await Task.WhenAll(songs, albums, artists, playlists);
            _songs.Clear();
            _songs.AddRange(songs.Result.Songs ?? []);
            Songs.Clear();
            var number = 1;
            foreach (var track in _songs)
            {
                var playability = track.GetPlayability(null, _account.IsLoggedIn, _account.VipType);
                Songs.Add(new TrackRow(track, playability, number++) { IsLiked = _account.IsLiked(track.Id) });
            }
            Albums.Clear();
            foreach (var album in albums.Result.Albums ?? []) Albums.Add(album);
            Artists.Clear();
            foreach (var artist in artists.Result.Artists ?? []) Artists.Add(artist);
            Playlists.Clear();
            foreach (var playlist in playlists.Result.Playlists ?? []) Playlists.Add(playlist);
        }
        catch (Exception error)
        {
            Error = ErrorText.Describe(error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task PlayAsync(TrackRow row) => _player.PlayAsync(_songs, _songs.FindIndex(t => t.Id == row.Track.Id));

    /// <summary>The "重试" button of the error bar: the same search again.</summary>
    [RelayCommand]
    private Task Retry() => Query.Length == 0 ? Task.CompletedTask : SearchAsync(Query);
}
