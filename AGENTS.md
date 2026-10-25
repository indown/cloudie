# AGENTS.md — Cloudie

Notes for anyone (human or agent) continuing this code base.

## Layout and build

- `src/Cloudie.Core` targets plain `net10.0`; build and test it anywhere with `dotnet test tests/Cloudie.Core.Tests`.
  On macOS the SDK lives in `~/.dotnet` (installed with Microsoft's `dotnet-install.sh`), so prefix commands with
  `export PATH="$HOME/.dotnet:$PATH"`.
- `src/Cloudie.App` is WinUI 3 (`net10.0-windows10.0.26100.0`, Windows App SDK 2.x, MSIX). The XAML compiler
  and the packaging targets only run on Windows; `dotnet build` on macOS stops at restore / XAML, so UI changes are
  compiled by the person on the Windows machine. Keep UI code thin and push logic into Core, where it is testable.
- From WSL on a Windows machine, `scripts/wsl-dev.sh` does the whole loop without Visual Studio: `build` rsyncs the
  tree to `%USERPROFILE%\work\cloudie` and compiles it with a user-local Windows .NET 10 SDK
  (`%USERPROFILE%\.dotnet`, installed with `dotnet-install.ps1`; a UNC `\\wsl.localhost` path would not build),
  `run` starts the Debug exe on the desktop, `shot <name> [delayMs]` captures the window to
  `/tmp/cloudie-shots/<name>.png`, `ui invoke|click|keys|type|tree` drives it through UI Automation (icon buttons
  carry `AutomationProperties.Name`, so `ui invoke "下一首"` works), `log` tails the app log, `test` runs the Core
  tests with the Linux SDK in `~/.dotnet`. `run` kills a running Cloudie first, so the developer's own instance
  goes down with it. Environment variables only reach the Windows process through `WSLENV`; to make every
  NetEase call fail (error bars, retry buttons): `WSLENV=HTTPS_PROXY:HTTP_PROXY HTTPS_PROXY=http://127.0.0.1:9
  HTTP_PROXY=http://127.0.0.1:9 scripts/wsl-dev.sh run`.
- Design reference: Kumone (https://github.com/missuo/kumone, LGPL-3.0). Feature scope and visual style follow it, but
  no code is copied from it: Cloudie is MIT, so keep it that way — reimplement from the NetEase API behaviour, never port.

## Facts worth knowing

- Encryption: `NeteaseCrypto` is verified against UnblockNeteaseMusic's `crypto.js` (eapi hex vector, weapi params
  with a fixed key, `encSecKey` against a BigInt computation in node). The weapi secret key is fixed on purpose.
- eapi requests go to `interface.music.163.com` with the desktop header dict (`os=pc`, `appver=3.1.17`); weapi to
  `music.163.com`. `X-Real-IP: 118.88.88.88` is added when `AppSettings.SpoofDomesticIp` is on (default).
- Gray tracks: `PlayerService.ResolveAsync` asks NetEase first; a missing URL or a trial falls through to
  `UnblockService` (pyncmd → Navidrome). The Navidrome matcher requires an exact normalized title plus artist or
  duration confirmation; never "first result".
- Some Subsonic-compatible proxies hang on suffix Range requests (`bytes=-N`); Windows Media Foundation uses
  explicit ranges, but keep this in mind if playback from a Navidrome source stalls.
- WinUI compile gotchas met so far: `x:DataType` cannot name a nested type; a `Window` cannot use a converter inside
  `x:Bind` (microsoft-ui-xaml #4966), so expose a `Visibility` property instead; a lambda whose single parameter is
  named `_` turns `_ = SomeAsync()` into an assignment to that parameter (name it `sender`). Compile errors from CI
  are listed in the job summary and in the check-run annotations (packed, since GitHub shows ten per step).
- 歌曲缓存 (`AudioCache`, Core) stores whole files as `cache\{trackId}-{quality}.cache` plus `index.json`, keyed by the
  quality the user asked for. The folder defaults to `%LOCALAPPDATA%\Cloudie\cache` and can be moved
  (`AudioCache.Relocate`; `cache.audio.directory` in settings); `AppServices` falls back to the default when the
  configured folder fails `AudioCache.IsUsableDirectory` at start-up. Playback opens them with `MediaSource.CreateFromStream` and a MIME type from
  `AudioFormat` (magic bytes first: CDNs answer `application/octet-stream`); the file is opened with
  `FileShare.Delete` so eviction can remove a song while it plays. Fill happens from `PlayerService.Tick` after
  `AudioCache.ShouldStartCaching`, one download at a time, never on a metered connection or for a trial. Each entry
  records the VIP tier at hand when it was fetched: a copy that fell short of the requested level (a non-VIP
  account gets a downgrade, not a refusal) or came from a gray-track source is looked up again once a higher tier
  is available (`AudioCache.IsUpgradable`, a 5 s lookup, the cached file plays if nothing better comes) and
  replaced when NetEase now answers better (`AudioCache.IsBetter`, `DownloadAsync(replace: true)`); a lookup that
  finds nothing better is remembered with `Confirm` so it is not repeated.
- 音源账号 (`SourceAccount` + `SongUrlResolver`, Core): a second NetEase session in `cookies-source.json` that
  only answers `song/enhance/player/url/v1`; playlists, recommendations, likes and scrobbles stay with the main
  account. The resolver asks it *first* (a non-VIP account answers a lossless request with a quietly downgraded
  file, not an error, so the VIP account cannot be a fallback), asks the main account first for 云盘 songs, and
  retires it on 301 until the user scans again. `AccountService.PlaybackVipType` is the higher tier of the two,
  so VIP-only songs are not marked unplayable in lists.
- Settings, cookies and the log live in `%LOCALAPPDATA%\Cloudie` (`AppPaths`), not in ApplicationData, so the app
  runs unpackaged; `.github/workflows/build.yml` publishes unpackaged self-contained zips for x64 and ARM64 on every
  push and is the quickest way to compile the WinUI project without a Windows machine.

## Conventions

- Conventional commits (`feat(player): …`, `fix(lyrics): …`), one topic per commit.
- Chinese UI strings inline for now; localization is a later task.
- The icon (gold cloud with a cut-out eighth note on a dark tile) is generated, not hand-edited: change
  `scripts/make-icons.cs` and run `dotnet run scripts/make-icons.cs` (add `--preview` for a small-size contact
  sheet) to refresh every PNG, `Cloudie.ico` and `Logo.svg` under `src/Cloudie.App/Assets`.
- Add a test in `tests/` for anything in Core; UI-only changes are verified by running the app.
