using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cloudie.Core.Unblock;

namespace Cloudie.App.ViewModels;

/// <summary>
/// One row of the Navidrome server list in Settings. Top-level on purpose: a DataTemplate's x:DataType cannot
/// name a nested type. The switch and the buttons go through the owning <see cref="SettingsViewModel"/>, which
/// writes the settings; the row only shows the entry.
/// </summary>
public sealed partial class SubsonicServerViewModel(SettingsViewModel owner, SubsonicServer server) : ObservableObject
{
    public SubsonicServer Server { get; private set; } = server;

    public string Id => Server.Id;
    public string Title => Server.DisplayName;

    /// <summary>Address and user name, then what the last 测试 said, or that the entry still needs editing.</summary>
    public string Detail
    {
        get
        {
            var text = Server.User.Length > 0 ? $"{Server.Url} · {Server.User}" : Server.Url;
            if (Server.Configuration is null) return text + " · 未填完整，请编辑";
            return TestResult is null ? text : $"{text} · {TestResult}";
        }
    }

    public bool Enabled
    {
        get => Server.Enabled;
        set
        {
            if (value == Server.Enabled) return;   // the two-way binding writes back what it was just told
            owner.SaveServer(Server with { Enabled = value });
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial string? TestResult { get; private set; }

    /// <summary>The first row cannot move up; the owner refreshes this after every add, remove or move.</summary>
    public bool CanMoveUp => owner.SubsonicServers.IndexOf(this) > 0;

    /// <summary>The stored entry changed (dialog or switch): repaint the row.</summary>
    public void Show(SubsonicServer server)
    {
        Server = server;
        TestResult = null;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Detail));
    }

    public void RefreshPosition() => OnPropertyChanged(nameof(CanMoveUp));

    [RelayCommand]
    private async Task Test()
    {
        TestResult = "正在连接…";
        TestResult = await DescribeProbeAsync(Server);
    }

    [RelayCommand]
    private void Remove() => owner.RemoveServer(this);

    [RelayCommand]
    private void MoveUp() => owner.MoveServerUp(this);

    /// <summary>测试连接 in one line of Chinese, for the row and for the dialog.</summary>
    public static async Task<string> DescribeProbeAsync(SubsonicServer server)
    {
        if (server.Configuration is null) return "请先填写完整";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var refusal = await new SubsonicAudioSource(server, http).ProbeAsync();
            return refusal is null ? "连接成功" : "服务器拒绝：" + refusal;
        }
        catch (Exception error)
        {
            return "连接失败：" + error.Message;
        }
    }
}
