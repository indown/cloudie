using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Library;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly NeteaseApi _api = App.Services.Api;
    private readonly AccountService _account = App.Services.Account;
    private readonly LogService _log = App.Services.Log;
    private readonly HomeSnapshotStore _store = App.Services.Home;
    /// <summary>What the page shows, from disk or fetched whole; the same account on the same recommendation day means nothing to fetch.</summary>
    private HomeSnapshot? _shown;

    public ObservableCollection<PlaylistSummary> RecommendedPlaylists { get; } = [];
    public ObservableCollection<Track> DailySongs { get; } = [];
    public ObservableCollection<ToplistItem> Toplists { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial string Greeting { get; private set; } = "推荐";
    [ObservableProperty] public partial bool HasDailySongs { get; private set; }

    /// <summary>
    /// The last snapshot goes up first: the signed-in one when a session cookie is stored, the signed-out one
    /// otherwise. Then the stored session is awaited, so the page is fetched once, signed in, rather than once
    /// signed out and again a second later. With nothing on screen a slow restore is given a few seconds, after
    /// which the page loads signed out and refreshes once the session lands; with a signed-in snapshot up there
    /// is no point in a signed-out load, so the restore is simply awaited.
    /// </summary>
    public async Task LoadInitialAsync()
    {
        if (_store.Load() is { } snapshot && (snapshot.UserId != 0) == App.Services.Client.IsLoggedIn)
        {
            Paint(snapshot);
            _shown = snapshot;
        }
        IsLoading = true;
        var restore = _account.Restoring;
        if (_shown is { UserId: not 0 }) await restore;
        else await Task.WhenAny(restore, Task.Delay(TimeSpan.FromSeconds(8)));
        await LoadAsync();
        if (restore.IsCompleted) return;
        await restore;
        if (_account.IsLoggedIn) await LoadAsync();
    }

    public async Task LoadAsync()
    {
        Greeting = DateTime.Now.Hour switch
        {
            < 6 => "夜深了",
            < 12 => "早上好",
            < 18 => "下午好",
            _ => "晚上好"
        };
        var userId = _account.Profile?.UserId ?? 0;
        if (_shown is { } shown && shown.IsCurrent(userId, DateTimeOffset.Now))
        {
            // NetEase renews the daily picks at 06:00 Beijing time; until then today's snapshot is the answer.
            _log.Info("home: snapshot is current");
            IsLoading = false;
            return;
        }
        IsLoading = true;
        try
        {
            var playlistsTask = _account.IsLoggedIn ? _api.GetRecommendResourceAsync() : _api.GetPersonalizedPlaylistsAsync(24);
            var toplistsTask = _api.GetToplistsAsync();
            var dailyTask = _account.IsLoggedIn ? _api.GetDailyRecommendSongsAsync() : Task.FromResult<IReadOnlyList<Track>>([]);
            await Task.WhenAll(Quiet(playlistsTask), Quiet(toplistsTask), Quiet(dailyTask));
            // A call that failed keeps the row the snapshot had rather than emptying it; such a page is not saved
            // as today's, so the next load tries again.
            var fresh = new HomeSnapshot(userId, HomeSnapshot.RecommendationDay(DateTimeOffset.Now),
                playlistsTask.IsCompletedSuccessfully ? playlistsTask.Result : _shown?.RecommendedPlaylists ?? [],
                toplistsTask.IsCompletedSuccessfully ? toplistsTask.Result.Take(8).ToList() : _shown?.Toplists ?? [],
                dailyTask.IsCompletedSuccessfully ? dailyTask.Result.Take(12).ToList() : _shown?.DailySongs ?? [],
                DateTimeOffset.UtcNow);
            Paint(fresh);
            if (playlistsTask.IsCompletedSuccessfully && toplistsTask.IsCompletedSuccessfully && dailyTask.IsCompletedSuccessfully)
            {
                _shown = fresh;
                _store.Save(fresh);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Paint(HomeSnapshot snapshot)
    {
        RecommendedPlaylists.Clear();
        foreach (var playlist in snapshot.RecommendedPlaylists) RecommendedPlaylists.Add(playlist);
        Toplists.Clear();
        foreach (var toplist in snapshot.Toplists) Toplists.Add(toplist);
        DailySongs.Clear();
        foreach (var track in snapshot.DailySongs) DailySongs.Add(track);
        HasDailySongs = DailySongs.Count > 0;
    }

    private async Task Quiet(Task task)
    {
        try { await task; }
        catch (Exception error) { _log.Warn($"home: {error.Message}"); }
    }

    public Task PlayDailySongsAsync(Track track) =>
        App.Services.Player.PlayAsync(DailySongs, DailySongs.IndexOf(track));
}
