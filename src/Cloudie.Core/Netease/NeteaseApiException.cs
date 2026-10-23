namespace Cloudie.Core.Netease;

/// <summary>A NetEase reply with a non-200 business code, or a transport failure.</summary>
public class NeteaseApiException(int code, string? message) : Exception(message ?? $"接口错误 ({code})")
{
    public int Code { get; } = code;
    public bool NeedsLogin => Code == 301;
}
