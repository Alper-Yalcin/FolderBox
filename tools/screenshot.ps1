# Captures the primary screen (or a region) to a PNG. Usage: screenshot.ps1 -Out file.png [-X 0 -Y 0 -W 800 -H 600]
param([string]$Out, [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System.Runtime.InteropServices;
public static class DpiHelper { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[DpiHelper]::SetProcessDPIAware() | Out-Null
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($W -le 0) { $W = $b.Width - $X }
if ($H -le 0) { $H = $b.Height - $Y }
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Saved $Out ($W x $H)"
