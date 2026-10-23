using System.Collections.ObjectModel;
using Cloudie.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Cloudie.App.Controls;

/// <summary>The song table used by playlist, album, artist, daily and search pages.</summary>
public sealed partial class TrackList : UserControl
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(ObservableCollection<TrackRow>), typeof(TrackList), new PropertyMetadata(null));

    public ObservableCollection<TrackRow>? Rows
    {
        get => (ObservableCollection<TrackRow>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>Raised when the user wants a row played (double-click, menu).</summary>
    public event Action<TrackRow>? PlayRequested;
    public event Action<TrackRow>? PlayNextRequested;
    public event Action<TrackRow>? LikeRequested;

    public TrackList() => InitializeComponent();

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        // Single click selects; playback starts on double-click.
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TrackRow row) PlayRequested?.Invoke(row);
    }

    private void PlayMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrackRow row) PlayRequested?.Invoke(row);
    }

    private void PlayNextMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrackRow row) PlayNextRequested?.Invoke(row);
    }

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrackRow row) LikeRequested?.Invoke(row);
    }
}
