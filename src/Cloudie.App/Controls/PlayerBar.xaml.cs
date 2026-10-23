using System.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Netease.Models;
using Cloudie.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Cloudie.App.Controls;

public sealed partial class PlayerBar : UserControl, INotifyPropertyChanged
{
    public PlayerService Player { get; } = App.Services.Player;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerBar()
    {
        InitializeComponent();
        Player.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerService.IsPlaying): Raise(nameof(PlayGlyph)); break;
                case nameof(PlayerService.Position): Raise(nameof(PositionSeconds)); break;
                case nameof(PlayerService.Duration): Raise(nameof(DurationSeconds)); break;
                case nameof(PlayerService.IsShuffled): Raise(nameof(ShuffleBrush)); break;
                case nameof(PlayerService.RepeatMode): Raise(nameof(RepeatGlyph)); Raise(nameof(RepeatBrush)); break;
                case nameof(PlayerService.UnblockSource):
                case nameof(PlayerService.ServedQuality): Raise(nameof(SourceLabel)); break;
                case nameof(PlayerService.IsFm):
                case nameof(PlayerService.IsHeartMode): Raise(nameof(ModeLabel)); break;
            }
        };
    }

    public string PlayGlyph => Player.IsPlaying ? "" : "";
    public double PositionSeconds => Player.Position.TotalSeconds;
    public double DurationSeconds => Math.Max(1, Player.Duration.TotalSeconds);
    public string RepeatGlyph => Player.RepeatMode == RepeatMode.One ? "" : "";
    public Brush ShuffleBrush => Player.IsShuffled ? AccentBrush : SecondaryBrush;
    public Brush RepeatBrush => Player.RepeatMode == RepeatMode.Off ? SecondaryBrush : AccentBrush;
    public string? SourceLabel => Player.UnblockSource is { } source ? $"来自 {source}" : Player.ServedQuality is { } q ? q.ToUpperInvariant() : null;
    public string? ModeLabel => Player.IsFm ? "私人 FM" : Player.IsHeartMode ? "心动模式" : null;

    public double VolumePercent
    {
        get => Player.Volume * 100;
        set => Player.Volume = value / 100;
    }

    private Brush AccentBrush => (Brush)Application.Current.Resources["CloudieAccentBrush"];
    private Brush SecondaryBrush => (Brush)Application.Current.Resources["CloudieTextSecondaryBrush"];

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Cover_Click(object sender, RoutedEventArgs e) => Player.ShowNowPlaying = !Player.ShowNowPlaying;
    private void PlayPause_Click(object sender, RoutedEventArgs e) => Player.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _ = Player.NextAsync();
    private void Previous_Click(object sender, RoutedEventArgs e) => _ = Player.PreviousAsync();
    private void Shuffle_Click(object sender, RoutedEventArgs e) => Player.ToggleShuffle();
    private void Repeat_Click(object sender, RoutedEventArgs e) => Player.CycleRepeatMode();
    private void Lyrics_Click(object sender, RoutedEventArgs e) => Player.ShowNowPlaying = !Player.ShowNowPlaying;

    private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        Player.Seek(TimeSpan.FromSeconds(ProgressSlider.Value));

    private void Queue_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            var index = Player.Queue.Items.ToList().FindIndex(t => t.Id == track.Id);
            if (index >= 0) _ = Player.PlayAtAsync(index);
        }
    }
}
