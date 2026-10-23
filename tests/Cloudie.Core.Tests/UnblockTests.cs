using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Cloudie.Core.Netease;
using Cloudie.Core.Unblock;

namespace Cloudie.Core.Tests;

public class UnblockTests
{
    private static readonly UnblockTrack Qinghuaci = new(185811, "青花瓷", ["周杰伦"], "我很忙", 238_000);

    [Fact]
    public void Matcher_requires_the_same_recording()
    {
        var candidates = new[]
        {
            new MatchCandidate("1", "青花瓷 (Live)", "周杰伦", 300),
            new MatchCandidate("2", "青花瓷", "某翻唱歌手", 200),
            new MatchCandidate("3", "青花瓷", "周杰伦 / 某某", 238, "mp3"),
            new MatchCandidate("4", "青花瓷", "周杰伦", 238, "flac")
        };
        Assert.Equal("4", TrackMatcher.Pick(candidates, Qinghuaci)!.Id);
        Assert.Equal("5", TrackMatcher.Pick([new MatchCandidate("5", "青花瓷", null, 239)], Qinghuaci)!.Id);
        Assert.Null(TrackMatcher.Pick([new MatchCandidate("6", "青花瓷", "别人", null)], Qinghuaci));
        Assert.Null(TrackMatcher.Pick([new MatchCandidate("7", "青花瓷", null, null)], Qinghuaci));
        Assert.Equal("8", TrackMatcher.Pick([new MatchCandidate("8", "青花瓷", "周杰伦", null)], Qinghuaci)!.Id);
    }

    [Fact]
    public void Matcher_prefers_known_playable_formats()
    {
        var track = new UnblockTrack(1, "Blue Moon", ["Example Artist"], null, 180_000);
        var lossy = new MatchCandidate("m4a", "Blue Moon", "Example Artist", 180, "m4a", 256);
        var alac = new MatchCandidate("alac", "Blue Moon", "Example Artist", 180, "m4a", 1100);
        var mp3 = new MatchCandidate("mp3", "Blue  Moon", "example artist", 180, "mp3", 320);
        Assert.Equal("mp3", TrackMatcher.Pick([alac, mp3], track)!.Id);
        Assert.Equal("m4a", TrackMatcher.Pick([lossy, alac], track)!.Id);
    }

    [Fact]
    public void Subsonic_endpoint_uses_token_authentication()
    {
        var config = SubsonicConfiguration.TryCreate(" https://music.example.com// ", "alice", "s3cret")!;
        var url = config.Endpoint("stream.view", new Dictionary<string, string> { ["id"] = "42", ["format"] = "raw" });
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("/rest/stream.view", url.AbsolutePath);
        Assert.Equal("alice", query["u"]);
        Assert.Equal("42", query["id"]);
        Assert.Equal("raw", query["format"]);
        var expected = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("s3cret" + query["s"])));
        Assert.Equal(expected, query["t"]);
        Assert.DoesNotContain("s3cret", url.ToString());
        Assert.Null(SubsonicConfiguration.TryCreate("music.example.com", "a", "b"));
        Assert.Null(SubsonicConfiguration.TryCreate("https://music.example.com", "", "b"));
    }

    [Fact]
    public async Task Subsonic_source_resolves_through_search3()
    {
        var handler = StubHandler.Json("""
            {"subsonic-response":{"status":"ok","searchResult3":{"song":[
              {"id":"abc","title":"青花瓷","artist":"周杰伦","album":"我很忙","duration":238,"suffix":"flac"}]}}}
            """);
        var source = new SubsonicAudioSource(() => SubsonicConfiguration.TryCreate("https://music.example.com", "alice", "pw"), new HttpClient(handler));
        var resolved = await source.ResolveAsync(Qinghuaci, AudioQuality.ExHigh);
        Assert.NotNull(resolved);
        Assert.Equal("navidrome", resolved.SourceId);
        Assert.Equal("music.example.com", resolved.Url.Host);
        Assert.Equal("/rest/stream.view", resolved.Url.AbsolutePath);
        Assert.Contains("id=abc", resolved.Url.Query);
        Assert.Contains("format=raw", resolved.Url.Query);
        Assert.Equal("/rest/search3.view", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Subsonic_source_tries_the_bare_title_and_gives_up_cleanly()
    {
        var handler = StubHandler.Json("""{"subsonic-response":{"status":"ok","searchResult3":{}}}""");
        var source = new SubsonicAudioSource(() => SubsonicConfiguration.TryCreate("https://music.example.com", "alice", "pw"), new HttpClient(handler));
        Assert.Null(await source.ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Null(await new SubsonicAudioSource(() => null).ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
    }

    [Fact]
    public async Task Pyncmd_source_upgrades_to_https_and_asks_for_lossless_when_wanted()
    {
        var handler = StubHandler.Json("""{"url":"http://m701.music.126.net/x.flac","br":999,"size":1}""");
        var source = new PyncmdAudioSource(new HttpClient(handler));
        var resolved = await source.ResolveAsync(Qinghuaci, AudioQuality.Lossless);
        Assert.Equal("https://m701.music.126.net/x.flac", resolved!.Url.ToString());
        Assert.Equal("flac", resolved.Format);
        Assert.Contains("br=999", handler.Requests.Single().RequestUri!.Query);
        var empty = new PyncmdAudioSource(new HttpClient(StubHandler.Json("""{"url":"","br":0}""")));
        Assert.Null(await empty.ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
    }

    [Fact]
    public async Task Service_tries_enabled_sources_in_order_and_reports_failures()
    {
        var failing = new FakeSource("a", _ => throw new HttpRequestException("down"));
        var hit = new FakeSource("b", t => new ResolvedAudio("b", "B", new Uri("https://b/" + t.Id)));
        var never = new FakeSource("c", _ => throw new InvalidOperationException("should not run"));
        var service = new UnblockService([failing, hit, never], () => new HashSet<string> { "a", "b", "c" });
        var failures = new List<string>();
        service.SourceFailed += (s, e) => failures.Add($"{s.Id}:{e.Message}");

        var resolved = await service.ResolveAsync(Qinghuaci, AudioQuality.ExHigh);

        Assert.Equal("b", resolved!.SourceId);
        Assert.Equal(["a:down"], failures);
        Assert.Null(await new UnblockService([hit], () => new HashSet<string>()).ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
    }

    private sealed class FakeSource(string id, Func<UnblockTrack, ResolvedAudio?> resolve) : IAudioSource
    {
        public string Id => id;
        public string DisplayName => id.ToUpperInvariant();
        public Task<ResolvedAudio?> ResolveAsync(UnblockTrack track, AudioQuality quality, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolve(track));
    }
}
