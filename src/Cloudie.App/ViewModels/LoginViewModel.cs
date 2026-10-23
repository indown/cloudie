using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.App.Services;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using Windows.Storage.Streams;

namespace Cloudie.App.ViewModels;

/// <summary>QR-code sign-in: fetch a key, render the code, poll until the phone confirms.</summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly NeteaseApi _api = App.Services.Api;
    private readonly AccountService _account = App.Services.Account;
    private CancellationTokenSource? _cts;

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
            Status = "用网易云音乐 App 扫码登录";
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
                        await _account.ReloadAsync();
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
