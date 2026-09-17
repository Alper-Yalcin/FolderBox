param([string]$Title, [string]$Action = "rect")
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class F {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 public struct RECT { public int L, T, R, B; }
 public static IntPtr Find(string title) { IntPtr found = IntPtr.Zero; EnumWindows((h, p) => { if (!IsWindowVisible(h)) return true; var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString() == title) { found = h; return false; } return true; }, IntPtr.Zero); return found; }
}
"@
[F]::SetProcessDPIAware() | Out-Null
$h = [F]::Find($Title)
if ($h -eq [IntPtr]::Zero) { "notfound"; exit }
if ($Action -eq "topmost") { [F]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x13) | Out-Null; "topmost"; exit }
if ($Action -eq "notopmost") { [F]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x13) | Out-Null; "notopmost"; exit }
if ($Action -eq "close") { [F]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; "closed"; exit }
$r = New-Object F+RECT; [F]::GetWindowRect($h, [ref]$r) | Out-Null; "$($r.L) $($r.T) $($r.R - $r.L) $($r.B - $r.T)"
