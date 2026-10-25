using System.Text.Json;
using Cloudie.Core.Library;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Tests;

public sealed class TrackListCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cloudie-list-tests", Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Track Song(long id, string name = "歌") =>
        JsonSerializer.Deserialize<Track>($$$"""{"id":{{{id}}},"name":"{{{name}}}","ar":[{"id":5,"name":"周华健"}],"al":{"id":7,"name":"专辑","picUrl":"https://p/{{{id}}}.jpg"},"dt":180000,"fee":8,"pc":{"uid":1}}""",
            NeteaseClient.ResponseOptions)!;

    private static CachedTrackList List(string key, params long[] ids) =>
        new(key, "我喜欢的音乐", "、小杨、 · 3 首", "描述", "https://p/cover.jpg",
            ids.Select(id => Song(id)).ToList(),
            [new TrackPrivilege { Id = ids.FirstOrDefault(), Fee = 8, Pl = 320000, St = 0 }],
            new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero),
            TrackUpdateTime: 1761390000000, TrackCount: ids.Length, SpecialType: 5, Subscribed: false,
            Albums: [new AlbumSummary { Id = 9, Name = "少年" }], CountLabel: "热门");

    private TrackListCache Cache(int maxFiles = 40, int memorySize = 8) => new(_dir, maxFiles, memorySize, () => _now);

    [Fact]
    public void A_saved_list_comes_back_whole_from_disk()
    {
        Cache().Save(List("playlist-1", 11, 12, 13));

        var loaded = Cache().TryGet("playlist-1");

        Assert.NotNull(loaded);
        Assert.Equal("我喜欢的音乐", loaded.Title);
        Assert.Equal("、小杨、 · 3 首", loaded.Subtitle);
        Assert.Equal([11L, 12L, 13L], loaded.Tracks.Select(static t => t.Id));
        Assert.Equal("周华健", loaded.Tracks[0].ArtistNames);
        Assert.Equal("https://p/11.jpg", loaded.Tracks[0].Album.PicUrl);
        Assert.True(loaded.Tracks[0].IsCloud);
        Assert.Equal(320000, loaded.Privileges![0].Pl);
        Assert.Equal(1761390000000, loaded.TrackUpdateTime);
        Assert.Equal(3, loaded.TrackCount);
        Assert.Equal(5, loaded.SpecialType);
        Assert.Equal("少年", loaded.Albums![0].Name);
        Assert.Equal("热门", loaded.CountLabel);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero), loaded.SavedAt);
    }

    [Fact]
    public void Nothing_cached_reads_as_null_and_so_does_a_corrupt_file()
    {
        var cache = Cache();
        Assert.Null(cache.TryGet("playlist-404"));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "playlist-9.json"), "{not json");

        Assert.Null(cache.TryGet("playlist-9"));
    }

    [Fact]
    public void The_memory_copy_serves_a_list_whose_file_is_gone()
    {
        var cache = Cache();
        cache.Save(List("album-1", 1));
        File.Delete(Path.Combine(_dir, "album-1.json"));

        Assert.NotNull(cache.TryGet("album-1"));
        Assert.Null(Cache().TryGet("album-1"));
    }

    [Fact]
    public void Memory_keeps_only_the_most_recently_used_lists()
    {
        var cache = Cache(memorySize: 2);
        cache.Save(List("a", 1));
        cache.Save(List("b", 2));
        Assert.NotNull(cache.TryGet("a"));          // a is now the more recent of the two
        cache.Save(List("c", 3));                   // b falls out of memory
        File.Delete(Path.Combine(_dir, "a.json"));
        File.Delete(Path.Combine(_dir, "b.json"));

        Assert.NotNull(cache.TryGet("a"));
        Assert.Null(cache.TryGet("b"));
    }

    [Fact]
    public void The_folder_keeps_the_most_recently_used_files_only()
    {
        var cache = Cache(maxFiles: 2);
        cache.Save(List("a", 1));
        _now += TimeSpan.FromMinutes(1);
        cache.Save(List("b", 2));
        _now += TimeSpan.FromMinutes(1);
        cache.Save(List("c", 3));                   // three files: a, the oldest, goes
        _now += TimeSpan.FromMinutes(1);
        Assert.NotNull(Cache().TryGet("b"));        // read from disk: b is used now
        _now += TimeSpan.FromMinutes(1);
        cache.Save(List("d", 4));                   // c is now the least recently used

        var files = Directory.GetFiles(_dir, "*.json").Select(Path.GetFileNameWithoutExtension).Order().ToList();
        Assert.Equal(["b", "d"], files);
    }

    [Fact]
    public void Update_rewrites_the_stored_list_and_Remove_forgets_it()
    {
        var cache = Cache();
        cache.Save(List("playlist-1", 11, 12));

        cache.Update("playlist-1", list => list.WithTrackOnTop(Song(13)));
        cache.Update("playlist-2", list => list.WithTrackOnTop(Song(13)));   // not cached: nothing happens

        var updated = Cache().TryGet("playlist-1");
        Assert.Equal([13L, 11L, 12L], updated!.Tracks.Select(static t => t.Id));
        Assert.Equal(3, updated.TrackCount);
        Assert.Null(Cache().TryGet("playlist-2"));

        cache.Remove("playlist-1");
        Assert.Null(cache.TryGet("playlist-1"));
        Assert.False(File.Exists(Path.Combine(_dir, "playlist-1.json")));
    }

    [Fact]
    public void Track_helpers_mirror_what_NetEase_does_to_a_playlist()
    {
        var list = List("playlist-1", 11, 12, 13);

        Assert.True(list.HasSameTracksAs([Song(11), Song(12), Song(13)]));
        Assert.False(list.HasSameTracksAs([Song(12), Song(11), Song(13)]));
        Assert.False(list.HasSameTracksAs([Song(11), Song(12)]));

        var moved = list.WithTrackOnTop(Song(12));
        Assert.Equal([12L, 11L, 13L], moved.Tracks.Select(static t => t.Id));
        Assert.Equal(3, moved.TrackCount);

        var without = list.WithoutTrack(12);
        Assert.Equal([11L, 13L], without.Tracks.Select(static t => t.Id));
        Assert.Equal(2, without.TrackCount);
        Assert.Same(list, list.WithoutTrack(99));
    }
}
