using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Settings;
using Cloudie.Core.Unblock;

namespace Cloudie.App.ViewModels;

public sealed record QualityOption(AudioQuality Value, string Name);
public sealed record ThemeOption(AppTheme Value, string Name);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings = App.Services.Settings;
    private readonly AccountService _account = App.Services.Account;

    public IReadOnlyList<QualityOption> Qualities { get; } =
        new[] { AudioQuality.Standard, AudioQuality.ExHigh, AudioQuality.Lossless, AudioQuality.HiRes }
            .Select(static q => new QualityOption(q, q.DisplayName())).ToList();

    public IReadOnlyList<ThemeOption> Themes { get; } =
        [new(AppTheme.System, "跟随系统"), new(AppTheme.Light, "浅色"), new(AppTheme.Dark, "深色")];

    public QualityOption SelectedQuality
    {
        get => Qualities.First(q => q.Value == _settings.AudioQuality);
        set
        {
            if (value is null) return;
            _settings.AudioQuality = value.Value;
            OnPropertyChanged();
        }
    }

    public ThemeOption SelectedTheme
    {
        get => Themes.First(t => t.Value == _settings.Theme);
        set
        {
            if (value is null) return;
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
