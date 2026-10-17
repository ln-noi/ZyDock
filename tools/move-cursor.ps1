# Moves the mouse cursor (DPI aware). Test helper.
# Usage: powershell -File tools\move-cursor.ps1 -X 1900 -Y 2152
param(
    [int]$X,
    [int]$Y
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CursorMove {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@

[CursorMove]::SetProcessDPIAware() | Out-Null
[CursorMove]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 120
$p = New-Object CursorMove+POINT
[CursorMove]::GetCursorPos([ref]$p) | Out-Null
Write-Output "cursor -> $($p.X),$($p.Y) (requested $X,$Y)"
