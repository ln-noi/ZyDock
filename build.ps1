# Builds ZyDock in the given configuration.
#   ./build.ps1              -> Release build of the solution
#   ./build.ps1 -Clean       -> wipe bin/obj first
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Clean
)

. "$PSScriptRoot\tools\env.ps1"

Set-Location $script:RepoRoot

if ($Clean) {
    Get-ChildItem -Path $script:RepoRoot -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# Two footguns avoided here:
#   1. a bare "-v minimal" is parsed by PowerShell as a parameter name, leaving "minimal"
#      behind as a stray positional argument -> use "--verbosity minimal";
#   2. Invoke-Dotnet takes ValueFromRemainingArguments, so "@('a','b')" would be passed as a
#      SINGLE argument -> build the array in a variable and splat it.
$restoreArgs = @('restore', 'ZyDock.sln')
Invoke-Dotnet @restoreArgs

$buildArgs = @('build', 'ZyDock.sln', '-c', $Configuration, '--no-restore', '--verbosity', 'minimal')
Invoke-Dotnet @buildArgs

Write-Host ""
Write-Host "Build OK -> src\ZyDock.App\bin\$Configuration\net8.0-windows\ZyDock.exe" -ForegroundColor Green
