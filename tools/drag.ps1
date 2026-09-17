# Drag using Windows.UI.Input.Preview.Injection (proper pointer semantics for WinUI targets)
param([int]$X1, [int]$Y1, [int]$X2, [int]$Y2, [int]$Steps = 25)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class C { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[C]::SetProcessDPIAware() | Out-Null
$null = [Windows.UI.Input.Preview.Injection.InputInjector, Windows.UI.Input.Preview.Injection, ContentType=WindowsRuntime]
$inj = [Windows.UI.Input.Preview.Injection.InputInjector]::TryCreate()
if ($null -eq $inj) { Write-Host "InputInjector unavailable"; exit 1 }
function Mouse([string]$opt, [int]$dx = 0, [int]$dy = 0) {
  $m = New-Object Windows.UI.Input.Preview.Injection.InjectedInputMouseInfo
  $m.MouseOptions = [Windows.UI.Input.Preview.Injection.InjectedInputMouseOptions]$opt
  $m.DeltaX = $dx; $m.DeltaY = $dy
  $list = New-Object 'System.Collections.Generic.List[Windows.UI.Input.Preview.Injection.InjectedInputMouseInfo]'
  $list.Add($m); $inj.InjectMouseInput($list)
}
[C]::SetCursorPos($X1, $Y1) | Out-Null; Start-Sleep -Milliseconds 100
Mouse "LeftDown"; Start-Sleep -Milliseconds 120
$px = $X1; $py = $Y1
for ($i = 1; $i -le $Steps; $i++) {
  $x = [int]($X1 + ($X2 - $X1) * $i / $Steps); $y = [int]($Y1 + ($Y2 - $Y1) * $i / $Steps)
  Mouse "Move" ($x - $px) ($y - $py); $px = $x; $py = $y
  Start-Sleep -Milliseconds 16
}
Start-Sleep -Milliseconds 120
Mouse "LeftUp"
Write-Host "drag done"
