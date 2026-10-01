# Generates every app/tray icon from one source image (default: assets/source/icon.png).
#   powershell -File scripts\make-assets.ps1 [-Source path\to\image.png]
# Current source: Microsoft Fluent Emoji "Telephone receiver" (3D), MIT licensed (see assets/source/ATTRIBUTION.md).
# To use an SVG later: export it to a >=256 px transparent PNG first (e.g. Inkscape: inkscape icon.svg -w 512 -o icon.png).
param([string]$Source)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
if (-not $Source) { $Source = Join-Path $root 'assets\source\icon.png' }
$out = Join-Path $root 'src\Hotline.App\Assets'
New-Item -ItemType Directory -Force $out | Out-Null
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))

# Draws the source centred on a transparent square canvas, with optional padding (fraction of the canvas).
function New-Png([int]$size, [string]$path, [double]$padding = 0.0) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $inner = [int][Math]::Round($size * (1 - 2 * $padding))
    $offset = [int][Math]::Round(($size - $inner) / 2)
    $g.DrawImage($src, $offset, $offset, $inner, $inner)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

# MSIX logos (MRT picks the best targetsize/scale variant automatically).
New-Png 44  (Join-Path $out 'Square44x44Logo.png') 0.04
New-Png 150 (Join-Path $out 'Square150x150Logo.png') 0.18
New-Png 50  (Join-Path $out 'StoreLogo.png') 0.04
foreach ($t in 16, 24, 32, 48, 256) {
    New-Png $t (Join-Path $out "Square44x44Logo.targetsize-$t.png") 0.02
    New-Png $t (Join-Path $out "Square44x44Logo.targetsize-${t}_altform-unplated.png") 0.02
}

# Multi-size .ico (PNG-compressed entries) for the tray icon and the exe.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $tmp = Join-Path $env:TEMP "hotline-ico-$s.png"
    New-Png $s $tmp 0.02
    , [IO.File]::ReadAllBytes($tmp)
    Remove-Item $tmp
}
$ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)    # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                      # ICONDIRENTRY
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $out 'Hotline.ico'), $ms.ToArray())
$src.Dispose()
Write-Host "Assets written to $out from $Source"
