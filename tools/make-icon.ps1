<#
.SYNOPSIS
    Draws Snip's icon (src/Snip/Assets/snip.ico and snip.png): a blue rounded square with white crop corners and a
    red arrow. Each size is drawn on its own, so the small ones stay crisp. Run it again after changing the design.
#>
#Requires -Version 7
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Snip\Assets'
New-Item -ItemType Directory -Force $out | Out-Null

function Draw([int]$s) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [Math]::Max(2, $s * 0.22)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $m = [Math]::Max(0.5, $s * 0.03); $w = $s - 2 * $m
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90); $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90); $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.PointF]::new(0, 0), [System.Drawing.PointF]::new($s, $s),
        [System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6), [System.Drawing.Color]::FromArgb(255, 0x1E, 0x40, 0xAF))
    $g.FillPath($brush, $path)
    # crop corners: top left and bottom right
    $t = [Math]::Max(1.5, $s * 0.085)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $t)
    $pen.StartCap = 'Square'; $pen.EndCap = 'Square'
    $a = $s * 0.24; $b = $s * 0.76; $l = $s * 0.24
    $g.DrawLines($pen, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new($a, $a + $l), [System.Drawing.PointF]::new($a, $a), [System.Drawing.PointF]::new($a + $l, $a)))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new($b - $l, $b), [System.Drawing.PointF]::new($b, $b), [System.Drawing.PointF]::new($b, $b - $l)))
    # the red arrow, pointing into the middle
    if ($s -ge 24) {
        $red = [System.Drawing.Color]::FromArgb(255, 0xF8, 0x71, 0x71)
        $ap = [System.Drawing.Pen]::new($red, [Math]::Max(1.5, $s * 0.075))
        $ap.StartCap = 'Round'
        $ap.CustomEndCap = [System.Drawing.Drawing2D.AdjustableArrowCap]::new(2.2, 2.2, $true)
        $g.DrawLine($ap, $s * 0.72, $s * 0.30, $s * 0.46, $s * 0.56)
    }
    $g.Dispose()
    $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { $b = Draw $s; $ms = [IO.MemoryStream]::new(); $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); if ($s -eq 256) { $b.Save((Join-Path $out 'snip.png')) }; $b.Dispose(); , $ms.ToArray() }

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
