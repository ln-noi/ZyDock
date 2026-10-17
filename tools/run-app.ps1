# Launches a built ZyDock instance for manual testing.
#   powershell -ExecutionPolicy Bypass -File tools\run-app.ps1 [-Config Release] [-DataDir <dir>] [-Wait]
param(
    [string]$Config = 'Release',
    [string]$DataDir = '',
    [switch]$Wait
)

$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo "src\ZyDock.App\bin\$Config\net8.0-windows\ZyDock.exe"
if (-not (Test-Path $exe)) {
    throw "Not found: $exe - run build.ps1 first"
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.WorkingDirectory = Split-Path -Parent $exe

$localDotnet = Join-Path $repo '.tools\dotnet'
if (Test-Path $localDotnet) {
    $psi.EnvironmentVariables['DOTNET_ROOT'] = $localDotnet
}

if ($DataDir) {
    if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Force -Path $DataDir | Out-Null }
    $psi.EnvironmentVariables['ZYDOCK_DATA_DIR'] = (Resolve-Path $DataDir).Path
}

$process = [System.Diagnostics.Process]::Start($psi)
Write-Output "PID=$($process.Id) exe=$exe"

if ($Wait) {
    $process.WaitForExit()
    Write-Output "exit=$($process.ExitCode)"
}
