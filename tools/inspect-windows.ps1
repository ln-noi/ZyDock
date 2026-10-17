# Lists top level windows (with class, title and rect) for a process id or all windows.
# Usage: powershell -File tools\inspect-windows.ps1 [-ProcessId 1234]
param(
    [int]$ProcessId = 0,
    [string]$Filter = '*'
)

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WinInspect {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$results = New-Object System.Collections.ArrayList
$callback = [WinInspect+EnumWindowsProc]{
    param($hWnd, $lParam)
    $pid2 = 0
    [WinInspect]::GetWindowThreadProcessId($hWnd, [ref]$pid2) | Out-Null
    if ($ProcessId -eq 0 -or $pid2 -eq $ProcessId) {
        $cls = New-Object System.Text.StringBuilder 256
        [WinInspect]::GetClassName($hWnd, $cls, 256) | Out-Null
        $txt = New-Object System.Text.StringBuilder 256
        [WinInspect]::GetWindowTextW($hWnd, $txt, 256) | Out-Null
        $rect = New-Object WinInspect+RECT
        [WinInspect]::GetWindowRect($hWnd, [ref]$rect) | Out-Null
        $ex = [WinInspect]::GetWindowLongPtr($hWnd, -20).ToInt64()
        $visible = [WinInspect]::IsWindowVisible($hWnd)
        [void]$results.Add([pscustomobject]@{
            Pid = $pid2
            Handle = $hWnd
            Class = $cls.ToString()
            Title = $txt.ToString()
            Visible = $visible
            ExStyle = ('0x{0:X}' -f $ex)
            Rect = ('{0},{1} {2}x{3}' -f $rect.Left, $rect.Top, ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
        })
    }
    return $true
}
[WinInspect]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
$results | Where-Object { $_.Title -like $Filter -or $_.Class -like $Filter } | Format-Table -AutoSize
