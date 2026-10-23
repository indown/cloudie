using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.App.Controls;
using Cloudie.App.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cloudie.App.ViewModels;

/// <summary>Sidebar contents, account display and the status line shared by the window.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AccountService _account = App.Services.Account;

    public PlayerService Player { get; } = App.Services.Player;
    public ObservableCollection<NavigationEntry> MenuItems { get; } = [];

    [ObservableProperty] public partial string AccountName { get; private set; } = "未登录";
    [ObservableProperty] public partial BitmapImage? AvatarImage { get; private set; }
    [ObservableProperty] public partial string? StatusMessage { get; private set; }
    [ObservableProperty] public partial bool IsStatusOpen { get; set; }

    public event Action<NavigationEntry>? NavigateRequested;

    public void Load()
    {
        RebuildMenu();
        _account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountService.Profile) or nameof(AccountService.Playlists)) RebuildMenu();
        };
        Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerService.StatusMessage) && Player.StatusMessage is { Length: > 0 } message)
            {
                StatusMessage = message;
                IsStatusOpen = true;
                Player.StatusMessage = null;
            }
        };
    }

    private void RebuildMenu()
    {
        MenuItems.Clear();
        MenuItems.Add(new NavigationEntry { Kind = NavigationKind.Home, Title = "推荐", Glyph = "" });
        MenuItems.Add(new NavigationEntry { Kind = NavigationKind.DailySongs, Title = "每日推荐", Glyph = "" });
        var profile = _account.Profile;
        AccountName = profile?.Nickname ?? "未登录";
        AvatarImage = profile?.AvatarUrl is { } avatar ? new BitmapImage(new Uri(avatar + "?param=64y64")) : null;
        if (profile is null) return;
        var playlists = _account.Playlists;
        var mine = playlists.Where(p => p.Creator?.UserId == profile.UserId).ToList();
        var subscribed = playlists.Where(p => p.Creator?.UserId != profile.UserId).ToList();
        if (mine.Count > 0)
        {
            MenuItems.Add(NavigationEntry.Header("创建的歌单"));
            foreach (var playlist in mine)
            {
                MenuItems.Add(new NavigationEntry
                {
                    Kind = NavigationKind.Playlist, Title = playlist.Name, Id = playlist.Id,
                    Glyph = playlist.IsFavorites ? "" : ""
                });
            }
        }
        if (subscribed.Count > 0)
        {
            MenuItems.Add(NavigationEntry.Header("收藏的歌单"));
            foreach (var playlist in subscribed)
            {
                MenuItems.Add(new NavigationEntry { Kind = NavigationKind.Playlist, Title = playlist.Name, Id = playlist.Id, Glyph = "" });
            }
        }
    }

    public void Navigate(NavigationEntry entry) => NavigateRequested?.Invoke(entry);
}
