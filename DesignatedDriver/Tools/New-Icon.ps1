# Draws the Designated Driver icon and writes icon.png (256 px) and icon.ico (256, 64, 48, 32, 24 and 16 px).
# A navy rounded square with two interlocking Ds, silver and gold, linked like a chain: the silver D passes over
# the gold top bar and under the gold stem.
# Each size is drawn directly rather than scaled, so the small ones stay crisp. At 24 px and below the mark is
# flatter: no gradients, no gaps at the crossings, and heavier strokes.
#   .\Tools\New-Icon.ps1
#   .\Tools\New-Icon.ps1 -OutDir <folder>    writes the files somewhere else, for a preview
param([string]$OutDir = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = if ($OutDir) { $OutDir } else { Split-Path -Parent $PSScriptRoot }

function Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# The outline of a D: a straight stem and bars, and a flat right side joined to them by corners of radius R.
function D-Path([double]$x, [double]$y, [double]$w, [double]$h, [double]$R) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine([single]$x, [single]($y + $h), [single]$x, [single]$y)
    $p.AddLine([single]$x, [single]$y, [single]($x + $w - $R), [single]$y)
    $p.AddArc([single]($x + $w - 2 * $R), [single]$y, [single](2 * $R), [single](2 * $R), 270, 90)
    $p.AddLine([single]($x + $w), [single]($y + $R), [single]($x + $w), [single]($y + $h - $R))
    $p.AddArc([single]($x + $w - 2 * $R), [single]($y + $h - 2 * $R), [single](2 * $R), [single](2 * $R), 0, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $flat = $size -le 24

    # Rounded square, navy.
    $r = 56 * $s
    $w = $size - 1
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r, $r, 180, 90)
    $path.AddArc($w - $r, 0, $r, $r, 270, 90)
    $path.AddArc($w - $r, $w - $r, $r, $r, 0, 90)
    $path.AddArc(0, $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $navy = Color '#1B2A4E'
    if ($flat) {
        $g.FillPath((New-Object System.Drawing.SolidBrush $navy), $path)
    } else {
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new($size, $size)), (Color '#23365F'), (Color '#121C36')
        $g.FillPath($bg, $path)
    }

    # The two Ds: silver up and left, gold down and right. The silver right side crosses the gold top bar,
    # and the gold stem crosses the silver bottom bar, both at right angles.
    $t = $(if ($flat) { 30 } else { 22 }) * $s
    $dw = 100 * $s; $dh = 124 * $s; $corner = 40 * $s
    $silverPath = D-Path (48 * $s) (44 * $s) $dw $dh $corner
    $goldPath = D-Path (108 * $s) (88 * $s) $dw $dh $corner

    if ($flat) {
        $silverBrush = New-Object System.Drawing.SolidBrush (Color '#D9DEE6')
        $goldBrush = New-Object System.Drawing.SolidBrush (Color '#EDB83F')
    } else {
        # Each gradient spans the whole height of its D's stroke, so it never repeats at an edge.
        $silverBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 30 * $s)), ([System.Drawing.PointF]::new(0, 182 * $s)), (Color '#F2F4F8'), (Color '#9EA7B5')
        $goldBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 74 * $s)), ([System.Drawing.PointF]::new(0, 226 * $s)), (Color '#FFD970'), (Color '#C48A1C')
    }
    $silverPen = New-Object System.Drawing.Pen $silverBrush, $t
    $goldPen = New-Object System.Drawing.Pen $goldBrush, $t
    $gapPen = New-Object System.Drawing.Pen $navy, ($t + 10 * $s)
    foreach ($p in $silverPen, $goldPen, $gapPen) { $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter }

    # Gold first, silver over it everywhere, then the gold stem again over the silver bottom bar.
    $g.DrawPath($goldPen, $goldPath)
    if (-not $flat) { $g.DrawPath($gapPen, $silverPath) }
    $g.DrawPath($silverPen, $silverPath)
    $g.SetClip([System.Drawing.RectangleF]::new(88 * $s, 140 * $s, 40 * $s, 50 * $s))
    if (-not $flat) { $g.DrawPath($gapPen, $goldPath) }
    $g.DrawPath($goldPen, $goldPath)
    $g.ResetClip()

    $g.Dispose()
    return $bmp
}

$big = Draw-Icon 256
$big.Save((Join-Path $root 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ICO with one PNG entry per size.
$sizes = 256, 64, 48, 32, 24, 16
$entries = foreach ($sz in $sizes) {
    $b = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{ Size = $sz; Bytes = $ms.ToArray() }
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'icon.ico'), $out.ToArray())
Write-Host 'icon.png and icon.ico written'
