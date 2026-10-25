using Cloudie.Core.Netease.Models;

namespace Cloudie.Core.Netease;

/// <summary>
/// 音源账号: a second NetEase account, meant to be one with 黑胶 VIP, that answers only song-URL lookups
/// (see <see cref="SongUrlResolver"/>). Playlists, recommendations, likes and scrobbles stay with the primary
/// account. It has its own cookie jar, so the two sessions never mix.
/// </summary>
public sealed class SourceAccount(NeteaseApi api, Func<bool> enabled)
{
    public NeteaseApi Api { get; } = api;

    /// <summary>Who is signed in, once <see cref="ReloadAsync"/> succeeded; null before that and after signing out.</summary>
    public UserProfile? Profile { get; private set; }

    /// <summary>The switch in Settings; off keeps the session but stops using it.</summary>
    public bool IsEnabled => enabled();

    /// <summary>A session cookie is stored, whether or not NetEase still accepts it.</summary>
    public bool IsLoggedIn => Api.Client.IsLoggedIn;

    /// <summary>NetEase rejected the stored session (301, or an anonymous account reply); cleared by signing in again.</summary>
    public bool IsExpired { get; private set; }

    /// <summary>True when song-URL lookups should go through this account.</summary>
    public bool IsUsable => IsEnabled && IsLoggedIn && !IsExpired;

    public int VipType => Profile?.VipType ?? 0;

    /// <summary>The profile or the session state changed; raised on whatever thread noticed it.</summary>
    public event Action? Changed;

    /// <summary>
    /// Start-up: refreshes the stored session and reads the profile. A transport failure propagates and leaves
    /// the account usable, so a start without network still tries it when a song plays.
    /// </summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        if (!IsLoggedIn) return;
        await Api.RefreshLoginAsync(ct).ConfigureAwait(false);
        await ReloadAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads the profile; an anonymous reply for a stored cookie means the session no longer counts.</summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            Profile = await Api.GetAccountAsync(ct).ConfigureAwait(false);
            IsExpired = Profile is null && IsLoggedIn;
        }
        catch (NeteaseApiException error) when (error.NeedsLogin)
        {
            Profile = null;
            IsExpired = true;
        }
        Changed?.Invoke();
    }

    /// <summary>A lookup got 301: the account sits out until the user signs in again. The nickname is kept for display.</summary>
    public void MarkExpired()
    {
        if (IsExpired) return;
        IsExpired = true;
        Changed?.Invoke();
    }

    /// <summary>Signs out; the cookies go even when NetEase could not be told, and the error then propagates.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await Api.LogoutAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Profile = null;
            IsExpired = false;
            Changed?.Invoke();
        }
    }
}
