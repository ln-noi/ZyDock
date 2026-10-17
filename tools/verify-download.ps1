param(
    [string]$Sums = 'SHA256SUMS.txt'
)

# Verifies every file listed in a SHA256SUMS.txt against its SHA-256 digest.
# Works on Windows PowerShell 5.1 and PowerShell 7+, no dependencies, ASCII only.
#
# Exit codes: 0 = all files OK, 1 = mismatch or missing file, 2 = sums file not found.

$ErrorActionPreference = 'Stop'

# Resolve the sums file:
#   1. an explicitly passed -Sums <path> (resolved against the current directory)
#   2. SHA256SUMS.txt next to this script   <- normal case: script + files + sums in one folder
#   3. SHA256SUMS.txt in the current directory
$sumsPath = $null

if ($PSBoundParameters.ContainsKey('Sums')) {
    if (Test-Path -LiteralPath $Sums) {
        $sumsPath = (Resolve-Path -LiteralPath $Sums).Path
    }
}
else {
    foreach ($dir in @($PSScriptRoot, (Get-Location).Path)) {
        if (-not $dir) { continue }
        $candidate = Join-Path $dir $Sums
        if (Test-Path -LiteralPath $candidate) {
            $sumsPath = $candidate
            break
        }
    }
}

if (-not $sumsPath) {
    Write-Host "SHA256SUMS file not found: $Sums"
    Write-Host "Put SHA256SUMS.txt next to this script, or pass -Sums <path>."
    exit 2
}

$resolved = $sumsPath
$root = Split-Path -Parent $resolved

$script:okCount = 0
$script:badCount = 0
$script:missingCount = 0

foreach ($raw in Get-Content -LiteralPath $resolved -Encoding UTF8) {
    $line = $raw.Trim()
    if ($line.Length -eq 0 -or $line.StartsWith('#')) {
        continue
    }

    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
        Write-Host ("SKIP     (bad format) {0}" -f $line)
        continue
    }

    $expect = $matches[1].ToLowerInvariant()
    $name = $matches[2].Trim().Trim('"')
    $path = Join-Path $root $name

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host ("MISSING  {0}" -f $name)
        $script:missingCount++
        continue
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()

    if ($actual -eq $expect) {
        Write-Host ("OK       {0}" -f $name)
        $script:okCount++
    }
    else {
        Write-Host ("MISMATCH {0}" -f $name)
        Write-Host ("         expected {0}" -f $expect)
        Write-Host ("         actual   {0}" -f $actual)
        $script:badCount++
    }
}

Write-Host ''
Write-Host ("Summary: {0} OK, {1} mismatch, {2} missing" -f $script:okCount, $script:badCount, $script:missingCount)

if ($script:badCount -gt 0 -or $script:missingCount -gt 0) {
    exit 1
}

exit 0
