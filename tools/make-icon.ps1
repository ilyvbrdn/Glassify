# Generates app/app.ico (16-256 px) and icon-preview.png for Glassify.
# Design: rounded tile with a pink -> violet gradient, a frosted glass pane, glossy top edge and a white music note.
param([string]$Out = (Join-Path $PSScriptRoot '..\app\app.ico'), [string]$Preview = (Join-Path $PSScriptRoot '..\icon-preview.png'))
Add-Type -AssemblyName System.Drawing
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p }
function Render([int]$S, [bool]$Simple = $false) {
  $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $m = $S * 0.04; $tile = RoundRect $m $m ($S - 2*$m) ($S - 2*$m) ($S * 0.23)
  # background gradient
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $S, $S), ([System.Drawing.Color]::FromArgb(255, 255, 95, 168)), ([System.Drawing.Color]::FromArgb(255, 96, 56, 236)), 50
  $g.FillPath($bg, $tile)
  # soft glow blobs for depth
  $g.SetClip($tile)
  $glow = New-Object System.Drawing.Drawing2D.GraphicsPath; $glow.AddEllipse([float]($S*-0.15), [float]($S*0.45), [float]($S*0.9), [float]($S*0.9))
  $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow; $pgb.CenterColor = [System.Drawing.Color]::FromArgb(110, 255, 170, 120); $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 170, 120)); $g.FillPath($pgb, $glow)
  # frosted glass pane
  $pane = RoundRect ($S*0.2) ($S*0.2) ($S*0.6) ($S*0.6) ($S*0.14)
  $pb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF ($S*0.2), ($S*0.2), ($S*0.6), ($S*0.6)), ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), 70
  if (-not $Simple) {
    $g.FillPath($pb, $pane)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(170, 255, 255, 255)), ([float][Math]::Max(1, $S*0.012)); $g.DrawPath($pen, $pane)
  }
  # glossy top highlight on the tile
  $gloss = New-Object System.Drawing.Drawing2D.GraphicsPath; $gloss.AddEllipse([float]($S*-0.2), [float]($S*-0.62), [float]($S*1.4), [float]($S*1.0))
  $gl = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $S, ($S*0.4)), ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
  $g.FillPath($gl, $gloss)
  $g.ResetClip()
  # music note (two eighth notes) in white with a soft shadow
  $fs = if ($Simple) { $S * 0.62 } else { $S * 0.4 }
  $font = New-Object System.Drawing.Font 'Segoe UI Symbol', ([float]$fs), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
  $note = [string][char]0x266B
  $rect = New-Object System.Drawing.RectangleF 0, ($S*0.02), $S, $S
  $shadow = New-Object System.Drawing.RectangleF ($S*0.012), ($S*0.035), $S, $S
  $g.DrawString($note, $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 60, 0, 60))), $shadow, $sf)
  $g.DrawString($note, $font, [System.Drawing.Brushes]::White, $rect, $sf)
  # crisp tile edge
  $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([float][Math]::Max(1, $S*0.008)); $g.DrawPath($edge, $tile)
  $g.Dispose(); $bmp
}
# render big once, scale down for crisp small sizes
$big = Render 1024
$small = Render 1024 $true     # bigger note, no glass frame: reads better at 16-32 px
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
  $b = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($b); $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.DrawImage($(if ($s -le 32) { $small } else { $big }), 0, 0, $s, $s); $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += ,$ms.ToArray(); $b.Dispose()
}
$ico = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) { $s = $sizes[$i]; $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$off); $off += $pngs[$i].Length }
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush(); [IO.File]::WriteAllBytes((Resolve-Path (Split-Path $Out)).Path + '\' + (Split-Path $Out -Leaf), $ico.ToArray())
# preview: large + small sizes side by side on light and dark backgrounds
$pv = New-Object System.Drawing.Bitmap 760, 300; $g = [System.Drawing.Graphics]::FromImage($pv); $g.InterpolationMode = 'HighQualityBicubic'
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 245, 247))), 0, 0, 380, 300); $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(24, 24, 28))), 380, 0, 380, 300)
foreach ($x0 in 0, 380) { $g.DrawImage($big, $x0 + 20, 20, 200, 200); $x = $x0 + 240; foreach ($s in 64, 48) { $g.DrawImage($big, $x, 230 - $s, $s, $s); $x += $s + 8 } ; $sx = $x0 + 20; foreach ($s in 32, 24, 20, 16) { $g.DrawImage($small, $sx, 250, $s, $s); $sx += $s + 14 } }
$g.Dispose(); $pv.Save((Join-Path (Resolve-Path (Split-Path $Preview)).Path (Split-Path $Preview -Leaf)), [System.Drawing.Imaging.ImageFormat]::Png)
"icon written: $Out"
