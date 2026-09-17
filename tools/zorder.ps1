# Lists top-level windows in Z-order (top -> bottom) around FolderBox windows and Progman.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class Z {
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
 public static List<IntPtr> All() { var l = new List<IntPtr>(); EnumWindows((h, p) => { l.Add(h); return true; }, IntPtr.Zero); return l; }
}
"@
$fb = (Get-Process FolderBox -ErrorAction SilentlyContinue).Id
$i = 0
foreach ($h in [Z]::All()) {
  $i++
  if (-not [Z]::IsWindowVisible($h)) { continue }
  $c = New-Object System.Text.StringBuilder 128; [Z]::GetClassName($h, $c, 128) | Out-Null
  $t = New-Object System.Text.StringBuilder 128; [Z]::GetWindowText($h, $t, 128) | Out-Null
  $p = 0; [Z]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
  $ex = [Z]::GetWindowLongPtr($h, -20).ToInt64()
  $isOurs = ($p -eq $fb)
  $cls = $c.ToString()
  if ($isOurs -or $cls -eq "Progman" -or $cls -eq "WorkerW" -or $cls -eq "Shell_TrayWnd" -or $cls -eq "Chrome_WidgetWin_1" -or $cls -eq "CabinetWClass") {
    $tool = if ($ex -band 0x80) { "TOOLWINDOW" } else { "" }
    $top = if ($ex -band 0x8) { "TOPMOST" } else { "" }
    "{0,4} {1,-28} {2,-30} pid={3} {4} {5} {6}" -f $i, $cls, $t.ToString().Substring(0, [Math]::Min(30, $t.Length)), $p, $(if ($isOurs) { "<-- FolderBox" } else { "" }), $tool, $top
  }
}
