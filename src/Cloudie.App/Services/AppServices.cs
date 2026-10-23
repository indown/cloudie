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
    public required UnblockService Unblock { get; init; }
    public required AccountService Account { get; init; }
    public required PlayerService Player { get; init; }
    public required LogService Log { get; init; }
    public required UpdateChecker Updates { get; init; }
    public required StatusService Status { get; init; }

    public static AppServices Create()
    {
        AppPaths.Ensure();
        var log = new LogService(AppPaths.LogFile);
        var settings = new AppSettings(new JsonFileSettingsStore(AppPaths.SettingsFile));
        var client = new NeteaseClient(new JsonFileCookieStore(AppPaths.CookiesFile)) { SpoofDomesticIp = settings.SpoofDomesticIp };
        var api = new NeteaseApi(client);
        var sources = new List<IAudioSource>
        {
            new PyncmdAudioSource(),
            new SubsonicAudioSource(() => settings.SubsonicConfiguration)
        };
        var unblock = new UnblockService(sources, () => settings.UnblockEnabled ? settings.EnabledSources : new HashSet<string>());
        unblock.SourceFailed += (source, error) => log.Warn($"音源 {source.DisplayName} 失败: {error.Message}");
        var account = new AccountService(api, log);
        var player = new PlayerService(api, unblock, settings, account, log, new PlaybackStore(AppPaths.QueueFile, AppPaths.PositionFile));
        return new AppServices
        {
            Settings = settings,
            Client = client,
            Api = api,
            Unblock = unblock,
            Account = account,
            Player = player,
            Log = log,
            Updates = new UpdateChecker(),
            Status = new StatusService()
        };
    }
}
