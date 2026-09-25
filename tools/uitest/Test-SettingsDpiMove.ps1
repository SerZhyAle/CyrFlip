# Moves the LIVE settings window onto every monitor in turn, captures it there and puts it back
# exactly where it was - the "I dragged Settings to my other screen" check. It needs monitors with
# different scale factors to prove anything, and it is the one audit step that cannot run on a
# window built in another process: powershell.exe is system-DPI-aware by manifest, and a window it
# creates keeps the primary monitor's DPI wherever it goes (measured 2026-09-24).
#
#   .\tools\uitest\Test-SettingsDpiMove.ps1                  # settings must be open, or -Open
#   .\tools\uitest\Test-SettingsDpiMove.ps1 -Open            # asks the running CyrFlip to open it
#
# -Open sends /launcher-settings to the running instance over its own pipe (LauncherIpc), so it
# works only while the scenario launcher is enabled; it never touches the mouse. Moving the window
# does not activate it. Output: artifacts\uitest\dpi-move\<device>-<dpi>.png plus one line per
# monitor with the window DPI Windows reports - which is the point: WinForms is told, and nothing
# in the window changes size.
[CmdletBinding()]
param(
    [switch]$Open,
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts\uitest\dpi-move' }
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class DpiMove {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# The largest titled window of the process: a drop-down menu CyrFlip keeps around is also a visible
# top-level window and can come first in the enumeration.
function Get-SettingsWindow {
    Get-AppWindows | Where-Object { $_.Width -ge 300 -and $_.Title } | Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1
}
$win = Get-SettingsWindow
if (-not $win -and $Open) {
    $exe = (Get-Process CyrFlip -ErrorAction Stop | Select-Object -First 1).Path
    Start-Process -FilePath $exe -ArgumentList '/launcher-settings'
    for ($k = 0; $k -lt 26 -and -not $win; $k++) { Start-Sleep -Milliseconds 300; $win = Get-SettingsWindow }
}
if (-not $win) { throw 'The settings window is not open (open it, or pass -Open with the launcher enabled).' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$orig = New-Object DpiMove+RECT
[void][DpiMove]::GetWindowRect($win.Handle, [ref]$orig)
$w = $orig.Right - $orig.Left; $h = $orig.Bottom - $orig.Top
$flags = 0x0014   # SWP_NOZORDER | SWP_NOACTIVATE, size kept: we want to see what the window does with it
try {
    foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
        $x = $s.WorkingArea.Left + 20; $y = $s.WorkingArea.Top + 20
        [void][DpiMove]::SetWindowPos($win.Handle, [IntPtr]::Zero, $x, $y, 0, 0, $flags -bor 0x0001)   # SWP_NOSIZE
        Start-Sleep -Milliseconds 900
        $r = New-Object DpiMove+RECT; [void][DpiMove]::GetWindowRect($win.Handle, [ref]$r)
        $dpi = [DpiMove]::GetDpiForWindow($win.Handle)
        $name = ('{0}-{1}dpi.png' -f ($s.DeviceName -replace '[\\.]', ''), $dpi)
        [void](Save-WindowShot -Handle $win.Handle -Path (Join-Path $OutDir $name))
        '{0,-14} scale {1,3}%  window {2}x{3} (was {4}x{5})  -> {6}' -f ($s.DeviceName -replace '\\\\\.\\', ''), [int]($dpi / 96 * 100),
            ($r.Right - $r.Left), ($r.Bottom - $r.Top), $w, $h, $name
    }
}
finally {
    [void][DpiMove]::SetWindowPos($win.Handle, [IntPtr]::Zero, $orig.Left, $orig.Top, $w, $h, $flags)
}
"restored to $($orig.Left),$($orig.Top) ${w}x$h"
