using System.Text.Json;
using Cloudie.Core.Library;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Tests;

public sealed class HomeSnapshotTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "cloudie-home-tests", Guid.NewGuid().ToString("N"), "home.json");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("2026-10-25T21:59:59+00:00", "2026-10-25")]   // 05:59:59 in Beijing: yesterday's picks still
    [InlineData("2026-10-25T22:00:00+00:00", "2026-10-26")]   // 06:00 in Beijing: today's
    [InlineData("2026-10-25T18:30:00-04:00", "2026-10-26")]   // an evening in New York is after 06:00 Beijing the next day
    [InlineData("2026-10-26T05:30:00+08:00", "2026-10-25")]
    [InlineData("2026-10-26T12:00:00+08:00", "2026-10-26")]
    public void The_recommendation_day_turns_over_at_six_in_Beijing(string now, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), HomeSnapshot.RecommendationDay(DateTimeOffset.Parse(now)));
    }

    [Fact]
    public void A_snapshot_is_current_for_its_account_on_its_day_only()
    {
        var snapshot = new HomeSnapshot(42, new DateOnly(2026, 10, 26), [], [], [], DateTimeOffset.UtcNow);
        var morning = DateTimeOffset.Parse("2026-10-26T09:00:00+08:00");

        Assert.True(snapshot.IsCurrent(42, morning));
        Assert.False(snapshot.IsCurrent(0, morning));
        Assert.False(snapshot.IsCurrent(42, DateTimeOffset.Parse("2026-10-27T06:00:00+08:00")));
        Assert.False(snapshot.IsCurrent(42, DateTimeOffset.Parse("2026-10-26T05:00:00+08:00")));
    }

    [Fact]
    public void The_store_round_trips_a_snapshot_and_treats_a_corrupt_file_as_absent()
    {
        var store = new HomeSnapshotStore(_path);
        Assert.Null(store.Load());
        var track = JsonSerializer.Deserialize<Track>("""{"id":7,"name":"歌","ar":[{"id":1,"name":"周华健"}],"al":{"id":2,"name":"专辑","picUrl":"https://p/7.jpg"},"dt":1000}""", NeteaseClient.ResponseOptions)!;
        var snapshot = new HomeSnapshot(42, new DateOnly(2026, 10, 26),
            [new PlaylistSummary { Id = 1, Name = "每日推荐歌单", CoverImgUrl = "https://p/1.jpg", PlayCount = 5 }],
            [new ToplistItem { Id = 3, Name = "飙升榜", CoverImgUrl = "https://p/3.jpg", UpdateFrequency = "每天更新" }],
            [track], new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.Zero));

        store.Save(snapshot);
        var loaded = new HomeSnapshotStore(_path).Load();

        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.UserId);
        Assert.Equal(new DateOnly(2026, 10, 26), loaded.Day);
        Assert.Equal("每日推荐歌单", loaded.RecommendedPlaylists[0].Name);
        Assert.Equal("每天更新", loaded.Toplists[0].UpdateFrequency);
        Assert.Equal("周华健", loaded.DailySongs[0].ArtistNames);
        Assert.Equal(snapshot.SavedAt, loaded.SavedAt);

        File.WriteAllText(_path, "{broken");
        Assert.Null(store.Load());

        store.Clear();
        Assert.False(File.Exists(_path));
    }
}
