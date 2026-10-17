# Posts ZyDock automation messages (see ZYDOCK_DEBUG_INPUT in DockWindow).
# Usage:
#   powershell -File tools\dock-debug.ps1 -Hover 184
#   powershell -File tools\dock-debug.ps1 -ClickX 352 -ClickY 96
#   powershell -File tools\dock-debug.ps1 -MenuX 38 -MenuY 100
param(
    [int]$Hover = -1,
    [int]$ClickX = -1,
    [int]$ClickY = -1,
    [int]$MenuX = -1,
    [int]$MenuY = -1
)

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class DockDebug {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@

[DockDebug]::SetProcessDPIAware() | Out-Null

$proc = Get-Process -Name ZyDock -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "ZyDock is not running" }

$script:pidTarget = $proc.Id
$script:hwnd = [IntPtr]::Zero
$callback = [DockDebug+EnumProc]{
    param($h, $l)
    $p = 0
    [DockDebug]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
    if ($p -eq $script:pidTarget -and [DockDebug]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 128
        [DockDebug]::GetWindowTextW($h, $sb, 128) | Out-Null
        if ($sb.ToString() -eq 'ZyDock') { $script:hwnd = $h; return $false }
    }
    return $true
}
[DockDebug]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

if ($script:hwnd -eq [IntPtr]::Zero) { throw "Dock window not found" }

if ($Hover -ge 0) {
    $msg = [DockDebug]::RegisterWindowMessageW("ZyDock.DebugHover")
    [DockDebug]::PostMessage($script:hwnd, $msg, [IntPtr]$Hover, [IntPtr]::Zero) | Out-Null
    Write-Output "hover -> $Hover (msg=$msg hwnd=$($script:hwnd))"
}

if ($ClickX -ge 0) {
    $msg = [DockDebug]::RegisterWindowMessageW("ZyDock.DebugClick")
    [DockDebug]::PostMessage($script:hwnd, $msg, [IntPtr]$ClickX, [IntPtr]$ClickY) | Out-Null
    Write-Output "click -> $ClickX,$ClickY (msg=$msg)"
}

if ($MenuX -ge 0) {
    $msg = [DockDebug]::RegisterWindowMessageW("ZyDock.DebugMenu")
    [DockDebug]::PostMessage($script:hwnd, $msg, [IntPtr]$MenuX, [IntPtr]$MenuY) | Out-Null
    Write-Output "menu -> $MenuX,$MenuY (msg=$msg)"
}
