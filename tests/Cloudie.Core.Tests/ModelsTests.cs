using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Tests;

public class ModelsTests
{
    [Fact]
    public void Track_decodes_the_v3_shape()
    {
        const string json = """
            {"id":185811,"name":"青花瓷","ar":[{"id":6452,"name":"周杰伦"}],"al":{"id":18886,"name":"我很忙","picUrl":"https://p1.music.126.net/x.jpg"},
             "dt":238000,"alia":["Blue and White Porcelain"],"fee":1,"mv":0,"no":4,"noCopyrightRcmd":{"type":1},"privilege":{"id":185811,"st":-200,"pl":0,"fee":0}}
            """;
        var track = NeteaseClient.Decode<Track>(json);
        Assert.Equal(185811, track.Id);
        Assert.Equal("周杰伦", track.ArtistNames);
        Assert.Equal("我很忙", track.Album.Name);
        Assert.Equal(238000, track.DurationMs);
        Assert.Equal("Blue and White Porcelain", track.Subtitle);
        Assert.True(track.NoCopyright);
        Assert.False(track.IsCloud);
        Assert.True(track.EmbeddedPrivilege!.IsGray);
        Assert.Equal(TrackPlayability.NoCopyright, track.GetPlayability(track.EmbeddedPrivilege, isLoggedIn: true, vipType: 0));
    }

    [Fact]
    public void Track_decodes_the_legacy_shape_and_playability_rules()
    {
        const string json = """{"id":1,"name":"Song","artists":[{"id":2,"name":"Artist"}],"album":{"id":3,"name":"Album"},"duration":1000,"fee":1}""";
        var track = NeteaseClient.Decode<Track>(json);
        Assert.Equal("Artist", track.ArtistNames);
        Assert.Equal(1000, track.DurationMs);
        Assert.Equal(TrackPlayability.VipOnly, track.GetPlayability(null, isLoggedIn: true, vipType: 0));
        Assert.Equal(TrackPlayability.Playable, track.GetPlayability(null, isLoggedIn: true, vipType: 11));
        Assert.Equal(TrackPlayability.Playable, track.GetPlayability(new TrackPrivilege { Pl = 320000 }, false, 0));
        Assert.Equal(TrackPlayability.Delisted, track.GetPlayability(new TrackPrivilege { Fee = 0, St = -200 }, true, 0));
    }

    [Fact]
    public void Playlist_summary_reads_either_cover_field()
    {
        var a = NeteaseClient.Decode<PlaylistSummary>("""{"id":1,"name":"A","picUrl":"p","playcount":5,"specialType":5}""");
        var b = NeteaseClient.Decode<PlaylistSummary>("""{"id":2,"name":"B","coverImgUrl":"c","playCount":7}""");
        Assert.Equal("p", a.CoverUrl);
        Assert.Equal(5, a.Plays);
        Assert.True(a.IsFavorites);
        Assert.Equal("c", b.CoverUrl);
        Assert.Equal(7, b.Plays);
    }

    [Fact]
    public void Song_url_and_qr_states_decode()
    {
        var url = NeteaseClient.Decode<SongUrlResponse>("""{"code":200,"data":[{"id":5,"url":null,"br":0,"size":0,"code":-110,"freeTrialInfo":{"start":30,"end":60}}]}""").Data.Single();
        Assert.Null(url.Url);
        Assert.True(url.IsTrial);
        var qr = System.Text.Json.JsonSerializer.Deserialize<QrCheckResponse>("""{"code":802,"message":"待确认"}""", NeteaseClient.ResponseOptions)!;
        Assert.Equal(QrLoginState.Scanned, qr.State);
    }

    [Fact]
    public void Audio_quality_levels_map_both_ways()
    {
        Assert.Equal("lossless", AudioQuality.Lossless.ToLevel());
        Assert.Equal(AudioQuality.HiRes, AudioQualityExtensions.FromLevel("hires"));
        Assert.True(AudioQuality.HiRes.IsLossless());
        Assert.False(AudioQuality.ExHigh.IsLossless());
    }
}
