using System.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Lyrics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace Cloudie.App.Controls;

/// <summary>The immersive page: cover and metadata on the left, synced lyrics on the right.</summary>
public sealed partial class NowPlayingView : UserControl, INotifyPropertyChanged
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));

    public PlayerService Player { get; } = App.Services.Player;
    private readonly AccountService _account = App.Services.Account;
    private int _highlighted = -1;
    private CancellationTokenSource? _paletteCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public NowPlayingView()
    {
        InitializeComponent();
        Player.PropertyChanged += (sender, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerService.Lyrics):
                    Raise(nameof(Lines));
                    Raise(nameof(HasLyrics));
                    Raise(nameof(EmptyLyricsText));
                    _highlighted = -1;
                    break;
                case nameof(PlayerService.ActiveLyricIndex):
                    Highlight(Player.ActiveLyricIndex);
                    break;
                case nameof(PlayerService.CurrentTrack):
                    Raise(nameof(LikeGlyph));
                    _ = TintAsync();
                    break;
                case nameof(PlayerService.UnblockSource):
                case nameof(PlayerService.ServedQuality):
                case nameof(PlayerService.IsCached):
                    Raise(nameof(SourceLabel));
                    break;
            }
        };
        _account.LikedSongsChanged += (_, _) => Raise(nameof(LikeGlyph));
    }

    public IReadOnlyList<LyricLine> Lines => Player.Lyrics.Lines;
    public bool HasLyrics => Player.Lyrics.Lines.Count > 0;
    public string EmptyLyricsText => Player.Lyrics.IsInstrumental ? "纯音乐，请欣赏" : Player.CurrentTrack is null ? string.Empty : "暂无歌词";
    public string LikeGlyph => Player.CurrentTrack is { } track && _account.IsLiked(track.Id) ? "" : "";
    public string? SourceLabel
    {
        get
        {
            var label = Player.UnblockSource is { } source ? $"音源：{source}" : Player.ServedQuality is { } q ? q.ToUpperInvariant() : null;
            return Player.IsCached ? (label is null ? "已缓存" : $"{label} · 已缓存") : label;
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Highlight(int index)
    {
        if (index == _highlighted) return;
        SetLineBrush(_highlighted, InactiveBrush, 24);
        SetLineBrush(index, ActiveBrush, 28);
        _highlighted = index;
        if (index >= 0 && index < Lines.Count)
        {
            LyricsList.ScrollIntoView(Lines[index], ScrollIntoViewAlignment.Default);
            _ = CenterAsync(Lines[index]);
        }
    }

    private async Task CenterAsync(LyricLine line)
    {
        await Task.Delay(30);
        if (LyricsList.ContainerFromItem(line) is not ListViewItem container) return;
        if (LyricsList.FindDescendant<ScrollViewer>() is not { } scroller) return;
        var transform = container.TransformToVisual(scroller);
        var offset = transform.TransformPoint(new Windows.Foundation.Point(0, 0)).Y + scroller.VerticalOffset;
        var target = offset - scroller.ViewportHeight / 2 + container.ActualHeight / 2;
        scroller.ChangeView(null, Math.Max(0, target), null, disableAnimation: false);
    }

    private void SetLineBrush(int index, Brush brush, double size)
    {
        if (index < 0 || index >= Lines.Count) return;
        if (LyricsList.ContainerFromIndex(index) is not ListViewItem container) return;
        if (container.ContentTemplateRoot is StackPanel panel && panel.Children.FirstOrDefault() is TextBlock text)
        {
            text.Foreground = brush;
            text.FontSize = size;
        }
    }

    private async Task TintAsync()
    {
        _paletteCts?.Cancel();
        var cts = _paletteCts = new CancellationTokenSource();
        var colors = await ArtworkPalette.ExtractAsync(Player.CurrentTrack?.Album.PicUrl, cts.Token);
        if (cts.IsCancellationRequested) return;
        Animate(PrimaryStop, colors.Primary);
        Animate(SecondaryStop, colors.Secondary);
    }

    private static void Animate(GradientStop stop, Color color)
    {
        var animation = new ColorAnimation { To = color, Duration = new Duration(TimeSpan.FromMilliseconds(600)), EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, stop);
        Storyboard.SetTargetProperty(animation, "Color");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => Player.ShowNowPlaying = false;

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        if (Player.CurrentTrack is { } track && _account.IsLoggedIn)
        {
            try { await _account.ToggleLikeAsync(track.Id); }
            catch (Exception error) { App.Services.Log.Warn($"like: {error.Message}"); }
        }
    }

    private void LyricsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LyricLine line) Player.Seek(line.Time);
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindDescendant<T>(this DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (child.FindDescendant<T>() is { } nested) return nested;
        }
        return null;
    }
}
