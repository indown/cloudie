# AGENTS.md — Cloudie

Notes for anyone (human or agent) continuing this code base.

## Layout and build

- `src/Cloudie.Core` targets plain `net10.0`; build and test it anywhere with `dotnet test tests/Cloudie.Core.Tests`.
  On macOS the SDK lives in `~/.dotnet` (installed with Microsoft's `dotnet-install.sh`), so prefix commands with
  `export PATH="$HOME/.dotnet:$PATH"`.
- `src/Cloudie.App` is WinUI 3 (`net10.0-windows10.0.26100.0`, Windows App SDK 2.x, MSIX). The XAML compiler
  and the packaging targets only run on Windows; `dotnet build` on macOS stops at restore / XAML, so UI changes are
  compiled by the person on the Windows machine. Keep UI code thin and push logic into Core, where it is testable.
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
- Settings live in `ApplicationData.Current.LocalSettings` through `LocalSettingsStore`; cookies in `cookies.json`
  under `LocalState`.

## Conventions

- Conventional commits (`feat(player): …`, `fix(lyrics): …`), one topic per commit.
- Chinese UI strings inline for now; localization is a later task.
- Add a test in `tests/` for anything in Core; UI-only changes are verified by running the app.
