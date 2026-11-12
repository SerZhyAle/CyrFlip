<#
.SYNOPSIS
The pixel half of ticket S0093: the translucent carriers' contrast read off the real screen, against the model
PanelColorContractTests holds.

.DESCRIPTION
The model says what reaches the eye through a translucent window: each colour composed over the backdrop
(ThemePalette.Over) at the window's alpha (ThemePalette.WindowAlpha). This script checks the model against
the pixels Windows really composes. For each backdrop - white, black, 50% grey - and each theme - light, dark -
it covers the primary monitor with a plain window of that grey, opens on top of it:

  1. the drawing toolbar (ScreenshotToolbar) at its ToolbarOpacity - the Cancel and OK captions;
  2. the translation result window at the windows' floor (TranslucentWindowMinOpacity) - its first button;
  3. the clipboard history window at the same floor - its body (a ground check: no caption on an empty strip),

grabs them with CAPTUREBLT (a plain screen copy misses layered windows), and for each carrier samples the
ground (the median of a solid inner strip of the button face or body) and the ink (the caption pixel farthest
from the ground). It prints a table - model ratio, pixel ratio, delta - and judges each line: the pixel ratio
within 5% of the model (a caption), the ground within 3 levels per channel of the model (a body).

Nothing is stored: CyrFlip's settings and files are not touched; every window here is built in this process
by reflection into the newest built CyrFlip.exe and closed again. It moves windows over the whole primary
monitor for about half a minute - run it at the keyboard, with nothing on screen you mind covering.
Windows PowerShell 5.1 only (the exe is .NET Framework).

Exit codes (CHECK-VERDICT): 0 every line holds, 1 a line is outside its tolerance, 2 a carrier could not be
built or measured (NOT VERIFIED). The last line of the output is the verdict line.

.PARAMETER OutFile
Where the report is written as well as printed. Default temp\panel-colors\report.txt under the repository.

.EXAMPLE
powershell.exe -NoProfile -File tools\uitest\Measure-PanelColors.ps1
#>
#requires -PSEdition Desktop
param(
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutFile) { $OutFile = Join-Path $root 'temp\panel-colors\report.txt' }
$exe = @('Release', 'Debug') | ForEach-Object { Join-Path $root "src\CyrFlip\bin\$_\net48\CyrFlip.exe" } |
    Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending | Select-Object -First 1
if (-not $exe) { Write-Output 'Measure-PanelColors: NOT VERIFIED (1) - CyrFlip.exe is not built'; exit 2 }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PanelColorsDpi
{
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    public static void PerMonitorV2() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } }
}
'@
[PanelColorsDpi]::PerMonitorV2()
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$asm = [Reflection.Assembly]::LoadFrom($exe)
$any = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$palette = $asm.GetType('CyrFlip.ThemePalette')
$themeApply = $asm.GetType('CyrFlip.ThemeApply')
$capture = $asm.GetType('CyrFlip.ScreenCapture')
$floor = [int]$palette.GetField('TranslucentWindowMinOpacity', $any).GetValue($null)
$toolbarOpacity = [double]$asm.GetType('CyrFlip.ScreenshotToolbar').GetField('ToolbarOpacity', $any).GetValue($null)

function Invoke-Static($type, [string]$name, [object[]]$arguments) { $type.GetMethod($name, $any).Invoke($null, $arguments) }
function Get-Ratio([Drawing.Color]$a, [Drawing.Color]$b) { [double](Invoke-Static $palette 'Contrast' @($a, $b)) }
function Get-Seen([Drawing.Color]$colour, [double]$opacity, [Drawing.Color]$backdrop)
{
    $alpha = [int](Invoke-Static $palette 'WindowAlpha' @($opacity))
    Invoke-Static $palette 'Over' @([Drawing.Color]::FromArgb($alpha, $colour), $backdrop)
}
function Get-Theme([string]$name) { $palette.GetField($name, $any).GetValue($null) }
function Wait-Paint([int]$ms) { $until = [DateTime]::UtcNow.AddMilliseconds($ms); while ([DateTime]::UtcNow -lt $until) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }

function Get-Grab([Drawing.Rectangle]$screen)
{
    $bitmap = Invoke-Static $capture 'Grab' @($screen)
    if ($null -eq $bitmap) { throw "the screen grab of $screen failed" }
    $bitmap
}

# The ground: the median of a strip a quarter in from the top edge of the control, away from its caption.
function Get-Ground([Drawing.Bitmap]$bitmap)
{
    $y = [Math]::Max(2, [int]($bitmap.Height / 5))
    $samples = @(for ($x = 3; $x -lt $bitmap.Width - 3; $x++) { $bitmap.GetPixel($x, $y) })
    $sorted = $samples | Sort-Object { $_.R * 299 + $_.G * 587 + $_.B * 114 }
    $sorted[[int]($sorted.Count / 2)]
}

# The ink: the pixel of the middle band (where a caption sits) farthest in luminance from the ground.
function Get-Ink([Drawing.Bitmap]$bitmap, [Drawing.Color]$ground)
{
    $groundLevel = $ground.R * 299 + $ground.G * 587 + $ground.B * 114
    $best = $ground; $distance = -1
    for ($y = [int]($bitmap.Height * 0.3); $y -lt [int]($bitmap.Height * 0.7); $y++)
    {
        for ($x = 2; $x -lt $bitmap.Width - 2; $x++)
        {
            $p = $bitmap.GetPixel($x, $y)
            $d = [Math]::Abs($p.R * 299 + $p.G * 587 + $p.B * 114 - $groundLevel)
            if ($d -gt $distance) { $distance = $d; $best = $p }
        }
    }
    $best
}

function Find-Control($parent, [scriptblock]$match)
{
    foreach ($child in $parent.Controls)
    {
        if (& $match $child) { return $child }
        $found = Find-Control $child $match
        if ($found) { return $found }
    }
    $null
}

$lines = New-Object Collections.Generic.List[string]
$results = New-Object Collections.Generic.List[object]
function Add-Line([string]$text) { $lines.Add($text); Write-Output $text }

function Measure-Caption([string]$carrier, [string]$theme, [int]$grey, $control, [Drawing.Color]$ink, [Drawing.Color]$ground, [double]$opacity)
{
    $backdrop = [Drawing.Color]::FromArgb($grey, $grey, $grey)
    $modelInk = Get-Seen $ink $opacity $backdrop; $modelGround = Get-Seen $ground $opacity $backdrop
    $model = Get-Ratio $modelInk $modelGround
    $bitmap = Get-Grab ($control.RectangleToScreen($control.ClientRectangle))
    try
    {
        $pixelGround = Get-Ground $bitmap
        $pixelInk = Get-Ink $bitmap $pixelGround
        $pixel = Get-Ratio $pixelInk $pixelGround
    }
    finally { $bitmap.Dispose() }
    $delta = [Math]::Abs($pixel - $model) / $model
    $ok = $delta -le 0.05
    Add-Line ('{0,-34} {1,-5} #{2:X2}  model {3,6:0.00}:1  pixel {4,6:0.00}:1  delta {5,5:0.0}%  {6}' -f $carrier, $theme, $grey, $model, $pixel, ($delta * 100), $(if ($ok) { 'PASS' } else { 'FAIL' }))
    $results.Add($ok)
}

function Measure-Ground([string]$carrier, [string]$theme, [int]$grey, $control, [Drawing.Color]$ground, [double]$opacity)
{
    $backdrop = [Drawing.Color]::FromArgb($grey, $grey, $grey)
    $model = Get-Seen $ground $opacity $backdrop
    $bitmap = Get-Grab ($control.RectangleToScreen($control.ClientRectangle))
    try { $pixel = Get-Ground $bitmap } finally { $bitmap.Dispose() }
    $diff = [Math]::Max([Math]::Abs($pixel.R - $model.R), [Math]::Max([Math]::Abs($pixel.G - $model.G), [Math]::Abs($pixel.B - $model.B)))
    $ok = $diff -le 3
    Add-Line ('{0,-34} {1,-5} #{2:X2}  model #{3:X2}{4:X2}{5:X2}  pixel #{6:X2}{7:X2}{8:X2}  diff {9} levels  {10}' -f $carrier, $theme, $grey, $model.R, $model.G, $model.B, $pixel.R, $pixel.G, $pixel.B, $diff, $(if ($ok) { 'PASS' } else { 'FAIL' }))
    $results.Add($ok)
}

$notVerified = 0
$screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
$config = [Activator]::CreateInstance($asm.GetType('CyrFlip.AppConfig'))
$config.UiLanguage = 'English'
$config.ClipboardHistoryOpacity = $floor
$config.TranslateWindowOpacity = $floor
$identity = [Func[string, string]] { param($s) $s }

Add-Line "Measure-PanelColors - $exe"
Add-Line "toolbar opacity $toolbarOpacity, windows floor $floor%"
Add-Line ''

foreach ($grey in 255, 0, 128)
{
    $backdrop = New-Object Windows.Forms.Form -Property @{
        FormBorderStyle = 'None'; StartPosition = 'Manual'; Bounds = $screen; TopMost = $true; ShowInTaskbar = $false
        BackColor = [Drawing.Color]::FromArgb($grey, $grey, $grey)
    }
    $backdrop.Show(); Wait-Paint 300
    try
    {
        foreach ($themeName in 'Light', 'Dark')
        {
            $theme = Get-Theme $themeName

            # 1. The drawing toolbar.
            try
            {
                $toolType = $asm.GetType('CyrFlip.AnnotationTool')
                $toolbar = [Activator]::CreateInstance($asm.GetType('CyrFlip.ScreenshotToolbar'), $any, $null,
                    @([Enum]::GetValues($toolType)[0], [Drawing.Color]::Red, $identity), $null)
                $toolbar.Location = New-Object Drawing.Point(($screen.Left + 200), ($screen.Top + 200))
                Invoke-Static $themeApply 'Apply' @($toolbar, $theme)
                $toolbar.Show(); Wait-Paint 500
                $cancel = Find-Control $toolbar { param($c) $c -is [Windows.Forms.Button] -and $c.Text -eq 'Cancel' }
                $ok = Find-Control $toolbar { param($c) $c -is [Windows.Forms.Button] -and $c.Text -eq 'OK' }
                if (-not $cancel -or -not $ok) { throw 'the Cancel or OK button was not found' }
                Measure-Caption 'toolbar Cancel (text.primary/control)' $themeName $grey $cancel $theme.TextPrimary $theme.Control $toolbarOpacity
                Measure-Caption 'toolbar OK (accent.ink/accent)' $themeName $grey $ok $theme.AccentInk $theme.Accent $toolbarOpacity
            }
            catch { Add-Line "toolbar                            $themeName #$('{0:X2}' -f $grey)  NOT VERIFIED - $($_.Exception.GetBaseException().Message)"; $notVerified++ }
            finally { if ($toolbar) { $toolbar.Close(); $toolbar.Dispose(); $toolbar = $null } }

            # 2. The translation result window at the floor.
            try
            {
                $translate = [Activator]::CreateInstance($asm.GetType('CyrFlip.TranslationResultWindow'), $any, $null, @($config, 'English'), $null)
                $translate.StartPosition = 'Manual'
                $translate.Location = New-Object Drawing.Point(($screen.Left + 200), ($screen.Top + 320))
                Invoke-Static $themeApply 'Apply' @($translate, $theme)
                $translate.ApplyOpacity()
                $translate.Show(); Wait-Paint 500
                $button = Find-Control $translate { param($c) $c -is [Windows.Forms.Button] -and $c.Visible -and $c.Text.Length -gt 0 }
                if (-not $button) { throw 'no visible button' }
                Measure-Caption "translation button '$($button.Text)'" $themeName $grey $button $theme.TextPrimary $button.BackColor ($floor / 100.0)
            }
            catch { Add-Line "translation window                 $themeName #$('{0:X2}' -f $grey)  NOT VERIFIED - $($_.Exception.GetBaseException().Message)"; $notVerified++ }
            finally { if ($translate) { $translate.Close(); $translate.Dispose(); $translate = $null } }

            # 3. The clipboard history window at the floor: its empty body.
            try
            {
                $service = [Activator]::CreateInstance($asm.GetType('CyrFlip.ClipboardHistoryService'), @($false, $true))
                $history = [Activator]::CreateInstance($asm.GetType('CyrFlip.ClipboardHistoryWindow'), $any, $null,
                    @($service, $config, [Action] { }, $null), $null)
                $history.StartPosition = 'Manual'
                $history.Location = New-Object Drawing.Point(($screen.Left + 700), ($screen.Top + 200))
                Invoke-Static $themeApply 'Apply' @($history, $theme)
                $history.ApplyOpacity()
                $history.Show(); Wait-Paint 500
                Measure-Ground 'history body (surface.raised)' $themeName $grey $history $theme.SurfaceRaised ($floor / 100.0)
            }
            catch { Add-Line "history window                     $themeName #$('{0:X2}' -f $grey)  NOT VERIFIED - $($_.Exception.GetBaseException().Message)"; $notVerified++ }
            finally
            {
                if ($history) { $history.Close(); $history.Dispose(); $history = $null }
                if ($service -is [IDisposable]) { $service.Dispose() }; $service = $null
            }
        }
    }
    finally { $backdrop.Close(); $backdrop.Dispose() }
}

$failed = @($results | Where-Object { -not $_ }).Count
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
Add-Line ''
Add-Line "report: $OutFile"
if ($failed -gt 0) { $verdict = "Measure-PanelColors: FAIL ($failed)"; $code = 1 }
elseif ($notVerified -gt 0) { $verdict = "Measure-PanelColors: NOT VERIFIED ($notVerified)"; $code = 2 }
else { $verdict = 'Measure-PanelColors: PASS'; $code = 0 }
$lines.Add($verdict)
[IO.File]::WriteAllLines($OutFile, $lines, (New-Object Text.UTF8Encoding($false)))
Write-Output $verdict
exit $code
