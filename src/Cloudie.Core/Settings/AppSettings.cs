using Cloudie.Core.Netease;
using Cloudie.Core.Unblock;

namespace Cloudie.Core.Settings;

public enum AppTheme { System, Light, Dark }

/// <summary>Typed view over the settings store; every property reads and writes through immediately.</summary>
public sealed class AppSettings(ISettingsStore store)
{
    public ISettingsStore Store { get; } = store;

    public static class Keys
    {
        public const string AudioQuality = "playback.audioQuality";
        public const string Volume = "playback.volume";
        public const string Theme = "appearance.theme";
        public const string SpoofDomesticIp = "network.spoofDomesticIp";
        public const string UnblockEnabled = "unblock.enabled";
        public const string EnabledSources = "unblock.sources";
        public const string SubsonicUrl = "unblock.navidrome.url";
        public const string SubsonicUser = "unblock.navidrome.user";
        public const string SubsonicPassword = "unblock.navidrome.password";
        public const string Scrobble = "playback.scrobble";
        public const string CheckUpdates = "app.checkUpdates";
        public const string WindowPlacement = "window.placement";
        public const string AudioCacheEnabled = "cache.audio.enabled";
        public const string AudioCacheLimit = "cache.audio.limitBytes";
        public const string AudioCacheDirectory = "cache.audio.directory";
    }

    public const long DefaultAudioCacheLimit = 1L << 30;
    public const long MinimumAudioCacheLimit = 64L << 20;
    /// <summary>The choices offered in Settings for the song cache size.</summary>
    public static readonly IReadOnlyList<long> AudioCacheLimitOptions = [512L << 20, 1L << 30, 2L << 30, 5L << 30, 10L << 30];

    public static readonly IReadOnlyList<string> DefaultSourceOrder = [PyncmdAudioSource.SourceId, SubsonicAudioSource.SourceId];

    public AudioQuality AudioQuality
    {
        get => Enum.TryParse<AudioQuality>(Store.GetString(Keys.AudioQuality), out var value) ? value : AudioQuality.ExHigh;
        set => Store.SetString(Keys.AudioQuality, value.ToString());
    }

    public double Volume
    {
        get => Math.Clamp(Store.GetDouble(Keys.Volume, 0.8), 0, 1);
        set => Store.SetDouble(Keys.Volume, Math.Clamp(value, 0, 1));
    }

    public AppTheme Theme
    {
        get => Enum.TryParse<AppTheme>(Store.GetString(Keys.Theme), out var value) ? value : AppTheme.System;
        set => Store.SetString(Keys.Theme, value.ToString());
    }

    /// <summary>海外解锁: send NetEase a domestic X-Real-IP so region-locked songs play directly.</summary>
    public bool SpoofDomesticIp
    {
        get => Store.GetBool(Keys.SpoofDomesticIp, true);
        set => Store.SetBool(Keys.SpoofDomesticIp, value);
    }

    /// <summary>Report plays to NetEase so they count toward listening history and recommendations.</summary>
    public bool Scrobble
    {
        get => Store.GetBool(Keys.Scrobble, true);
        set => Store.SetBool(Keys.Scrobble, value);
    }

    /// <summary>Look up the newest GitHub release at start-up and offer it in the status bar.</summary>
    public bool CheckUpdates
    {
        get => Store.GetBool(Keys.CheckUpdates, true);
        set => Store.SetBool(Keys.CheckUpdates, value);
    }

    /// <summary>歌曲缓存: keep played songs on disk and play them from there next time.</summary>
    public bool AudioCacheEnabled
    {
        get => Store.GetBool(Keys.AudioCacheEnabled, true);
        set => Store.SetBool(Keys.AudioCacheEnabled, value);
    }

    /// <summary>Upper bound of the song cache in bytes; never below <see cref="MinimumAudioCacheLimit"/>.</summary>
    public long AudioCacheLimitBytes
    {
        get => Math.Max(MinimumAudioCacheLimit, Store.GetLong(Keys.AudioCacheLimit, DefaultAudioCacheLimit));
        set => Store.SetLong(Keys.AudioCacheLimit, Math.Max(MinimumAudioCacheLimit, value));
    }

    /// <summary>Where 歌曲缓存 lives when the user moved it; null means the default under the app's data folder.</summary>
    public string? AudioCacheDirectory
    {
        get => Store.GetString(Keys.AudioCacheDirectory);
        set => Store.SetString(Keys.AudioCacheDirectory, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Last window bounds; null until the window has been moved or closed once.</summary>
    public WindowPlacement? WindowPlacement
    {
        get => WindowPlacement.Parse(Store.GetString(Keys.WindowPlacement));
        set => Store.SetString(Keys.WindowPlacement, value?.ToJson());
    }

    public bool UnblockEnabled
    {
        get => Store.GetBool(Keys.UnblockEnabled, true);
        set => Store.SetBool(Keys.UnblockEnabled, value);
    }

    public IReadOnlySet<string> EnabledSources
    {
        get
        {
            var raw = Store.GetString(Keys.EnabledSources);
            return raw is null
                ? new HashSet<string>(DefaultSourceOrder)
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        }
        set => Store.SetString(Keys.EnabledSources, string.Join(",", value.Order(StringComparer.Ordinal)));
    }

    public bool IsSourceEnabled(string id) => EnabledSources.Contains(id);

    public void SetSourceEnabled(string id, bool enabled)
    {
        var set = new HashSet<string>(EnabledSources);
        if (enabled) set.Add(id); else set.Remove(id);
        EnabledSources = set;
    }

    public string? SubsonicUrl
    {
        get => Store.GetString(Keys.SubsonicUrl);
        set => Store.SetString(Keys.SubsonicUrl, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string? SubsonicUser
    {
        get => Store.GetString(Keys.SubsonicUser);
        set => Store.SetString(Keys.SubsonicUser, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string? SubsonicPassword
    {
        get => Store.GetString(Keys.SubsonicPassword);
        set => Store.SetString(Keys.SubsonicPassword, string.IsNullOrEmpty(value) ? null : value);
    }

    public SubsonicConfiguration? SubsonicConfiguration => SubsonicConfiguration.TryCreate(SubsonicUrl, SubsonicUser, SubsonicPassword);
}
