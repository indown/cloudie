namespace Cloudie.Core.Netease;

/// <summary>Playback quality levels as NetEase names them.</summary>
public enum AudioQuality
{
    Standard,
    Higher,
    ExHigh,
    Lossless,
    HiRes,
    Sky,
    JyEffect
}

public static class AudioQualityExtensions
{
    public static string ToLevel(this AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => "standard",
        AudioQuality.Higher => "higher",
        AudioQuality.ExHigh => "exhigh",
        AudioQuality.Lossless => "lossless",
        AudioQuality.HiRes => "hires",
        AudioQuality.Sky => "sky",
        AudioQuality.JyEffect => "jyeffect",
        _ => "exhigh"
    };

    public static string DisplayName(this AudioQuality quality) => quality switch
    {
        AudioQuality.Standard => "标准",
        AudioQuality.Higher => "较高",
        AudioQuality.ExHigh => "极高",
        AudioQuality.Lossless => "无损",
        AudioQuality.HiRes => "Hi-Res",
        AudioQuality.Sky => "沉浸环绕声",
        AudioQuality.JyEffect => "高清环绕声",
        _ => quality.ToString()
    };

    public static AudioQuality FromLevel(string? level) => ParseLevel(level) ?? AudioQuality.ExHigh;

    /// <summary>The level as NetEase names it, or null for anything else (a format, a source's own word).</summary>
    public static AudioQuality? ParseLevel(string? level) => level switch
    {
        "standard" => AudioQuality.Standard,
        "higher" => AudioQuality.Higher,
        "exhigh" => AudioQuality.ExHigh,
        "lossless" => AudioQuality.Lossless,
        "hires" => AudioQuality.HiRes,
        "sky" => AudioQuality.Sky,
        "jyeffect" => AudioQuality.JyEffect,
        _ => null
    };

    public static bool IsLossless(this AudioQuality quality) => quality >= AudioQuality.Lossless;
}
