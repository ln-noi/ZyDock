# Publishes ZyDock.
#
#   dist\ZyDock-<ver>\            <- DEFAULT: self contained folder, double click ZyDock.exe
#   dist\ZyDock-<ver>.zip         <- DEFAULT: same folder zipped, ready to hand out
#
#   -SingleFile   also produce dist\ZyDock-<ver>-singlefile\ZyDock.exe (+ zip).
#                 Single file needs to unpack itself into %TEMP% at start up; machines that
#                 block writes outside the app folder cannot run it, the folder build can.
#   -FrameworkDependent
#                 also produce dist\optional\ZyDock-<ver>-needs-dotnet8-runtime\ (needs the
#                 .NET 8 Desktop Runtime installed).
#
# Usage: powershell -ExecutionPolicy Bypass -File publish.ps1 [-SkipZip] [-SingleFile] [-FrameworkDependent]
param(
    [switch]$SkipZip,
    [switch]$SingleFile,
    [switch]$FrameworkDependent,
    [string]$Configuration = 'Release'
)

. "$PSScriptRoot\tools\env.ps1"
Set-Location $script:RepoRoot

function Invoke-DotnetArg {
    param([string[]]$Arguments)
    & $script:Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Copy-Docs {
    param([string]$Target)
    foreach ($doc in @('README.md', 'docs\BUILD.md')) {
        $source = Join-Path $script:RepoRoot $doc
        if (Test-Path $source) {
            Copy-Item $source (Join-Path $Target (Split-Path $doc -Leaf)) -Force
        }
    }

    # Usage guide: copied by its own file name (taken from the file system so the Chinese
    # name survives) and re-encoded as UTF-8 with BOM so Notepad shows it correctly.
    $guide = Get-ChildItem (Join-Path $script:RepoRoot 'docs') -Filter '*.txt' -File |
        Where-Object { $_.Name -notlike 'BUILD*' } |
        Select-Object -First 1
    if ($guide) {
        $text = [System.IO.File]::ReadAllText($guide.FullName, [System.Text.Encoding]::UTF8)
        [System.IO.File]::WriteAllText(
            (Join-Path $Target $guide.Name),
            $text,
            (New-Object System.Text.UTF8Encoding($true)))
    }
}

$version = ([xml](Get-Content "$PSScriptRoot\Directory.Build.props")).PropertyGroup.Version
if (-not $version) { $version = '1.0.0' }

$dist = Join-Path $script:RepoRoot 'dist'
$portableDir = Join-Path $dist "ZyDock-$version"
$singleFileDir = Join-Path $dist "ZyDock-$version-singlefile"
$frameworkDir = Join-Path $dist "optional\ZyDock-$version-needs-dotnet8-runtime"

# Clean the previous layout (it shipped two look-alike executables and confused users).
foreach ($stale in @(
    (Join-Path $dist "ZyDock-$version-win-x64"),
    (Join-Path $dist "ZyDock-$version-standalone"),
    (Join-Path $dist "optional\ZyDock-$version-portable"),
    $portableDir
)) {
    Remove-Item $stale -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $portableDir | Out-Null

Write-Host "== publishing self contained folder (default) ==" -ForegroundColor Cyan
Invoke-DotnetArg @(
    'publish', 'src\ZyDock.App\ZyDock.App.csproj',
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '--output', $portableDir
)

if ($SingleFile) {
    Remove-Item $singleFileDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $singleFileDir | Out-Null
    Write-Host "== publishing single file bundle ==" -ForegroundColor Cyan
    Invoke-DotnetArg @(
        'publish', 'src\ZyDock.App\ZyDock.App.csproj',
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '--output', $singleFileDir
    )
    Copy-Docs -Target $singleFileDir
}

if ($FrameworkDependent) {
    Remove-Item $frameworkDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $frameworkDir | Out-Null
    Write-Host "== publishing framework dependent (optional) ==" -ForegroundColor Cyan
    Invoke-DotnetArg @(
        'publish', 'src\ZyDock.App\ZyDock.App.csproj',
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'false',
        '--output', $frameworkDir
    )
}

Copy-Docs -Target $portableDir

$exe = Join-Path $portableDir 'ZyDock.exe'
if (-not (Test-Path $exe)) { throw "publish failed: $exe missing" }

if (-not $SkipZip) {
    $zip = Join-Path $dist "ZyDock-$version.zip"
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("zip   {0} ({1} MB)" -f $zip, [Math]::Round((Get-Item $zip).Length / 1MB, 1)) -ForegroundColor Green

    if ($SingleFile) {
        $singleZip = Join-Path $dist "ZyDock-$version-singlefile.zip"
        Remove-Item $singleZip -Force -ErrorAction SilentlyContinue
        Compress-Archive -Path (Join-Path $singleFileDir '*') -DestinationPath $singleZip -CompressionLevel Optimal
        Write-Host ("zip   {0} ({1} MB)" -f $singleZip, [Math]::Round((Get-Item $singleZip).Length / 1MB, 1)) -ForegroundColor Green
    }
}

Write-Host ""
Write-Host ("RUN THIS   : {0}" -f $exe) -ForegroundColor Green
Write-Host ("folder size: {0} MB in {1} files (self contained, no runtime needed)" -f `
        [Math]::Round((Get-ChildItem $portableDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1), `
        (Get-ChildItem $portableDir -Recurse -File | Measure-Object).Count)
if ($SingleFile) {
    Write-Host "single file bundle: $singleFileDir  (needs %TEMP% writable, unpacked at start)" -ForegroundColor Yellow
}
if ($FrameworkDependent) {
    Write-Host "framework dependent: $frameworkDir  (needs .NET 8 Desktop Runtime)" -ForegroundColor Yellow
}
