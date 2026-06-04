# Dev aid: screenshot a process's main window. Usage: shoot-window.ps1 [-Name VpncBar] [-Out path.png]
param([string]$Name = 'VpncBar', [string]$Out = "$PSScriptRoot\..\window-shot.png")

Add-Type @'
using System;
using System.Runtime.InteropServices;
public class WinShot {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@ -ReferencedAssemblies System.Runtime.InteropServices
Add-Type -AssemblyName System.Drawing

[WinShot]::SetProcessDPIAware() | Out-Null
$p = Get-Process $Name -ErrorAction Stop | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (-not $p) { throw "no window for process '$Name'" }
[WinShot]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
$r = New-Object WinShot+RECT
[WinShot]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out)
$g.Dispose(); $bmp.Dispose()
"saved $w x $h -> $Out"
