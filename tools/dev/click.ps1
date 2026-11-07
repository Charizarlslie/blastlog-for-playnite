param([int]$X, [int]$Y, [switch]$Double, [switch]$Right)
# Clicks at physical screen pixels (the coordinates in shot.ps1's screenshots, whose window sits at 0,0).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class C {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@
[C]::SetProcessDPIAware() | Out-Null
[C]::SetCursorPos($X, $Y) | Out-Null
$down = if ($Right) { 8 } else { 2 }; $up = if ($Right) { 16 } else { 4 }
$n = if ($Double) { 2 } else { 1 }
for ($i = 0; $i -lt $n; $i++) { [C]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero); [C]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60 }
# Using Playnite keeps this checkout's lock fresh (tools/dev/playnite.ps1).
$lock = Join-Path $env:TEMP "blastlog-playnite.lock"; if (Test-Path $lock) { (Get-Item $lock).LastWriteTime = Get-Date }
