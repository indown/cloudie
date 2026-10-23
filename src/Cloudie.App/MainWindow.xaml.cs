using Cloudie.App.Controls;
using Cloudie.App.ViewModels;
using Cloudie.App.Views;
using Cloudie.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace Cloudie.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1280, 820));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ViewModel.NavigateRequested += Navigate;
        ViewModel.Load();
        ContentFrame.Navigate(typeof(HomePage));
    }

    public void ApplyTheme(AppTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    public void Navigate(NavigationEntry entry)
    {
        var pageType = entry.Kind switch
        {
            NavigationKind.Home => typeof(HomePage),
            NavigationKind.DailySongs => typeof(TrackListPage),
            NavigationKind.Playlist => typeof(TrackListPage),
            NavigationKind.Search => typeof(SearchPage),
            NavigationKind.Settings => typeof(SettingsPage),
            _ => typeof(HomePage)
        };
        object? parameter = entry.Kind switch
        {
            NavigationKind.DailySongs => TrackListRequest.DailySongs(),
            NavigationKind.Playlist => TrackListRequest.Playlist(entry.Id, entry.Title),
            NavigationKind.Search => entry.Title,
            _ => null
        };
        ViewModel.Player.ShowNowPlaying = false;
        ContentFrame.Navigate(pageType, parameter);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ViewModel.Player.ShowNowPlaying = false;
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }
        if (args.SelectedItem is NavigationEntry entry && entry.Kind is not (NavigationKind.Header or NavigationKind.Separator))
        {
            Navigate(entry);
        }
    }

    private void Navigation_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ViewModel.Player.ShowNowPlaying)
        {
            ViewModel.Player.ShowNowPlaying = false;
            return;
        }
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        Navigation.IsBackEnabled = ContentFrame.CanGoBack;
        if (e.SourcePageType == typeof(SettingsPage))
        {
            Navigation.SelectedItem = Navigation.SettingsItem;
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim();
        if (string.IsNullOrEmpty(query)) return;
        ViewModel.Player.ShowNowPlaying = false;
        ContentFrame.Navigate(typeof(SearchPage), query);
    }
}
