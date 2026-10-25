using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cloudie.App.Services;
using Cloudie.Core.Library;
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
    /// <summary>
    /// Dimmed only when nothing could play it: with 灰色歌曲解锁 on, the badge is enough and a dimmed row would
    /// read as "cannot be played" although the other sources usually can.
    /// </summary>
    public double Opacity => IsGray && !App.Services.Settings.UnblockEnabled ? 0.6 : 1.0;
    [ObservableProperty] public partial bool IsLiked { get; set; }
    [ObservableProperty] public partial bool IsCurrent { get; set; }
}

public sealed partial class TrackListViewModel : ObservableObject
{
    private readonly NeteaseApi _api = App.Services.Api;
    private readonly AccountService _account = App.Services.Account;
    private readonly PlayerService _player = App.Services.Player;
    private readonly LogService _log = App.Services.Log;
    private readonly TrackListCache _lists = App.Services.Lists;
    private readonly List<Track> _tracks = [];
    private long _sourceId;
    private TrackListRequest? _request;

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
        // The hearts follow the liked set inside TrackList itself, so the search page gets the same behaviour.
    }

    public async Task LoadAsync(TrackListRequest request)
    {
        _request = request;
        Error = null;
        _sourceId = request.Kind == TrackListKind.DailySongs ? 0 : request.Id;
        _playlistId = request.Kind == TrackListKind.Playlist ? request.Id : 0;
        // What the page showed last time goes up at once; the network answers behind it and replaces it only if changed.
        var key = KeyOf(request);
        var cached = _lists.TryGet(key);
        if (cached is not null)
        {
            Apply(cached);
        }
        else
        {
            Reset(request);
            IsLoading = true;
        }
        try
        {
            var fresh = await FetchAsync(request, key, cached);
            if (_request != request) return;
            if (fresh is not null)
            {
                Apply(fresh);
                _lists.Save(fresh);
            }
        }
        catch (Exception error)
        {
            if (_request != request) return;
            if (cached is null)
            {
                _log.Error($"track list {request}: {error}");
                Error = ErrorText.Describe(error);
            }
            else
            {
                // The cached list stays up; a failed refresh is not worth an error bar.
                _log.Warn($"track list {request}: refresh failed, cached copy kept: {error.Message}");
            }
        }
        finally
        {
            if (_request == request) IsLoading = false;
        }
    }

    private static string KeyOf(TrackListRequest request) => request.Kind switch
    {
        TrackListKind.Playlist => CachedTrackList.PlaylistKey(request.Id),
        TrackListKind.Album => CachedTrackList.AlbumKey(request.Id),
        TrackListKind.Artist => CachedTrackList.ArtistKey(request.Id),
        _ => CachedTrackList.DailyKey(HomeSnapshot.RecommendationDay(DateTimeOffset.Now))
    };

    /// <summary>The empty page while a first fetch runs: the name from the request, nothing else.</summary>
    private void Reset(TrackListRequest request)
    {
        Title = request.Title ?? string.Empty;
        Subtitle = null;
        Description = null;
        CoverUrl = null;
        Albums.Clear();
        HasAlbums = false;
        IsOwnPlaylist = false;
        CanSubscribe = false;
        IsSubscribed = false;
        CanHeartMode = false;
        CanEditPlaylist = false;
        _tracks.Clear();
        Rows.Clear();
        Summary = string.Empty;
    }

    /// <summary>Puts a list, cached or fresh, on the page. The rows are rebuilt only when the songs differ.</summary>
    private void Apply(CachedTrackList list)
    {
        Title = list.Title;
        Subtitle = list.Subtitle;
        Description = list.Description;
        CoverUrl = list.CoverUrl;
        Albums.Clear();
        foreach (var album in list.Albums ?? []) Albums.Add(album);
        HasAlbums = Albums.Count > 0;
        if (_playlistId != 0)
        {
            IsOwnPlaylist = _account.IsOwn(_playlistId);
            CanSubscribe = _account.IsLoggedIn && !IsOwnPlaylist;
            IsSubscribed = list.Subscribed || _account.IsSubscribed(_playlistId);
            CanHeartMode = IsOwnPlaylist && list.SpecialType == 5;
            CanEditPlaylist = IsOwnPlaylist && list.SpecialType != 5;
        }
        if (Rows.Count == 0 || !list.HasSameTracksAs(_tracks)) SetTracks(list.Tracks, list.Privileges, list.CountLabel);
    }

    /// <summary>
    /// The fresh list for the page, or null when <paramref name="cached"/> is known to be current. One of the
    /// account's own or subscribed playlists is checked first against the small <c>/user/playlist</c> reply
    /// (trackUpdateTime and trackCount); the heavy detail is fetched only when they moved, and always for lists
    /// that are not the account's.
    /// </summary>
    private async Task<CachedTrackList?> FetchAsync(TrackListRequest request, string key, CachedTrackList? cached)
    {
        switch (request.Kind)
        {
            case TrackListKind.Playlist:
                if (cached is { TrackUpdateTime: > 0 } && _account.Profile is { } me && _account.Playlists.Any(p => p.Id == request.Id))
                {
                    var summary = (await _api.GetUserPlaylistsAsync(me.UserId)).FirstOrDefault(p => p.Id == request.Id);
                    if (summary is not null && summary.TrackUpdateTime == cached.TrackUpdateTime && summary.TrackCount == cached.TrackCount)
                    {
                        _log.Info($"playlist {request.Id}: cached copy is current");
                        return null;
                    }
                }
                return await FetchPlaylistAsync(request.Id);
            case TrackListKind.Album:
                return await FetchAlbumAsync(request.Id);
            case TrackListKind.Artist:
                return await FetchArtistAsync(request.Id);
            default:
                if (cached is not null)
                {
                    // Keyed by the recommendation day: NetEase does not change the daily picks before the next 06:00 in Beijing.
                    _log.Info("daily songs: cached copy is current");
                    return null;
                }
                var daily = await _api.GetDailyRecommendSongsAsync();
                return new CachedTrackList(key, "每日推荐", DateTime.Now.ToString("M月d日"), null,
                    daily.FirstOrDefault()?.Album.PicUrl, daily, null, DateTimeOffset.UtcNow);
        }
    }

    private async Task<CachedTrackList> FetchPlaylistAsync(long id)
    {
        var detail = await _api.GetPlaylistDetailAsync(id);
        var playlist = detail.Playlist;
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
        return new CachedTrackList(CachedTrackList.PlaylistKey(id), playlist.Name,
            playlist.Creator is { } creator ? $"{creator.Nickname} · {playlist.TrackCount} 首" : $"{playlist.TrackCount} 首",
            playlist.Description, playlist.CoverImgUrl, tracks, privileges, DateTimeOffset.UtcNow,
            TrackUpdateTime: playlist.TrackUpdateTime, TrackCount: playlist.TrackCount, SpecialType: playlist.SpecialType, Subscribed: playlist.Subscribed);
    }

    private async Task<CachedTrackList> FetchAlbumAsync(long id)
    {
        var response = await _api.GetAlbumAsync(id);
        var album = response.Album;
        return new CachedTrackList(CachedTrackList.AlbumKey(id), album?.Name ?? Title,
            album is not null ? $"{album.ArtistName} · {album.PublishDate?.Year.ToString() ?? ""}" : null,
            album?.Description, album?.PicUrl, response.Songs, null, DateTimeOffset.UtcNow);
    }

    private async Task<CachedTrackList> FetchArtistAsync(long id)
    {
        var response = await _api.GetArtistAsync(id);
        var artist = response.Artist;
        IReadOnlyList<AlbumSummary>? albums = null;
        try
        {
            albums = (await _api.GetArtistAlbumsAsync(id, limit: 30)).HotAlbums;
        }
        catch (Exception error)
        {
            _log.Warn($"artist albums {id}: {error.Message}");
        }
        return new CachedTrackList(CachedTrackList.ArtistKey(id), artist?.Name ?? Title,
            artist is not null ? $"{artist.MusicSize} 首歌曲 · {artist.AlbumSize} 张专辑" : null,
            artist?.BriefDesc, artist?.ImageUrl, response.HotSongs, null, DateTimeOffset.UtcNow, Albums: albums, CountLabel: "热门");
    }

    /// <param name="countLabel">Names what the rows are when the header already counts something else, e.g. 热门 under an artist's full song count.</param>
    private void SetTracks(IReadOnlyList<Track> tracks, IReadOnlyList<TrackPrivilege>? privileges, string? countLabel = null)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);
        var byId = privileges?.ToDictionary(static p => p.Id) ?? new Dictionary<long, TrackPrivilege>();
        Rows.Clear();
        var number = 1;
        foreach (var track in tracks)
        {
            var playability = track.GetPlayability(byId.GetValueOrDefault(track.Id), _account.IsLoggedIn, _account.PlaybackVipType);
            Rows.Add(new TrackRow(track, playability, number++) { IsLiked = _account.IsLiked(track.Id) });
        }
        var total = TimeSpan.FromMilliseconds(tracks.Sum(static t => (double)t.DurationMs));
        var count = countLabel is null ? $"{tracks.Count} 首歌曲" : $"{countLabel} {tracks.Count} 首";
        Summary = $"{count}，{(int)total.TotalHours} 小时 {total.Minutes} 分钟";
        MarkCurrent();
    }

    private void MarkCurrent()
    {
        var currentId = _player.CurrentTrack?.Id;
        foreach (var row in Rows) row.IsCurrent = row.Track.Id == currentId;
    }

    /// <summary>The "重试" button of the error bar.</summary>
    [RelayCommand]
    private Task Retry() => _request is null ? Task.CompletedTask : LoadAsync(_request);

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
            _lists.Update(CachedTrackList.PlaylistKey(_playlistId), list => list with { Subscribed = IsSubscribed });
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
            await _account.ToggleLikeAsync(row.Track);
        }
        catch (Exception error)
        {
            _log.Warn($"like {row.Track.Id}: {error.Message}");
            App.Services.Status.Show("操作失败：" + ErrorText.Describe(error));
        }
    }
}
