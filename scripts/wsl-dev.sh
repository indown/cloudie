#!/usr/bin/env bash
# Develop the WinUI app from WSL: the Windows .NET SDK compiles a copy of the tree on the Windows drive,
# the app runs on the Windows desktop, and screenshots come back as PNGs readable from WSL.
#
#   scripts/wsl-dev.sh build [Debug|Release]   sync the tree to $CLOUDIE_WIN_DIR and build x64
#   scripts/wsl-dev.sh run                     start the last Debug build (stops a running instance first)
#   scripts/wsl-dev.sh stop                    stop Cloudie.exe
#   scripts/wsl-dev.sh shot [name] [delayMs] [x,y,w,h]   capture the Cloudie window to /tmp/cloudie-shots/<name>.png (crop = 2x zoom)
#   scripts/wsl-dev.sh ui <args...>            drive the window: ui invoke "每日推荐" | ui click 100 200 | ui rclick 100 200 |
#                                              ui keys "^l" | ui type "text" | ui select ComboBox 1 "深色" | ui resize 1200 750 | ui tree
#   scripts/wsl-dev.sh log [n]                 tail the app log in %LOCALAPPDATA%\Cloudie
#   scripts/wsl-dev.sh stderr [n]              tail what the app wrote to stderr (crash traces)
#   scripts/wsl-dev.sh test                    run the Core tests with the Linux SDK (~/.dotnet)
#
# Requires: a Windows .NET 10 SDK (dotnet-install.ps1 -InstallDir %USERPROFILE%\.dotnet) and rsync.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WIN_USER="${CLOUDIE_WIN_USER:-$(cmd.exe /c 'echo %USERNAME%' 2>/dev/null | tr -d '\r')}"
WIN_HOME="/mnt/c/Users/$WIN_USER"
WIN_DIR="${CLOUDIE_WIN_DIR:-$WIN_HOME/work/cloudie}"
DOTNET_WIN="${CLOUDIE_DOTNET_WIN:-C:\\Users\\$WIN_USER\\.dotnet}"
CONFIG="${2:-Debug}"
OUT="$WIN_DIR/src/Cloudie.App/bin/x64/$CONFIG/net10.0-windows10.0.26100.0/win-x64"
SHOTS=/tmp/cloudie-shots

win_path() { wslpath -w "$1"; }

# Runs a command through cmd.exe with the user-local SDK first on PATH; cwd must be on a Windows drive.
win_dotnet() {
  (cd "$WIN_DIR" && cmd.exe /c "set DOTNET_ROOT=$DOTNET_WIN&& set PATH=$DOTNET_WIN;%PATH%&& set DOTNET_CLI_TELEMETRY_OPTOUT=1&& set DOTNET_NOLOGO=1&& dotnet $*" 2>&1 | tr -d '\r')
}

cmd_build() {
  cmd_stop   # a running instance locks Cloudie.exe, and the copy step would fail after ten retries
  mkdir -p "$WIN_DIR"
  rsync -a --delete --exclude .git --exclude bin --exclude obj --exclude out "$REPO/" "$WIN_DIR/"
  win_dotnet build "src\\Cloudie.App\\Cloudie.App.csproj" -c "$CONFIG" -r win-x64 -p:Platform=x64 -v minimal -nowarn:PRI257
}

cmd_stop() {
  (cd "$WIN_HOME" && taskkill.exe /IM Cloudie.exe /F >/dev/null 2>&1) || true
}

cmd_run() {
  cmd_stop
  [ -f "$OUT/Cloudie.exe" ] || { echo "no build at $OUT; run: $0 build" >&2; exit 1; }
  mkdir -p "$SHOTS"
  # Launched straight through WSL interop, detached from this shell. stdout/stderr land in files on the Linux side,
  # so a managed stack overflow (which .NET prints before aborting) is kept: see `stderr`. PowerShell's
  # Start-Process with redirection never returned under interop, and cmd's `start` hung.
  (cd "$OUT" && setsid nohup "$OUT/Cloudie.exe" >"$SHOTS/stdout.txt" 2>"$SHOTS/stderr.txt" </dev/null &)
  echo "started $OUT/Cloudie.exe"
}

cmd_shot() {
  local name="${2:-shot-$(date +%H%M%S)}"
  mkdir -p "$SHOTS"
  local target
  target="$(win_path "$SHOTS/$name.png")"
  (cd "$WIN_HOME" && powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$(win_path "$REPO/scripts/win-screenshot.ps1")" -Process Cloudie -Path "$target" -DelayMs "${3:-0}" -Crop "${4:-}" 2>&1 | tr -d '\r')
  echo "$SHOTS/$name.png"
}

cmd_ui() {
  local action="$2"; shift 2
  local args=()
  case "$action" in
    invoke) args=(-Name "$1") ;;
    click|rclick|dblclick|resize|hover) args=(-X "$1" -Y "$2") ;;
    scroll|drag) args=(-X "$1" -Y "$2" -Value "$3") ;;
    select) args=(-Type "$1" -Index "$2" -Value "$3") ;;
    keys|type) args=(-Keys "$1") ;;
    tree) args=(-Depth "${1:-8}") ;;
  esac
  (cd "$WIN_HOME" && powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$(win_path "$REPO/scripts/win-ui.ps1")" -Process Cloudie -Action "$action" "${args[@]}" 2>&1 | tr -d '\r')
}

cmd_stderr() {
  tail -n "${2:-60}" "$SHOTS/stderr.txt" 2>/dev/null
}

cmd_log() {
  tail -n "${2:-40}" "$WIN_HOME/AppData/Local/Cloudie/cloudie.log"
}

cmd_test() {
  export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
  (cd "$REPO" && dotnet test tests/Cloudie.Core.Tests --nologo -v quiet)
}

case "${1:-}" in
  build) cmd_build ;;
  run) cmd_run ;;
  stop) cmd_stop ;;
  shot) cmd_shot "$@" ;;
  ui) cmd_ui "$@" ;;
  log) cmd_log "$@" ;;
  stderr) cmd_stderr "$@" ;;
  test) cmd_test ;;
  *) sed -n '2,12p' "$0"; exit 1 ;;
esac
