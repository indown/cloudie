using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage.Streams;

namespace Cloudie.App.ViewModels;

/// <summary>
/// QR-code sign-in: fetch a key, render the code, poll until the phone confirms. Works for the main
/// account and for 音源账号 alike: the cookies land in whichever client's jar made the requests.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly NeteaseApi _api;
    private readonly Func<Task> _onConfirmed;
    private readonly string _scanHint;
    private CancellationTokenSource? _cts;

    /// <param name="api">The client whose cookie jar receives the session.</param>
    /// <param name="onConfirmed">Run after NetEase confirmed the scan, typically a profile reload.</param>
    /// <param name="scanHint">What to show next to the code, e.g. which account to scan with.</param>
    public LoginViewModel(NeteaseApi api, Func<Task> onConfirmed, string scanHint)
    {
        _api = api;
        _onConfirmed = onConfirmed;
        _scanHint = scanHint;
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
            var key = await _api.GetQrKeyAsync(cts.Token);
            QrImage = await RenderAsync(NeteaseApi.QrLoginUrl(key));
            Status = _scanHint;
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                var check = await _api.CheckQrAsync(key, cts.Token);
                switch (check.State)
                {
                    case QrLoginState.Scanned:
                        Status = "已扫码，请在手机上确认";
                        break;
                    case QrLoginState.Expired:
                        Status = "二维码已过期，正在刷新…";
                        key = await _api.GetQrKeyAsync(cts.Token);
                        QrImage = await RenderAsync(NeteaseApi.QrLoginUrl(key));
                        break;
                    case QrLoginState.Confirmed:
                        Status = "登录成功";
                        await _onConfirmed();
                        IsDone = true;
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Status = "登录失败：" + error.Message;
        }
    }

    public void Stop() => _cts?.Cancel();

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
