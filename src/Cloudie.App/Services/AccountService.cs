using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.App.Services;

/// <summary>The signed-in NetEase account, its playlists and liked songs.</summary>
public sealed partial class AccountService(NeteaseApi api, LogService log) : ObservableObject
{
    [ObservableProperty] public partial UserProfile? Profile { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<PlaylistSummary> Playlists { get; private set; } = [];
    [ObservableProperty] public partial bool IsBusy { get; private set; }

    private HashSet<long> _likedIds = [];

    public bool IsLoggedIn => Profile is not null;
    public int VipType => Profile?.VipType ?? 0;
    /// <summary>VIP tier of 音源账号 while it is usable; wired by <see cref="AppServices"/>.</summary>
    public Func<int>? SourceVipType { get; init; }
    /// <summary>The tier that decides what plays: the higher of this account and 音源账号.</summary>
    public int PlaybackVipType => Math.Max(VipType, SourceVipType?.Invoke() ?? 0);
    public PlaylistSummary? Favorites => Playlists.FirstOrDefault(static p => p.IsFavorites);

    public event EventHandler? LikedSongsChanged;

    /// <summary>The start-up restore of a stored session; pages that want to load signed in await it first.</summary>
    public Task Restoring { get; private set; } = Task.CompletedTask;

    /// <summary>Reuses a stored session at start-up; a dead cookie just leaves the app signed out.</summary>
    public Task RestoreAsync() => Restoring = RestoreCoreAsync();

    private async Task RestoreCoreAsync()
    {
        if (!api.Client.IsLoggedIn) return;
        try
        {
            await api.RefreshLoginAsync();
            await ReloadAsync();
        }
        catch (Exception error)
        {
            log.Warn($"恢复登录失败: {error.Message}");
        }
    }

    public async Task ReloadAsync()
    {
        IsBusy = true;
        try
        {
            var profile = await api.GetAccountAsync();
            Profile = profile;
            if (profile is null)
            {
                Playlists = [];
                _likedIds = [];
                return;
            }
            Playlists = await api.GetUserPlaylistsAsync(profile.UserId);
            try
            {
                _likedIds = (await api.GetLikedTrackIdsAsync(profile.UserId)).ToHashSet();
            }
            catch (Exception error)
            {
                log.Warn($"读取喜欢列表失败: {error.Message}");
            }
            LikedSongsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (NeteaseApiException error) when (error.NeedsLogin)
        {
            Profile = null;
            Playlists = [];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool IsLiked(long trackId) => _likedIds.Contains(trackId);

    public async Task ToggleLikeAsync(long trackId)
    {
        var like = !_likedIds.Contains(trackId);
        await api.LikeTrackAsync(trackId, like);
        if (like) _likedIds.Add(trackId); else _likedIds.Remove(trackId);
        LikedSongsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Playlists the account created, without "我喜欢的音乐" (the heart handles that one).</summary>
    public IReadOnlyList<PlaylistSummary> OwnPlaylists =>
        Profile is { } profile ? Playlists.Where(p => p.Creator?.UserId == profile.UserId && !p.IsFavorites).ToList() : [];

    public bool IsOwn(long playlistId) =>
        Profile is { } profile && Playlists.Any(p => p.Id == playlistId && p.Creator?.UserId == profile.UserId);

    public bool IsSubscribed(long playlistId) =>
        Profile is { } profile && Playlists.Any(p => p.Id == playlistId && p.Creator?.UserId != profile.UserId);

    public async Task ReloadPlaylistsAsync()
    {
        if (Profile is not { } profile) return;
        Playlists = await api.GetUserPlaylistsAsync(profile.UserId);
    }

    public async Task SubscribePlaylistAsync(long playlistId, bool subscribe)
    {
        await api.SubscribePlaylistAsync(playlistId, subscribe);
        await ReloadPlaylistsAsync();
    }

    /// <summary>Throws a <see cref="NeteaseApiException"/> with code 502 when the song is already in the playlist.</summary>
    public Task AddToPlaylistAsync(long playlistId, long trackId) =>
        api.ManipulatePlaylistTracksAsync(playlistId, [trackId], add: true);

    public Task RemoveFromPlaylistAsync(long playlistId, long trackId) =>
        api.ManipulatePlaylistTracksAsync(playlistId, [trackId], add: false);

    public async Task<PlaylistSummary?> CreatePlaylistAsync(string name, bool isPrivate = false)
    {
        var playlist = await api.CreatePlaylistAsync(name, isPrivate);
        await ReloadPlaylistsAsync();
        return playlist;
    }

    public async Task RenamePlaylistAsync(long playlistId, string name)
    {
        await api.RenamePlaylistAsync(playlistId, name);
        await ReloadPlaylistsAsync();
    }

    public async Task DeletePlaylistAsync(long playlistId)
    {
        await api.DeletePlaylistAsync(playlistId);
        await ReloadPlaylistsAsync();
    }

    public async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); }
        catch (Exception error) { log.Warn($"退出登录: {error.Message}"); }
        Profile = null;
        Playlists = [];
        _likedIds = [];
        LikedSongsChanged?.Invoke(this, EventArgs.Empty);
    }
}
