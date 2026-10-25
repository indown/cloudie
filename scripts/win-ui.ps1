# Drives the Cloudie window through UI Automation (used from WSL by scripts/wsl-dev.sh ui ...).
#   -Action invoke -Name "每日推荐"      invoke / select / click the first element with that name
#   -Action click -X 100 -Y 200          click at window-relative physical pixels (as in the screenshots)
#   -Action keys -Keys "^l"              SendKeys to the window (^ Ctrl, % Alt, + Shift, {ENTER}, {ESC}, ...)
#   -Action type -Keys "周杰伦"          type literal text (no SendKeys escaping)
#   -Action rclick -X 100 -Y 200         right-click at window-relative physical pixels
#   -Action dblclick -X 100 -Y 200       double-click there
#   -Action drag -X 100 -Y 200 -Value "100,900"   press at X,Y, move to the end point, release (scrollbar scrubbing)
#   -Action hover -X 100 -Y 200          move the pointer there without clicking (hover states)
#   -Action select -Type ComboBox -Index 1 -Value "深色"   open the nth control of that type and pick an item
#   -Action resize -X 1200 -Y 750        resize the window to physical pixels (position unchanged)
#   -Action scroll -X 100 -Y 200 -Value -5   mouse-wheel at a window-relative point (negative = down, in notches)
#   -Action sscroll -X 100 -Y 200 -Value -5  the same with Shift held (sideways scrolling of a strip)
#   -Action tree [-Depth 6]              print the automation tree (control type, name, class)
param(
    [string]$Process = "Cloudie",
    [Parameter(Mandatory = $true)][string]$Action,
    [string]$Name,
    [int]$X, [int]$Y,
    [string]$Keys,
    [string]$Type,
    [int]$Index = 0,
    [string]$Value,
    [int]$Depth = 8
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
}
"@
[void][Native]::SetProcessDpiAwareness(2)
$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw "no window for process '$Process'" }
$hwnd = $proc.MainWindowHandle
# A background process is usually refused SetForegroundWindow; a topmost / not-topmost flip still puts the window on top.
[void][Native]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0043)   # HWND_TOPMOST, NOSIZE|NOMOVE|SHOWWINDOW
[void][Native]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0043)   # HWND_NOTOPMOST
[void][Native]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds 150
$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
# A background process may be refused SetForegroundWindow; UIA focus gets keyboard input to the app anyway.
if ($Action -ne "tree") { try { $root.SetFocus(); Start-Sleep -Milliseconds 150 } catch {} }

function Click-Point([int]$sx, [int]$sy, [bool]$right = $false) {
    [void][Native]::SetCursorPos($sx, $sy)
    Start-Sleep -Milliseconds 60
    if ($right) {
        [Native]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)   # right down
        [Native]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)   # right up
    } else {
        [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # left down
        [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # left up
    }
}

function Window-Rect() {
    $rect = New-Object Native+RECT
    [void][Native]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, [System.Runtime.InteropServices.Marshal]::SizeOf($rect))
    return $rect
}

function Write-Tree($element, $depth, $indent) {
    if ($depth -lt 0) { return }
    $type = $element.Current.ControlType.ProgrammaticName -replace "ControlType\.", ""
    $name = $element.Current.Name
    $class = $element.Current.ClassName
    if ($name -or $type -notin @("Pane", "Custom", "Group")) { Write-Output ("{0}{1} '{2}' [{3}]" -f $indent, $type, $name, $class) }
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($element)
    while ($child) {
        Write-Tree $child ($depth - 1) ($indent + "  ")
        $child = $walker.GetNextSibling($child)
    }
}

switch ($Action) {
    "tree" { Write-Tree $root $Depth "" }
    "invoke" {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (-not $element) { throw "no element named '$Name'" }
        $pattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); Write-Output "invoked '$Name'"; break }
        if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select(); Write-Output "selected '$Name'"; break }
        $rect = $element.Current.BoundingRectangle
        Click-Point ([int]($rect.Left + $rect.Width / 2)) ([int]($rect.Top + $rect.Height / 2))
        Write-Output "clicked '$Name' at $([int]$rect.Left),$([int]$rect.Top) $([int]$rect.Width)x$([int]$rect.Height)"
    }
    "click" {
        $rect = Window-Rect
        Click-Point ($rect.Left + $X) ($rect.Top + $Y)
        Write-Output "clicked $X,$Y"
    }
    { $_ -in "scroll", "sscroll" } {
        $rect = Window-Rect
        [void][Native]::SetCursorPos($rect.Left + $X, $rect.Top + $Y)
        Start-Sleep -Milliseconds 60
        $delta = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]([int]$Value * 120)), 0)   # negative notches as DWORD
        $shift = $Action -eq "sscroll"
        if ($shift) { [Native]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }   # VK_SHIFT down
        [Native]::mouse_event(0x0800, 0, 0, $delta, [UIntPtr]::Zero)
        if ($shift) { Start-Sleep -Milliseconds 30; [Native]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero) }   # KEYEVENTF_KEYUP
        Write-Output "$(if ($shift) { 'shift-' })scrolled $Value at $X,$Y"
    }
    "hover" {
        $rect = Window-Rect
        # SetCursorPos alone does not reach WinUI's pointer input, and enter/leave needs a path that crosses the
        # element's edge: start 48 px up-left and walk in with injected relative moves.
        [void][Native]::SetCursorPos($rect.Left + $X - 48, $rect.Top + $Y - 48)
        Start-Sleep -Milliseconds 40
        for ($i = 0; $i -lt 8; $i++) {
            [Native]::mouse_event(0x0001, 6, 6, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 15
        }
        Start-Sleep -Milliseconds 200
        Write-Output "pointer at $X,$Y"
    }
    "drag" {
        # -X/-Y start, -Value "x2,y2" end: press, move in 24 steps, release.
        $rect = Window-Rect
        $end = $Value.Split(",") | ForEach-Object { [int]$_ }
        # Injected absolute moves (0..65535 across the primary display): SetCursorPos while the button is held
        # does not count as pointer movement for WinUI.
        $sw = [Native]::GetSystemMetrics(0); $sh = [Native]::GetSystemMetrics(1)
        $ax = { param($px) [int](($rect.Left + $px) * 65535 / $sw) }
        $ay = { param($py) [int](($rect.Top + $py) * 65535 / $sh) }
        [Native]::mouse_event(0x8001, (& $ax $X), (& $ay $Y), 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 120
        [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        for ($i = 1; $i -le 24; $i++) {
            $px = [int]($X + ($end[0] - $X) * $i / 24); $py = [int]($Y + ($end[1] - $Y) * $i / 24)
            [Native]::mouse_event(0x8001, (& $ax $px), (& $ay $py), 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 25
        }
        Start-Sleep -Milliseconds 80
        [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Write-Output "dragged $X,$Y -> $($end[0]),$($end[1])"
    }
    "dblclick" {
        $rect = Window-Rect
        Click-Point ($rect.Left + $X) ($rect.Top + $Y)
        Start-Sleep -Milliseconds 80
        Click-Point ($rect.Left + $X) ($rect.Top + $Y)
        Write-Output "double-clicked $X,$Y"
    }
    "rclick" {
        $rect = Window-Rect
        Click-Point ($rect.Left + $X) ($rect.Top + $Y) $true
        Write-Output "right-clicked $X,$Y"
    }
    "select" {
        $typeField = [System.Windows.Automation.ControlType].GetField($Type)
        if (-not $typeField) { throw "unknown control type '$Type'" }
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $typeField.GetValue($null))
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($Index -ge $all.Count) { throw "only $($all.Count) $Type elements" }
        $box = $all.Item($Index)
        $pattern = $null
        if ($box.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) { $pattern.Expand(); Start-Sleep -Milliseconds 400 }
        $itemCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Value)
        $item = $box.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
        if (-not $item) { $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition) }
        if (-not $item) { throw "no item named '$Value'" }
        $select = $null
        if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$select)) { $select.Select() }
        else { $r = $item.Current.BoundingRectangle; Click-Point ([int]($r.Left + $r.Width / 2)) ([int]($r.Top + $r.Height / 2)) }
        Write-Output "selected '$Value' in $Type #$Index"
    }
    "resize" {
        $rect = New-Object Native+RECT
        [void][Native]::GetWindowRect($hwnd, [ref]$rect)
        [void][Native]::MoveWindow($hwnd, $rect.Left, $rect.Top, $X, $Y, $true)
        Start-Sleep -Milliseconds 300
        $after = Window-Rect
        Write-Output "resized to $($after.Right - $after.Left)x$($after.Bottom - $after.Top)"
    }
    "keys" { [System.Windows.Forms.SendKeys]::SendWait($Keys); Write-Output "sent $Keys" }
    "type" {
        $escaped = $Keys -replace '([+^%~(){}\[\]])', '{$1}'
        [System.Windows.Forms.SendKeys]::SendWait($escaped); Write-Output "typed $Keys"
    }
    default { throw "unknown action '$Action'" }
}
