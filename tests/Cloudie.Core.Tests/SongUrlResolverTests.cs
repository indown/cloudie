using System.Text.Json;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Tests;

public class SongUrlResolverTests
{
    private const string Lossless = """{"code":200,"data":[{"id":7,"url":"http://m.music.126.net/7.flac","br":999000,"level":"lossless","type":"flac"}]}""";
    private const string Downgraded = """{"code":200,"data":[{"id":7,"url":"http://m.music.126.net/7.mp3","br":320000,"level":"exhigh","type":"mp3"}]}""";
    private const string Trial = """{"code":200,"data":[{"id":7,"url":"http://m.music.126.net/7-trial.mp3","br":320000,"level":"exhigh","type":"mp3","freeTrialInfo":{"start":30000,"end":60000}}]}""";
    private const string Nothing = """{"code":200,"data":[{"id":7,"url":null,"br":0,"code":-110}]}""";
    private const string NeedsLogin = """{"code":301,"msg":"需要登录"}""";

    private static Track Song(bool cloud = false) =>
        JsonSerializer.Deserialize<Track>(cloud ? """{"id":7,"name":"歌","pc":{"uid":1}}""" : """{"id":7,"name":"歌"}""", NeteaseClient.ResponseOptions)!;

    /// <summary>Two signed-in clients, each answering from its own stub, behind one resolver.</summary>
    private sealed class Accounts : IDisposable
    {
        public StubHandler PrimaryHandler { get; }
        public StubHandler SourceHandler { get; }
        public NeteaseClient Primary { get; }
        public NeteaseClient Source { get; }
        public SourceAccount SourceAccount { get; }
        public SongUrlResolver Resolver { get; }
        public bool Enabled { get; set; } = true;
        public List<(long TrackId, bool ViaSource, Exception Error)> Failures { get; } = [];

        public Accounts(StubHandler primary, StubHandler source)
        {
            PrimaryHandler = primary;
            SourceHandler = source;
            Primary = new NeteaseClient(handler: primary);
            Primary.SetCookies(new Dictionary<string, string> { ["MUSIC_U"] = "main" });
            Source = new NeteaseClient(handler: source);
            Source.SetCookies(new Dictionary<string, string> { ["MUSIC_U"] = "vip" });
            SourceAccount = new SourceAccount(new NeteaseApi(Source), () => Enabled);
            Resolver = new SongUrlResolver(new NeteaseApi(Primary), SourceAccount);
            Resolver.LookupFailed += (id, viaSource, error) => Failures.Add((id, viaSource, error));
        }

        public Accounts(string primaryReply, string sourceReply) : this(StubHandler.Json(primaryReply), StubHandler.Json(sourceReply)) { }

        public void Dispose()
        {
            Primary.Dispose();
            Source.Dispose();
        }
    }

    [Fact]
    public async Task The_source_account_is_asked_first_with_its_own_cookie_and_its_answer_is_used()
    {
        using var accounts = new Accounts(Downgraded, Lossless);

        var lookup = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.NotNull(lookup);
        Assert.True(lookup.ViaSourceAccount);
        Assert.Equal("lossless", lookup.Data.Level);
        Assert.Empty(accounts.PrimaryHandler.Requests);
        var cookie = Assert.Single(accounts.SourceHandler.Requests).Headers.GetValues("Cookie").Single();
        Assert.Contains("MUSIC_U=vip", cookie);
        Assert.DoesNotContain("MUSIC_U=main", cookie);
    }

    [Fact]
    public async Task A_cloud_disk_song_goes_to_the_primary_account_first()
    {
        using var accounts = new Accounts(Downgraded, Lossless);

        var lookup = await accounts.Resolver.ResolveAsync(Song(cloud: true), AudioQuality.Lossless);

        Assert.NotNull(lookup);
        Assert.False(lookup.ViaSourceAccount);
        Assert.Empty(accounts.SourceHandler.Requests);
    }

    [Fact]
    public async Task A_cloud_disk_song_the_primary_cannot_fetch_is_asked_of_the_source_account()
    {
        using var accounts = new Accounts(Nothing, Lossless);

        var lookup = await accounts.Resolver.ResolveAsync(Song(cloud: true), AudioQuality.Lossless);

        Assert.True(lookup!.ViaSourceAccount);
    }

    [Fact]
    public async Task A_trial_from_the_source_account_falls_through_to_the_primary()
    {
        using var accounts = new Accounts(Lossless, Trial);

        var lookup = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.NotNull(lookup);
        Assert.False(lookup.ViaSourceAccount);
        Assert.False(lookup.Data.IsTrial);
    }

    [Fact]
    public async Task When_both_accounts_only_offer_a_trial_the_trial_is_returned()
    {
        using var accounts = new Accounts(Trial, Trial);

        var lookup = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.NotNull(lookup);
        Assert.True(lookup.Data.IsTrial);
        Assert.Single(accounts.PrimaryHandler.Requests);
        Assert.Single(accounts.SourceHandler.Requests);
    }

    [Fact]
    public async Task A_dead_source_session_retires_the_account_until_it_signs_in_again()
    {
        using var accounts = new Accounts(Downgraded, NeedsLogin);

        var first = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);
        var second = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.False(first!.ViaSourceAccount);
        Assert.False(second!.ViaSourceAccount);
        Assert.True(accounts.SourceAccount.IsExpired);
        Assert.False(accounts.SourceAccount.IsUsable);
        Assert.Single(accounts.SourceHandler.Requests);
        Assert.Equal(2, accounts.PrimaryHandler.Requests.Count);
        var failure = Assert.Single(accounts.Failures);
        Assert.True(failure.ViaSource);
        Assert.True(Assert.IsType<NeteaseApiException>(failure.Error).NeedsLogin);
    }

    [Fact]
    public async Task A_source_account_turned_off_in_settings_is_not_asked()
    {
        using var accounts = new Accounts(Downgraded, Lossless) { Enabled = false };

        var lookup = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.False(lookup!.ViaSourceAccount);
        Assert.Empty(accounts.SourceHandler.Requests);
    }

    [Fact]
    public async Task A_transport_failure_on_the_source_account_alone_does_not_cost_the_song()
    {
        var broken = new StubHandler(_ => throw new HttpRequestException("boom"));
        using var accounts = new Accounts(StubHandler.Json(Downgraded), broken);

        var lookup = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.False(lookup!.ViaSourceAccount);
        var failure = Assert.Single(accounts.Failures);
        Assert.True(failure.ViaSource);
        Assert.IsType<HttpRequestException>(failure.Error);
        Assert.False(accounts.SourceAccount.IsExpired);
    }

    [Fact]
    public async Task A_time_out_on_the_source_account_is_not_mistaken_for_cancellation()
    {
        var slow = new StubHandler(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        using var accounts = new Accounts(StubHandler.Json(Downgraded), slow);

        var lookup = await accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.False(lookup!.ViaSourceAccount);
        Assert.Single(accounts.Failures);
    }

    [Fact]
    public async Task Cancellation_while_asking_the_source_account_propagates()
    {
        using var cts = new CancellationTokenSource();
        var cancelling = new StubHandler(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var accounts = new Accounts(StubHandler.Json(Downgraded), cancelling);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless, cts.Token));

        Assert.Empty(accounts.PrimaryHandler.Requests);
        Assert.Empty(accounts.Failures);
    }

    [Fact]
    public async Task A_transport_failure_on_the_primary_account_still_surfaces()
    {
        var broken = new StubHandler(_ => throw new HttpRequestException("boom"));
        using var accounts = new Accounts(broken, StubHandler.Json(Nothing));

        await Assert.ThrowsAsync<HttpRequestException>(() => accounts.Resolver.ResolveAsync(Song(), AudioQuality.Lossless));
    }

    [Fact]
    public async Task A_business_error_from_the_primary_yields_nothing_so_the_gray_track_sources_get_their_turn()
    {
        using var client = new NeteaseClient(handler: StubHandler.Json("""{"code":404,"message":"not found"}"""));
        var resolver = new SongUrlResolver(new NeteaseApi(client));
        var failures = new List<bool>();
        resolver.LookupFailed += (_, viaSource, _) => failures.Add(viaSource);

        var lookup = await resolver.ResolveAsync(Song(), AudioQuality.ExHigh);

        Assert.Null(lookup);
        Assert.Equal([false], failures);
    }

    [Fact]
    public async Task Without_a_source_account_only_the_primary_is_asked()
    {
        var handler = StubHandler.Json(Lossless);
        using var client = new NeteaseClient(handler: handler);
        var resolver = new SongUrlResolver(new NeteaseApi(client));

        var lookup = await resolver.ResolveAsync(Song(), AudioQuality.Lossless);

        Assert.False(lookup!.ViaSourceAccount);
        Assert.Single(handler.Requests);
    }
}
