param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 0,
    [int]$H = 0,
    [double]$Scale = 1.0
)

Add-Type -AssemblyName System.Drawing

$image = [System.Drawing.Image]::FromFile($Source)
if ($W -le 0) { $W = $image.Width }
if ($H -le 0) { $H = $image.Height }

$crop = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($crop)
$g.DrawImage($image, (New-Object System.Drawing.Rectangle 0, 0, $W, $H), (New-Object System.Drawing.Rectangle $X, $Y, $W, $H), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()

$outW = [int]($W * $Scale)
$outH = [int]($H * $Scale)
$result = New-Object System.Drawing.Bitmap $outW, $outH
$g2 = [System.Drawing.Graphics]::FromImage($result)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g2.DrawImage($crop, 0, 0, $outW, $outH)
$g2.Dispose()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$result.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$result.Dispose()
$crop.Dispose()
$image.Dispose()
Write-Output "crop ${W}x${H} -> $Out"
