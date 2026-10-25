using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Playback;
using Cloudie.Core.Settings;
using Cloudie.Core.Unblock;
using Microsoft.UI.Dispatching;

namespace Cloudie.App.ViewModels;

public sealed record QualityOption(AudioQuality Value, string Name);
public sealed record ThemeOption(AppTheme Value, string Name);
public sealed record CacheLimitOption(long Bytes, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings = App.Services.Settings;
    private readonly AccountService _account = App.Services.Account;
    private readonly AudioCache _audioCache = App.Services.AudioCache;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Action _cacheChanged;

    public SettingsViewModel()
    {
        _cacheChanged = () => _dispatcher.TryEnqueue(RefreshCacheUsage);
        _audioCache.Changed += _cacheChanged;
        RefreshCacheUsage();
    }

    /// <summary>The page is leaving: stop listening to the cache.</summary>
    public void Detach() => _audioCache.Changed -= _cacheChanged;

    public IReadOnlyList<QualityOption> Qualities { get; } =
        new[] { AudioQuality.Standard, AudioQuality.ExHigh, AudioQuality.Lossless, AudioQuality.HiRes }
            .Select(static q => new QualityOption(q, q.DisplayName())).ToList();

    public IReadOnlyList<ThemeOption> Themes { get; } =
        [new(AppTheme.System, "跟随系统"), new(AppTheme.Light, "浅色"), new(AppTheme.Dark, "深色")];

    // Both setters bail out on the value they already hold. WinUI treats every assignment of an object-typed
    // SelectedItem as a change, so without the guard the two-way binding and PropertyChanged feed each other
    // until the stack overflows (the theme one re-applied the theme on every pass, which is how it showed up).
    public QualityOption SelectedQuality
    {
        get => Qualities.First(q => q.Value == _settings.AudioQuality);
        set
        {
            if (value is null || value.Value == _settings.AudioQuality) return;
            _settings.AudioQuality = value.Value;
            OnPropertyChanged();
        }
    }

    public ThemeOption SelectedTheme
    {
        get => Themes.First(t => t.Value == _settings.Theme);
        set
        {
            if (value is null || value.Value == _settings.Theme) return;
            _settings.Theme = value.Value;
            App.MainWindow?.ApplyTheme(value.Value);
            OnPropertyChanged();
        }
    }

    public bool SpoofDomesticIp
    {
        get => _settings.SpoofDomesticIp;
        set
        {
            _settings.SpoofDomesticIp = value;
            App.Services.Client.SpoofDomesticIp = value;
            OnPropertyChanged();
        }
    }

    public bool Scrobble
    {
        get => _settings.Scrobble;
        set { _settings.Scrobble = value; OnPropertyChanged(); }
    }

    public bool CheckUpdates
    {
        get => _settings.CheckUpdates;
        set { _settings.CheckUpdates = value; OnPropertyChanged(); }
    }

    public bool UnblockEnabled
    {
        get => _settings.UnblockEnabled;
        set { _settings.UnblockEnabled = value; OnPropertyChanged(); }
    }

    public bool IsPyncmdEnabled
    {
        get => _settings.IsSourceEnabled(PyncmdAudioSource.SourceId);
        set { _settings.SetSourceEnabled(PyncmdAudioSource.SourceId, value); OnPropertyChanged(); }
    }

    public bool IsNavidromeEnabled
    {
        get => _settings.IsSourceEnabled(SubsonicAudioSource.SourceId);
        set { _settings.SetSourceEnabled(SubsonicAudioSource.SourceId, value); OnPropertyChanged(); }
    }

    public string NavidromeUrl
    {
        get => _settings.SubsonicUrl ?? string.Empty;
        set { _settings.SubsonicUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(NavidromeStatus)); }
    }

    public string NavidromeUser
    {
        get => _settings.SubsonicUser ?? string.Empty;
        set { _settings.SubsonicUser = value; OnPropertyChanged(); OnPropertyChanged(nameof(NavidromeStatus)); }
    }

    public string NavidromePassword
    {
        get => _settings.SubsonicPassword ?? string.Empty;
        set { _settings.SubsonicPassword = value; OnPropertyChanged(); OnPropertyChanged(nameof(NavidromeStatus)); }
    }

    public string NavidromeStatus => _settings.SubsonicConfiguration is null ? "未配置：填写地址、用户名和密码" : "已配置";

    // MARK: 歌曲缓存

    public bool AudioCacheEnabled
    {
        get => _settings.AudioCacheEnabled;
        set { _settings.AudioCacheEnabled = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<CacheLimitOption> CacheLimits { get; } =
        AppSettings.AudioCacheLimitOptions.Select(static bytes => new CacheLimitOption(bytes, ByteText(bytes))).ToList();

    public CacheLimitOption SelectedCacheLimit
    {
        get => CacheLimits.FirstOrDefault(o => o.Bytes == _settings.AudioCacheLimitBytes) ?? CacheLimits[1];
        set
        {
            if (value is null || value.Bytes == _settings.AudioCacheLimitBytes) return;   // same-value guard: see SelectedTheme
            _settings.AudioCacheLimitBytes = value.Bytes;
            _audioCache.Trim();
            OnPropertyChanged();
        }
    }

    [ObservableProperty] public partial string CacheUsage { get; private set; } = string.Empty;
    public string CacheLocation => _audioCache.Directory;
    /// <summary>The cache was moved away from the default folder; "恢复默认" is offered.</summary>
    public bool IsCustomCacheLocation => _settings.AudioCacheDirectory is not null;

    private void RefreshCacheUsage() =>
        CacheUsage = _audioCache.Count == 0 ? "还没有缓存的歌曲" : $"已缓存 {_audioCache.Count} 首，占用 {ByteText(_audioCache.TotalBytes)}";

    private static string ByteText(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB" : $"{bytes / (double)(1L << 20):0} MB";

    [RelayCommand]
    private void ClearCache()
    {
        _audioCache.Clear();
        RefreshCacheUsage();
        App.Services.Status.Show("已清空歌曲缓存");
    }

    [RelayCommand]
    private async Task OpenCacheFolder()
    {
        Directory.CreateDirectory(_audioCache.Directory);
        await Windows.System.Launcher.LaunchFolderPathAsync(_audioCache.Directory);
    }

    /// <summary>Lets the user pick a folder (a big D: drive, say); the cache goes into Cloudie\cache under it.</summary>
    [RelayCommand]
    private async Task ChangeCacheLocation()
    {
        if (App.MainWindow is null) return;
        var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");   // required on desktop, or PickSingleFolderAsync throws
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        // A folder that already is a Cloudie cache (moved here before) is used as is; anything else gets a subfolder.
        var target = File.Exists(Path.Combine(folder.Path, "index.json")) ? folder.Path : Path.Combine(folder.Path, "Cloudie", "cache");
        await MoveCacheAsync(target, custom: true);
    }

    [RelayCommand]
    private Task ResetCacheLocation() => MoveCacheAsync(AppPaths.CacheDirectory, custom: false);

    private async Task MoveCacheAsync(string target, bool custom)
    {
        if (!AudioCache.IsUsableDirectory(target))
        {
            App.Services.Status.Show($"无法写入 {target}，缓存位置未改变");
            return;
        }
        var before = _audioCache.Count;
        var moved = await Task.Run(() => _audioCache.Relocate(target));
        _settings.AudioCacheDirectory = custom ? target : null;
        OnPropertyChanged(nameof(CacheLocation));
        OnPropertyChanged(nameof(IsCustomCacheLocation));
        RefreshCacheUsage();
        App.Services.Status.Show(before == 0 ? $"缓存位置已改为 {target}" : $"缓存位置已改为 {target}，已搬运 {moved} / {before} 首");
    }

    public string AccountName => _account.Profile?.Nickname ?? "未登录";
    public bool IsLoggedIn => _account.IsLoggedIn;
    public string Version => AppInfo.VersionText;

    [ObservableProperty] public partial string? NavidromeTestResult { get; private set; }
    [ObservableProperty] public partial string? UpdateResult { get; private set; }

    /// <summary>Re-reads the account after the login dialog closes.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(IsLoggedIn));
    }

    [RelayCommand]
    private async Task TestNavidrome()
    {
        var config = _settings.SubsonicConfiguration;
        if (config is null)
        {
            NavidromeTestResult = "请先填写完整";
            return;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetStringAsync(config.Endpoint("ping.view"));
            NavidromeTestResult = response.Contains("\"status\":\"ok\"") ? "连接成功" : "服务器拒绝：" + response[..Math.Min(120, response.Length)];
        }
        catch (Exception error)
        {
            NavidromeTestResult = "连接失败：" + error.Message;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        UpdateResult = "正在检查…";
        try
        {
            var release = await App.Services.Updates.CheckAsync(AppInfo.Version);
            if (release is null)
            {
                UpdateResult = "已是最新版本";
                return;
            }
            UpdateResult = $"有新版本 {release.Version}";
            App.MainWindow?.ViewModel.OfferUpdate(release);
        }
        catch (Exception error)
        {
            UpdateResult = "检查失败：" + error.Message;
        }
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _account.LogoutAsync();
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(IsLoggedIn));
    }
}
