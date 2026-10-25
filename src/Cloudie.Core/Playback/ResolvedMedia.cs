using Cloudie.Core.Netease;

namespace Cloudie.Core.Playback;

/// <summary>
/// Where a song's audio comes from once a source agreed to serve it, and when that answer was obtained.
/// <see cref="SourceName"/> is null for NetEase itself; <see cref="Quality"/> is the served level or format;
/// <see cref="ViaSourceAccount"/> marks a NetEase link fetched through 音源账号 rather than the primary account;
/// <see cref="VipTier"/> is the VIP tier (main account or 音源账号) at hand when the answer was obtained, so a cached
/// copy made at a lower tier can be looked up again once a higher one arrives (<see cref="AudioCache.IsUpgradable"/>).
/// </summary>
public sealed record ResolvedMedia(long TrackId, Uri Url, string? SourceName, string? Quality, AudioQuality RequestedQuality, DateTimeOffset ResolvedAt, bool IsTrial = false, bool ViaSourceAccount = false, int VipTier = 0)
{
    /// <summary>NetEase links stop working after a while, so an answer fetched ahead of time is only trusted this long.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>True when this answer still fits: same song, the quality the user wants now, and not too old.</summary>
    public bool IsUsable(long trackId, AudioQuality quality, DateTimeOffset now) =>
        TrackId == trackId && RequestedQuality == quality && now - ResolvedAt < Lifetime && now >= ResolvedAt;
}
