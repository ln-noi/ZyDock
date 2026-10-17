# Captures the primary screen to a PNG (used for visual self checks).
# Usage: pwsh -NoProfile -ExecutionPolicy Bypass -File tools\capture-screen.ps1 -Path out.png
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$Width = 0,
    [int]$Height = 0
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ScreenCap {
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@

[ScreenCap]::SetProcessDPIAware() | Out-Null

if ($Width -le 0) { $Width = [ScreenCap]::GetSystemMetrics(0) }
if ($Height -le 0) { $Height = [ScreenCap]::GetSystemMetrics(1) }

$screenDc = [ScreenCap]::GetDC([IntPtr]::Zero)
$bitmap = New-Object System.Drawing.Bitmap $Width, $Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$destDc = $graphics.GetHdc()
$ok = [ScreenCap]::BitBlt($destDc, 0, 0, $Width, $Height, $screenDc, $X, $Y, 0x00CC0020)
$graphics.ReleaseHdc($destDc)
$graphics.Dispose()
[ScreenCap]::ReleaseDC([IntPtr]::Zero, $screenDc) | Out-Null

$dir = Split-Path -Parent $Path
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output "captured ${Width}x${Height} -> $Path (ok=$ok)"
