# Generates ResGUI.ico (16-256 px, PNG-compressed entries): dark rounded tile with three green bars.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\ResGUI.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [Math]::Max(2, $s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new(0, $s)), ([System.Drawing.Color]::FromArgb(255, 40, 40, 48)), ([System.Drawing.Color]::FromArgb(255, 20, 20, 24))
    $g.FillPath($bg, $path)
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 52, 211, 153))
    $w = $s * 0.16; $gap = $s * 0.09; $x0 = ($s - (3 * $w + 2 * $gap)) / 2; $bottom = $s * 0.78
    $heights = 0.30, 0.46, 0.60
    for ($i = 0; $i -lt 3; $i++) {
        $h = $s * $heights[$i]
        $g.FillRectangle($green, [single]($x0 + $i * ($w + $gap)), [single]($bottom - $h), [single]$w, [single]$h)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
"Wrote $out"
