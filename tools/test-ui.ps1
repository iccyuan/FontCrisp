# Launches FontCrisp (Release build) and screenshots its window for a visual check.
# Usage: pwsh tools/test-ui.ps1 [-Out <dir>] [-Keep]   (-Keep leaves the app running)
param([string]$Out = "$env:TEMP\fontcrisp-test", [switch]$Keep, [int]$SettleMs = 3500)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class FC {
 public struct RECT { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
New-Item -ItemType Directory -Force $Out | Out-Null
$exe = Join-Path $PSScriptRoot "..\FontCrisp\bin\Release\net10.0-windows\FontCrisp.exe"
Get-Process FontCrisp -ErrorAction SilentlyContinue | Stop-Process -Force
$p = Start-Process $exe -PassThru
Start-Sleep -Milliseconds $SettleMs
$p.Refresh()
if ($p.HasExited) { Write-Output "FontCrisp exited early (code $($p.ExitCode))"; exit 1 }
$h = $p.MainWindowHandle
[FC]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400
$r = New-Object FC+RECT; [FC]::GetWindowRect($h, [ref]$r) | Out-Null
Write-Output ("window [{0},{1}-{2},{3}] WS={4}MB" -f $r.L, $r.T, $r.R, $r.B, [int]($p.WorkingSet64 / 1MB))
$w = $r.R - $r.L; $hh = $r.B - $r.T
$b = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($r.L, $r.T, 0, 0, $b.Size); $g.Dispose()
$b.Save("$Out\window.png")
# 1:1 crop of the preview area so sub-pixel rendering can be judged without downscaling
$cw = [Math]::Min(900, $w - 40); $ch = [Math]::Min(420, $hh - 150)
$rect = New-Object System.Drawing.Rectangle -ArgumentList 40, 150, $cw, $ch
$c = $b.Clone($rect, $b.PixelFormat)
$c.Save("$Out\preview.png"); $c.Dispose(); $b.Dispose()
Write-Output "saved $Out\window.png and preview.png"
if (-not $Keep) { Stop-Process -Id $p.Id -Force }
