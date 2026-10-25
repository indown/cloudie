using System.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Netease.Models;
using Cloudie.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Cloudie.App.Controls;

public sealed partial class PlayerBar : UserControl, INotifyPropertyChanged
{
    public PlayerService Player { get; } = App.Services.Player;
    /// <summary>While the thumb is held, ticks must not snap it back to the playing position.</summary>
    private bool _scrubbing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerBar()
    {
        InitializeComponent();
        // The slider marks PointerPressed as handled, so the subscription has to ask for handled events too.
        ProgressSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _scrubbing = true), handledEventsToo: true);
        Player.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerService.IsPlaying): Raise(nameof(PlayGlyph)); Raise(nameof(PlayPauseLabel)); break;
                case nameof(PlayerService.Position): if (!_scrubbing) Raise(nameof(PositionSeconds)); break;
                case nameof(PlayerService.Duration): Raise(nameof(DurationSeconds)); break;
                case nameof(PlayerService.RepeatMode): Raise(nameof(RepeatGlyph)); Raise(nameof(IsRepeatOn)); Raise(nameof(RepeatLabel)); break;
                case nameof(PlayerService.UnblockSource):
                case nameof(PlayerService.ServedQuality):
                case nameof(PlayerService.IsCached): Raise(nameof(SourceLabel)); break;
                case nameof(PlayerService.IsFm):
                case nameof(PlayerService.IsHeartMode): Raise(nameof(ModeLabel)); break;
            }
        };
    }

    public string PlayGlyph => Player.IsPlaying ? "" : "";
    public string PlayPauseLabel => Player.IsPlaying ? "暂停" : "播放";
    public double PositionSeconds => Player.Position.TotalSeconds;
    public double DurationSeconds => Math.Max(1, Player.Duration.TotalSeconds);
    public string RepeatGlyph => Player.RepeatMode == RepeatMode.One ? "" : "";
    public bool IsRepeatOn => Player.RepeatMode != RepeatMode.Off;
    public string RepeatLabel => Player.RepeatMode switch { RepeatMode.One => "单曲循环", RepeatMode.All => "列表循环", _ => "循环模式" };
    public string? SourceLabel
    {
        get
        {
            var label = Player.UnblockSource is { } source ? $"来自 {source}" : Player.ServedQuality is { } q ? q.ToUpperInvariant() : null;
            return Player.IsCached ? (label is null ? "已缓存" : $"{label} · 已缓存") : label;
        }
    }
    public string? ModeLabel => Player.IsFm ? "私人 FM" : Player.IsHeartMode ? "心动模式" : null;

    public double VolumePercent
    {
        get => Player.Volume * 100;
        set => Player.Volume = value / 100;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Cover_Click(object sender, RoutedEventArgs e) => Player.ShowNowPlaying = !Player.ShowNowPlaying;
    private void PlayPause_Click(object sender, RoutedEventArgs e) => Player.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _ = Player.NextAsync();
    private void Previous_Click(object sender, RoutedEventArgs e) => _ = Player.PreviousAsync();
    private void Shuffle_Click(object sender, RoutedEventArgs e) => Player.ToggleShuffle();
    private void Repeat_Click(object sender, RoutedEventArgs e) => Player.CycleRepeatMode();
    private void Lyrics_Click(object sender, RoutedEventArgs e) => Player.ShowNowPlaying = !Player.ShowNowPlaying;

    private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _scrubbing = false;
        Player.Seek(TimeSpan.FromSeconds(ProgressSlider.Value));
    }

    /// <summary>Arrow, Home/End and Page keys move the thumb without a pointer capture; seek when they land.</summary>
    private void ProgressSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Home
            or Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
        {
            Player.Seek(TimeSpan.FromSeconds(ProgressSlider.Value));
        }
    }

    /// <summary>The list exists only once the flyout is open, so the scroll waits for Opened.</summary>
    private void QueueFlyout_Opened(object sender, object e)
    {
        if (Player.CurrentTrack is { } track) QueueList.ScrollIntoView(track, ScrollIntoViewAlignment.Leading);
    }

    private void Queue_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            var index = Player.Queue.Items.ToList().FindIndex(t => t.Id == track.Id);
            if (index >= 0) _ = Player.PlayAtAsync(index);
        }
    }
}
