# Captures a window's rendered content even when occluded. Usage: printwin.ps1 -Title "FolderBox" -Out x.png [-Index 0]
param([string]$Title, [string]$Out, [int]$Index = 0)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class PW {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public struct RECT { public int L, T, R, B; }
 public static List<IntPtr> FindAll(string title) { var l = new List<IntPtr>(); EnumWindows((h, p) => { if (!IsWindowVisible(h)) return true; var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString() == title) l.Add(h); return true; }, IntPtr.Zero); return l; }
}
"@
[PW]::SetProcessDPIAware() | Out-Null
$list = [PW]::FindAll($Title)
if ($list.Count -le $Index) { "notfound"; exit 1 }
$h = $list[$Index]
$r = New-Object PW+RECT; [PW]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[PW]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"saved $Out ($w x $hh) at $($r.L),$($r.T)"
