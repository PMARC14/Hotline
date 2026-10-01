# Generates placeholder logo PNGs and a PNG-embedded .ico for the tray. Run with Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Hotline.App\Assets'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Logo([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 91, 75, 245))
    $r = [int]($size * 0.2)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc(0, 0, $r, $r, 180, 90); $gp.AddArc($size - $r - 1, 0, $r, $r, 270, 90)
    $gp.AddArc($size - $r - 1, $size - $r - 1, $r, $r, 0, 90); $gp.AddArc(0, $size - $r - 1, $r, $r, 90, 90)
    $gp.CloseFigure(); $g.FillPath($brush, $gp)
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float]($size * 0.55)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('H', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $size, $size), $fmt)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

New-Logo 44  (Join-Path $out 'Square44x44Logo.png')
New-Logo 150 (Join-Path $out 'Square150x150Logo.png')
New-Logo 50  (Join-Path $out 'StoreLogo.png')
New-Logo 32  (Join-Path $out 'tray32.png')

# ICO container holding one 32x32 PNG (supported by LoadImage on Vista+).
$png = [IO.File]::ReadAllBytes((Join-Path $out 'tray32.png'))
$ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)            # ICONDIR
$bw.Write([byte]32); $bw.Write([byte]32); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
$bw.Write($png); $bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $out 'Hotline.ico'), $ms.ToArray())
Remove-Item (Join-Path $out 'tray32.png')
Write-Host "Assets written to $out"
