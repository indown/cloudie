using Cloudie.App.Controls;
using Cloudie.App.ViewModels;
using Cloudie.App.Views;
using Cloudie.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace Cloudie.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    private const int MinWidth = 960;
    private const int MinHeight = 600;

    private readonly DispatcherQueueTimer _suggestTimer;
    private readonly DispatcherQueueTimer _placementTimer;
    private CancellationTokenSource? _suggestCts;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RestorePlacement();
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        _suggestTimer = DispatcherQueue.CreateTimer();
        _suggestTimer.Interval = TimeSpan.FromMilliseconds(250);
        _suggestTimer.IsRepeating = false;
        _suggestTimer.Tick += (_, _) => _ = LoadSuggestionsAsync();
        _placementTimer = DispatcherQueue.CreateTimer();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(500);
        _placementTimer.IsRepeating = false;
        _placementTimer.Tick += (_, _) => RememberPlacement();
        AppWindow.Changed += (_, args) =>
        {
            if (!args.DidSizeChange && !args.DidPositionChange) return;
            _placementTimer.Stop();
            _placementTimer.Start();
        };
        Closed += (_, _) =>
        {
            _placementTimer.Stop();
            RememberPlacement();
            ViewModel.Player.SavePosition();
        };
        ViewModel.NavigateRequested += Navigate;
        ViewModel.Load();
        ContentFrame.Navigate(typeof(HomePage));
    }

    // MARK: Window placement

    /// <summary>Last size and position, kept on a display that still exists; first run gets a sensible default.</summary>
    private void RestorePlacement()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }
        var saved = App.Services.Settings.WindowPlacement;
        if (saved is null)
        {
            AppWindow.Resize(new SizeInt32(1280, 820));
            return;
        }
        var area = DisplayArea.GetFromRect(new RectInt32(saved.X, saved.Y, saved.Width, saved.Height), DisplayAreaFallback.Nearest).WorkArea;
        var fit = saved.FitInto(area.X, area.Y, area.Width, area.Height, MinWidth, MinHeight);
        AppWindow.MoveAndResize(new RectInt32(fit.X, fit.Y, fit.Width, fit.Height));
        if (saved.IsMaximized && AppWindow.Presenter is OverlappedPresenter overlapped) overlapped.Maximize();
    }

    /// <summary>Stores the normal bounds; while maximized or minimized only the flag changes.</summary>
    private void RememberPlacement()
    {
        var settings = App.Services.Settings;
        var state = (AppWindow.Presenter as OverlappedPresenter)?.State ?? OverlappedPresenterState.Restored;
        if (state != OverlappedPresenterState.Restored)
        {
            if (settings.WindowPlacement is { } previous) settings.WindowPlacement = previous with { IsMaximized = state == OverlappedPresenterState.Maximized };
            return;
        }
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        settings.WindowPlacement = new WindowPlacement(position.X, position.Y, size.Width, size.Height, IsMaximized: false);
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
            NavigationKind.Fm => typeof(FmPage),
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

    // MARK: Account

    private void AccountMenu_Opening(object sender, object e)
    {
        var loggedIn = App.Services.Account.IsLoggedIn;
        LoginItem.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LogoutItem.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await ShowLoginAsync();

    private async void Logout_Click(object sender, RoutedEventArgs e) => await App.Services.Account.LogoutAsync();

    /// <summary>Opens the QR sign-in dialog; the account service reloads itself once the phone confirms.</summary>
    public async Task ShowLoginAsync()
    {
        if (Content?.XamlRoot is not { } root) return;
        try
        {
            await new LoginDialog { XamlRoot = root }.ShowAsync();
        }
        catch (Exception error)
        {
            App.Services.Log.Warn($"login dialog: {error.Message}");
        }
    }

    // MARK: Search

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _suggestTimer.Stop();
        _suggestCts?.Cancel();
        if (string.IsNullOrWhiteSpace(sender.Text))
        {
            sender.ItemsSource = null;
            return;
        }
        _suggestTimer.Start();
    }

    /// <summary>Runs a quarter second after the last keystroke; a stale reply is dropped.</summary>
    private async Task LoadSuggestionsAsync()
    {
        var text = SearchBox.Text;
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            var suggestions = await App.Services.Api.GetSearchSuggestionsAsync(text, cts.Token);
            if (!cts.IsCancellationRequested && SearchBox.Text == text) SearchBox.ItemsSource = suggestions;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            App.Services.Log.Warn($"search suggest: {error.Message}");
        }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string keyword) sender.Text = keyword;
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _suggestTimer.Stop();
        _suggestCts?.Cancel();
        var query = (args.ChosenSuggestion as string ?? args.QueryText)?.Trim();
        if (string.IsNullOrEmpty(query)) return;
        ViewModel.Player.ShowNowPlaying = false;
        ContentFrame.Navigate(typeof(SearchPage), query);
    }

    // MARK: Shortcuts

    private object? FocusedElement => Content is null ? null : FocusManager.GetFocusedElement(Content.XamlRoot);

    private bool IsTyping() => FocusedElement is TextBox or PasswordBox or RichEditBox;

    /// <summary>Space must keep typing in text boxes and activating whatever control has keyboard focus.</summary>
    private bool SpaceBelongsToFocus() =>
        FocusedElement is TextBox or PasswordBox or RichEditBox or ButtonBase or Slider or ToggleSwitch or ComboBox
            or GridViewItem or NavigationViewItemBase;

    private void PlayPause_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (SpaceBelongsToFocus()) return;
        ViewModel.Player.TogglePlayPause();
        args.Handled = true;
    }

    private void Next_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping()) return;
        _ = ViewModel.Player.NextAsync();
        args.Handled = true;
    }

    private void Previous_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTyping()) return;
        _ = ViewModel.Player.PreviousAsync();
        args.Handled = true;
    }

    private void Lyrics_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.Player.HasCurrentTrack) return;
        ViewModel.Player.ShowNowPlaying = !ViewModel.Player.ShowNowPlaying;
        args.Handled = true;
    }

    private void Search_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.Player.ShowNowPlaying) return;
        ViewModel.Player.ShowNowPlaying = false;
        args.Handled = true;
    }
}
