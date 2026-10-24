# Captures the main window of a process to a PNG (used by scripts/wsl-dev.sh shot).
param(
    [string]$Process = "Cloudie",
    [Parameter(Mandatory = $true)][string]$Path,
    # Extra wait after the window is found, for pages that are still loading.
    [int]$DelayMs = 0,
    # "x,y,w,h" in window-relative physical pixels: save only that part, scaled up 2x for inspection.
    [string]$Crop = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
}
"@
[void][Win32]::SetProcessDpiAwareness(2)
$deadline = (Get-Date).AddSeconds(15)
do {
    $proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($proc) { break }
    Start-Sleep -Milliseconds 300
} while ((Get-Date) -lt $deadline)
if (-not $proc) { throw "no window for process '$Process'" }
$hwnd = $proc.MainWindowHandle
if ([Win32]::IsIconic($hwnd)) { [void][Win32]::ShowWindow($hwnd, 9) }
# A background process is usually refused SetForegroundWindow; a topmost / not-topmost flip still puts the window on top.
[void][Win32]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0043)   # HWND_TOPMOST, NOSIZE|NOMOVE|SHOWWINDOW
[void][Win32]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0043)   # HWND_NOTOPMOST
[void][Win32]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds (400 + $DelayMs)
$rect = New-Object Win32+RECT
# DWMWA_EXTENDED_FRAME_BOUNDS (9): the visible frame without the invisible resize border.
[void][Win32]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, [System.Runtime.InteropServices.Marshal]::SizeOf($rect))
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "window has no size" }
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
if ($Crop) {
    $c = $Crop.Split(",") | ForEach-Object { [int]$_ }
    $part = $bitmap.Clone((New-Object System.Drawing.Rectangle $c[0], $c[1], $c[2], $c[3]), $bitmap.PixelFormat)
    $zoom = New-Object System.Drawing.Bitmap ($c[2] * 2), ($c[3] * 2)
    $g2 = [System.Drawing.Graphics]::FromImage($zoom)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g2.DrawImage($part, 0, 0, $c[2] * 2, $c[3] * 2)
    $zoom.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g2.Dispose(); $zoom.Dispose(); $part.Dispose()
    $width = $c[2] * 2; $height = $c[3] * 2
} else {
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}
$graphics.Dispose(); $bitmap.Dispose()
Write-Output "$Path ${width}x${height}"
