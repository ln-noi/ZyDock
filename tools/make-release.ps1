param(
    [string]$Version = '1.0.0'
)

# Prepares a GitHub release folder with upload-ready asset names, then regenerates
# SHA256SUMS.txt (both in dist\release\ and in the repository root, which is committed).
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -Version 1.0.0

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$release = Join-Path $dist 'release'

if (-not (Test-Path $dist)) {
    Write-Host "dist\ not found - run publish.ps1 first."
    exit 1
}

New-Item -ItemType Directory -Force -Path $release | Out-Null
Get-ChildItem $release -File | Remove-Item -Force

# asset name -> source file
$assets = @(
    @{ Name = "ZyDock-$Version.exe";              Src = (Join-Path $dist "ZyDock-$Version-singlefile\ZyDock.exe") },
    @{ Name = "ZyDock-$Version-singlefile.zip";   Src = (Join-Path $dist "ZyDock-$Version-singlefile.zip") },
    @{ Name = "ZyDock-$Version.zip";              Src = (Join-Path $dist "ZyDock-$Version.zip") }
)

$missing = @()
foreach ($a in $assets) {
    if (Test-Path $a.Src) {
        Copy-Item $a.Src (Join-Path $release $a.Name) -Force
    }
    else {
        $missing += $a.Src
    }
}

if ($missing.Count -gt 0) {
    Write-Host "Missing build output(s):"
    $missing | ForEach-Object { Write-Host "  $_" }
    Write-Host "Run publish.ps1 (add -SingleFile for the single-file exe) first."
    exit 1
}

$renameNote1 = "# A file name does not affect its digest: renaming ZyDock-$Version.exe to"
$renameNote2 = '# ZyDock.exe keeps the same SHA-256 value.'

$header = @(
    "# ZyDock $Version - SHA-256 checksums for the official release files",
    '#',
    '# This file is committed to the repository, so its history is auditable:',
    '#   git log -p SHA256SUMS.txt',
    '#',
    '# Verify after downloading (keep this file next to the downloaded files):',
    '#   PowerShell : powershell -ExecutionPolicy Bypass -File verify-download.ps1',
    '#   PowerShell : Get-FileHash .\<file> -Algorithm SHA256',
    '#   CMD        : certutil -hashfile <file> SHA256',
    '#   7-Zip      : right click -> CRC SHA -> SHA-256',
    '#',
    $renameNote1,
    $renameNote2,
    '#',
    '# Hashes prove the file matches this list; they do not prove authorship.',
    '# Only a signed artifact (code signing / attestation) proves who built it.'
)

$body = foreach ($a in $assets) {
    $path = Join-Path $release $a.Name
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($a.Name)"
}

$text = ($header + $body) -join "`r`n"
$sumsRelease = Join-Path $release 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($sumsRelease, $text + "`r`n", (New-Object System.Text.UTF8Encoding($false)))

$sumsRoot = Join-Path $repo 'SHA256SUMS.txt'
Copy-Item $sumsRelease $sumsRoot -Force

Write-Host "release folder: $release"
Get-ChildItem $release -File | ForEach-Object {
    Write-Host ("  {0,-34} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB))
}
Write-Host ''
Write-Host "SHA256SUMS.txt written to dist\release\ and to the repository root."
Write-Host 'Paste this line into the GitHub release notes:'
Write-Host ''
foreach ($line in $body) {
    if ($line -like "*ZyDock-$Version.exe*") {
        Write-Host ("SHA-256 (ZyDock-$Version.exe): " + ($line -split '\s+')[0])
    }
}
