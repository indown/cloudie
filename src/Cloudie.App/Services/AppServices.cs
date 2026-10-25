using Cloudie.Core.Library;
using Cloudie.Core.Netease;
using Cloudie.Core.Playback;
using Cloudie.Core.Settings;
using Cloudie.Core.Unblock;
using Cloudie.Core.Updates;

namespace Cloudie.App.Services;

/// <summary>Wires the core together once; everything in the app reaches shared state through here.</summary>
public sealed class AppServices
{
    public required AppSettings Settings { get; init; }
    public required NeteaseClient Client { get; init; }
    public required NeteaseApi Api { get; init; }
    /// <summary>音源账号: the second account that only fetches song URLs; its client is <c>SourceAccount.Api.Client</c>.</summary>
    public required SourceAccount SourceAccount { get; init; }
    public required UnblockService Unblock { get; init; }
    public required AccountService Account { get; init; }
    public required PlayerService Player { get; init; }
    public required LogService Log { get; init; }
    public required UpdateChecker Updates { get; init; }
    public required StatusService Status { get; init; }
    public required AudioCache AudioCache { get; init; }
    /// <summary>List pages as last shown; the account service keeps the user's own playlists in it up to date.</summary>
    public required TrackListCache Lists { get; init; }
    /// <summary>Set when the configured cache folder was unusable at start-up and the default is in use for this session.</summary>
    public string? AudioCacheNotice { get; init; }

    public static AppServices Create()
    {
        AppPaths.Ensure();
        var log = new LogService(AppPaths.LogFile);
        var settings = new AppSettings(new JsonFileSettingsStore(AppPaths.SettingsFile));
        var client = new NeteaseClient(new JsonFileCookieStore(AppPaths.CookiesFile)) { SpoofDomesticIp = settings.SpoofDomesticIp };
        var api = new NeteaseApi(client);
        // 音源账号 lives in its own cookie file and only ever answers song-URL lookups (see SongUrlResolver).
        var sourceClient = new NeteaseClient(new JsonFileCookieStore(AppPaths.SourceCookiesFile)) { SpoofDomesticIp = settings.SpoofDomesticIp };
        var sourceAccount = new SourceAccount(new NeteaseApi(sourceClient), () => settings.SourceAccountEnabled);
        var songUrls = new SongUrlResolver(api, sourceAccount);
        songUrls.LookupFailed += (id, viaSource, error) => log.Warn($"song url {id}{(viaSource ? " (音源账号)" : "")}: {error.Message}");
        var sources = new List<IAudioSource>
        {
            new PyncmdAudioSource(),
            new SubsonicAudioSource(() => settings.SubsonicConfiguration)
        };
        var unblock = new UnblockService(sources, () => settings.UnblockEnabled ? settings.EnabledSources : new HashSet<string>());
        unblock.SourceFailed += (source, error) => log.Warn($"音源 {source.DisplayName} 失败: {error.Message}");
        var lists = new TrackListCache(AppPaths.ListsDirectory);
        var account = new AccountService(api, log, lists) { SourceVipType = () => sourceAccount.IsUsable ? sourceAccount.VipType : 0 };
        // A moved cache lives where the user put it, unless that place is gone (unplugged drive, locked volume).
        var cacheDirectory = AppPaths.CacheDirectory;
        string? cacheNotice = null;
        if (settings.AudioCacheDirectory is { } chosen)
        {
            if (AudioCache.IsUsableDirectory(chosen)) cacheDirectory = chosen;
            else
            {
                cacheNotice = $"歌曲缓存目录不可用，本次使用默认位置：{chosen}";
                log.Warn($"cache directory unusable, falling back to default: {chosen}");
            }
        }
        var audioCache = new AudioCache(cacheDirectory, () => settings.AudioCacheLimitBytes);
        var player = new PlayerService(api, songUrls, unblock, settings, account, log, new PlaybackStore(AppPaths.QueueFile, AppPaths.PositionFile), audioCache);
        return new AppServices
        {
            Settings = settings,
            Client = client,
            Api = api,
            SourceAccount = sourceAccount,
            Unblock = unblock,
            Account = account,
            Player = player,
            Log = log,
            Updates = new UpdateChecker(),
            Status = new StatusService(),
            AudioCache = audioCache,
            Lists = lists,
            AudioCacheNotice = cacheNotice
        };
    }
}
