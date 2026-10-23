using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.App.ViewModels;

public enum TrackListKind { Playlist, Album, Artist, DailySongs }

/// <summary>What a track-list page should load; passed as the navigation parameter.</summary>
public sealed record TrackListRequest(TrackListKind Kind, long Id, string? Title)
{
    public static TrackListRequest Playlist(long id, string? title = null) => new(TrackListKind.Playlist, id, title);
    public static TrackListRequest Album(long id, string? title = null) => new(TrackListKind.Album, id, title);
    public static TrackListRequest Artist(long id, string? title = null) => new(TrackListKind.Artist, id, title);
    public static TrackListRequest DailySongs() => new(TrackListKind.DailySongs, 0, "每日推荐");

    /// <summary>The page for the track's first artist, or null when NetEase lists none.</summary>
    public static TrackListRequest? ForArtistOf(Track track) =>
        track.Artists.FirstOrDefault() is { Id: > 0 } artist ? Artist(artist.Id, artist.Name) : null;

    public static TrackListRequest? ForAlbumOf(Track track) =>
        track.Album.Id > 0 ? Album(track.Album.Id, track.Album.Name) : null;
}

/// <summary>A song row as shown in lists: the track plus what the account may do with it.</summary>
public sealed partial class TrackRow(Track track, TrackPlayability playability, int number) : ObservableObject
{
    public Track Track { get; } = track;
    public int Number { get; } = number;
    public string Name => Track.Name;
    public string? Subtitle => Track.Subtitle;
    public string Artists => Track.ArtistNames;
    public string Album => Track.Album.Name;
    public string? CoverUrl => Track.Album.PicUrl;
    public TimeSpan Duration => Track.Duration;
    public TrackPlayability Playability { get; } = playability;
    public bool IsGray => Playability is not TrackPlayability.Playable;
    public string? GrayReason => Playability.Reason();
    public double Opacity => IsGray ? 0.6 : 1.0;
    [ObservableProperty] public partial bool IsLiked { get; set; }
    [ObservableProperty] public partial bool IsCurrent { get; set; }
}

public sealed partial class TrackListViewModel : ObservableObject
{
    private readonly NeteaseApi _api = App.Services.Api;
    private readonly AccountService _account = App.Services.Account;
    private readonly PlayerService _player = App.Services.Player;
    private readonly LogService _log = App.Services.Log;
    private readonly List<Track> _tracks = [];
    private long _sourceId;

    public ObservableCollection<TrackRow> Rows { get; } = [];
    /// <summary>Artist pages also list the artist's albums.</summary>
    public ObservableCollection<AlbumSummary> Albums { get; } = [];

    [ObservableProperty] public partial string Title { get; private set; } = string.Empty;
    [ObservableProperty] public partial string? Subtitle { get; private set; }
    [ObservableProperty] public partial string? Description { get; private set; }
    [ObservableProperty] public partial string? CoverUrl { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] public partial string? Error { get; private set; }
    public bool HasError => Error is { Length: > 0 };
    [ObservableProperty] public partial string Summary { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool HasAlbums { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubscribeLabel), nameof(SubscribeGlyph))] public partial bool IsSubscribed { get; private set; }
    /// <summary>A playlist that is not the account's own, viewed while signed in.</summary>
    [ObservableProperty] public partial bool CanSubscribe { get; private set; }
    [ObservableProperty] public partial bool IsOwnPlaylist { get; private set; }
    /// <summary>"我喜欢的音乐" while signed in: 心动模式 can start from any of its songs.</summary>
    [ObservableProperty] public partial bool CanHeartMode { get; private set; }
    /// <summary>One of the account's own playlists other than "我喜欢的音乐": it can be renamed or deleted.</summary>
    [ObservableProperty] public partial bool CanEditPlaylist { get; private set; }
    public long PlaylistId => _playlistId;
    public string SubscribeLabel => IsSubscribed ? "已收藏" : "收藏歌单";
    public string SubscribeGlyph => IsSubscribed ? "" : "";
    private long _playlistId;

    public TrackListViewModel()
    {
        _player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerService.CurrentTrack)) MarkCurrent();
        };
        _account.LikedSongsChanged += (_, _) =>
        {
            foreach (var row in Rows) row.IsLiked = _account.IsLiked(row.Track.Id);
        };
    }

    public async Task LoadAsync(TrackListRequest request)
    {
        IsLoading = true;
        Error = null;
        Title = request.Title ?? string.Empty;
        _sourceId = request.Kind == TrackListKind.DailySongs ? 0 : request.Id;
        Albums.Clear();
        HasAlbums = false;
        _playlistId = request.Kind == TrackListKind.Playlist ? request.Id : 0;
        IsOwnPlaylist = false;
        CanSubscribe = false;
        IsSubscribed = false;
        CanHeartMode = false;
        CanEditPlaylist = false;
        try
        {
            switch (request.Kind)
            {
                case TrackListKind.Playlist:
                    await LoadPlaylistAsync(request.Id);
                    break;
                case TrackListKind.Album:
                    await LoadAlbumAsync(request.Id);
                    break;
                case TrackListKind.Artist:
                    await LoadArtistAsync(request.Id);
                    break;
                case TrackListKind.DailySongs:
                    SetTracks(await _api.GetDailyRecommendSongsAsync(), null);
                    Title = "每日推荐";
                    Subtitle = DateTime.Now.ToString("M月d日");
                    break;
            }
        }
        catch (Exception error)
        {
            _log.Error($"track list {request}: {error}");
            Error = error is NeteaseApiException { NeedsLogin: true } ? "需要登录后查看" : error.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPlaylistAsync(long id)
    {
        var detail = await _api.GetPlaylistDetailAsync(id);
        var playlist = detail.Playlist;
        Title = playlist.Name;
        Subtitle = playlist.Creator is { } creator ? $"{creator.Nickname} · {playlist.TrackCount} 首" : $"{playlist.TrackCount} 首";
        Description = playlist.Description;
        CoverUrl = playlist.CoverImgUrl;
        IsOwnPlaylist = _account.IsOwn(id);
        CanSubscribe = _account.IsLoggedIn && !IsOwnPlaylist;
        IsSubscribed = playlist.Subscribed || _account.IsSubscribed(id);
        CanHeartMode = IsOwnPlaylist && playlist.SpecialType == 5;
        CanEditPlaylist = IsOwnPlaylist && playlist.SpecialType != 5;
        var tracks = playlist.Tracks;
        var privileges = detail.Privileges;
        // v6 returns the first thousand tracks inline; the rest come from song/detail in pages.
        if (playlist.TrackIds.Count > tracks.Count)
        {
            var missing = playlist.TrackIds.Skip(tracks.Count).Select(static t => t.Id).ToList();
            var fetched = new List<Track>();
            var fetchedPrivileges = new List<TrackPrivilege>();
            foreach (var chunk in missing.Chunk(500))
            {
                var page = await _api.GetSongDetailsAsync(chunk);
                fetched.AddRange(page.Songs);
                if (page.Privileges is not null) fetchedPrivileges.AddRange(page.Privileges);
            }
            tracks = [.. tracks, .. fetched];
            privileges = [.. privileges ?? [], .. fetchedPrivileges];
        }
        SetTracks(tracks, privileges);
    }

    private async Task LoadAlbumAsync(long id)
    {
        var response = await _api.GetAlbumAsync(id);
        Title = response.Album?.Name ?? Title;
        Subtitle = response.Album is { } album ? $"{album.ArtistName} · {album.PublishDate?.Year.ToString() ?? ""}" : null;
        Description = response.Album?.Description;
        CoverUrl = response.Album?.PicUrl;
        SetTracks(response.Songs, null);
    }

    private async Task LoadArtistAsync(long id)
    {
        var response = await _api.GetArtistAsync(id);
        Title = response.Artist?.Name ?? Title;
        Subtitle = response.Artist is { } artist ? $"{artist.MusicSize} 首歌曲 · {artist.AlbumSize} 张专辑" : null;
        Description = response.Artist?.BriefDesc;
        CoverUrl = response.Artist?.ImageUrl;
        SetTracks(response.HotSongs, null);
        try
        {
            var albums = await _api.GetArtistAlbumsAsync(id, limit: 30);
            foreach (var album in albums.HotAlbums) Albums.Add(album);
        }
        catch (Exception error)
        {
            _log.Warn($"artist albums {id}: {error.Message}");
        }
        HasAlbums = Albums.Count > 0;
    }

    private void SetTracks(IReadOnlyList<Track> tracks, IReadOnlyList<TrackPrivilege>? privileges)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        var byId = privileges?.ToDictionary(static p => p.Id) ?? new Dictionary<long, TrackPrivilege>();
        Rows.Clear();
        var number = 1;
        foreach (var track in tracks)
        {
            var playability = track.GetPlayability(byId.GetValueOrDefault(track.Id), _account.IsLoggedIn, _account.VipType);
            Rows.Add(new TrackRow(track, playability, number++) { IsLiked = _account.IsLiked(track.Id) });
        }
        var total = TimeSpan.FromMilliseconds(tracks.Sum(static t => (double)t.DurationMs));
        Summary = $"{tracks.Count} 首歌曲，{(int)total.TotalHours} 小时 {total.Minutes} 分钟";
        MarkCurrent();
    }

    private void MarkCurrent()
    {
        var currentId = _player.CurrentTrack?.Id;
        foreach (var row in Rows) row.IsCurrent = row.Track.Id == currentId;
    }

    [RelayCommand]
    private Task PlayAll() => _tracks.Count == 0 ? Task.CompletedTask : _player.PlayAsync(_tracks, 0, _sourceId);

    [RelayCommand]
    private Task PlayRow(TrackRow? row) =>
        row is null ? Task.CompletedTask : _player.PlayAsync(_tracks, _tracks.FindIndex(t => t.Id == row.Track.Id), _sourceId);

    [RelayCommand]
    private void PlayNext(TrackRow? row)
    {
        if (row is not null) _player.PlayNext(row.Track);
    }

    /// <summary>After a rename elsewhere in the window.</summary>
    public void ApplyName(string name) => Title = name;

    [RelayCommand]
    private Task StartHeartMode() =>
        _tracks.Count == 0 || !CanHeartMode ? Task.CompletedTask : _player.StartHeartModeAsync(_tracks[Random.Shared.Next(_tracks.Count)]);

    [RelayCommand]
    private Task StartHeartModeFrom(TrackRow? row) =>
        row is null || !CanHeartMode ? Task.CompletedTask : _player.StartHeartModeAsync(row.Track);

    [RelayCommand]
    private async Task ToggleSubscribe()
    {
        if (_playlistId == 0 || !CanSubscribe) return;
        try
        {
            await _account.SubscribePlaylistAsync(_playlistId, !IsSubscribed);
            IsSubscribed = !IsSubscribed;
            App.Services.Status.Show(IsSubscribed ? "已收藏到我的歌单" : "已取消收藏");
        }
        catch (Exception error)
        {
            App.Services.Status.Show("操作失败：" + error.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveRow(TrackRow? row)
    {
        if (row is null || !IsOwnPlaylist) return;
        try
        {
            await _account.RemoveFromPlaylistAsync(_playlistId, row.Track.Id);
            Rows.Remove(row);
            _tracks.RemoveAll(t => t.Id == row.Track.Id);
            var total = TimeSpan.FromMilliseconds(_tracks.Sum(static t => (double)t.DurationMs));
            Summary = $"{_tracks.Count} 首歌曲，{(int)total.TotalHours} 小时 {total.Minutes} 分钟";
            App.Services.Status.Show($"已从歌单移除《{row.Track.Name}》");
        }
        catch (Exception error)
        {
            App.Services.Status.Show("移除失败：" + error.Message);
        }
    }

    [RelayCommand]
    private async Task ToggleLike(TrackRow? row)
    {
        if (row is null || !_account.IsLoggedIn) return;
        try
        {
            await _account.ToggleLikeAsync(row.Track.Id);
        }
        catch (Exception error)
        {
            _log.Warn($"like {row.Track.Id}: {error.Message}");
        }
    }
}
