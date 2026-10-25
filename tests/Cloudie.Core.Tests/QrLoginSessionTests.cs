using System.Net;
using System.Text;
using Cloudie.Core.Netease;

namespace Cloudie.Core.Tests;

public class QrLoginSessionTests
{
    /// <summary>
    /// A session over a scripted NetEase: key requests and polls each consume their own queue of replies
    /// (a key string or a status code) or exceptions. When a queue runs dry the token is cancelled, which
    /// is how a test that does not reach "done" ends.
    /// </summary>
    private sealed class Scripted : IDisposable
    {
        private readonly Queue<object> _keys;
        private readonly Queue<object> _checks;
        private readonly NeteaseClient _client;

        public CancellationTokenSource Cts { get; } = new();
        public QrLoginSession Session { get; }
        public List<(QrLoginPhase Phase, Exception? Error)> Phases { get; } = [];
        public List<string> ShownKeys { get; } = [];
        public int KeyCalls { get; private set; }
        public int CheckCalls { get; private set; }
        public int ConfirmedCalls { get; private set; }
        /// <summary>What the profile read does; the default just counts.</summary>
        public Func<Task>? OnConfirmed { get; set; }

        public Scripted(object[] keys, object[] checks, TimeSpan? retryDelay = null)
        {
            _keys = new Queue<object>(keys);
            _checks = new Queue<object>(checks);
            _client = new NeteaseClient(handler: new StubHandler(Answer));
            Session = new QrLoginSession(new NeteaseApi(_client), (key, _) =>
                {
                    ShownKeys.Add(key);
                    return Task.CompletedTask;
                }, () =>
                {
                    ConfirmedCalls++;
                    return OnConfirmed?.Invoke() ?? Task.CompletedTask;
                })
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(1)
            };
            Session.PhaseChanged += (phase, error) => Phases.Add((phase, error));
        }

        public Task RunAsync() => Session.RunAsync(Cts.Token);

        /// <summary>Runs until the script is used up; the loop then sees the cancellation.</summary>
        public async Task RunOutAsync() => await Assert.ThrowsAnyAsync<OperationCanceledException>(RunAsync);

        private Task<HttpResponseMessage> Answer(HttpRequestMessage request)
        {
            var isKey = request.RequestUri!.AbsolutePath.EndsWith("/unikey", StringComparison.Ordinal);
            var queue = isKey ? _keys : _checks;
            if (isKey) KeyCalls++; else CheckCalls++;
            if (queue.Count == 0)
            {
                Cts.Cancel();
                throw new OperationCanceledException(Cts.Token);
            }
            var body = queue.Dequeue() switch
            {
                Exception error => throw error,
                string key when isKey => $$"""{"code":200,"unikey":"{{key}}"}""",
                int code when !isKey => $$"""{"code":{{code}}}""",
                var other => throw new InvalidOperationException($"script: {other} does not fit {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        public void Dispose() => _client.Dispose();
    }

    private static IEnumerable<QrLoginPhase> PhasesOf(Scripted s) => s.Phases.Select(static p => p.Phase);

    [Fact]
    public async Task A_retired_code_is_replaced_at_once_and_the_phase_says_so()
    {
        using var s = new Scripted(keys: ["k1", "k2"], checks: [801, 800, 801, 801]);

        await s.RunOutAsync();

        Assert.Equal(["k1", "k2"], s.ShownKeys);
        Assert.Equal([QrLoginPhase.Waiting, QrLoginPhase.Refreshed], PhasesOf(s));   // the later 801s change nothing
    }

    [Fact]
    public async Task A_failed_poll_is_retried_and_the_hint_returns_with_the_next_good_poll()
    {
        using var s = new Scripted(keys: ["k1"], checks: [new HttpRequestException("boom"), 801]);

        await s.RunOutAsync();

        Assert.Equal([QrLoginPhase.Waiting, QrLoginPhase.Retrying, QrLoginPhase.Waiting], PhasesOf(s));
        Assert.IsType<HttpRequestException>(s.Phases[1].Error);
        Assert.Equal(1, s.KeyCalls);   // the code on screen is still good; only the poll is repeated
    }

    [Fact]
    public async Task A_time_out_counts_as_a_failed_request_not_as_cancellation()
    {
        using var s = new Scripted(keys: ["k1"], checks: [new TaskCanceledException("timed out"), 801]);

        await s.RunOutAsync();

        Assert.Equal([QrLoginPhase.Waiting, QrLoginPhase.Retrying, QrLoginPhase.Waiting], PhasesOf(s));
    }

    [Fact]
    public async Task A_failed_first_key_fetch_is_retried()
    {
        using var s = new Scripted(keys: [new HttpRequestException("offline"), "k1"], checks: [801]);

        await s.RunOutAsync();

        Assert.Equal([QrLoginPhase.Retrying, QrLoginPhase.Waiting], PhasesOf(s));
        Assert.Equal(["k1"], s.ShownKeys);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_polling_the_old_code_until_a_new_one_arrives()
    {
        using var s = new Scripted(keys: ["k1", new HttpRequestException("boom"), "k2"], checks: [800, 800, 801]);

        await s.RunOutAsync();

        Assert.Equal([QrLoginPhase.Waiting, QrLoginPhase.Retrying, QrLoginPhase.Refreshed], PhasesOf(s));
        Assert.Equal(["k1", "k2"], s.ShownKeys);
        Assert.Equal(3, s.KeyCalls);
    }

    [Fact]
    public async Task A_confirmed_scan_reads_the_profile_and_finishes()
    {
        using var s = new Scripted(keys: ["k1"], checks: [802, 803]);

        await s.RunAsync();

        Assert.Equal([QrLoginPhase.Waiting, QrLoginPhase.Scanned, QrLoginPhase.Confirmed, QrLoginPhase.Done], PhasesOf(s));
        Assert.Equal(1, s.ConfirmedCalls);
        Assert.Equal(2, s.CheckCalls);
        Assert.Equal(QrLoginPhase.Done, s.Session.Phase);
    }

    [Fact]
    public async Task A_failed_profile_read_after_confirmation_is_retried_without_polling_again()
    {
        using var s = new Scripted(keys: ["k1"], checks: [803]);
        var attempts = 0;
        s.OnConfirmed = () => ++attempts == 1 ? throw new HttpRequestException("boom") : Task.CompletedTask;

        await s.RunAsync();

        Assert.Equal([QrLoginPhase.Waiting, QrLoginPhase.Confirmed, QrLoginPhase.Retrying, QrLoginPhase.Done], PhasesOf(s));
        Assert.Equal(2, s.ConfirmedCalls);
        Assert.Equal(1, s.CheckCalls);
    }

    [Fact]
    public async Task Cancelling_during_the_retry_pause_ends_the_loop()
    {
        using var s = new Scripted(keys: ["k1"], checks: [new HttpRequestException("boom")], retryDelay: TimeSpan.FromMinutes(1));
        s.Session.PhaseChanged += (phase, _) =>
        {
            if (phase == QrLoginPhase.Retrying) s.Cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(s.RunAsync);

        Assert.Equal(1, s.CheckCalls);
    }
}
