using Cloudie.Core.Netease;

namespace Cloudie.App.Services;

/// <summary>Turns an exception into the short Chinese line a page or the status bar shows for it.</summary>
public static class ErrorText
{
    public static string Describe(Exception error) => error switch
    {
        NeteaseApiException { NeedsLogin: true } => "需要登录后查看",
        NeteaseApiException api => api.Message,
        HttpRequestException or IOException => "网络连接失败，请稍后重试",
        TaskCanceledException or TimeoutException => "请求超时，请稍后重试",
        _ => error.Message
    };
}
