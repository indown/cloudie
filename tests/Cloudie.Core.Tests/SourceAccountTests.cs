using Cloudie.Core.Netease;

namespace Cloudie.Core.Tests;

public class SourceAccountTests
{
    private const string VipProfile = """{"code":200,"profile":{"userId":42,"nickname":"副号","vipType":11}}""";

    private static NeteaseClient SignedIn(StubHandler handler)
    {
        var client = new NeteaseClient(handler: handler);
        client.SetCookies(new Dictionary<string, string> { ["MUSIC_U"] = "vip", ["__csrf"] = "c" });
        return client;
    }

    [Fact]
    public async Task Reload_reads_the_profile_and_makes_the_account_usable()
    {
        using var client = SignedIn(StubHandler.Json(VipProfile));
        var account = new SourceAccount(new NeteaseApi(client), () => true);
        var changes = 0;
        account.Changed += () => changes++;

        await account.ReloadAsync();

        Assert.Equal("副号", account.Profile?.Nickname);
        Assert.Equal(11, account.VipType);
        Assert.True(account.IsUsable);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Restore_refreshes_the_session_before_reading_the_profile()
    {
        var handler = StubHandler.Json(VipProfile);
        using var client = SignedIn(handler);
        var account = new SourceAccount(new NeteaseApi(client), () => true);

        await account.RestoreAsync();

        Assert.Equal(
            ["https://music.163.com/weapi/login/token/refresh", "https://music.163.com/weapi/w/nuser/account/get"],
            handler.Requests.Select(static r => r.RequestUri!.ToString()).ToList());
        Assert.True(account.IsUsable);
    }

    [Fact]
    public async Task Restore_does_nothing_without_a_stored_session()
    {
        var handler = StubHandler.Json(VipProfile);
        using var client = new NeteaseClient(handler: handler);
        var account = new SourceAccount(new NeteaseApi(client), () => true);

        await account.RestoreAsync();

        Assert.Empty(handler.Requests);
        Assert.False(account.IsLoggedIn);
        Assert.False(account.IsUsable);
        Assert.False(account.IsExpired);
    }

    [Fact]
    public async Task An_anonymous_reply_for_a_stored_cookie_means_the_session_expired()
    {
        using var client = SignedIn(StubHandler.Json("""{"code":200,"account":null,"profile":null}"""));
        var account = new SourceAccount(new NeteaseApi(client), () => true);

        await account.ReloadAsync();

        Assert.True(account.IsExpired);
        Assert.False(account.IsUsable);
    }

    [Fact]
    public async Task A_301_on_reload_marks_the_session_expired()
    {
        using var client = SignedIn(StubHandler.Json("""{"code":301}"""));
        var account = new SourceAccount(new NeteaseApi(client), () => true);

        await account.ReloadAsync();

        Assert.True(account.IsExpired);
        Assert.Null(account.Profile);
    }

    [Fact]
    public async Task Signing_in_again_clears_the_expiry()
    {
        using var client = SignedIn(StubHandler.Json(VipProfile));
        var account = new SourceAccount(new NeteaseApi(client), () => true);
        account.MarkExpired();
        Assert.False(account.IsUsable);

        await account.ReloadAsync();

        Assert.False(account.IsExpired);
        Assert.True(account.IsUsable);
    }

    [Fact]
    public async Task The_settings_switch_stops_use_without_signing_out()
    {
        var enabled = true;
        using var client = SignedIn(StubHandler.Json(VipProfile));
        var account = new SourceAccount(new NeteaseApi(client), () => enabled);
        await account.ReloadAsync();

        enabled = false;

        Assert.False(account.IsUsable);
        Assert.True(account.IsLoggedIn);
        Assert.Equal(11, account.VipType);
    }

    [Fact]
    public async Task Logout_clears_the_cookies_and_the_expiry_even_when_the_request_fails()
    {
        using var client = SignedIn(new StubHandler(_ => throw new HttpRequestException("offline")));
        var account = new SourceAccount(new NeteaseApi(client), () => true);
        account.MarkExpired();

        await Assert.ThrowsAsync<HttpRequestException>(() => account.LogoutAsync());

        Assert.False(client.IsLoggedIn);
        Assert.False(account.IsExpired);
        Assert.Null(account.Profile);
    }
}
