<#
.SYNOPSIS
WINDOWS-UI section 11 / ICON-RENDER section 14 on the real dialogs: every command button carries its
glyph, the answer pair is green and red, and the numbers the contract leaves as reference values
(16 px, 6 px, 12 percent) are measured on rendered pixels.

.DESCRIPTION
Compiles tools\uitest\ButtonGlyphHarness.cs with the framework's own csc.exe - and with the
application's own manifest, because WinForms takes its per-monitor DPI awareness from the exe, so a
PowerShell host would show every glyph at 96 DPI whatever the screen does - then runs it. The harness
builds each dialog of the product in the languages asked for (reflection into the built exe; no tray
app, no mouse, no keyboard), puts it through the theme walk (light, dark, high contrast, then the one
measured - a live theme change, not a rebuild), shows it off-screen and draws it with DrawToBitmap.
For every button that carries a glyph it reports

  - the glyph's ink against the face pixel it is actually drawn on (3 : 1 required) and the caption's
    against the same pixel (4.5 : 1), the tinted face included;
  - the measured gap between the end of the glyph and the start of the caption and the glyph's own
    ink width, in device pixels, next to the DPI the button reports;
  - the button's size, so a row of narrow buttons can be read against the longest caption.

A PNG per surface, language and theme and measurements.csv are written to -OutDir. Exit code 0 = every
number held, 1 = at least one did not (listed), 2 = could not run.

What it does not do: it does not move the pointer, press Escape or Enter, or drive the settings window
(Save-SettingsShots.ps1 / Audit-SettingsWindow.ps1 own that). Escape / Enter / the default focus are
held by the unit tests (ButtonGlyphTests, DialogLayoutTests). It runs at the DPI the screen has: to see
150 or 200 percent, change Windows' scaling and run it again.

.PARAMETER ExePath
The built CyrFlip.exe. Default: src\CyrFlip\bin\Release\net48\CyrFlip.exe of this repository.

.PARAMETER OutDir
Where the PNGs and measurements.csv go. Default: a new folder under %TEMP%.

.PARAMETER Languages
UI language endonyms to build the dialogs in (UTF-8 in this file is avoided: pass them by name or by
code point with -LanguagesFile).

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-ButtonGlyphs.ps1
#>
[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\src\CyrFlip\bin\Release\net48\CyrFlip.exe'),
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) ('cyrflip-buttons-' + (Get-Date -Format 'yyMMdd_HHmmss'))),
    [string[]]$Languages = @('English', 'Deutsch'),
    [string]$LanguagesFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) { Write-Error "Not built: $ExePath"; exit 2 }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { Write-Error 'csc.exe of .NET Framework 4 not found.'; exit 2 }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$source = Join-Path $PSScriptRoot 'ButtonGlyphHarness.cs'
$manifest = Join-Path $PSScriptRoot '..\..\src\CyrFlip\app.manifest'
$harness = Join-Path $OutDir 'ButtonGlyphHarness.exe'
& $csc /nologo /target:exe "/out:$harness" "/win32manifest:$manifest" /r:System.Windows.Forms.dll /r:System.Drawing.dll $source
if ($LASTEXITCODE -ne 0) { Write-Error 'The harness did not compile.'; exit 2 }
# The same startup record the application ships: WinForms switches its 4.7+ per-monitor behaviour on only
# for a process that says it targets 4.8.
[IO.File]::WriteAllText("$harness.config", '<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>')

if ($LanguagesFile) { $Languages = @(Get-Content -LiteralPath $LanguagesFile -Encoding UTF8 | Where-Object { $_ }) }
$exe = (Resolve-Path -LiteralPath $ExePath).Path
# The harness must sit beside nothing it does not need; the assembly under test is loaded by path.
& $harness -exe $exe -out $OutDir -languages ($Languages -join '|')
exit $LASTEXITCODE
