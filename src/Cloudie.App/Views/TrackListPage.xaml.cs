using Cloudie.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Cloudie.App.Views;

/// <summary>Playlist, album, artist and daily-recommendation pages share this layout.</summary>
public sealed partial class TrackListPage : Page
{
    public TrackListViewModel ViewModel { get; } = new();

    public TrackListPage()
    {
        InitializeComponent();
        List.PlayRequested += row => ViewModel.PlayRowCommand.Execute(row);
        List.PlayNextRequested += row => ViewModel.PlayNextCommand.Execute(row);
        List.LikeRequested += row => ViewModel.ToggleLikeCommand.Execute(row);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is TrackListRequest request) await ViewModel.LoadAsync(request);
    }
}
