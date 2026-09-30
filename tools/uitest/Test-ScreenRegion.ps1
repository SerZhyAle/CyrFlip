<#
.SYNOPSIS
The screen region capture (ticket S0026): the frozen grab, the crop, the PNG and DIB encodings, the
CAPTURE-OUTPUT saver on a real disk, the clipboard write, and GDI handles across a hundred captures.

.DESCRIPTION
Two halves, because only one of them can run unattended.

The first drives the real code by reflection into the built exe - CyrFlip ignores injected keystrokes,
so a synthesized chord would prove nothing:

  1. a throwaway window paints four known colours at a known place; ScreenCapture.Grab (BitBlt with
     SRCCOPY | CAPTUREBLT) freezes the virtual screen and ScreenCapture.Crop takes exactly that window's
     rectangle - every quadrant must come back in its colour, at the right size;
  2. the PNG decodes to the same size and the DIB is a bottom-up 32bpp BI_RGB of it;
  3. ScreenshotSaver writes into a temp folder: the name is screenshot_yyMMdd_HHmmss.png, a second save
     in the same second is " (2)", and the file is byte-equal to the PNG the clipboard gets;
  4. with -Clipboard only: Win32Clipboard.TrySetImage puts PNG and CF_DIB on the clipboard in one open.
     This REPLACES YOUR CLIPBOARD (an image cannot be handed back the way text is), so it is opt-in;
  5. a hundred grab/crop/encode rounds do not grow the GDI or USER handle count.

The second half is the checklist of what only a person can check: the chord, the drag across mixed-DPI
monitors, the paste into Paint (the DIB) and into a browser or Telegram (the PNG), the cancels, a
tooltip frozen into the picture, the caret marker absent from it.

Run it from Windows PowerShell (the exe is .NET Framework).

.PARAMETER InteropOnly
Run only the unattended half.

.PARAMETER Clipboard
Also run scene 4, which replaces whatever is on the clipboard.

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-ScreenRegion.ps1 -InteropOnly
#>
[CmdletBinding()]
param(
    [switch]$InteropOnly,
    [switch]$Clipboard,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

if (-not ('ScreenRegionUi' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ScreenRegionUi {
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormatW(string name);
}
'@
}

$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}{2}" -f $mark, $name, $(if ($detail) { " - $detail" } else { '' })) -ForegroundColor $colour
    if (-not $ok) { $script:failures += $name }
}

# ---- reflection into the built exe --------------------------------------------------------------
$exe = Get-CyrFlipExe -Configuration $Configuration
$asm = [Reflection.Assembly]::LoadFrom($exe)
$bf = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
function T([string]$name) { $asm.GetType("CyrFlip.$name", $true) }
function Call($type, [string]$method, [object[]]$arguments, $target = $null) {
    $m = $type.GetMethods($bf) | Where-Object { $_.Name -eq $method -and $_.GetParameters().Count -eq $arguments.Count } | Select-Object -First 1
    if (-not $m) { throw "No $($type.Name).$method/$($arguments.Count)" }
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].psobject.BaseObject }
    }
    , $m.Invoke($target, $arguments)
}
$Capture = T 'ScreenCapture'
$Image = T 'ClipboardImage'
$SaverType = T 'ScreenshotSaver'

Write-Host "Screen region capture (S0026) - unattended half" -ForegroundColor Cyan

# ---- 1. grab and crop a known pattern ------------------------------------------------------------
$colours = @([Drawing.Color]::FromArgb(255, 200, 30, 40), [Drawing.Color]::FromArgb(255, 30, 160, 60),
             [Drawing.Color]::FromArgb(255, 40, 70, 210), [Drawing.Color]::FromArgb(255, 240, 200, 20))
$size = 240
$form = New-Object Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.TopMost = $true; $form.ShowInTaskbar = $false
$primary = [Windows.Forms.Screen]::PrimaryScreen.Bounds
$form.Bounds = New-Object Drawing.Rectangle(($primary.X + 120), ($primary.Y + 120), $size, $size)
$half = $size / 2
foreach ($i in 0..3) {
    $panel = New-Object Windows.Forms.Panel
    $panel.BackColor = $colours[$i]
    $panel.Bounds = New-Object Drawing.Rectangle((($i % 2) * $half), ([Math]::Floor($i / 2) * $half), $half, $half)
    $form.Controls.Add($panel)
}
$form.Show(); $form.Refresh()
for ($n = 0; $n -lt 10; $n++) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }

$virtual = $Capture.GetProperty('VirtualScreen', $bf).GetValue($null)
$frame = Call $Capture 'Grab' @($virtual)
$region = $form.Bounds
$form.Close(); $form.Dispose()
Check 'the grab returned the virtual screen' ($null -ne $frame -and $frame.Width -eq $virtual.Width -and $frame.Height -eq $virtual.Height) "$($virtual.Width)x$($virtual.Height) at $($virtual.X),$($virtual.Y)"
$crop = Call $Capture 'Crop' @($frame, $virtual, $region)
$frame.Dispose()
Check 'the crop has the window''s size' ($crop.Width -eq $size -and $crop.Height -eq $size) "$($crop.Width)x$($crop.Height)"
$quadrantsOk = $true
foreach ($i in 0..3) {
    $x = ($i % 2) * $half + $half / 2; $y = [Math]::Floor($i / 2) * $half + $half / 2
    $got = $crop.GetPixel($x, $y)
    if ($got.R -ne $colours[$i].R -or $got.G -ne $colours[$i].G -or $got.B -ne $colours[$i].B) {
        $quadrantsOk = $false; Write-Host "    quadrant $i at $x,$y is $got, expected $($colours[$i])"
    }
}
Check 'every quadrant came back in its colour, in its place' $quadrantsOk ''

# ---- 2. the encodings -----------------------------------------------------------------------------
$png = Call $Image 'EncodePng' @($crop)
$dib = Call $Image 'EncodeDib' @($crop)
$stream = New-Object IO.MemoryStream(, $png)
$decoded = New-Object Drawing.Bitmap($stream)
Check 'the PNG decodes to the same size' ($decoded.Width -eq $size -and $decoded.Height -eq $size) ''
$decoded.Dispose(); $stream.Dispose()
Check 'the DIB is bottom-up 32bpp' ([BitConverter]::ToInt32($dib, 8) -eq $size -and [BitConverter]::ToInt16($dib, 14) -eq 32 -and $dib.Length -eq 40 + $size * $size * 4) ''

# ---- 3. the saver on a real disk ------------------------------------------------------------------
$folder = Join-Path ([IO.Path]::GetTempPath()) ("CyrFlip-S0026-" + [Guid]::NewGuid().ToString('N'))
try {
    $saverInstance = [Activator]::CreateInstance($SaverType, $true)
    $started = Get-Date
    $first = Call $SaverType 'Save' @($png, $started, $folder) $saverInstance
    $second = Call $SaverType 'Save' @($png, $started, $folder) $saverInstance
    $stem = 'screenshot_' + $started.ToString('yyMMdd_HHmmss', [Globalization.CultureInfo]::InvariantCulture)
    $firstPath = $first.GetType().GetProperty('FilePath').GetValue($first)
    $secondPath = $second.GetType().GetProperty('FilePath').GetValue($second)
    Check 'the file carries the contract''s name' ((Split-Path $firstPath -Leaf) -eq "$stem.png") (Split-Path $firstPath -Leaf)
    Check 'a second save in the same second is (2)' ((Split-Path $secondPath -Leaf) -eq "$stem (2).png") (Split-Path $secondPath -Leaf)
    $bytes = [IO.File]::ReadAllBytes($firstPath)
    Check 'the file is byte-equal to the clipboard PNG' ([Linq.Enumerable]::SequenceEqual([byte[]]$bytes, [byte[]]$png)) "$($bytes.Length) bytes"
    Check 'no temporary file is left' (@(Get-ChildItem $folder -Filter '*.tmp').Count -eq 0) ''
}
finally {
    Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
}

# ---- 4. the clipboard (opt-in) --------------------------------------------------------------------
if ($Clipboard) {
    $ok = Call (T 'Win32Clipboard') 'TrySetImage' @($png, $dib)
    $pngFormat = [ScreenRegionUi]::RegisterClipboardFormatW('PNG')
    Check 'TrySetImage reports success' ([bool]$ok) ''
    Check 'PNG is on the clipboard' ([ScreenRegionUi]::IsClipboardFormatAvailable($pngFormat)) ''
    Check 'CF_DIB is on the clipboard' ([ScreenRegionUi]::IsClipboardFormatAvailable(8)) ''
}
else {
    Write-Host "  [SKIP] the clipboard write - run with -Clipboard (it replaces what is on the clipboard)" -ForegroundColor DarkGray
}
$crop.Dispose()

# ---- 5. handles across a hundred rounds -----------------------------------------------------------
function Handles { @([CyrFlipUi]::GuiResources([uint32]$PID) -split ' ' | ForEach-Object { [int]$_ }) }
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
$before = Handles
for ($n = 0; $n -lt 100; $n++) {
    $f = Call $Capture 'Grab' @($virtual)
    $c = Call $Capture 'Crop' @($f, $virtual, (New-Object Drawing.Rectangle($virtual.X, $virtual.Y, 300, 200)))
    $f.Dispose()
    [void](Call $Image 'EncodePng' @($c)); [void](Call $Image 'EncodeDib' @($c))
    $c.Dispose()
}
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
$after = Handles
Check 'a hundred captures do not grow GDI/USER handles' (($after[0] - $before[0]) -le 2 -and ($after[1] - $before[1]) -le 2) "GDI $($before[0])->$($after[0]), USER $($before[1])->$($after[1])"

if ($InteropOnly) {
    if ($failures.Count) { Write-Host "`n$($failures.Count) check(s) failed." -ForegroundColor Red; exit 1 }
    Write-Host "`nAll unattended checks passed." -ForegroundColor Green
    exit 0
}

# ---- the human half -------------------------------------------------------------------------------
Write-Host "`nNow by hand (CyrFlip running):" -ForegroundColor Cyan
@(
    'Press the chord (default Ctrl+Shift+PrintScreen) and, separately, pick "Screen region capture" in the tray menu: the screen freezes and dims, the pointer is a cross.',
    'Drag a rectangle, release: the overlay closes, the focus is back in the app you came from.',
    'Ctrl+V into Paint - the picture appears (CF_DIB).',
    'Ctrl+V into a browser text field, Telegram or Slack - the picture appears (PNG).',
    'Esc, a right click, and a click without a drag each cancel; the clipboard is unchanged.',
    'Hover a tooltip or open a menu, press the chord: the tooltip / menu is in the picture.',
    'The layout marker next to the caret is NOT in the picture.',
    'Two monitors with different scaling: a drag across both is one continuous picture, no seam.',
    'With "Also save captures to a folder" on: the file appears in Pictures\Screenshots, named screenshot_yyMMdd_HHmmss.png.',
    'Point the folder at a pulled USB stick: the capture still reaches the clipboard and a balloon names Pictures\Screenshots.',
    'With Windows 11 "Use the Print screen key to open Snipping Tool" ON: the chord does not also open Snipping Tool (Q1).'
) | ForEach-Object { Write-Host "  [ ] $_" }

if ($failures.Count) { Write-Host "`n$($failures.Count) unattended check(s) failed." -ForegroundColor Red; exit 1 }
