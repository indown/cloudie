# Changelog — Cloudie

## 0.1.0 - Unreleased

### Added

- Core: NetEase weapi/eapi encryption, client with cookie jar and overseas `X-Real-IP`, typed API (login QR, account,
  playlists, daily songs, recommendations, toplists, playlist/album/artist details, song URLs, lyrics, search).
- Core: LRC/YRC lyrics parser with translations, gray-track unlock (pyncmd, Navidrome/Subsonic) with strict matching,
  settings model, playback queue with shuffle/repeat; 31 unit tests.
- App: WinUI 3 shell with sidebar (推荐 / 每日推荐 / 歌单), title-bar search, player bar, immersive now-playing page
  with artwork-tinted backdrop and synced lyrics, home / track-list / search / settings pages, QR login dialog,
  system media transport controls.
- App: listening-history reporting (scrobble) with a setting, title-bar account menu (QR login / logout), search
  suggestions, artist pages list the artist's albums, "view artist / view album" context actions, keyboard
  shortcuts (Space, Ctrl+←/→, Ctrl+L, Ctrl+F, Esc), update check against GitHub Releases with a status-bar offer.
- Build: GitHub Actions compiles and publishes unpackaged x64/ARM64 zips; the version is stamped from the tag
  (`0.1.0-ci.N` for other builds) and shown in Settings → 关于.
- App: 私人 FM page (start, dislike, like, play/pause, next; the queue refills itself), queue and position
  restored at start-up (paused, URL fetched on play), playlist subscribe / unsubscribe, "添加到歌单" submenu and
  "从歌单中移除" on own playlists, window size / position / maximized state remembered with a 960×600 minimum.
- Core: personal FM and playlist endpoints, `PlaybackStore` (queue.json + playback.json), `PlaybackQueue.Restore`,
  `WindowPlacement` setting with display fitting; 63 unit tests.
