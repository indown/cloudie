using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Unblock;

/// <summary>What a third-party source needs to know about the song it must find.</summary>
public sealed record UnblockTrack(long Id, string Name, IReadOnlyList<string> Artists, string? Album, int DurationMs)
{
    public static UnblockTrack From(Track track) =>
        new(track.Id, track.Name, track.Artists.Select(static a => a.Name).ToList(), track.Album.Name, track.DurationMs);
}

/// <summary>A playable replacement for a gray track.</summary>
public sealed record ResolvedAudio(string SourceId, string SourceName, Uri Url, string? Format = null, int? BitRate = null);

public interface IAudioSource
{
    /// <summary>Stable identifier used in settings, e.g. <c>navidrome</c>.</summary>
    string Id { get; }
    string DisplayName { get; }
    /// <summary>Finds the track in this source; null when it is not there, an exception when the source failed.</summary>
    Task<ResolvedAudio?> ResolveAsync(UnblockTrack track, AudioQuality quality, CancellationToken cancellationToken = default);
}
