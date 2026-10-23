# Cloudie

[![build](https://github.com/indown/cloudie/actions/workflows/build.yml/badge.svg)](https://github.com/indown/cloudie/actions/workflows/build.yml)

一个 Windows 原生（WinUI 3）的网易云音乐第三方客户端：登录账号听自己的歌单，灰色歌曲自动从其他音源补上，海外也能直接播放。

> 仅供学习交流使用。音乐版权归网易云音乐及各音源平台所有，请支持正版。

## 功能（初版）

- 扫码登录，账号歌单、每日推荐、推荐歌单、排行榜
- 歌单 / 专辑 / 歌手页，搜索（歌曲、专辑、歌手、歌单）
- 播放队列、随机 / 循环、系统媒体控制（SMTC）
- 逐行同步歌词（解析 yrc 逐字时间轴，供后续卡拉 OK 高亮）、翻译
- 沉浸播放页：封面取色渐变背景，封面在左、歌词在右
- 灰色歌曲解锁：pyncmd（网易原始文件）+ Navidrome / Subsonic 自建曲库，歌名精确匹配并以歌手或时长校验
- 海外解锁：向网易接口附带国内 `X-Real-IP`，海外账号可直接播放锁区歌曲
- 设置：音质、主题、解锁开关与音源、Navidrome 账号

## 结构

| 目录 | 说明 |
| --- | --- |
| `src/Cloudie.Core` | 纯 .NET 10 类库：网易 weapi / eapi 加密与接口、模型、歌词解析、灰歌解锁、设置抽象、播放队列。可在任何平台编译和测试 |
| `src/Cloudie.App` | WinUI 3（Windows App SDK 2.x）应用：界面、播放、系统集成。只能在 Windows 上编译 |
| `tests/Cloudie.Core.Tests` | xUnit 单元测试，含与 UnblockNeteaseMusic 实现比对过的加密向量 |

## 构建

### Core 与测试（任意平台）

```bash
dotnet test tests/Cloudie.Core.Tests
```

### 应用（Windows）

需要 .NET 10 SDK；用 Visual Studio 2026（或 2022 17.12+）打开 `Cloudie.slnx` 的话请安装 **Windows App SDK / WinUI** 工作负载。

```powershell
# 非打包、自包含，产物在 out/ 下，解压即可运行
dotnet publish src/Cloudie.App/Cloudie.App.csproj -c Release -r win-x64 -p:Platform=x64 -o out/Cloudie-win-x64

# 需要 MSIX 包时
dotnet build src/Cloudie.App -c Release -p:Platform=x64 -p:Packaged=true
```

GitHub Actions 会在每次推送后编译 x64 与 ARM64 的 zip（Actions 页面的 Artifacts），打 `v*` 标签时自动发布到 Releases。

数据目录：`%LOCALAPPDATA%\Cloudie\`（`settings.json`、`cookies.json`、`cloudie.log`）。

## 设置项

| 键 | 说明 |
| --- | --- |
| `playback.audioQuality` | 请求音质：Standard / ExHigh / Lossless / HiRes |
| `network.spoofDomesticIp` | 海外解锁，默认开 |
| `unblock.enabled` / `unblock.sources` | 灰歌解锁总开关与启用的音源（`pyncmd,navidrome`） |
| `unblock.navidrome.url` / `user` / `password` | Navidrome 服务器 |


## 许可与致谢

Cloudie 以 [MIT](LICENSE) 发布。代码为独立实现，没有包含下列项目的代码；它们是设计与资料上的参照，在此致谢：

- [Kumone](https://github.com/missuo/kumone)（LGPL-3.0）：功能范围与界面风格的参照
- [UnblockNeteaseMusic/server](https://github.com/UnblockNeteaseMusic/server)（LGPL-3.0）：灰歌音源与匹配思路；pyncmd 指 [GD Studio 音乐 API](https://music-api.gdstudio.xyz/) 这一公开接口
- [NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi)（MIT）：网易接口与加密方式的资料
- [YesPlayMusic](https://github.com/qier222/YesPlayMusic)（MIT）：歌曲可播放性的判断规则
