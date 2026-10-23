using System.Net;
using System.Web;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Crypto;

namespace Cloudie.Core.Tests;

public class NeteaseClientTests
{
    [Fact]
    public async Task Eapi_request_carries_the_desktop_headers_cookies_and_domestic_ip()
    {
        var handler = StubHandler.Json("""{"code":200,"data":[]}""");
        using var client = new NeteaseClient(handler: handler);
        client.SetCookies(new Dictionary<string, string> { ["MUSIC_U"] = "token123", ["__csrf"] = "csrf456" });

        await client.EapiAsync("/song/enhance/player/url/v1", new Dictionary<string, object?> { ["ids"] = "[1]", ["level"] = "exhigh" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://interface.music.163.com/eapi/song/enhance/player/url/v1", request.RequestUri!.ToString());
        Assert.Equal(NeteaseClient.DomesticIp, request.Headers.GetValues("X-Real-IP").Single());
        var cookie = request.Headers.GetValues("Cookie").Single();
        Assert.Contains("MUSIC_U=token123", cookie);
        Assert.Contains("os=pc", cookie);
        Assert.Contains("appver=3.1.17", cookie);

        var form = HttpUtility.ParseQueryString(handler.Bodies.Single());
        var plain = NeteaseCrypto.EapiDecrypt(form["params"]!);
        Assert.StartsWith("/api/song/enhance/player/url/v1-36cd479b6b5-", plain);
        Assert.Contains("\"ids\":\"[1]\"", plain);
        Assert.Contains("\"os\":\"pc\"", plain);
        Assert.Contains("\"MUSIC_U\":\"token123\"", plain);
    }

    [Fact]
    public async Task Overseas_header_is_omitted_when_the_spoof_is_off()
    {
        var handler = StubHandler.Json("""{"code":200}""");
        using var client = new NeteaseClient(handler: handler) { SpoofDomesticIp = false };
        await client.WeapiAsync("/w/nuser/account/get");
        Assert.False(handler.Requests.Single().Headers.Contains("X-Real-IP"));
        Assert.Equal("https://music.163.com/weapi/w/nuser/account/get", handler.Requests.Single().RequestUri!.ToString());
        var form = HttpUtility.ParseQueryString(handler.Bodies.Single());
        Assert.NotNull(form["params"]);
        Assert.Equal(256, form["encSecKey"]!.Length);
    }

    [Fact]
    public async Task Set_cookie_headers_are_absorbed_and_persisted()
    {
        var store = new InMemoryCookieStore();
        var handler = StubHandler.Json("""{"code":803}""", HttpStatusCode.OK,
            ("Set-Cookie", "MUSIC_U=newtoken; Path=/; HttpOnly"),
            ("Set-Cookie", "__csrf=abc; Path=/"),
            ("Set-Cookie", "empty=; Path=/"));
        using var client = new NeteaseClient(store, handler);

        await client.WeapiAsync("/login/qrcode/client/login");

        Assert.True(client.IsLoggedIn);
        Assert.Equal("abc", client.Cookie("__csrf"));
        Assert.Null(client.Cookie("empty"));
        Assert.Equal("newtoken", store.Load()["MUSIC_U"]);
    }

    [Fact]
    public void Decode_surfaces_business_errors_and_login_requirements()
    {
        var error = Assert.Throws<NeteaseApiException>(() => NeteaseClient.Decode<object>("""{"code":400,"message":"bad"}"""));
        Assert.Equal(400, error.Code);
        Assert.Equal("bad", error.Message);
        var login = Assert.Throws<NeteaseApiException>(() => NeteaseClient.Decode<object>("""{"code":301}"""));
        Assert.True(login.NeedsLogin);
    }

    [Fact]
    public async Task Http_failures_become_api_exceptions()
    {
        var handler = StubHandler.Json("nope", HttpStatusCode.BadGateway);
        using var client = new NeteaseClient(handler: handler);
        var error = await Assert.ThrowsAsync<NeteaseApiException>(() => client.WeapiAsync("/x"));
        Assert.Equal(502, error.Code);
    }
}
