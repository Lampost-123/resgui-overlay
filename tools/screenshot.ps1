# Captures the running ResGUI overlay to a PNG with transparent rounded corners.
param([Parameter(Mandatory = $true)][string]$Out, [int]$Radius = 12)
Add-Type -AssemblyName System.Drawing
if (-not ('ResGuiShot2.W' -as [type])) {
Add-Type -Name W -Namespace ResGuiShot2 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
public struct RECT { public int L, T, R, B; }
'@
}
[ResGuiShot2.W]::SetProcessDPIAware() | Out-Null
$h = [ResGuiShot2.W]::FindWindow([NullString]::Value, 'ResGUI')
if ($h -eq [IntPtr]::Zero) { throw 'ResGUI window not found' }
$r = New-Object ResGuiShot2.W+RECT
[ResGuiShot2.W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$shot = New-Object System.Drawing.Bitmap $w, $ht
# PrintWindow with PW_RENDERFULLCONTENT reads the window's own pixels, even if other windows cover it.
$g = [System.Drawing.Graphics]::FromImage($shot)
$hdc = $g.GetHdc()
$ok = [ResGuiShot2.W]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
if (-not $ok) { throw 'PrintWindow failed' }
# Copy through a rounded-rectangle clip so the desktop behind the corners becomes transparent.
$final = New-Object System.Drawing.Bitmap $w, $ht, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($final)
$g.SmoothingMode = 'AntiAlias'
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$d = $Radius * 2
$path.AddArc(0, 0, $d, $d, 180, 90)
$path.AddArc($w - $d, 0, $d, $d, 270, 90)
$path.AddArc($w - $d, $ht - $d, $d, $d, 0, 90)
$path.AddArc(0, $ht - $d, $d, $d, 90, 90)
$path.CloseFigure()
$brush = New-Object System.Drawing.TextureBrush $shot
$g.FillPath($brush, $path)
$g.Dispose()
$final.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"Saved $Out ($w x $ht)"
