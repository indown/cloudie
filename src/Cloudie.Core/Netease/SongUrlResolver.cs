using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Netease;

/// <summary>A song-URL answer and which account gave it.</summary>
public sealed record SongUrlLookup(SongUrlData Data, bool ViaSourceAccount);

/// <summary>
/// Chooses the account that asks NetEase for a song's URL. 音源账号 goes first whenever it is usable: a
/// non-VIP account answers a lossless request with a quietly downgraded file rather than a refusal, so the
/// VIP account cannot be a fallback. 云盘 songs go to the primary account first, since only their owner can
/// fetch them. The other account is asked when the first gives nothing or only a trial. A dead source
/// session (301) retires that account until the user signs in again.
/// </summary>
public sealed class SongUrlResolver(NeteaseApi primary, SourceAccount? source = null)
{
    /// <summary>A lookup failed (track id, whether it was 音源账号, the error); the other account is still tried.</summary>
    public event Action<long, bool, Exception>? LookupFailed;

    /// <summary>The best answer either account gave: a full URL, else a trial, else null.</summary>
    public async Task<SongUrlLookup?> ResolveAsync(Track track, AudioQuality quality, CancellationToken ct = default)
    {
        // true = 音源账号, false = the primary account.
        bool[] order = source is { IsUsable: true } ? (track.IsCloud ? [false, true] : [true, false]) : [false];
        SongUrlLookup? trial = null;
        foreach (var viaSource in order)
        {
            var lookup = await LookupAsync(track.Id, quality, viaSource, ct).ConfigureAwait(false);
            if (lookup is null) continue;
            if (!lookup.Data.IsTrial) return lookup;
            trial ??= lookup;
        }
        return trial;
    }

    private async Task<SongUrlLookup?> LookupAsync(long trackId, AudioQuality quality, bool viaSource, CancellationToken ct)
    {
        var api = viaSource ? source!.Api : primary;
        try
        {
            var data = await api.GetSongUrlAsync(trackId, quality, ct).ConfigureAwait(false);
            return data?.Url is { Length: > 0 } ? new SongUrlLookup(data, viaSource) : null;
        }
        catch (NeteaseApiException error)
        {
            if (viaSource && error.NeedsLogin) source!.MarkExpired();
            LookupFailed?.Invoke(trackId, viaSource, error);
            return null;
        }
        catch (Exception error) when (viaSource && !ct.IsCancellationRequested)
        {
            // A transport failure (or a time-out) on 音源账号 alone should not cost the song: the primary is asked.
            LookupFailed?.Invoke(trackId, true, error);
            return null;
        }
    }
}
