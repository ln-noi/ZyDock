# Shared environment for every build / run command in this repository.
# Dot sourced by build.ps1, run.ps1, publish.ps1.
#
# The .NET 8 SDK used to build ZyDock lives in .tools\dotnet (see docs/BUILD.md).
# If it is missing we fall back to whatever SDK is on PATH.

$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $script:RepoRoot) { $script:RepoRoot = (Get-Location).Path }

$localDotnet = Join-Path $script:RepoRoot '.tools\dotnet\dotnet.exe'
if (Test-Path $localDotnet) {
    $env:DOTNET_ROOT = Join-Path $script:RepoRoot '.tools\dotnet'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    $script:Dotnet = $localDotnet
} else {
    $script:Dotnet = 'dotnet'
}

# Keep every MSBuild / NuGet artifact inside the repository so the build works on
# machines where the user profile or %TEMP% is locked down.
$toolsHome = Join-Path $script:RepoRoot '.tools'
foreach ($dir in @('home', 'nuget', 'nuget-local', 'temp')) {
    $path = Join-Path $toolsHome $dir
    if (-not (Test-Path $path)) { New-Item -ItemType Directory -Force -Path $path | Out-Null }
}

$env:DOTNET_CLI_HOME = Join-Path $toolsHome 'home'
$env:NUGET_PACKAGES = Join-Path $toolsHome 'nuget'
$env:TEMP = Join-Path $toolsHome 'temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & $script:Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}
