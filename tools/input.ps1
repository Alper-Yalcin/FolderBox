# Simulates input for manual-style testing. Usage:
#   input.ps1 click X Y        | dblclick X Y | rclick X Y | drag X1 Y1 X2 Y2 | key VK | move X Y
param([string]$Action, [int]$A = 0, [int]$B = 0, [int]$C = 0, [int]$D = 0)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Sim {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public const uint LDOWN=2, LUP=4, RDOWN=8, RUP=16;
}
"@
[Sim]::SetProcessDPIAware() | Out-Null
function Down { [Sim]::mouse_event([Sim]::LDOWN,0,0,0,[UIntPtr]::Zero) }
function Up { [Sim]::mouse_event([Sim]::LUP,0,0,0,[UIntPtr]::Zero) }
switch ($Action) {
  "move"     { [Sim]::SetCursorPos($A,$B) | Out-Null }
  "click"    { [Sim]::SetCursorPos($A,$B) | Out-Null; Start-Sleep -Milliseconds 80; Down; Start-Sleep -Milliseconds 50; Up }
  "dblclick" { [Sim]::SetCursorPos($A,$B) | Out-Null; Start-Sleep -Milliseconds 80; Down; Up; Start-Sleep -Milliseconds 90; Down; Up }
  "rclick"   { [Sim]::SetCursorPos($A,$B) | Out-Null; Start-Sleep -Milliseconds 80; [Sim]::mouse_event([Sim]::RDOWN,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 50; [Sim]::mouse_event([Sim]::RUP,0,0,0,[UIntPtr]::Zero) }
  "drag"     {
    [Sim]::SetCursorPos($A,$B) | Out-Null; Start-Sleep -Milliseconds 80; Down; Start-Sleep -Milliseconds 100
    $steps = 20
    $px = $A; $py = $B
    for ($i = 1; $i -le $steps; $i++) {
      $x = [int]($A + ($C - $A) * $i / $steps); $y = [int]($B + ($D - $B) * $i / $steps)
      # absolute move over the virtual desktop (MOVE|ABSOLUTE|VIRTUALDESK)
      $vx = [System.Windows.Forms.SystemInformation]::VirtualScreen
      $ax = [int](($x - $vx.Left) * 65535 / $vx.Width); $ay = [int](($y - $vx.Top) * 65535 / $vx.Height)
      [Sim]::mouse_event(0x8001 -bor 0x4000, $ax, $ay, 0, [UIntPtr]::Zero); $px = $x; $py = $y
      Start-Sleep -Milliseconds 15
    }
    Start-Sleep -Milliseconds 100; Up
  }
  "hold"     { [Sim]::SetCursorPos($A,$B) | Out-Null; Start-Sleep -Milliseconds 80; Down; Start-Sleep -Milliseconds 700; Up }
  "key"      { [Sim]::keybd_event([byte]$A,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [Sim]::keybd_event([byte]$A,0,2,[UIntPtr]::Zero) }
}
Write-Host "done $Action"
