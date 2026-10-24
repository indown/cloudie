using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly NeteaseApi _api = App.Services.Api;
    private readonly AccountService _account = App.Services.Account;
    private readonly LogService _log = App.Services.Log;

    public ObservableCollection<PlaylistSummary> RecommendedPlaylists { get; } = [];
    public ObservableCollection<Track> DailySongs { get; } = [];
    public ObservableCollection<ToplistItem> Toplists { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial string Greeting { get; private set; } = "推荐";
    [ObservableProperty] public partial bool HasDailySongs { get; private set; }

    /// <summary>
    /// The first load waits for the stored session to come back, so the page is built once, signed in,
    /// instead of once signed out and again a second later. A slow restore is given a few seconds; after
    /// that the page loads signed out and refreshes once the session lands.
    /// </summary>
    public async Task LoadInitialAsync()
    {
        IsLoading = true;
        var restore = _account.Restoring;
        await Task.WhenAny(restore, Task.Delay(TimeSpan.FromSeconds(8)));
        await LoadAsync();
        if (restore.IsCompleted) return;
        await restore;
        if (_account.IsLoggedIn) await LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        Greeting = DateTime.Now.Hour switch
        {
            < 6 => "夜深了",
            < 12 => "早上好",
            < 18 => "下午好",
            _ => "晚上好"
        };
        try
        {
            var playlistsTask = _account.IsLoggedIn ? _api.GetRecommendResourceAsync() : _api.GetPersonalizedPlaylistsAsync(24);
            var toplistsTask = _api.GetToplistsAsync();
            var dailyTask = _account.IsLoggedIn ? _api.GetDailyRecommendSongsAsync() : Task.FromResult<IReadOnlyList<Track>>([]);
            await Task.WhenAll(Quiet(playlistsTask), Quiet(toplistsTask), Quiet(dailyTask));
            RecommendedPlaylists.Clear();
            foreach (var playlist in playlistsTask.IsCompletedSuccessfully ? playlistsTask.Result : []) RecommendedPlaylists.Add(playlist);
            Toplists.Clear();
            foreach (var toplist in toplistsTask.IsCompletedSuccessfully ? toplistsTask.Result.Take(8) : []) Toplists.Add(toplist);
            DailySongs.Clear();
            foreach (var track in dailyTask.IsCompletedSuccessfully ? dailyTask.Result.Take(12) : []) DailySongs.Add(track);
            HasDailySongs = DailySongs.Count > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task Quiet(Task task)
    {
        try { await task; }
        catch (Exception error) { _log.Warn($"home: {error.Message}"); }
    }

    public Task PlayDailySongsAsync(Track track) =>
        App.Services.Player.PlayAsync(DailySongs, DailySongs.IndexOf(track));
}
