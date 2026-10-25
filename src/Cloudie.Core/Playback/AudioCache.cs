using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudie.Core.Netease;

namespace Cloudie.Core.Playback;

public readonly record struct AudioCacheKey(long TrackId, AudioQuality Quality)
{
    public string FileStem => $"{TrackId}-{Quality.ToString().ToLowerInvariant()}";
}

/// <summary>One cached song: where it came from, what it is, how big, and when it was last played.</summary>
/// <summary>
/// One cached file. <see cref="Quality"/> is what was asked for, <see cref="ServedQuality"/> what came back;
/// <see cref="VipTier"/> is the VIP tier at hand when it was fetched (0 in indexes written before it existed), which
/// decides whether a copy that fell short is worth looking up again (<see cref="AudioCache.IsUpgradable"/>).
/// </summary>
public sealed record AudioCacheEntry(
    long TrackId,
    AudioQuality Quality,
    string? SourceName,
    string? ServedQuality,
    string ContentType,
    long Bytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastPlayedAt,
    int VipTier = 0)
{
    [JsonIgnore] public AudioCacheKey Key => new(TrackId, Quality);
}

/// <summary>
/// Songs kept on disk after they were played (歌曲缓存), so the next play needs no network. Files live under
/// one directory with a non-audio extension; <c>index.json</c> beside them records what each file is. The
/// cache is bounded by a byte limit: least recently played out first, never the one just added or played.
/// Thread-safe; downloads run on the caller's thread pool context, the index is guarded by a lock.
/// </summary>
public sealed class AudioCache : IDisposable
{
    public const string FileExtension = ".cache";
    private const string PartExtension = ".part";
    private const string IndexName = "index.json";

    /// <summary>A song is cached once it has played this long, or past its halfway point, so skips cost nothing.</summary>
    public static readonly TimeSpan MinimumPlayedBeforeCaching = TimeSpan.FromSeconds(30);
    /// <summary>A download with no bytes for this long is abandoned.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Index(List<AudioCacheEntry> Entries);

    private string _directory;
    private readonly Func<long> _limitBytes;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<AudioCacheKey, AudioCacheEntry> _entries = new();
    private readonly HashSet<AudioCacheKey> _inFlight = new();
    private long _total;
    private bool _loaded;

    public AudioCache(string directory, Func<long> limitBytes, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        _limitBytes = limitBytes;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.None }, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", NeteaseClient.DefaultUserAgent);
    }

    public static bool ShouldStartCaching(TimeSpan position, TimeSpan duration) =>
        position >= MinimumPlayedBeforeCaching || (duration > TimeSpan.Zero && position >= duration / 2);

    /// <summary>True when the folder exists (or can be created) and a file can be written there: the test a chosen location must pass.</summary>
    public static bool IsUsableDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            System.IO.Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Where the files are now; changes with <see cref="Relocate"/>.</summary>
    public string Directory
    {
        get { lock (_gate) return _directory; }
    }

    /// <summary>
    /// Moves the cache to <paramref name="directory"/>: every file, then the index. A file that cannot be
    /// moved is dropped from the cache rather than left behind. Returns how many songs made the move.
    /// </summary>
    public int Relocate(string directory)
    {
        int moved;
        lock (_gate)
        {
            EnsureLoaded();
            var target = Path.GetFullPath(directory);
            if (string.Equals(target, Path.GetFullPath(_directory), StringComparison.OrdinalIgnoreCase)) return _entries.Count;
            System.IO.Directory.CreateDirectory(target);
            var previous = _directory;
            moved = 0;
            foreach (var (key, entry) in _entries.ToList())
            {
                try
                {
                    File.Move(Path.Combine(previous, key.FileStem + FileExtension), Path.Combine(target, key.FileStem + FileExtension), overwrite: true);
                    moved++;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _entries.Remove(key);
                    _total -= entry.Bytes;
                }
            }
            _directory = target;
            SaveLocked();
            TryDelete(Path.Combine(previous, IndexName));
        }
        Changed?.Invoke();
        return moved;
    }

    /// <summary>Raised after the contents changed (a song added, removed or everything cleared); any thread.</summary>
    public event Action? Changed;

    public long TotalBytes
    {
        get { lock (_gate) { EnsureLoaded(); return _total; } }
    }

    public int Count
    {
        get { lock (_gate) { EnsureLoaded(); return _entries.Count; } }
    }

    public string PathOf(AudioCacheKey key) => Path.Combine(_directory, key.FileStem + FileExtension);

    public bool Contains(long trackId, AudioQuality quality)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _entries.ContainsKey(new AudioCacheKey(trackId, quality));
        }
    }

    public bool IsDownloading(long trackId, AudioQuality quality)
    {
        lock (_gate) return _inFlight.Contains(new AudioCacheKey(trackId, quality));
    }

    /// <summary>The cached song without counting it as played (for looking ahead); null when there is none.</summary>
    public AudioCacheEntry? Peek(long trackId, AudioQuality quality)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _entries.GetValueOrDefault(new AudioCacheKey(trackId, quality));
        }
    }

    /// <summary>
    /// The cached song, marked as played now; null when there is none. An entry whose file has gone missing
    /// is dropped on the spot.
    /// </summary>
    public AudioCacheEntry? TryGet(long trackId, AudioQuality quality)
    {
        var key = new AudioCacheKey(trackId, quality);
        lock (_gate)
        {
            EnsureLoaded();
            if (!_entries.TryGetValue(key, out var entry)) return null;
            if (!File.Exists(PathOf(key)))
            {
                _entries.Remove(key);
                _total -= entry.Bytes;
                SaveLocked();
                return null;
            }
            var touched = entry with { LastPlayedAt = _clock() };
            _entries[key] = touched;
            SaveLocked();
            return touched;
        }
    }

    /// <summary>
    /// Downloads <paramref name="media"/> into the cache. Returns null when the song is already cached (unless
    /// <paramref name="replace"/> asks for the file to be swapped for this better one) or being downloaded; throws
    /// when the download fails, leaving the cache as it was.
    /// </summary>
    public async Task<AudioCacheEntry?> DownloadAsync(ResolvedMedia media, bool replace = false, CancellationToken cancellationToken = default)
    {
        var key = new AudioCacheKey(media.TrackId, media.RequestedQuality);
        lock (_gate)
        {
            EnsureLoaded();
            if ((!replace && _entries.ContainsKey(key)) || !_inFlight.Add(key)) return null;
        }
        var final = PathOf(key);
        var part = final + PartExtension;
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            using var response = await _http.GetAsync(media.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var expected = response.Content.Headers.ContentLength;
            var head = new byte[16];
            var headLength = 0;
            long written = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var buffer = new byte[1 << 16];
                while (true)
                {
                    idle.CancelAfter(IdleTimeout);
                    int read;
                    try
                    {
                        read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new IOException($"下载停滞超过 {IdleTimeout.TotalSeconds:0} 秒");
                    }
                    if (read == 0) break;
                    if (headLength < head.Length)
                    {
                        var take = Math.Min(head.Length - headLength, read);
                        buffer.AsSpan(0, take).CopyTo(head.AsSpan(headLength));
                        headLength += take;
                    }
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                }
            }
            if (written == 0) throw new IOException("下载内容为空");
            if (expected is { } length && written != length) throw new IOException($"下载不完整：{written} / {length} 字节");
            var contentType = AudioFormat.Sniff(head.AsSpan(0, headLength))
                              ?? AudioFormat.FromContentType(response.Content.Headers.ContentType?.MediaType)
                              ?? AudioFormat.FromServedQuality(media.Quality)
                              ?? AudioFormat.Mpeg;
            File.Move(part, final, overwrite: true);
            var now = _clock();
            var entry = new AudioCacheEntry(media.TrackId, media.RequestedQuality, media.SourceName, media.Quality, contentType, written, now, now, media.VipTier);
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var previous)) _total -= previous.Bytes;
                _entries[key] = entry;
                _total += written;
                TrimLocked(keep: key);
                SaveLocked();
            }
            Changed?.Invoke();
            return entry;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
        finally
        {
            lock (_gate) _inFlight.Remove(key);
        }
    }

    /// <summary>
    /// A fresh lookup at <paramref name="vipTier"/> found nothing better than the cached file: the tier is remembered,
    /// so the song is not looked up again until VIP improves once more.
    /// </summary>
    public void Confirm(long trackId, AudioQuality quality, int vipTier)
    {
        var key = new AudioCacheKey(trackId, quality);
        lock (_gate)
        {
            EnsureLoaded();
            if (!_entries.TryGetValue(key, out var entry) || entry.VipTier >= vipTier) return;
            _entries[key] = entry with { VipTier = vipTier };
            SaveLocked();
        }
    }

    // MARK: 升级 — a copy made before VIP was at hand

    /// <summary>
    /// True when <paramref name="entry"/> was fetched with less VIP than <paramref name="vipTier"/> and did not get
    /// what was asked for: a NetEase file below the requested level (a non-VIP account is served a downgrade, not a
    /// refusal) or a gray-track copy. Such a song is looked up once more before it plays from the cache.
    /// </summary>
    public static bool IsUpgradable(AudioCacheEntry entry, int vipTier) =>
        vipTier > entry.VipTier && (entry.SourceName is not null || Rank(entry.ServedQuality) < (int)entry.Quality);

    /// <summary>
    /// True when a fresh NetEase answer beats the cached file: a higher level, or the same level where the cache holds
    /// a third-party copy. Trials and gray-track answers never replace a cached file.
    /// </summary>
    public static bool IsBetter(ResolvedMedia media, AudioCacheEntry entry)
    {
        if (media.SourceName is not null || media.IsTrial) return false;
        var fresh = Rank(media.Quality);
        var cached = Rank(entry.ServedQuality);
        return entry.SourceName is null ? fresh > cached : fresh >= cached;
    }

    /// <summary>Orders served qualities: NetEase levels as <see cref="AudioQuality"/>, formats by what they imply, unknown lowest.</summary>
    internal static int Rank(string? servedQuality) => servedQuality switch
    {
        "flac" => (int)AudioQuality.Lossless,
        "mp3" or "aac" or "m4a" => (int)AudioQuality.ExHigh,
        _ => AudioQualityExtensions.ParseLevel(servedQuality) is { } level ? (int)level : -1
    };

    public void Remove(long trackId, AudioQuality quality)
    {
        var key = new AudioCacheKey(trackId, quality);
        lock (_gate)
        {
            EnsureLoaded();
            if (!_entries.Remove(key, out var entry)) return;
            _total -= entry.Bytes;
            TryDelete(PathOf(key));
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Deletes every cached song. A file that cannot be deleted right now keeps its entry.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            foreach (var (key, entry) in _entries.ToList())
            {
                if (!TryDelete(PathOf(key))) continue;
                _entries.Remove(key);
                _total -= entry.Bytes;
            }
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Applies the current limit, e.g. after it was lowered in Settings.</summary>
    public void Trim()
    {
        bool changed;
        lock (_gate)
        {
            EnsureLoaded();
            changed = TrimLocked(keep: null);
            if (changed) SaveLocked();
        }
        if (changed) Changed?.Invoke();
    }

    public void Dispose() => _http.Dispose();

    // MARK: Internals (callers hold the lock)

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _entries.Clear();
        _total = 0;
        if (!System.IO.Directory.Exists(_directory)) return;
        foreach (var stray in System.IO.Directory.EnumerateFiles(_directory, "*" + PartExtension)) TryDelete(stray);
        var indexPath = Path.Combine(_directory, IndexName);
        if (!File.Exists(indexPath)) return;
        Index? index = null;
        try
        {
            index = JsonSerializer.Deserialize<Index>(File.ReadAllText(indexPath), JsonOptions);
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
        }
        foreach (var entry in index?.Entries ?? [])
        {
            var info = new FileInfo(PathOf(entry.Key));
            if (!info.Exists || info.Length != entry.Bytes) continue;
            _entries[entry.Key] = entry;
            _total += entry.Bytes;
        }
    }

    /// <summary>Evicts least recently played songs until the limit holds; <paramref name="keep"/> and the newest survive.</summary>
    private bool TrimLocked(AudioCacheKey? keep)
    {
        var limit = Math.Max(0, _limitBytes());
        var changed = false;
        var stuck = new HashSet<AudioCacheKey>();
        while (_total > limit)
        {
            var newest = _entries.Count == 0 ? default : _entries.Values.MaxBy(e => e.LastPlayedAt)!.Key;
            var victim = _entries.Values
                .Where(e => e.Key != keep && e.Key != newest && !stuck.Contains(e.Key))
                .MinBy(e => e.LastPlayedAt);
            if (victim is null) break;
            if (!TryDelete(PathOf(victim.Key)))
            {
                stuck.Add(victim.Key);
                continue;
            }
            _entries.Remove(victim.Key);
            _total -= victim.Bytes;
            changed = true;
        }
        return changed;
    }

    private void SaveLocked()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, IndexName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Index(_entries.Values.OrderBy(e => e.LastPlayedAt).ToList()), JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
            // The index is rebuilt from the files on the next load; losing one save is harmless.
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
