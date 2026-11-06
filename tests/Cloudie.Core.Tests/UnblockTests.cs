using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Cloudie.Core.Netease;
using Cloudie.Core.Settings;
using Cloudie.Core.Unblock;

namespace Cloudie.Core.Tests;

public class UnblockTests
{
    private static readonly UnblockTrack Qinghuaci = new(185811, "青花瓷", ["周杰伦"], "我很忙", 238_000);

    private static SubsonicServer Server(string host, string name = "") => new(host, name, $"https://{host}", "alice", "pw");

    private const string NoSongs = """{"subsonic-response":{"status":"ok","searchResult3":{}}}""";
    private const string QinghuaciFlac = """
        {"subsonic-response":{"status":"ok","searchResult3":{"song":[
          {"id":"abc","title":"青花瓷","artist":"周杰伦","album":"我很忙","duration":238,"suffix":"flac"}]}}}
        """;

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
        var handler = StubHandler.Json(QinghuaciFlac);
        var source = new SubsonicAudioSource(Server("music.example.com", "家里"), new HttpClient(handler));
        var resolved = await source.ResolveAsync(Qinghuaci, AudioQuality.ExHigh);
        Assert.NotNull(resolved);
        Assert.Equal("navidrome", resolved.SourceId);
        Assert.Equal("Navidrome · 家里", resolved.SourceName);
        Assert.Equal("music.example.com", resolved.Url.Host);
        Assert.Equal("/rest/stream.view", resolved.Url.AbsolutePath);
        Assert.Contains("id=abc", resolved.Url.Query);
        Assert.Contains("format=raw", resolved.Url.Query);
        Assert.Equal("/rest/search3.view", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Subsonic_source_tries_the_bare_title_and_gives_up_cleanly()
    {
        var handler = StubHandler.Json(NoSongs);
        var source = new SubsonicAudioSource(Server("music.example.com"), new HttpClient(handler));
        Assert.Null(await source.ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
        Assert.Equal(2, handler.Requests.Count);
        // An entry without a user name (half-filled, or migrated from an older settings file) is skipped without a request.
        var incomplete = new SubsonicAudioSource(new SubsonicServer("x", "", "https://music.example.com", "", ""), new HttpClient(handler));
        Assert.Null(await incomplete.ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Subsonic_servers_are_asked_in_order_and_the_answer_names_the_one_that_had_it()
    {
        var handler = new StubHandler(request => Task.FromResult(request.RequestUri!.Host switch
        {
            "a.example.com" => JsonReply(NoSongs),
            "b.example.com" => JsonReply(QinghuaciFlac),
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        }));
        var http = new HttpClient(handler);
        var servers = new List<SubsonicServer> { Server("a.example.com"), Server("b.example.com", "朋友的"), Server("c.example.com") };
        var service = new UnblockService(() => servers.Select(s => new SubsonicAudioSource(s, http)).ToList<IAudioSource>(), () => new HashSet<string> { "navidrome" });
        var failures = new List<string>();
        service.SourceFailed += (source, _) => failures.Add(source.DisplayName);

        var resolved = await service.ResolveAsync(Qinghuaci, AudioQuality.ExHigh);

        Assert.Equal("Navidrome · 朋友的", resolved!.SourceName);
        Assert.Equal("b.example.com", resolved.Url.Host);
        Assert.Empty(failures);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host == "c.example.com");

        // The list is read again for every lookup: a reordered list puts the dead server first, which is reported and skipped.
        servers.Reverse();
        resolved = await service.ResolveAsync(Qinghuaci, AudioQuality.ExHigh);
        Assert.Equal("b.example.com", resolved!.Url.Host);
        Assert.Equal(["Navidrome · c.example.com"], failures);
    }

    [Fact]
    public async Task Subsonic_probe_searches_and_reports_what_the_server_said()
    {
        var handler = StubHandler.Json(NoSongs);
        var ok = new SubsonicAudioSource(Server("music.example.com"), new HttpClient(handler));
        Assert.Null(await ok.ProbeAsync());
        // A search, not ping.view: a reverse proxy that only passes what a player needs blocks ping while lookups work.
        var request = handler.Requests.Single().RequestUri!;
        Assert.Equal("/rest/search3.view", request.AbsolutePath);
        Assert.Contains("songCount=1", request.Query);
        var refused = new SubsonicAudioSource(Server("music.example.com"), new HttpClient(StubHandler.Json(
            """{"subsonic-response":{"status":"failed","error":{"code":40,"message":"Wrong username or password"}}}""")));
        Assert.Equal("Wrong username or password", await refused.ProbeAsync());
        var down = new SubsonicAudioSource(Server("music.example.com"), new HttpClient(StubHandler.Json("", HttpStatusCode.BadGateway)));
        await Assert.ThrowsAsync<HttpRequestException>(() => down.ProbeAsync());
        var incomplete = new SubsonicAudioSource(new SubsonicServer("x", "", "https://music.example.com", "", ""), new HttpClient(StubHandler.Json("")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => incomplete.ProbeAsync());
    }

    [Fact]
    public void Subsonic_server_list_round_trips_and_absorbs_the_single_server_keys()
    {
        var store = new InMemorySettingsStore();
        store.SetString(AppSettings.Keys.SubsonicUrl, " https://music.example.com ");
        store.SetString(AppSettings.Keys.SubsonicUser, "alice");
        store.SetString(AppSettings.Keys.SubsonicPassword, "pw");
        var settings = new AppSettings(store);

        var legacy = Assert.Single(settings.SubsonicServers);
        Assert.Equal(SubsonicServer.LegacyId, legacy.Id);
        Assert.Equal("music.example.com", legacy.DisplayName);
        Assert.True(legacy.Enabled);
        Assert.NotNull(legacy.Configuration);

        settings.SaveSubsonicServer(new SubsonicServer("b", "朋友的", "https://b.example.com", "bob", "pw2"));
        Assert.Null(store.GetString(AppSettings.Keys.SubsonicUrl));
        Assert.Null(store.GetString(AppSettings.Keys.SubsonicPassword));
        Assert.Equal([SubsonicServer.LegacyId, "b"], settings.SubsonicServers.Select(s => s.Id));

        settings.SaveSubsonicServer(legacy with { Name = "家里", Enabled = false });
        Assert.Equal(["家里", "朋友的"], settings.SubsonicServers.Select(s => s.DisplayName));
        Assert.False(settings.SubsonicServers[0].Enabled);

        settings.MoveSubsonicServer("b", -1);
        Assert.Equal(["b", SubsonicServer.LegacyId], settings.SubsonicServers.Select(s => s.Id));
        settings.MoveSubsonicServer("b", -1);   // already first: nothing happens
        settings.MoveSubsonicServer("missing", 1);
        Assert.Equal("b", settings.SubsonicServers[0].Id);
        settings.RemoveSubsonicServer(SubsonicServer.LegacyId);
        Assert.Equal("pw2", Assert.Single(settings.SubsonicServers).Password);

        // A file written by hand or by another version: missing fields fall back, entries without an id or address and bad JSON are dropped.
        var parsed = Assert.Single(SubsonicServer.ParseList("""[{"id":"x","url":"https://x.example.com","user":"u","password":"p"},{"Name":"no id"}]"""));
        Assert.True(parsed.Enabled);
        Assert.Equal("x.example.com", parsed.DisplayName);
        Assert.Empty(SubsonicServer.ParseList("not json"));
        Assert.Empty(new AppSettings(new InMemorySettingsStore()).SubsonicServers);
    }

    private static HttpResponseMessage JsonReply(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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
        var service = new UnblockService(() => [failing, hit, never], () => new HashSet<string> { "a", "b", "c" });
        var failures = new List<string>();
        service.SourceFailed += (s, e) => failures.Add($"{s.Id}:{e.Message}");

        var resolved = await service.ResolveAsync(Qinghuaci, AudioQuality.ExHigh);

        Assert.Equal("b", resolved!.SourceId);
        Assert.Equal(["a:down"], failures);
        Assert.Null(await new UnblockService(() => [hit], () => new HashSet<string>()).ResolveAsync(Qinghuaci, AudioQuality.ExHigh));
    }

    private sealed class FakeSource(string id, Func<UnblockTrack, ResolvedAudio?> resolve) : IAudioSource
    {
        public string Id => id;
        public string DisplayName => id.ToUpperInvariant();
        public Task<ResolvedAudio?> ResolveAsync(UnblockTrack track, AudioQuality quality, CancellationToken cancellationToken = default) =>
            Task.FromResult(resolve(track));
    }
}
