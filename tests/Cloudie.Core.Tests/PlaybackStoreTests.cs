using Cloudie.Core.Netease.Models;
using Cloudie.Core.Playback;

namespace Cloudie.Core.Tests;

public class PlaybackStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cloudie-tests", Guid.NewGuid().ToString("N"));

    private PlaybackStore Store => new(Path.Combine(_dir, "queue.json"), Path.Combine(_dir, "position.json"));

    private static Track T(long id) => new()
    {
        Id = id, Name = $"song {id}", Dt = 1000 * (int)id,
        Ar = [new ArtistRef { Id = 10 + id, Name = $"artist {id}" }],
        Al = new AlbumRef { Id = 100 + id, Name = "album", PicUrl = "https://p1.music.126.net/x.jpg" }
    };

    [Fact]
    public void Queue_round_trips_with_shuffle_order_and_flags()
    {
        var queue = new PlaybackQueue(new Random(3));
        queue.Replace([T(1), T(2), T(3), T(4)], startIndex: 1);
        queue.SetShuffle(true);
        queue.RepeatMode = RepeatMode.All;
        var store = Store;

        store.SaveQueue(new PlaybackSnapshot(queue.OrderedItems, queue.PlayOrder, queue.IsShuffled, queue.RepeatMode, SourceId: 77, IsFm: false));
        var loaded = store.LoadQueue();

        Assert.NotNull(loaded);
        Assert.Equal([1L, 2L, 3L, 4L], loaded.Tracks.Select(t => t.Id));
        Assert.Equal(queue.PlayOrder, loaded.PlayOrder);
        Assert.True(loaded.IsShuffled);
        Assert.Equal(RepeatMode.All, loaded.RepeatMode);
        Assert.Equal(77, loaded.SourceId);
        Assert.Equal("artist 2", loaded.Tracks[1].ArtistNames);
        Assert.Equal(TimeSpan.FromSeconds(3), loaded.Tracks[2].Duration);

        var restored = new PlaybackQueue();
        restored.Restore(loaded.Tracks, loaded.PlayOrder, queue.CurrentIndex, loaded.IsShuffled);
        Assert.Equal(queue.Items.Select(t => t.Id), restored.Items.Select(t => t.Id));
        Assert.Equal(2, restored.Current!.Id);
        Assert.True(restored.IsShuffled);
        restored.SetShuffle(false);
        Assert.Equal([1L, 2L, 3L, 4L], restored.Items.Select(t => t.Id));
    }

    [Fact]
    public void Restore_falls_back_to_the_plain_order_when_the_play_order_is_broken()
    {
        var queue = new PlaybackQueue();
        queue.Restore([T(1), T(2), T(3)], playOrder: [2, 2, 0], currentIndex: 9, shuffled: true);
        Assert.False(queue.IsShuffled);
        Assert.Equal([1L, 2L, 3L], queue.Items.Select(t => t.Id));
        Assert.Equal(2, queue.CurrentIndex);
    }

    [Fact]
    public void Position_round_trips_and_missing_or_corrupt_files_read_as_null()
    {
        var store = Store;
        Assert.Null(store.LoadQueue());
        Assert.Null(store.LoadPosition());
        store.SavePosition(new PlaybackPosition(186006, 3, TimeSpan.FromSeconds(95.5)));
        Assert.Equal(new PlaybackPosition(186006, 3, TimeSpan.FromSeconds(95.5)), store.LoadPosition());
        File.WriteAllText(store.PositionPath, "{not json");
        Assert.Null(store.LoadPosition());
        store.Clear();
        Assert.False(File.Exists(store.PositionPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
