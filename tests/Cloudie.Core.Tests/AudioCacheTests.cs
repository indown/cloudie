using System.Net;
using Cloudie.Core.Netease;
using Cloudie.Core.Playback;
using Cloudie.Core.Settings;

namespace Cloudie.Core.Tests;

public sealed class AudioCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cloudie-cache-tests", Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private long _limit = 10_000;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static byte[] Flac(int length) => [.. "fLaC"u8.ToArray(), .. new byte[length - 4]];
    private static byte[] Mp3(int length) => [.. "ID3"u8.ToArray(), .. new byte[length - 3]];

    private static ResolvedMedia Media(long id, string path, AudioQuality quality = AudioQuality.ExHigh, string? served = "exhigh") =>
        new(id, new Uri("https://cdn.example.com" + path), null, served, quality, DateTimeOffset.UnixEpoch);

    /// <summary>Serves one body per path; a content length header can be forced to lie.</summary>
    private static StubHandler Server(Dictionary<string, byte[]> files, string contentType = "application/octet-stream", long? claimLength = null) =>
        new(request =>
        {
            if (!files.TryGetValue(request.RequestUri!.AbsolutePath, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            if (claimLength is { } length) content.Headers.ContentLength = length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

    private AudioCache Cache(StubHandler handler) => new(_dir, () => _limit, handler, () => _now);

    [Fact]
    public async Task Download_stores_the_file_sniffs_the_format_and_survives_a_reload()
    {
        var handler = Server(new() { ["/a.flac"] = Flac(1000), ["/b.mp3"] = Mp3(500) });
        using (var cache = Cache(handler))
        {
            var a = await cache.DownloadAsync(Media(1, "/a.flac", served: "lossless"));
            var b = await cache.DownloadAsync(Media(2, "/b.mp3"));
            Assert.Equal(AudioFormat.Flac, a!.ContentType);
            Assert.Equal(AudioFormat.Mpeg, b!.ContentType);
            Assert.Equal(1000, a.Bytes);
            Assert.EndsWith(".cache", cache.PathOf(a.Key));
            Assert.Equal(1000, new FileInfo(cache.PathOf(a.Key)).Length);
            Assert.Null(await cache.DownloadAsync(Media(1, "/a.flac")));   // already cached: no second download
            Assert.Equal(2, handler.Requests.Count);
        }
        using (var reloaded = Cache(handler))
        {
            Assert.Equal(2, reloaded.Count);
            Assert.Equal(1500, reloaded.TotalBytes);
            Assert.True(reloaded.Contains(1, AudioQuality.ExHigh));
            Assert.False(reloaded.Contains(1, AudioQuality.Lossless));   // keyed by the quality that was asked for
            var entry = reloaded.TryGet(2, AudioQuality.ExHigh)!;
            Assert.Equal("exhigh", entry.ServedQuality);
            Assert.Equal(AudioFormat.Mpeg, entry.ContentType);
        }
    }

    [Fact]
    public async Task TryGet_marks_the_song_played_and_drops_an_entry_whose_file_vanished()
    {
        using var cache = Cache(Server(new() { ["/a"] = Mp3(100) }));
        var stored = await cache.DownloadAsync(Media(1, "/a"));
        _now += TimeSpan.FromMinutes(5);
        var played = cache.TryGet(1, AudioQuality.ExHigh)!;
        Assert.Equal(stored!.CreatedAt, played.CreatedAt);
        Assert.Equal(_now, played.LastPlayedAt);
        File.Delete(cache.PathOf(played.Key));
        Assert.Null(cache.TryGet(1, AudioQuality.ExHigh));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public async Task Eviction_removes_the_least_recently_played_and_spares_the_newest()
    {
        _limit = 2500;
        using var cache = Cache(Server(new() { ["/a"] = Mp3(1000), ["/b"] = Mp3(1000), ["/c"] = Mp3(1000), ["/d"] = Mp3(1000) }));
        await cache.DownloadAsync(Media(1, "/a"));
        _now += TimeSpan.FromMinutes(1);
        await cache.DownloadAsync(Media(2, "/b"));
        _now += TimeSpan.FromMinutes(1);
        await cache.DownloadAsync(Media(3, "/c"));          // 3000 > 2500: the oldest (a) goes
        Assert.False(cache.Contains(1, AudioQuality.ExHigh));
        Assert.Equal(2000, cache.TotalBytes);
        _now += TimeSpan.FromMinutes(1);
        cache.TryGet(2, AudioQuality.ExHigh);               // b played again: now newer than c
        _now += TimeSpan.FromMinutes(1);
        await cache.DownloadAsync(Media(4, "/d"));          // c is the least recently played
        Assert.True(cache.Contains(2, AudioQuality.ExHigh));
        Assert.False(cache.Contains(3, AudioQuality.ExHigh));
        Assert.True(cache.Contains(4, AudioQuality.ExHigh));
        Assert.False(File.Exists(cache.PathOf(new AudioCacheKey(3, AudioQuality.ExHigh))));
        _limit = 1500;
        cache.Trim();                                        // lowered in Settings: only the newest stays
        Assert.Equal(1, cache.Count);
        Assert.True(cache.Contains(4, AudioQuality.ExHigh));
    }

    [Fact]
    public async Task A_short_or_failed_download_leaves_no_file_and_no_entry()
    {
        using var cache = Cache(Server(new() { ["/short"] = Mp3(1000) }, claimLength: 2000));
        await Assert.ThrowsAsync<IOException>(() => cache.DownloadAsync(Media(1, "/short")));
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.DownloadAsync(Media(2, "/missing")));
        Assert.Equal(0, cache.Count);
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*.cache") : []);
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*.part") : []);
        Assert.False(cache.IsDownloading(1, AudioQuality.ExHigh));
    }

    [Fact]
    public async Task Clear_deletes_everything_and_a_stray_part_file_is_removed_on_load()
    {
        using var cache = Cache(Server(new() { ["/a"] = Mp3(100) }));
        await cache.DownloadAsync(Media(1, "/a"));
        var changes = 0;
        cache.Changed += () => changes++;
        cache.Clear();
        Assert.Equal(1, changes);
        Assert.Equal(0, cache.Count);
        Assert.Empty(Directory.GetFiles(_dir, "*.cache"));
        File.WriteAllBytes(Path.Combine(_dir, "9-exhigh.cache.part"), new byte[10]);
        using var reloaded = Cache(Server(new()));
        Assert.Equal(0, reloaded.Count);
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task Relocate_moves_the_files_and_the_index_and_the_old_folder_is_left_empty()
    {
        using var cache = Cache(Server(new() { ["/a"] = Mp3(100), ["/b"] = Mp3(200) }));
        await cache.DownloadAsync(Media(1, "/a"));
        await cache.DownloadAsync(Media(2, "/b"));
        var target = Path.Combine(_dir, "moved", "cache");
        var changes = 0;
        cache.Changed += () => changes++;
        Assert.Equal(2, cache.Relocate(target));
        Assert.Equal(1, changes);
        Assert.Equal(target, cache.Directory);
        Assert.StartsWith(target, cache.PathOf(new AudioCacheKey(1, AudioQuality.ExHigh)));
        Assert.True(File.Exists(cache.PathOf(new AudioCacheKey(1, AudioQuality.ExHigh))));
        Assert.Empty(Directory.GetFiles(_dir, "*.cache"));
        Assert.False(File.Exists(Path.Combine(_dir, "index.json")));
        Assert.Equal(2, cache.Relocate(target));   // same place again: nothing to do
        using var reloaded = new AudioCache(target, () => _limit, Server(new()), () => _now);
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(300, reloaded.TotalBytes);
        Assert.NotNull(reloaded.TryGet(2, AudioQuality.ExHigh));
    }

    [Fact]
    public void A_usable_directory_is_writable_and_rooted()
    {
        Assert.True(AudioCache.IsUsableDirectory(Path.Combine(_dir, "probe")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "probe")));   // the probe file is gone again
        var file = Path.Combine(_dir, "file");
        File.WriteAllText(file, "x");
        Assert.False(AudioCache.IsUsableDirectory(Path.Combine(file, "sub")));   // a file where a folder must be
        Assert.False(AudioCache.IsUsableDirectory("relative/cache"));
        Assert.False(AudioCache.IsUsableDirectory(" "));
    }

    [Fact]
    public void Caching_starts_after_thirty_seconds_or_past_the_halfway_point()
    {
        Assert.False(AudioCache.ShouldStartCaching(TimeSpan.FromSeconds(29), TimeSpan.FromMinutes(4)));
        Assert.True(AudioCache.ShouldStartCaching(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(4)));
        Assert.True(AudioCache.ShouldStartCaching(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(18)));
        Assert.False(AudioCache.ShouldStartCaching(TimeSpan.FromSeconds(10), TimeSpan.Zero));
    }

    [Fact]
    public void Formats_are_recognised_from_magic_bytes_headers_and_level_names()
    {
        Assert.Equal(AudioFormat.Flac, AudioFormat.Sniff("fLaC\0\0\0"u8));
        Assert.Equal(AudioFormat.Mpeg, AudioFormat.Sniff("ID3\x04"u8));
        Assert.Equal(AudioFormat.Mpeg, AudioFormat.Sniff([0xFF, 0xFB, 0x90, 0x00]));
        Assert.Equal(AudioFormat.Aac, AudioFormat.Sniff([0xFF, 0xF1, 0x50, 0x80]));
        Assert.Equal(AudioFormat.Mp4, AudioFormat.Sniff([0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ']));
        Assert.Equal(AudioFormat.Ogg, AudioFormat.Sniff("OggS\0\x02"u8));
        Assert.Null(AudioFormat.Sniff("<html>"u8));
        Assert.Equal(AudioFormat.Flac, AudioFormat.FromContentType("audio/x-flac"));
        Assert.Equal(AudioFormat.Mpeg, AudioFormat.FromContentType("Audio/MP3"));
        Assert.Null(AudioFormat.FromContentType("application/octet-stream"));
        Assert.Equal(AudioFormat.Flac, AudioFormat.FromServedQuality("lossless"));
        Assert.Equal(AudioFormat.Mpeg, AudioFormat.FromServedQuality("exhigh"));
        Assert.Equal(AudioFormat.Flac, AudioFormat.FromServedQuality("flac"));
        Assert.Null(AudioFormat.FromServedQuality("试听"));
    }

    [Fact]
    public void Resolved_media_cache_returns_fresh_answers_only_and_forgets_failed_songs()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var cache = new ResolvedMediaCache(capacity: 2);
        var a = new ResolvedMedia(1, new Uri("https://x/a"), null, "exhigh", AudioQuality.ExHigh, now);
        cache.Remember(a);
        Assert.Same(a, cache.TryGet(1, AudioQuality.ExHigh, now + TimeSpan.FromMinutes(9)));
        Assert.Null(cache.TryGet(1, AudioQuality.Lossless, now));
        Assert.Null(cache.TryGet(1, AudioQuality.ExHigh, now + ResolvedMedia.Lifetime));   // expired, and dropped
        Assert.Equal(0, cache.Count);
        cache.Remember(a);
        cache.Remember(new ResolvedMedia(2, new Uri("https://x/b"), null, "exhigh", AudioQuality.ExHigh, now + TimeSpan.FromMinutes(1)));
        cache.Remember(new ResolvedMedia(3, new Uri("https://x/c"), null, "exhigh", AudioQuality.ExHigh, now + TimeSpan.FromMinutes(2)));
        Assert.Equal(2, cache.Count);
        Assert.Null(cache.TryGet(1, AudioQuality.ExHigh, now + TimeSpan.FromMinutes(2)));   // oldest made room
        cache.Forget(2);
        Assert.Null(cache.TryGet(2, AudioQuality.ExHigh, now + TimeSpan.FromMinutes(2)));
        Assert.NotNull(cache.TryGet(3, AudioQuality.ExHigh, now + TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Cache_settings_have_defaults_and_a_floor()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        Assert.True(settings.AudioCacheEnabled);
        Assert.Equal(AppSettings.DefaultAudioCacheLimit, settings.AudioCacheLimitBytes);
        settings.AudioCacheLimitBytes = 2L << 30;
        Assert.Equal(2L << 30, settings.AudioCacheLimitBytes);
        settings.AudioCacheLimitBytes = 1;
        Assert.Equal(AppSettings.MinimumAudioCacheLimit, settings.AudioCacheLimitBytes);
        settings.AudioCacheEnabled = false;
        Assert.False(settings.AudioCacheEnabled);
        Assert.Null(settings.AudioCacheDirectory);
        settings.AudioCacheDirectory = @"  D:\Cloudie\cache  ";
        Assert.Equal(@"D:\Cloudie\cache", settings.AudioCacheDirectory);
        settings.AudioCacheDirectory = "";
        Assert.Null(settings.AudioCacheDirectory);
    }
}
