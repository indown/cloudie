using System.ComponentModel;
using Cloudie.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cloudie.App.Views;

/// <summary>私人 FM: one song at a time with "不喜欢", like, play/pause and next.</summary>
public sealed partial class FmPage : Page, INotifyPropertyChanged
{
    public PlayerService Player { get; } = App.Services.Player;
    private readonly AccountService _account = App.Services.Account;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FmPage()
    {
        InitializeComponent();
        Player.PropertyChanged += (sender, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerService.IsFm):
                case nameof(PlayerService.CurrentTrack):
                    Raise(nameof(IsActive));
                    Raise(nameof(IsIdle));
                    Raise(nameof(LikeGlyph));
                    break;
                case nameof(PlayerService.IsPlaying):
                    Raise(nameof(PlayGlyph));
                    break;
            }
        };
        _account.LikedSongsChanged += (_, _) => Raise(nameof(LikeGlyph));
    }

    public bool IsActive => Player.IsFm && Player.HasCurrentTrack;
    public bool IsIdle => !IsActive;
    public string PlayGlyph => Player.IsPlaying ? "" : "";
    public string LikeGlyph => Player.CurrentTrack is { } track && _account.IsLiked(track.Id) ? "" : "";

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Start_Click(object sender, RoutedEventArgs e) => _ = Player.StartFmAsync();
    private void Trash_Click(object sender, RoutedEventArgs e) => _ = Player.TrashFmAsync();
    private void PlayPause_Click(object sender, RoutedEventArgs e) => Player.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _ = Player.NextAsync();

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        if (Player.CurrentTrack is not { } track || !_account.IsLoggedIn) return;
        try
        {
            await _account.ToggleLikeAsync(track.Id);
        }
        catch (Exception error)
        {
            App.Services.Status.Show("操作失败：" + error.Message);
        }
    }
}
