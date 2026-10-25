using Cloudie.Core.Netease;

namespace Cloudie.Core.Playback;

/// <summary>
/// Recently resolved song URLs by song and requested quality, so playing a song again soon after (a repeat,
/// a jump back in the queue) skips the NetEase / unlock-source round trip. Entries expire with
/// <see cref="ResolvedMedia.Lifetime"/>; the oldest go first when the cache is full.
/// </summary>
public sealed class ResolvedMediaCache(int capacity = 256)
{
    private readonly Dictionary<(long TrackId, AudioQuality Quality), ResolvedMedia> _entries = new();

    public int Count => _entries.Count;

    public void Remember(ResolvedMedia media)
    {
        _entries[(media.TrackId, media.RequestedQuality)] = media;
        if (_entries.Count <= capacity) return;
        var now = media.ResolvedAt;
        foreach (var key in _entries.Where(e => !e.Value.IsUsable(e.Key.TrackId, e.Key.Quality, now)).Select(e => e.Key).ToList())
        {
            _entries.Remove(key);
        }
        while (_entries.Count > capacity)
        {
            _entries.Remove(_entries.MinBy(e => e.Value.ResolvedAt).Key);
        }
    }

    /// <summary>The remembered answer when it is still usable; an expired one is dropped on the way out.</summary>
    public ResolvedMedia? TryGet(long trackId, AudioQuality quality, DateTimeOffset now)
    {
        if (!_entries.TryGetValue((trackId, quality), out var media)) return null;
        if (media.IsUsable(trackId, quality, now)) return media;
        _entries.Remove((trackId, quality));
        return null;
    }

    /// <summary>After a link failed to play: the next attempt resolves afresh.</summary>
    public void Forget(long trackId)
    {
        foreach (var key in _entries.Keys.Where(k => k.TrackId == trackId).ToList()) _entries.Remove(key);
    }
}
