# Moves the cursor to a screen position, waits, and captures the screen.
# Usage: powershell -ExecutionPolicy Bypass -File tools\hover-shot.ps1 -X 1710 -Y 2026 -Out shot.png [-WaitMs 500] [-CropW 1400 -CropH 300]
param(
    [int]$X,
    [int]$Y,
    [string]$Out,
    [int]$WaitMs = 500,
    [int]$CropX = -1,
    [int]$CropY = -1,
    [int]$CropW = 0,
    [int]$CropH = 0,
    [double]$Scale = 1.0
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CursorMover {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@

[CursorMover]::SetProcessDPIAware() | Out-Null
[CursorMover]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds $WaitMs

$capture = Join-Path $PSScriptRoot 'capture-screen.ps1'
$temp = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($Out), 'full-' + [System.IO.Path]::GetFileName($Out))
powershell -NoProfile -ExecutionPolicy Bypass -File $capture -Path $temp | Out-Null

if ($CropW -gt 0) {
    if ($CropX -lt 0) { $CropX = [Math]::Max(0, $X - [int]($CropW / 2)) }
    if ($CropY -lt 0) { $CropY = [Math]::Max(0, $Y - [int]($CropH / 2)) }
    $crop = Join-Path $PSScriptRoot 'crop-image.ps1'
    powershell -NoProfile -ExecutionPolicy Bypass -File $crop -Source $temp -Out $Out -X $CropX -Y $CropY -W $CropW -H $CropH -Scale $Scale
    Remove-Item $temp -Force -ErrorAction SilentlyContinue
} else {
    Move-Item -Force $temp $Out
}

Write-Output "shot -> $Out (cursor at $X,$Y)"
