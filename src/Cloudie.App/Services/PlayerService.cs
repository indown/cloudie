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

    public PlayerService(NeteaseApi api, UnblockService unblock, AppSettings settings, AccountService account, LogService log)
    {
        _api = api;
        _unblock = unblock;
        _settings = settings;
        _account = account;
        _log = log;
        Queue = new PlaybackQueue();
        _player.AudioCategory = MediaPlayerAudioCategory.Media;
        _player.CommandManager.IsEnabled = false;
        _player.Volume = settings.Volume;
        _player.MediaEnded += (_, _) => _dispatcher.TryEnqueue(() =>
        {
            ReportPlay(completed: true);
            _ = NextAsync(manual: false);
        });
        _player.MediaFailed += (sender, e) => _dispatcher.TryEnqueue(() =>
        {
            _log.Error($"播放失败: {e.ErrorMessage}");
            StatusMessage = "系统播放错误，已跳过";
            _ = NextAsync(manual: false);
        });
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
        _sourceId = sourceId;
        Queue.Replace(tracks, index);
        IsShuffled = Queue.IsShuffled;
        OnPropertyChanged(nameof(Queue));
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
        if (CurrentTrack is null) _ = PlayCurrentAsync();
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null) return;
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause();
        else _player.Play();
    }

    public Task NextAsync(bool manual = true)
    {
        if (!Queue.AdvanceToNext(manual))
        {
            _player.Pause();
            return Task.CompletedTask;
        }
        return PlayCurrentAsync();
    }

    public Task PreviousAsync()
    {
        if (Position > TimeSpan.FromSeconds(3))
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
    }

    private async Task PlayCurrentAsync()
    {
        var track = Queue.Current;
        if (track is null) return;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var generation = ++_loadGeneration;
        ReportPlay(completed: false);
        CurrentTrack = track;
        _playReported = false;
        OnPropertyChanged(nameof(HasCurrentTrack));
        Lyrics = ParsedLyrics.Empty;
        ActiveLyricIndex = -1;
        UnblockSource = null;
        ServedQuality = null;
        Position = TimeSpan.Zero;
        Duration = track.Duration;
        IsLoading = true;
        try
        {
            var resolved = await ResolveAsync(track, cts.Token);
            if (generation != _loadGeneration) return;
            if (resolved is null)
            {
                StatusMessage = $"《{track.Name}》没有可用的音源，已跳过";
                _log.Warn($"no source for {track.Id} {track.Name}");
                await NextAsync(manual: false);
                return;
            }
            UnblockSource = resolved.Value.Source;
            ServedQuality = resolved.Value.Quality;
            _player.Source = MediaSource.CreateFromUri(resolved.Value.Url);
            _player.Play();
            UpdateTransportMetadata(track);
            _log.Info($"now playing {track.Id} {track.Name} via {resolved.Value.Source ?? "netease"} ({resolved.Value.Quality})");
            _ = LoadLyricsAsync(track, generation, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (generation != _loadGeneration) return;
            _log.Error($"load {track.Id}: {error}");
            StatusMessage = $"加载失败：{error.Message}";
        }
        finally
        {
            if (generation == _loadGeneration) IsLoading = false;
        }
    }

    /// <summary>
    /// Sends the finished (or abandoned) track to NetEase's listening history, once per play. Nothing is
    /// reported when signed out, when the setting is off, or when the song was skipped within its first second.
    /// </summary>
    private void ReportPlay(bool completed)
    {
        if (_playReported || CurrentTrack is not { } track) return;
        _playReported = true;
        if (!_settings.Scrobble || !_account.IsLoggedIn) return;
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
    private async Task<(Uri Url, string? Source, string? Quality)?> ResolveAsync(Track track, CancellationToken ct)
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
            return (new Uri(url.Replace("http://", "https://")), null, data.Level);
        }
        if (!_settings.UnblockEnabled) return null;
        var resolved = await _unblock.ResolveAsync(UnblockTrack.From(track), quality, ct);
        if (resolved is null)
        {
            // A 30-second trial is still better than nothing.
            return data?.Url is { Length: > 0 } trial ? (new Uri(trial.Replace("http://", "https://")), null, "试听") : null;
        }
        return (resolved.Url, resolved.SourceName, resolved.Format);
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
        if (CurrentTrack is null) return;
        var session = _player.PlaybackSession;
        Position = session.Position;
        if (session.NaturalDuration > TimeSpan.Zero) Duration = session.NaturalDuration;
        var index = Lyrics.ActiveIndex(Position);
        if (index != ActiveLyricIndex) ActiveLyricIndex = index;
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
