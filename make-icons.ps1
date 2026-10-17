# Generates the build icons from assets\icon.png and assets\toolbar.png:
#   src\SqlAssist.Vsix\Resources\icon.png  128x128, the extension's icon in SSMS
#   src\SqlAssist.Vsix\Resources\toolbar{16,24,32,48}.png  from assets\toolbar.png, the toolbar button
#   src\SqlAssist.Setup\app.ico            16..256, the setup exe's icon
# Run by build-setup.ps1; replace the appropriate source asset and rebuild.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = $PSScriptRoot
$source = Join-Path $root "assets\icon.png"
if (-not (Test-Path $source)) { throw "Missing $source" }
$toolbarSource = Join-Path $root "assets\toolbar.png"
if (-not (Test-Path $toolbarSource)) { throw "Missing $toolbarSource" }

function Get-ResizedPng([System.Drawing.Image]$image, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($image, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$image = [System.Drawing.Image]::FromFile($source)
try {
    # Extension icon
    $resources = Join-Path $root "src\SqlAssist.Vsix\Resources"
    New-Item -ItemType Directory -Force $resources | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $resources "icon.png"), (Get-ResizedPng $image 128))
    # Toolbar button image, one PNG per size listed in SqlAssist.imagemanifest. SSMS picks the size that matches
    # the display scale (16 at 100%, 24 at 150%, 32 at 200%, 48 at 300%) instead of stretching a single 16x16.
    # Use a simplified, tightly framed source designed for small toolbar sizes.
    $toolbarImage = [System.Drawing.Image]::FromFile($toolbarSource)
    try {
        foreach ($size in 16, 24, 32, 48) {
            [System.IO.File]::WriteAllBytes((Join-Path $resources "toolbar$size.png"), (Get-ResizedPng $toolbarImage $size))
        }
        $old = Join-Path $resources "toolbar.png"
        if (Test-Path $old) { Remove-Item $old }
    }
    finally {
        $toolbarImage.Dispose()
    }

    # .ico with PNG-compressed entries (supported since Windows Vista)
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $pngs = $sizes | ForEach-Object { ,(Get-ResizedPng $image $_) }
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {                                     # ICONDIRENTRY
        $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $root "src\SqlAssist.Setup\app.ico"), $ms.ToArray())
    $w.Dispose()
}
finally {
    $image.Dispose()
}
Write-Host "Icons generated from assets\icon.png and assets\toolbar.png"
