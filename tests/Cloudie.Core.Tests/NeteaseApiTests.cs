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
