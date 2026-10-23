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
    }

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
