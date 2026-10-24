using CommunityToolkit.Mvvm.ComponentModel;
using Cloudie.Core.Lyrics;
using Cloudie.Core.Netease;
using Cloudie.Core.Netease.Models;
using Cloudie.Core.Playback;
using Cloudie.Core.Settings;
using Cloudie.Core.Unblock;
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Cloudie.App.Services;

/// <summary>
/// Owns the queue, the MediaPlayer and the system transport controls. Resolves each track's
/// URL from NetEase first and from the gray-track sources when NetEase refuses or only offers
/// a trial.
/// </summary>
public sealed partial class PlayerService : ObservableObject
{
    private readonly NeteaseApi _api;
    private readonly UnblockService _unblock;
    private readonly AppSettings _settings;
    private readonly AccountService _account;
    private readonly LogService _log;
    private readonly MediaPlayer _player = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherQueueTimer _ticker;
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private long _sourceId;
    private bool _playReported = true;
    private readonly PlaybackStore _store;
    private TimeSpan? _pendingSeek;
    private DateTime _lastPositionSave;
    private bool _fetchingFm;
    /// <summary>The current track's media reached the player; until then there is nothing to report or tick.</summary>
    private bool _playbackStarted;
    /// <summary>Automatic skips since a song last played, so a queue where nothing resolves does not cycle forever.</summary>
    private int _skipsInARow;
    /// <summary>The source handed to the player for the current song, disposed when replaced.</summary>
    private MediaSource? _currentSource;
    /// <summary>How the current song was resolved; reused when the same song plays again (repeat one).</summary>
    private ResolvedMedia? _currentMedia;
    /// <summary>True when the current source was opened ahead of time; a failure then gets one cold retry.</summary>
    private bool _currentWasPreopened;
    /// <summary>The load generation whose source is in the player, and the one whose failure was already handled.</summary>
    private int _sourceGeneration;
    private int _failedGeneration;
    /// <summary>Where a restored session asked to resume; a failure to seek there is retried from the start.</summary>
    private TimeSpan? _resumedAt;
    private Prefetch? _prefetch;
    /// <summary>The URL of the next song is resolved this long after the current one starts, so the start buffers first.</summary>
    private static readonly TimeSpan PrefetchDelay = TimeSpan.FromSeconds(3);
    /// <summary>With this much left, the next song's stream is opened so the switch has nothing to wait for.</summary>
    private static readonly TimeSpan PreopenBeforeEnd = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 歌曲预热: the song after the current one, resolved (and near the end, opened) ahead of time. The URL is
    /// fetched early because it is cheap; the stream only shortly before it is needed, so an idle connection
    /// does not go stale and the current song keeps the bandwidth.
    /// </summary>
    private sealed class Prefetch(long trackId, AudioQuality quality)
    {
        public long TrackId { get; } = trackId;
        public AudioQuality Quality { get; } = quality;
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public CancellationTokenSource Cts { get; } = new();
        public Task<ResolvedMedia?> Resolving { get; set; } = Task.FromResult<ResolvedMedia?>(null);
        public Task<MediaSource?>? Opening { get; set; }
        public bool IsStale => DateTimeOffset.UtcNow - StartedAt > ResolvedMedia.Lifetime;
    }

    public PlayerService(NeteaseApi api, UnblockService unblock, AppSettings settings, AccountService account, LogService log, PlaybackStore store)
    {
        _api = api;
        _store = store;
        _unblock = unblock;
        _settings = settings;
        _account = account;
        _log = log;
        Queue = new PlaybackQueue();
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.CommandManager.IsEnabled = false;
        _player.Volume = settings.Volume;
        _player.MediaOpened += (_, _) =>
        {
            // A restored session resumes where it stopped; the seek has to wait until the media is open.
            if (_pendingSeek is { } seek)
            {
                _pendingSeek = null;
                _player.PlaybackSession.Position = seek;
            }
        };
        _player.MediaEnded += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            ReportPlay(completed: true);
            _ = NextAsync(manual: false);
        });
        _player.MediaFailed += (sender, e) =>
        {
            // Read on the player's thread, before the UI thread can swap the source: it tells which load failed.
            var generation = Volatile.Read(ref _sourceGeneration);
            var detail = $"{e.Error} (0x{e.ExtendedErrorCode?.HResult ?? 0:X8}) {e.ErrorMessage}".Trim();
            _dispatcher.TryEnqueue(() => _ = OnMediaFailedAsync(generation, detail));
        };
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => _dispatcher.TryEnqueue(() =>
            IsPlaying = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing);
        _ticker = _dispatcher.CreateTimer();
        _ticker.Interval = TimeSpan.FromMilliseconds(100);
        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();
        ConfigureTransportControls();
    }

    public PlaybackQueue Queue { get; }

    [ObservableProperty] public partial Track? CurrentTrack { get; private set; }
    [ObservableProperty] public partial bool IsPlaying { get; private set; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial TimeSpan Position { get; private set; }
    [ObservableProperty] public partial TimeSpan Duration { get; private set; }
    [ObservableProperty] public partial ParsedLyrics Lyrics { get; private set; } = ParsedLyrics.Empty;
    [ObservableProperty] public partial int ActiveLyricIndex { get; private set; } = -1;
    [ObservableProperty] public partial string? UnblockSource { get; private set; }
    [ObservableProperty] public partial string? ServedQuality { get; private set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty] public partial bool IsShuffled { get; private set; }
    [ObservableProperty] public partial RepeatMode RepeatMode { get; private set; }
    [ObservableProperty] public partial bool ShowNowPlaying { get; set; }
    /// <summary>私人 FM: the queue is NetEase's station and refills itself near its end.</summary>
    [ObservableProperty] public partial bool IsFm { get; private set; }
    /// <summary>心动模式: a run of songs NetEase picked to go with one liked song; tops itself up like FM.</summary>
    [ObservableProperty] public partial bool IsHeartMode { get; private set; }

    public double Volume
    {
        get => _player.Volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(_player.Volume - clamped) < 0.001) return;
            _player.Volume = clamped;
            _settings.Volume = clamped;
            OnPropertyChanged();
        }
    }

    public bool HasCurrentTrack => CurrentTrack is not null;

    /// <summary>
    /// Replaces the queue with <paramref name="tracks"/> and plays the one at <paramref name="index"/>.
    /// <paramref name="sourceId"/> is the playlist or album the songs came from; it is reported with the listening history.
    /// </summary>
    public Task PlayAsync(IEnumerable<Track> tracks, int index = 0, long sourceId = 0)
    {
        IsFm = false;
        IsHeartMode = false;
        _sourceId = sourceId;
        Queue.Replace(tracks, index);
        IsShuffled = Queue.IsShuffled;
        OnPropertyChanged(nameof(Queue));
        SaveQueue();
        return PlayCurrentAsync();
    }

    public Task PlayAtAsync(int index)
    {
        if (!Queue.MoveTo(index)) return Task.CompletedTask;
        return PlayCurrentAsync();
    }

    public void PlayNext(Track track)
    {
        Queue.InsertNext(track);
        OnPropertyChanged(nameof(Queue));
        SaveQueue();
        if (CurrentTrack is null) _ = PlayCurrentAsync();
        else PrefetchNext();
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null || IsLoading) return;
        if (_player.Source is null)
        {
            // Restored from the last session: nothing is loaded yet, so fetch the URL now and resume.
            _ = PlayCurrentAsync(resumeAt: Position);
            return;
        }
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
        else _player.Play();
    }

    public async Task NextAsync(bool manual = true)
    {
        if ((IsFm || IsHeartMode) && Queue.CurrentIndex >= Queue.Count - 1) await TopUpAsync();
        if (!Queue.AdvanceToNext(manual || IsFm || IsHeartMode))
        {
            _player.Pause();
            return;
        }
        await PlayCurrentAsync();
    }

    public Task PreviousAsync()
    {
        if (_playbackStarted && Position > TimeSpan.FromSeconds(3))
        {
            Seek(TimeSpan.Zero);
            return Task.CompletedTask;
        }
        return Queue.GoToPrevious() ? PlayCurrentAsync() : Task.CompletedTask;
    }

    public void Seek(TimeSpan position)
    {
        if (!_player.PlaybackSession.CanSeek) return;
        _player.PlaybackSession.Position = position;
        Position = position;
    }

    public void ToggleShuffle()
    {
        Queue.SetShuffle(!Queue.IsShuffled);
        IsShuffled = Queue.IsShuffled;
        OnPropertyChanged(nameof(Queue));
        SaveQueue();
        PrefetchNext();
    }

    public void CycleRepeatMode()
    {
        Queue.RepeatMode = Queue.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
        RepeatMode = Queue.RepeatMode;
        SaveQueue();
        PrefetchNext();
    }

    private async Task PlayCurrentAsync(TimeSpan? resumeAt = null)
    {
        var track = Queue.Current;
        if (track is null) return;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var generation = ++_loadGeneration;
        ReportPlay(completed: false);
        // Silence the previous song now. Left alone it would keep playing until the new URL arrives, and its
        // position would keep ticking into the new track's state (and into its listening report).
        _player.Pause();
        _player.Source = null;
        _currentSource?.Dispose();
        _currentSource = null;
        _currentWasPreopened = false;
        _resumedAt = resumeAt;
        _playbackStarted = false;
        CurrentTrack = track;
        _playReported = false;
        OnPropertyChanged(nameof(HasCurrentTrack));
        Lyrics = ParsedLyrics.Empty;
        ActiveLyricIndex = -1;
        UnblockSource = null;
        ServedQuality = null;
        Position = resumeAt ?? TimeSpan.Zero;
        Duration = track.Duration;
        SavePosition();
        IsLoading = true;
        try
        {
            // A restored session seeks after MediaOpened, so it always opens its stream itself.
            var prefetched = resumeAt is null ? await TakePrefetchedAsync(track, cts.Token) : null;
            var resolved = prefetched?.Media ?? ReuseCurrentMedia(track) ?? await ResolveAsync(track, cts.Token);
            if (generation != _loadGeneration)
            {
                prefetched?.Source?.Dispose();
                return;
            }
            if (resolved is null)
            {
                StatusMessage = $"《{track.Name}》没有可用的音源，已跳过";
                _log.Warn($"no source for {track.Id} {track.Name}");
                await SkipUnplayableAsync();
                return;
            }
            _skipsInARow = 0;
            UnblockSource = resolved.SourceName;
            ServedQuality = resolved.Quality;
            _pendingSeek = resumeAt is { } seek && seek > TimeSpan.FromSeconds(1) ? seek : null;
            _currentSource = prefetched?.Source ?? MediaSource.CreateFromUri(resolved.Url);
            _currentMedia = resolved;
            _currentWasPreopened = prefetched?.Source is not null;
            _sourceGeneration = generation;
            _player.Source = _currentSource;
            _playbackStarted = true;
            _player.Play();
            UpdateTransportMetadata(track);
            var how = prefetched is null ? "" : _currentWasPreopened ? " [预热: 链接+流]" : " [预热: 链接]";
            _log.Info($"now playing {track.Id} {track.Name} via {resolved.SourceName ?? "netease"} ({resolved.Quality}){how}");
            _ = LoadLyricsAsync(track, generation, cts.Token);
            if ((IsFm || IsHeartMode) && Queue.CurrentIndex >= Queue.Count - 1) _ = TopUpAsync();
            PrefetchNext();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (generation != _loadGeneration) return;
            _log.Error($"load {track.Id}: {error}");
            StatusMessage = $"加载失败：{ErrorText.Describe(error)}";
        }
        finally
        {
            if (generation == _loadGeneration) IsLoading = false;
        }
    }

    /// <summary>
    /// Media Foundation reports one failure several times in a burst. The first report for a loaded source
    /// decides; the rest are ignored, and the decision waits a moment so the burst is over before any new
    /// source is in the player (otherwise the stragglers would be blamed on the new song).
    /// </summary>
    private async Task OnMediaFailedAsync(int generation, string detail)
    {
        if (generation != _loadGeneration || _failedGeneration == generation) return;
        _failedGeneration = generation;
        var preopened = _currentWasPreopened;
        var resumedAt = _resumedAt;
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        if (generation != _loadGeneration) return;   // the user moved on meanwhile
        _playReported = true;   // a song that failed to play is not a listen, whatever position it claims
        if (preopened)
        {
            // The stream opened ahead of time may have gone stale; load the song again from scratch.
            _log.Warn($"播放失败 (预热的流): {detail}，重新加载");
            await PlayCurrentAsync();
            return;
        }
        if (resumedAt is { } resume)
        {
            // A restored session seeks on open; a file shorter than NetEase's duration cannot. Start over instead.
            _log.Warn($"播放失败 (恢复到 {resume:mm\\:ss}): {detail}，从头播放");
            await PlayCurrentAsync();
            return;
        }
        _log.Error($"播放失败: {detail}");
        StatusMessage = "系统播放错误，已跳过";
        await SkipUnplayableAsync();
    }

    /// <summary>
    /// Moves on from a song that cannot play. After a whole queue's worth of failures in a row it stops instead,
    /// so a list of unavailable songs on repeat does not hammer the network.
    /// </summary>
    private async Task SkipUnplayableAsync()
    {
        if (++_skipsInARow >= Queue.Count)
        {
            _skipsInARow = 0;
            StatusMessage = "队列里没有可以播放的歌曲";
            return;
        }
        await NextAsync(manual: false);
    }

    /// <summary>
    /// Sends the finished (or abandoned) track to NetEase's listening history, once per play. Nothing is
    /// reported for a song that never started, when signed out, when the setting is off, or when the song
    /// was skipped within its first second.
    /// </summary>
    private void ReportPlay(bool completed)
    {
        if (_playReported || CurrentTrack is not { } track) return;
        _playReported = true;
        if (!_playbackStarted || !_settings.Scrobble || !_account.IsLoggedIn) return;
        var heard = completed ? Duration : Position;
        var seconds = (int)Math.Round(heard.TotalSeconds);
        if (seconds < 1) return;
        var sourceId = _sourceId;
        _ = Task.Run(async () =>
        {
            try
            {
                await _api.ScrobbleAsync(track.Id, seconds, sourceId);
                _log.Info($"scrobbled {track.Id} {track.Name} ({seconds}s)");
            }
            catch (Exception error)
            {
                _log.Warn($"scrobble {track.Id}: {error.Message}");
            }
        });
    }

    /// <summary>NetEase first, in the chosen quality; refusals and trials fall through to the gray-track sources.</summary>
    private async Task<ResolvedMedia?> ResolveAsync(Track track, CancellationToken ct)
    {
        var quality = _settings.AudioQuality;
        SongUrlData? data = null;
        try
        {
            data = await _api.GetSongUrlAsync(track.Id, quality, ct);
        }
        catch (NeteaseApiException error)
        {
            _log.Warn($"song url {track.Id}: {error.Message}");
        }
        if (data?.Url is { Length: > 0 } url && !data.IsTrial)
        {
            return new ResolvedMedia(track.Id, new Uri(url.Replace("http://", "https://")), null, data.Level, quality, DateTimeOffset.UtcNow);
        }
        if (!_settings.UnblockEnabled) return null;
        var resolved = await _unblock.ResolveAsync(UnblockTrack.From(track), quality, ct);
        if (resolved is null)
        {
            // A 30-second trial is still better than nothing.
            return data?.Url is { Length: > 0 } trial
                ? new ResolvedMedia(track.Id, new Uri(trial.Replace("http://", "https://")), null, "试听", quality, DateTimeOffset.UtcNow)
                : null;
        }
        return new ResolvedMedia(track.Id, resolved.Url, resolved.SourceName, resolved.Format, quality, DateTimeOffset.UtcNow);
    }

    /// <summary>Repeat-one plays the same song again: its link is still good, so no lookup is needed.</summary>
    private ResolvedMedia? ReuseCurrentMedia(Track track) =>
        _currentMedia is { } media && media.IsUsable(track.Id, _settings.AudioQuality, DateTimeOffset.UtcNow) ? media : null;

    // MARK: 歌曲预热

    /// <summary>
    /// Starts resolving the song that follows the current one. Does nothing while a fresh answer for that
    /// song is already here or on its way; a changed "next" (shuffle, 下一首播放, a refill) replaces it.
    /// </summary>
    private void PrefetchNext()
    {
        var next = Queue.PeekNext();
        if (next is null || next.Id == CurrentTrack?.Id)
        {
            DropPrefetch();
            return;
        }
        var quality = _settings.AudioQuality;
        if (_prefetch is { } existing)
        {
            if (existing.TrackId == next.Id && existing.Quality == quality && !existing.IsStale) return;
            DropPrefetch();
        }
        var prefetch = new Prefetch(next.Id, quality);
        prefetch.Resolving = ResolveAheadAsync(next, prefetch.Cts.Token);
        _prefetch = prefetch;
    }

    private async Task<ResolvedMedia?> ResolveAheadAsync(Track track, CancellationToken ct)
    {
        try
        {
            await Task.Delay(PrefetchDelay, ct);
            ResolvedMedia? media;
            try
            {
                media = await ResolveAsync(track, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A dropped connection is common right after a song starts streaming; one more try a bit later.
                _log.Warn($"prefetch {track.Id}: {error.Message}, retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                media = await ResolveAsync(track, ct);
            }
            if (media is null) _log.Warn($"prefetch {track.Id} {track.Name}: no source");
            else _log.Info($"prefetched {track.Id} {track.Name} via {media.SourceName ?? "netease"} ({media.Quality})");
            return media;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error)
        {
            _log.Warn($"prefetch {track.Id}: {error.Message}");
            return null;
        }
    }

    /// <summary>Opens the stream of a resolved next song so the player can start it without a connection round trip.</summary>
    private async Task<MediaSource?> OpenAheadAsync(ResolvedMedia media, CancellationToken ct)
    {
        var source = MediaSource.CreateFromUri(media.Url);
        try
        {
            await source.OpenAsync().AsTask(ct);
            if (!ct.IsCancellationRequested) return source;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.Warn($"preopen {media.TrackId}: {error.Message}");
        }
        source.Dispose();
        return null;
    }

    /// <summary>
    /// Hands over the prefetched answer when it is for <paramref name="track"/> and still fits the quality
    /// setting and the age limit; anything else is dropped. The entry is consumed either way.
    /// </summary>
    private async Task<(ResolvedMedia Media, MediaSource? Source)?> TakePrefetchedAsync(Track track, CancellationToken ct)
    {
        if (_prefetch is not { } prefetch) return null;
        _prefetch = null;
        if (prefetch.TrackId != track.Id)
        {
            Discard(prefetch);
            return null;
        }
        try
        {
            var media = await prefetch.Resolving.WaitAsync(ct);
            if (media is null || !media.IsUsable(track.Id, _settings.AudioQuality, DateTimeOffset.UtcNow))
            {
                Discard(prefetch);
                return null;
            }
            var source = prefetch.Opening is { } opening ? await opening.WaitAsync(ct) : null;
            return (media, source);
        }
        catch (OperationCanceledException)
        {
            Discard(prefetch);
            throw;
        }
    }

    private void DropPrefetch()
    {
        if (_prefetch is { } prefetch) Discard(prefetch);
        _prefetch = null;
    }

    /// <summary>Cancels what is still running and disposes a stream that was already opened, whenever it lands.</summary>
    private static void Discard(Prefetch prefetch)
    {
        prefetch.Cts.Cancel();
        prefetch.Opening?.ContinueWith(static t =>
        {
            if (t.IsCompletedSuccessfully) t.Result?.Dispose();
        }, TaskScheduler.Default);
    }

    private async Task LoadLyricsAsync(Track track, int generation, CancellationToken ct)
    {
        try
        {
            var response = await _api.GetLyricAsync(track.Id, ct);
            if (generation != _loadGeneration) return;
            Lyrics = LyricsParser.Parse(response);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.Warn($"lyrics {track.Id}: {error.Message}");
        }
    }

    private void Tick()
    {
        if (CurrentTrack is null || _player.Source is null) return;
        var session = _player.PlaybackSession;
        Position = session.Position;
        if (session.NaturalDuration > TimeSpan.Zero) Duration = session.NaturalDuration;
        var index = Lyrics.ActiveIndex(Position);
        if (index != ActiveLyricIndex) ActiveLyricIndex = index;
        if (IsPlaying && DateTime.UtcNow - _lastPositionSave > TimeSpan.FromSeconds(10)) SavePosition();
        if (IsPlaying && Duration > TimeSpan.Zero && Duration - Position < PreopenBeforeEnd)
        {
            if (_prefetch is null || _prefetch.IsStale) PrefetchNext();
            if (_prefetch is { Opening: null, Resolving.IsCompletedSuccessfully: true } prefetch && prefetch.Resolving.Result is { } media)
            {
                prefetch.Opening = OpenAheadAsync(media, prefetch.Cts.Token);
            }
        }
    }

    // MARK: 私人 FM

    /// <summary>Switches to 私人 FM, or resumes it when it is already the queue.</summary>
    public async Task StartFmAsync()
    {
        if (IsFm && CurrentTrack is not null)
        {
            if (!IsPlaying) TogglePlayPause();
            return;
        }
        IReadOnlyList<Track> tracks;
        try
        {
            tracks = await _api.GetPersonalFmAsync();
        }
        catch (Exception error)
        {
            _log.Warn($"fm: {error.Message}");
            StatusMessage = "私人 FM 获取失败：" + error.Message;
            return;
        }
        if (tracks.Count == 0)
        {
            StatusMessage = "私人 FM 暂时没有推荐";
            return;
        }
        IsFm = true;
        IsHeartMode = false;
        _sourceId = 0;
        Queue.SetShuffle(false);
        IsShuffled = false;
        Queue.Replace(tracks, 0);
        OnPropertyChanged(nameof(Queue));
        SaveQueue();
        await PlayCurrentAsync();
    }

    /// <summary>"不喜欢": tells NetEase, moves on, and drops the song from the queue.</summary>
    public async Task TrashFmAsync()
    {
        if (!IsFm || CurrentTrack is not { } track) return;
        try
        {
            await _api.TrashFmTrackAsync(track.Id);
        }
        catch (Exception error)
        {
            _log.Warn($"fm trash {track.Id}: {error.Message}");
        }
        var index = Queue.CurrentIndex;
        await NextAsync(manual: true);
        if (index >= 0 && index < Queue.CurrentIndex)
        {
            Queue.RemoveAt(index);
            OnPropertyChanged(nameof(Queue));
            SaveQueue();
            PrefetchNext();
        }
    }

    // MARK: 心动模式

    /// <summary>Starts 心动模式 from <paramref name="seed"/>: it plays first, NetEase's picks follow.</summary>
    public async Task StartHeartModeAsync(Track seed)
    {
        if (_account.Favorites is not { } favorites)
        {
            StatusMessage = "心动模式需要登录";
            return;
        }
        IReadOnlyList<Track> picks;
        try
        {
            picks = await _api.GetHeartModeListAsync(seed.Id, favorites.Id);
        }
        catch (Exception error)
        {
            _log.Warn($"heart mode {seed.Id}: {error.Message}");
            StatusMessage = "心动模式获取失败：" + error.Message;
            return;
        }
        var tracks = new List<Track> { seed };
        tracks.AddRange(picks.Where(t => t.Id != seed.Id));
        IsFm = false;
        IsHeartMode = true;
        _sourceId = favorites.Id;
        Queue.SetShuffle(false);
        IsShuffled = false;
        Queue.Replace(tracks, 0);
        OnPropertyChanged(nameof(Queue));
        SaveQueue();
        await PlayCurrentAsync();
    }

    /// <summary>
    /// Appends the next songs of the current mode (the FM station, or 心动模式 picks seeded by the song
    /// playing now); called when the current song is the last one.
    /// </summary>
    private async Task TopUpAsync()
    {
        if (_fetchingFm) return;
        _fetchingFm = true;
        var mode = IsFm ? "私人 FM" : "心动模式";
        try
        {
            IReadOnlyList<Track> tracks;
            if (IsFm)
            {
                tracks = await _api.GetPersonalFmAsync();
            }
            else if (IsHeartMode && CurrentTrack is { } seed && _account.Favorites is { } favorites)
            {
                tracks = await _api.GetHeartModeListAsync(seed.Id, favorites.Id);
            }
            else
            {
                return;
            }
            if (!IsFm && !IsHeartMode) return;
            var known = Queue.Items;
            var fresh = tracks.Where(t => known.All(q => q.Id != t.Id)).ToList();
            if (fresh.Count == 0) return;
            Queue.Append(fresh);
            OnPropertyChanged(nameof(Queue));
            SaveQueue();
            PrefetchNext();
        }
        catch (Exception error)
        {
            _log.Warn($"{mode}: {error.Message}");
            StatusMessage = $"{mode}获取失败：{error.Message}";
        }
        finally
        {
            _fetchingFm = false;
        }
    }

    // MARK: Persistence

    /// <summary>
    /// Puts the last session's queue back, paused on the song that was playing, with its position. The
    /// URL is only fetched when the user presses play, so start-up makes no network call for this.
    /// </summary>
    public void RestoreQueue()
    {
        try
        {
            var snapshot = _store.LoadQueue();
            if (snapshot is null || snapshot.Tracks.Count == 0) return;
            var position = _store.LoadPosition();
            Queue.Restore(snapshot.Tracks, snapshot.PlayOrder, position?.CurrentIndex ?? 0, snapshot.IsShuffled);
            Queue.RepeatMode = snapshot.RepeatMode;
            IsShuffled = Queue.IsShuffled;
            RepeatMode = Queue.RepeatMode;
            _sourceId = snapshot.SourceId;
            IsFm = snapshot.IsFm;
            IsHeartMode = snapshot.IsHeartMode;
            if (Queue.Current is not { } track) return;
            if (position is not null && position.TrackId != track.Id)
            {
                var byId = Queue.Items.ToList().FindIndex(t => t.Id == position.TrackId);
                if (byId >= 0 && Queue.MoveTo(byId)) track = Queue.Current!;
            }
            CurrentTrack = track;
            OnPropertyChanged(nameof(HasCurrentTrack));
            OnPropertyChanged(nameof(Queue));
            Duration = track.Duration;
            Position = position is { } saved && saved.TrackId == track.Id && saved.Position < track.Duration ? saved.Position : TimeSpan.Zero;
            UpdateTransportMetadata(track);
            _log.Info($"restored queue: {Queue.Count} songs, at {track.Id} {track.Name} {Position:mm\\:ss}");
        }
        catch (Exception error)
        {
            _log.Warn($"restore queue: {error.Message}");
        }
    }

    private void SaveQueue()
    {
        try
        {
            _store.SaveQueue(new PlaybackSnapshot(Queue.OrderedItems, Queue.PlayOrder, Queue.IsShuffled, Queue.RepeatMode, _sourceId, IsFm, IsHeartMode));
        }
        catch (Exception error)
        {
            _log.Warn($"save queue: {error.Message}");
        }
    }

    /// <summary>Written on every track change, every ten seconds while playing, and when the window closes.</summary>
    public void SavePosition()
    {
        if (CurrentTrack is not { } track) return;
        try
        {
            _store.SavePosition(new PlaybackPosition(track.Id, Queue.CurrentIndex, Position));
            _lastPositionSave = DateTime.UtcNow;
        }
        catch (Exception error)
        {
            _log.Warn($"save position: {error.Message}");
        }
    }

    private void ConfigureTransportControls()
    {
        var controls = _player.SystemMediaTransportControls;
        controls.IsEnabled = true;
        controls.IsPlayEnabled = true;
        controls.IsPauseEnabled = true;
        controls.IsNextEnabled = true;
        controls.IsPreviousEnabled = true;
        controls.ButtonPressed += (sender, e) => _dispatcher.TryEnqueue(() =>
        {
            switch (e.Button)
            {
                case SystemMediaTransportControlsButton.Play: _player.Play(); break;
                case SystemMediaTransportControlsButton.Pause: _player.Pause(); break;
                case SystemMediaTransportControlsButton.Next: _ = NextAsync(); break;
                case SystemMediaTransportControlsButton.Previous: _ = PreviousAsync(); break;
            }
        });
    }

    private void UpdateTransportMetadata(Track track)
    {
        var updater = _player.SystemMediaTransportControls.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = track.Name;
        updater.MusicProperties.Artist = track.ArtistNames;
        updater.MusicProperties.AlbumTitle = track.Album.Name;
        if (track.Album.PicUrl is { } pic && Uri.TryCreate(pic + "?param=300y300", UriKind.Absolute, out var uri))
        {
            updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(uri);
        }
        updater.Update();
    }
}
