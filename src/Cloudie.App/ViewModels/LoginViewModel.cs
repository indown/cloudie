using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage.Streams;

namespace Cloudie.App.ViewModels;

/// <summary>
/// QR-code sign-in for the main account and for 音源账号 alike: <see cref="QrLoginSession"/> runs the loop
/// (fetch a key, poll, replace a retired code, retry after a failed request), this renders the code and
/// words the phase. The cookies land in whichever client's jar made the requests.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly QrLoginSession _session;
    private readonly string _scanHint;
    private CancellationTokenSource? _cts;

    /// <param name="api">The client whose cookie jar receives the session.</param>
    /// <param name="onConfirmed">Run after NetEase confirmed the scan, typically a profile reload.</param>
    /// <param name="scanHint">What to show next to the code, e.g. which account to scan with.</param>
    public LoginViewModel(NeteaseApi api, Func<Task> onConfirmed, string scanHint)
    {
        _scanHint = scanHint;
        _session = new QrLoginSession(api, ShowCodeAsync, onConfirmed);
        _session.PhaseChanged += OnPhaseChanged;
    }

    [ObservableProperty] public partial BitmapImage? QrImage { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = "正在生成二维码…";
    [ObservableProperty] public partial bool IsDone { get; private set; }

    public async Task StartAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await _session.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Stop() => _cts?.Cancel();

    private async Task ShowCodeAsync(string key, CancellationToken ct) => QrImage = await RenderAsync(NeteaseApi.QrLoginUrl(key));

    /// <summary>On the UI thread: the session keeps the context it was started on.</summary>
    private void OnPhaseChanged(QrLoginPhase phase, Exception? error)
    {
        Status = phase switch
        {
            QrLoginPhase.Loading => "正在生成二维码…",
            QrLoginPhase.Waiting => _scanHint,
            QrLoginPhase.Refreshed => "二维码已刷新，" + _scanHint,
            QrLoginPhase.Scanned => "已扫码，请在手机上确认",
            QrLoginPhase.Retrying => "网络出错，稍后重试：" + ErrorText.Describe(error!),
            _ => "登录成功"
        };
        if (phase == QrLoginPhase.Done) IsDone = true;
    }

    private static async Task<BitmapImage> RenderAsync(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        await image.SetSourceAsync(stream);
        return image;
    }
}
