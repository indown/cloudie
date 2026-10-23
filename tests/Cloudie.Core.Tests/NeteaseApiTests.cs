using System.Text;
using System.Text.Json;
using System.Web;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Crypto;

namespace Cloudie.Core.Tests;

public class NeteaseApiTests
{
    /// <summary>Undoes both weapi AES rounds of a captured form body and returns the JSON payload.</summary>
    private static JsonDocument DecryptWeapi(string body)
    {
        var form = HttpUtility.ParseQueryString(body);
        var inner = NeteaseCrypto.WeapiDecryptRound(Convert.FromBase64String(form["params"]!), NeteaseCrypto.DefaultWeapiSecretKey);
        var plain = NeteaseCrypto.WeapiDecryptRound(Convert.FromBase64String(Encoding.UTF8.GetString(inner)), "0CoJUm6Qyw8W8jud");
        return JsonDocument.Parse(Encoding.UTF8.GetString(plain));
    }

    [Fact]
    public async Task Scrobble_posts_a_play_log_like_the_web_client()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler);
        var api = new NeteaseApi(client);

        await api.ScrobbleAsync(186006, seconds: 215, sourceId: 2829816518);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://music.163.com/weapi/feedback/weblog", request.RequestUri!.ToString());
        using var payload = DecryptWeapi(handler.Bodies.Single());
        using var logs = JsonDocument.Parse(payload.RootElement.GetProperty("logs").GetString()!);
        var entry = Assert.Single(logs.RootElement.EnumerateArray());
        Assert.Equal("play", entry.GetProperty("action").GetString());
        var json = entry.GetProperty("json");
        Assert.Equal(186006, json.GetProperty("id").GetInt64());
        Assert.Equal(215, json.GetProperty("time").GetInt32());
        Assert.Equal("2829816518", json.GetProperty("sourceId").GetString());
        Assert.Equal("playend", json.GetProperty("end").GetString());
        Assert.Equal("song", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Scrobble_omits_the_source_when_unknown_and_never_sends_negative_time()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler);

        await new NeteaseApi(client).ScrobbleAsync(1, seconds: -5);

        using var payload = DecryptWeapi(handler.Bodies.Single());
        using var logs = JsonDocument.Parse(payload.RootElement.GetProperty("logs").GetString()!);
        var json = logs.RootElement[0].GetProperty("json");
        Assert.False(json.TryGetProperty("sourceId", out _));
        Assert.Equal(0, json.GetProperty("time").GetInt32());
    }

    [Fact]
    public async Task Search_suggestions_keep_order_and_drop_blanks_and_repeats()
    {
        var handler = StubHandler.Json("""
            {"code":200,"result":{"allMatch":[
              {"keyword":"周杰伦","type":1},{"keyword":"","type":1},{"keyword":"周杰伦 晴天","type":1},{"keyword":"周杰伦","type":1}]}}
            """);
        using var client = new NeteaseClient(handler: handler);

        var suggestions = await new NeteaseApi(client).GetSearchSuggestionsAsync(" 周杰 ");

        Assert.Equal(new[] { "周杰伦", "周杰伦 晴天" }, suggestions);
        Assert.Equal("https://music.163.com/weapi/search/suggest/keyword", handler.Requests.Single().RequestUri!.ToString());
        using var payload = DecryptWeapi(handler.Bodies.Single());
        Assert.Equal("周杰", payload.RootElement.GetProperty("s").GetString());
    }

    [Fact]
    public async Task Blank_search_input_yields_no_suggestions_and_no_request()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler);
        Assert.Empty(await new NeteaseApi(client).GetSearchSuggestionsAsync("  "));
        Assert.Empty(handler.Requests);
    }
}

public class NeteaseApiPlaylistAndFmTests
{
    private static System.Text.Json.JsonDocument DecryptWeapi(string body)
    {
        var form = HttpUtility.ParseQueryString(body);
        var inner = NeteaseCrypto.WeapiDecryptRound(Convert.FromBase64String(form["params"]!), NeteaseCrypto.DefaultWeapiSecretKey);
        var plain = NeteaseCrypto.WeapiDecryptRound(Convert.FromBase64String(Encoding.UTF8.GetString(inner)), "0CoJUm6Qyw8W8jud");
        return System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(plain));
    }

    [Fact]
    public async Task Personal_fm_returns_legacy_shaped_tracks()
    {
        var handler = StubHandler.Json("""
            {"code":200,"data":[{"id":5,"name":"fm song","artists":[{"id":1,"name":"a"}],"album":{"id":2,"name":"b","picUrl":"https://p/x.jpg"},"duration":123000}]}
            """);
        using var client = new NeteaseClient(handler: handler);

        var tracks = await new NeteaseApi(client).GetPersonalFmAsync();

        var track = Assert.Single(tracks);
        Assert.Equal("fm song", track.Name);
        Assert.Equal("a", track.ArtistNames);
        Assert.Equal(TimeSpan.FromSeconds(123), track.Duration);
        Assert.Equal("https://music.163.com/weapi/v1/radio/get", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task Trash_carries_the_song_in_the_query_and_the_body()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler);
        await new NeteaseApi(client).TrashFmTrackAsync(5);
        Assert.Equal("https://music.163.com/weapi/radio/trash/add?alg=RT&songId=5&time=25", handler.Requests.Single().RequestUri!.ToString());
        using var payload = DecryptWeapi(handler.Bodies.Single());
        Assert.Equal(5, payload.RootElement.GetProperty("songId").GetInt64());
    }

    [Fact]
    public async Task Subscribe_and_unsubscribe_hit_their_own_endpoints()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler);
        var api = new NeteaseApi(client);
        await api.SubscribePlaylistAsync(42, subscribe: true);
        await api.SubscribePlaylistAsync(42, subscribe: false);
        Assert.Equal(
            ["https://music.163.com/weapi/playlist/subscribe", "https://music.163.com/weapi/playlist/unsubscribe"],
            handler.Requests.Select(r => r.RequestUri!.ToString()));
    }

    [Fact]
    public async Task Manipulate_tracks_sends_the_operation_and_the_ids_as_a_json_string()
    {
        var handler = StubHandler.Json("""{"code":200,"count":2}""");
        using var client = new NeteaseClient(handler: handler);
        await new NeteaseApi(client).ManipulatePlaylistTracksAsync(42, [1, 2], add: true);
        using var payload = DecryptWeapi(handler.Bodies.Single());
        Assert.Equal("add", payload.RootElement.GetProperty("op").GetString());
        Assert.Equal(42, payload.RootElement.GetProperty("pid").GetInt64());
        Assert.Equal("[1,2]", payload.RootElement.GetProperty("trackIds").GetString());
    }

    [Fact]
    public async Task Duplicate_adds_surface_netease_s_502()
    {
        var handler = StubHandler.Json("""{"code":502,"message":"歌曲已存在"}""");
        using var client = new NeteaseClient(handler: handler);
        var error = await Assert.ThrowsAsync<NeteaseApiException>(() => new NeteaseApi(client).ManipulatePlaylistTracksAsync(42, [1], add: true));
        Assert.Equal(502, error.Code);
    }

    [Fact]
    public async Task Create_playlist_returns_the_new_playlist()
    {
        var handler = StubHandler.Json("""{"code":200,"id":99,"playlist":{"id":99,"name":"new list","trackCount":0}}""");
        using var client = new NeteaseClient(handler: handler);
        var playlist = await new NeteaseApi(client).CreatePlaylistAsync("new list");
        Assert.Equal(99, playlist!.Id);
        using var payload = DecryptWeapi(handler.Bodies.Single());
        Assert.Equal("new list", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal(0, payload.RootElement.GetProperty("privacy").GetInt32());
    }

    [Fact]
    public async Task Heart_mode_sends_the_seed_and_the_liked_list_and_unwraps_song_info()
    {
        var handler = StubHandler.Json("""
            {"code":200,"data":[
              {"id":7,"songInfo":{"id":7,"name":"goes with it","ar":[{"id":1,"name":"x"}],"al":{"id":2,"name":"y"},"dt":200000},"recommended":true},
              {"id":8,"songInfo":null}]}
            """);
        using var client = new NeteaseClient(handler: handler);

        var tracks = await new NeteaseApi(client).GetHeartModeListAsync(186006, 2829816518);

        var track = Assert.Single(tracks);
        Assert.Equal(7, track.Id);
        Assert.Equal("https://music.163.com/weapi/playmode/intelligence/list", handler.Requests.Single().RequestUri!.ToString());
        using var payload = DecryptWeapi(handler.Bodies.Single());
        Assert.Equal(186006, payload.RootElement.GetProperty("songId").GetInt64());
        Assert.Equal("fromPlayOne", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(2829816518, payload.RootElement.GetProperty("playlistId").GetInt64());
        Assert.Equal(186006, payload.RootElement.GetProperty("startMusicId").GetInt64());
        Assert.Equal(1, payload.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Rename_and_delete_use_the_update_name_and_remove_endpoints()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler);
        var api = new NeteaseApi(client);

        await api.RenamePlaylistAsync(42, "周末");
        await api.DeletePlaylistAsync(42);

        Assert.Equal(
            ["https://music.163.com/weapi/playlist/update/name", "https://music.163.com/weapi/playlist/remove"],
            handler.Requests.Select(r => r.RequestUri!.ToString()));
        using var rename = DecryptWeapi(handler.Bodies[0]);
        Assert.Equal(42, rename.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("周末", rename.RootElement.GetProperty("name").GetString());
        using var delete = DecryptWeapi(handler.Bodies[1]);
        Assert.Equal("[42]", delete.RootElement.GetProperty("ids").GetString());
    }
}
