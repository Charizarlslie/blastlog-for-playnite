param([string]$Out, [int]$Width = 1600, [int]$Height = 1000, [switch]$NoMove)
# Puts Playnite's main window at the top-left of the screen at a fixed size, brings it forward and saves a screenshot of it.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
}
"@
# Real pixels at any Windows scaling (otherwise the capture covers only part of the window).
[W]::SetProcessDPIAware() | Out-Null
$p = Get-Process Playnite.DesktopApp -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 9) | Out-Null
if (-not $NoMove) { [W]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, $Width, $Height, 0x40) | Out-Null } # topmost while we capture
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 900
$r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out)
[W]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x3) | Out-Null # not topmost again
"saved $Out ($($bmp.Width)x$($bmp.Height))"
# Using Playnite keeps this checkout's lock fresh (tools/dev/playnite.ps1).
$lock = Join-Path $env:TEMP "blastlog-playnite.lock"; if (Test-Path $lock) { (Get-Item $lock).LastWriteTime = Get-Date }
