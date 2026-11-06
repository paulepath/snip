<#
.SYNOPSIS
    Draws Snip's icon (src/Snip/Assets/snip.ico and snip.png): the chosen S-brackets mark on a dark #121417
    rounded tile. Drawn on its 64-unit viewBox with whole-pixel snapping at small sizes (16, 20, 24 px).
#>
#Requires -Version 7
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Snip\Assets'
New-Item -ItemType Directory -Force $out | Out-Null

function Draw([int]$s) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # 1. Dark squircle tile: #121417
    $tileColor = [System.Drawing.Color]::FromArgb(255, 0x12, 0x14, 0x17)
    $tileBrush = [System.Drawing.SolidBrush]::new($tileColor)
    $r = [Math]::Max(2.0, $s * 0.22)
    $m = [Math]::Max(0.5, $s * 0.03)
    $w = $s - 2 * $m

    $tilePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tilePath.AddArc([float]$m, [float]$m, [float](2 * $r), [float](2 * $r), 180, 90)
    $tilePath.AddArc([float]($m + $w - 2 * $r), [float]$m, [float](2 * $r), [float](2 * $r), 270, 90)
    $tilePath.AddArc([float]($m + $w - 2 * $r), [float]($m + $w - 2 * $r), [float](2 * $r), [float](2 * $r), 0, 90)
    $tilePath.AddArc([float]$m, [float]($m + $w - 2 * $r), [float](2 * $r), [float](2 * $r), 90, 90)
    $tilePath.CloseFigure()
    $g.FillPath($tileBrush, $tilePath)

    # Subtle border on larger tiles for contrast
    if ($s -ge 32) {
        $borderPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 0x30, 0x35, 0x3C), [float][Math]::Max(1.0, $s * 0.015))
        $g.DrawPath($borderPen, $tilePath)
        $borderPen.Dispose()
    }
    $tileBrush.Dispose()
    $tilePath.Dispose()

    # 2. S-brackets mark: blue #5AA9F2 and red #F87171
    $blue = [System.Drawing.Color]::FromArgb(255, 0x5A, 0xA9, 0xF2)
    $red  = [System.Drawing.Color]::FromArgb(255, 0xF8, 0x71, 0x71)
    $blueBrush = [System.Drawing.SolidBrush]::new($blue)
    $redBrush  = [System.Drawing.SolidBrush]::new($red)

    # Top-left bracket path on 64-unit viewBox
    # M12 4H50A2 2 0 0 1 52 6V14A2 2 0 0 1 50 16H20V34A2 2 0 0 1 18 36H10A2 2 0 0 1 8 34V8A4 4 0 0 1 12 4Z
    $bp = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $bp.AddLine(12.0, 4.0, 50.0, 4.0)
    $bp.AddArc(48.0, 4.0, 4.0, 4.0, 270.0, 90.0)
    $bp.AddLine(52.0, 6.0, 52.0, 14.0)
    $bp.AddArc(48.0, 12.0, 4.0, 4.0, 0.0, 90.0)
    $bp.AddLine(50.0, 16.0, 20.0, 16.0)
    $bp.AddLine(20.0, 16.0, 20.0, 34.0)
    $bp.AddArc(16.0, 32.0, 4.0, 4.0, 0.0, 90.0)
    $bp.AddLine(18.0, 36.0, 10.0, 36.0)
    $bp.AddArc(8.0, 32.0, 4.0, 4.0, 90.0, 90.0)
    $bp.AddLine(8.0, 34.0, 8.0, 8.0)
    $bp.AddArc(8.0, 4.0, 8.0, 8.0, 180.0, 90.0)
    $bp.CloseFigure()

    $state = $g.Save()
    if ($s -le 24) {
        # At small sizes, snap 1:1 on 64-unit grid to keep whole pixels (3px bars, 2px gaps at 16px)
        $scale = [float]($s / 64.0)
        $g.ScaleTransform($scale, $scale)
    } else {
        # At 32px and above, give breathing room inside the tile (0.75 scale) centered at (s/2, s/2)
        $targetScale = [float](($s * 0.75) / 64.0)
        $xOffset = [float](($s / 2.0) - (32.0 * $targetScale))
        $yOffset = [float](($s / 2.0) - (30.0 * $targetScale))
        $g.TranslateTransform($xOffset, $yOffset)
        $g.ScaleTransform($targetScale, $targetScale)
    }

    # Red middle bar: x=19.5, y=24, w=25, h=12
    $g.FillRectangle($redBrush, [float]19.5, [float]24.0, [float]25.0, [float]12.0)

    # Bracket 1
    $g.FillPath($blueBrush, $bp)

    # Bracket 2: rotated 180 around (32, 30)
    $innerState = $g.Save()
    $g.TranslateTransform(32.0, 30.0)
    $g.RotateTransform(180.0)
    $g.TranslateTransform(-32.0, -30.0)
    $g.FillPath($blueBrush, $bp)
    $g.Restore($innerState)

    $g.Restore($state)

    $blueBrush.Dispose()
    $redBrush.Dispose()
    $bp.Dispose()
    $g.Dispose()

    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw $s
    $ms = [IO.MemoryStream]::new()
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) {
        $b.Save((Join-Path $out 'snip.png'))
    }
    $b.Dispose()
    , $ms.ToArray()
}

# ICO: a header, one entry per size, then each PNG
$ico = [IO.MemoryStream]::new(); $w = [IO.BinaryWriter]::new($ico)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $out 'snip.ico'), $ico.ToArray())
"Wrote $out\snip.ico ($($sizes -join ', ') px) and snip.png"
