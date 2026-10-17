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

Invoke-Dotnet restore ZyDock.sln
Invoke-Dotnet build ZyDock.sln -c $Configuration --no-restore -v minimal

Write-Host ""
Write-Host "Build OK -> src\ZyDock.App\bin\$Configuration\net8.0-windows\ZyDock.exe" -ForegroundColor Green
