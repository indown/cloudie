using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Netease;

/// <summary>What the QR sign-in is doing, for the dialog's status line.</summary>
public enum QrLoginPhase
{
    /// <summary>The first code is being fetched.</summary>
    Loading,
    /// <summary>A code is on screen and nobody scanned it yet.</summary>
    Waiting,
    /// <summary>Like <see cref="Waiting"/>, but the code on screen replaced one NetEase retired.</summary>
    Refreshed,
    /// <summary>Scanned; the phone still has to confirm.</summary>
    Scanned,
    /// <summary>A request failed; the loop pauses and goes on from where it was. The event carries the error.</summary>
    Retrying,
    /// <summary>The phone confirmed and the session is stored; the profile is being read.</summary>
    Confirmed,
    /// <summary>The profile is read; the dialog can close.</summary>
    Done
}

/// <summary>
/// The QR sign-in loop: fetch a key, show its code, poll until the phone confirms. A code NetEase retired (it
/// does after a few minutes) is replaced at once and the phase says so. A failed request anywhere (NetEase
/// drops idle connections, Wi-Fi blips) is reported, waited out and retried from where it left off: an expired
/// code is fetched again, a confirmed sign-in reads the profile again, and the hint comes back with the next
/// good poll. Awaits keep the caller's context on purpose, so <paramref name="showCode"/>,
/// <paramref name="onConfirmed"/> and <see cref="PhaseChanged"/> run where <see cref="RunAsync"/> was
/// started: the UI thread.
/// </summary>
public sealed class QrLoginSession(NeteaseApi api, Func<string, CancellationToken, Task> showCode, Func<Task> onConfirmed)
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public QrLoginPhase Phase { get; private set; } = QrLoginPhase.Loading;

    /// <summary>The phase changed; the exception is set while <see cref="QrLoginPhase.Retrying"/>.</summary>
    public event Action<QrLoginPhase, Exception?>? PhaseChanged;

    /// <summary>Runs until the sign-in is done, or throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        string? key = null;
        var idle = QrLoginPhase.Waiting;
        var confirmed = false;
        while (true)
        {
            try
            {
                if (confirmed)
                {
                    await onConfirmed();
                    Report(QrLoginPhase.Done);
                    return;
                }
                if (key is null)
                {
                    key = await ShowFreshCodeAsync(ct);
                    Report(idle);
                }
                await Task.Delay(PollInterval, ct);
                switch ((await api.CheckQrAsync(key, ct)).State)
                {
                    case QrLoginState.Waiting:
                        Report(idle);   // also takes a retry notice down once the network is back
                        break;
                    case QrLoginState.Scanned:
                        Report(QrLoginPhase.Scanned);
                        break;
                    case QrLoginState.Expired:
                        key = await ShowFreshCodeAsync(ct);
                        idle = QrLoginPhase.Refreshed;
                        Report(idle);
                        break;
                    case QrLoginState.Confirmed:
                        // The 803 reply carried the cookies, so the session is stored whatever happens next.
                        confirmed = true;
                        Report(QrLoginPhase.Confirmed);
                        break;
                }
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                Report(QrLoginPhase.Retrying, error);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private async Task<string> ShowFreshCodeAsync(CancellationToken ct)
    {
        var key = await api.GetQrKeyAsync(ct);
        await showCode(key, ct);
        return key;
    }

    /// <summary>Raises the event on a change only, so a poll that finds nothing new stays silent.</summary>
    private void Report(QrLoginPhase phase, Exception? error = null)
    {
        if (phase == Phase && error is null) return;
        Phase = phase;
        PhaseChanged?.Invoke(phase, error);
    }
}
