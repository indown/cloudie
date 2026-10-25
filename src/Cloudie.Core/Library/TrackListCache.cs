using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Library;

/// <summary>
/// What a list page showed last time: enough to draw it before the network answers. A playlist also carries
/// NetEase's <c>trackUpdateTime</c> and <c>trackCount</c>, so the cheap <c>/user/playlist</c> reply can tell
/// whether the heavy detail is still current.
/// </summary>
public sealed record CachedTrackList(
    string Key,
    string Title,
    string? Subtitle,
    string? Description,
    string? CoverUrl,
    IReadOnlyList<Track> Tracks,
    IReadOnlyList<TrackPrivilege>? Privileges,
    DateTimeOffset SavedAt,
    long TrackUpdateTime = 0,
    int TrackCount = 0,
    int SpecialType = 0,
    bool Subscribed = false,
    IReadOnlyList<AlbumSummary>? Albums = null,
    string? CountLabel = null)
{
    public static string PlaylistKey(long id) => $"playlist-{id}";
    public static string AlbumKey(long id) => $"album-{id}";
    public static string ArtistKey(long id) => $"artist-{id}";
    public static string DailyKey(DateOnly day) => $"daily-{day:yyyy-MM-dd}";

    /// <summary>Same songs in the same order: a refresh that changes nothing the rows show.</summary>
    public bool HasSameTracksAs(IReadOnlyList<Track> tracks) =>
        Tracks.Count == tracks.Count && Tracks.Select(static t => t.Id).SequenceEqual(tracks.Select(static t => t.Id));

    /// <summary>The list with <paramref name="track"/> first, where NetEase puts a song just added or liked.</summary>
    public CachedTrackList WithTrackOnTop(Track track)
    {
        var present = Tracks.Any(t => t.Id == track.Id);
        return this with { Tracks = [track, .. Tracks.Where(t => t.Id != track.Id)], TrackCount = present ? TrackCount : TrackCount + 1 };
    }

    public CachedTrackList WithoutTrack(long trackId) =>
        Tracks.Any(t => t.Id == trackId)
            ? this with { Tracks = Tracks.Where(t => t.Id != trackId).ToList(), TrackCount = Math.Max(0, TrackCount - 1) }
            : this;
}

/// <summary>
/// Lists kept after they were shown: one JSON file per list, written atomically, with the most recently used few
/// in memory, so a page opens at once and fetches afresh behind it. The folder is bounded by a file count, least
/// recently used out first. Thread-safe. Disk trouble never fails a caller: a list that cannot be written is
/// simply not kept, one that cannot be read reads as absent.
/// </summary>
public sealed class TrackListCache(string directory, int maxFiles = 40, int memorySize = 8, Func<DateTimeOffset>? clock = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, CachedTrackList> _memory = new();
    /// <summary>Keys in memory, least recently used first.</summary>
    private readonly List<string> _recent = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (static () => DateTimeOffset.UtcNow);

    public string Directory { get; } = directory;

    public CachedTrackList? TryGet(string key)
    {
        lock (_gate)
        {
            if (_memory.TryGetValue(key, out var list))
            {
                Touch(key);
                return list;
            }
            var path = PathOf(key);
            try
            {
                if (!File.Exists(path)) return null;
                list = JsonSerializer.Deserialize<CachedTrackList>(File.ReadAllText(path), Options);
                if (list is null) return null;
                File.SetLastWriteTimeUtc(path, _clock().UtcDateTime);   // used now, for the trim in Save
                Remember(key, list);
                return list;
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void Save(CachedTrackList list)
    {
        lock (_gate)
        {
            Remember(list.Key, list);
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = PathOf(list.Key);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(list, Options));
                File.Move(temp, path, overwrite: true);
                File.SetLastWriteTimeUtc(path, _clock().UtcDateTime);
                Trim();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Changes a cached list in place (a like, a removal, a rename); nothing happens when it is not cached.</summary>
    public void Update(string key, Func<CachedTrackList, CachedTrackList> change)
    {
        lock (_gate)
        {
            if (TryGet(key) is { } list) Save(change(list));
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _memory.Remove(key);
            _recent.Remove(key);
            try
            {
                File.Delete(PathOf(key));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void Remember(string key, CachedTrackList list)
    {
        _memory[key] = list;
        Touch(key);
        while (_recent.Count > memorySize)
        {
            _memory.Remove(_recent[0]);
            _recent.RemoveAt(0);
        }
    }

    private void Touch(string key)
    {
        _recent.Remove(key);
        _recent.Add(key);
    }

    /// <summary>Drops the least recently used files beyond the limit; the memory copies stay for this session.</summary>
    private void Trim()
    {
        var files = new DirectoryInfo(Directory).GetFiles("*.json").OrderBy(static f => f.LastWriteTimeUtc).ToList();
        foreach (var stale in files.Take(Math.Max(0, files.Count - maxFiles)))
        {
            try
            {
                stale.Delete();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private string PathOf(string key) => Path.Combine(Directory, key + ".json");
}
