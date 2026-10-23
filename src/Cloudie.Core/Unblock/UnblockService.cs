using Cloudie.Core.Netease;

namespace Cloudie.Core.Unblock;

/// <summary>Resolves gray tracks from the enabled third-party sources, in order, first hit wins.</summary>
public sealed class UnblockService(IReadOnlyList<IAudioSource> sources, Func<IReadOnlySet<string>> enabledSourceIds)
{
    public IReadOnlyList<IAudioSource> Sources { get; } = sources;

    /// <summary>Raised for every source that failed (not for sources that simply had no match).</summary>
    public event Action<IAudioSource, Exception>? SourceFailed;

    public async Task<ResolvedAudio?> ResolveAsync(UnblockTrack track, AudioQuality quality, CancellationToken cancellationToken = default)
    {
        var enabled = enabledSourceIds();
        foreach (var source in Sources)
        {
            if (!enabled.Contains(source.Id)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await source.ResolveAsync(track, quality, cancellationToken).ConfigureAwait(false) is { } resolved)
                {
                    return resolved;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                SourceFailed?.Invoke(source, exception);
            }
        }
        return null;
    }
}
