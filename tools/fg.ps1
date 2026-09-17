Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
$fg=[W]::GetForegroundWindow(); $t=New-Object System.Text.StringBuilder 256; [W]::GetWindowText($fg,$t,256)|Out-Null; $c=New-Object System.Text.StringBuilder 256; [W]::GetClassName($fg,$c,256)|Out-Null; $p=0; [W]::GetWindowThreadProcessId($fg,[ref]$p)|Out-Null
"fg=$fg title='$($t.ToString())' class=$($c.ToString()) proc=$((Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName)"
