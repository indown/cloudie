using Cloudie.Core.Netease.Models;
using Cloudie.Core.Playback;
using Cloudie.Core.Settings;

namespace Cloudie.Core.Tests;

public class PlaybackQueueTests
{
    private static Track T(long id) => new() { Id = id, Name = $"song {id}" };

    [Fact]
    public void Advances_and_wraps_according_to_repeat_mode()
    {
        var queue = new PlaybackQueue();
        queue.Replace([T(1), T(2), T(3)], startIndex: 2);
        Assert.Equal(3, queue.Current!.Id);
        Assert.False(queue.AdvanceToNext());
        queue.RepeatMode = RepeatMode.All;
        Assert.True(queue.AdvanceToNext());
        Assert.Equal(1, queue.Current!.Id);
        queue.RepeatMode = RepeatMode.One;
        Assert.True(queue.AdvanceToNext());
        Assert.Equal(1, queue.Current!.Id);
        Assert.True(queue.AdvanceToNext(manual: true));
        Assert.Equal(2, queue.Current!.Id);
        Assert.True(queue.GoToPrevious());
        Assert.Equal(1, queue.Current!.Id);
        Assert.True(queue.GoToPrevious());
        Assert.Equal(3, queue.Current!.Id);
    }

    [Fact]
    public void Shuffle_keeps_the_current_song_first_and_restores_order()
    {
        var queue = new PlaybackQueue(new Random(7));
        queue.Replace(Enumerable.Range(1, 10).Select(i => T(i)), startIndex: 4);
        queue.SetShuffle(true);
        Assert.Equal(5, queue.Current!.Id);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal(10, queue.Count);
        Assert.NotEqual(Enumerable.Range(1, 10).Select(i => (long)i), queue.Items.Select(t => t.Id));
        queue.SetShuffle(false);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), queue.Items.Select(t => t.Id));
        Assert.Equal(4, queue.CurrentIndex);
    }

    [Fact]
    public void Insert_next_remove_and_move()
    {
        var queue = new PlaybackQueue();
        queue.Replace([T(1), T(2)], 0);
        queue.InsertNext(T(9));
        Assert.Equal([1L, 9L, 2L], queue.Items.Select(t => t.Id));
        Assert.True(queue.MoveTo(2));
        Assert.True(queue.RemoveAt(0));
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Equal(2, queue.Current!.Id);
        queue.Append([T(3)]);
        Assert.Equal(3, queue.Count);
    }

    [Fact]
    public void Settings_round_trip_through_the_store()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        Assert.True(settings.SpoofDomesticIp);
        Assert.Equal(Cloudie.Core.Netease.AudioQuality.ExHigh, settings.AudioQuality);
        settings.AudioQuality = Cloudie.Core.Netease.AudioQuality.Lossless;
        settings.SetSourceEnabled("pyncmd", false);
        settings.SubsonicUrl = " https://music.example.com ";
        settings.SubsonicUser = "alice";
        settings.SubsonicPassword = "pw";
        settings.Volume = 1.7;
        Assert.Equal(Cloudie.Core.Netease.AudioQuality.Lossless, settings.AudioQuality);
        Assert.False(settings.IsSourceEnabled("pyncmd"));
        Assert.True(settings.IsSourceEnabled("navidrome"));
        Assert.NotNull(settings.SubsonicConfiguration);
        Assert.Equal(1, settings.Volume);
    }
}
