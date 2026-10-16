<#
.SYNOPSIS
The screen captures (tickets S0026, S0057 and S0071): the frozen grab, the crop, the PNG and DIB
encodings, the CAPTURE-OUTPUT saver on a real disk, the clipboard write, GDI handles across a hundred
captures, the S0071 annotation compose and editor OK/Cancel, the S0071 chord re-key migration, and
the live full-capture chord.

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
  5. a hundred grab/crop/encode rounds do not grow the GDI or USER handle count;
  6. with -FolderChord only: the screenshots-folder chord (S0057) - a human presses the key and the
     script verifies the Explorer window, its LocationURL and that it is in front (L1);
  7. S0071 unattended: AnnotationComposer composes a red rectangle over a grabbed frame and the
     annotated PNG differs from the plain crop exactly inside the shape's bounds; an AnnotationEditor
     built over a synthetic frame answers OK with the composed image and Cancel with nothing;
  8. with -Migration only: the S0071 chord re-key through the real exe - the script stages
     ScreenshotHotkey = Ctrl+PrintScreen (your value is backed up and restored), CyrFlip must NOT be
     running; starting it once must leave Ctrl+F12 and the migration marker in the registry;
  9. with -FullChord only: the S0071 full-capture chord - a human presses Ctrl+F11 and the script
     verifies PNG and CF_DIB of the virtual screen's size on the clipboard. REPLACES YOUR CLIPBOARD.

The second half is the checklist of what only a person can check: the chords, the drag across
mixed-DPI monitors, the paste into Paint (the DIB) and into a browser or Telegram (the PNG), the
cancels, a tooltip frozen into the picture, the caret marker absent from it, the S0057 folder-chord
live checks, and the S0071 editor checks (the toolbar under the pointer and draggable, the 0.67 look
on light, dark and high contrast, Ctrl+Z, focus-loss cancellation).

Run it from Windows PowerShell (the exe is .NET Framework).

.PARAMETER InteropOnly
Run only the unattended half.

.PARAMETER Clipboard
Also run scene 4, which replaces whatever is on the clipboard.

.PARAMETER FolderChord
Also run scene 6 (S0057): CyrFlip must be running with saving on; the script asks you to press the
chord and then checks the Explorer window it opened.

.PARAMETER Migration
Also run scene 8 (S0071): stages the legacy chord in the real registry (backed up first), starts the
built exe once and reads the re-keyed values back. CyrFlip must not be running.

.PARAMETER FullChord
Also run scene 9 (S0071): CyrFlip must be running; the script asks you to press Ctrl+F11 and checks
the clipboard. Replaces whatever is on the clipboard.

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-ScreenRegion.ps1 -InteropOnly
#>
[CmdletBinding()]
param(
    [switch]$InteropOnly,
    [switch]$Clipboard,
    [switch]$FolderChord,
    [switch]$Migration,
    [switch]$FullChord,
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
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
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
    # CAPTURE-OUTPUT rule 16: the saver injects a 19-byte tIME chunk (signature + IHDR + tIME + the rest).
    $body = ($bytes.Length -eq $png.Length + 19) `
        -and [Linq.Enumerable]::SequenceEqual([byte[]]($bytes[0..32] + $bytes[52..($bytes.Length - 1)]), [byte[]]($png[0..32] + $png[33..($png.Length - 1)]))
    $timeOk = (([int]$bytes[41] * 256 + [int]$bytes[42]) -eq $started.Year) -and ([int]$bytes[43] -eq $started.Month) `
        -and ([int]$bytes[44] -eq $started.Day) -and ([int]$bytes[45] -eq $started.Hour) -and ([int]$bytes[46] -eq $started.Minute)
    Check 'the file is the clipboard PNG plus its tIME chunk (rule 16)' ($body -and $timeOk) "$($bytes.Length) bytes, started $($started.ToString('yyyy-MM-dd HH:mm'))"
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

# ---- 6. the screenshots folder chord (S0057, -FolderChord) ----------------------------------------
# Live, so a human presses the key: CyrFlip must be running with saving on and the folder existing.
if ($FolderChord) {
    $settings = Get-ItemProperty 'HKCU:\Software\CyrFlip' -ErrorAction SilentlyContinue
    Check 'CyrFlip is running with settings present' ($null -ne $settings) ''
    if ($null -ne $settings) {
        $saving = [bool]($settings.ScreenshotSaveEnabled)
        Check 'saving screenshots to a folder is on (Graphics tab)' $saving ''
        # The folder the settings show: the user's choice, else the Screenshots known folder.
        $shown = [string]$settings.ScreenshotFolder
        if (-not $shown) {
            try { $shown = [string](New-Object -ComObject Shell.Application).NameSpace('shell:Screenshots').Self.Path }
            catch { $shown = '' }
        }
        $shown = $shown.TrimEnd('\')
        Write-Host "  The folder to open: $shown" -ForegroundColor Cyan
        Write-Host '  Press the folder chord now (default Ctrl+Shift+PrintScreen).' -ForegroundColor Cyan
        Read-Host '  Press Enter once the Explorer window has opened (or should have)'

        $shell = New-Object -ComObject Shell.Application
        $hit = $null
        foreach ($window in $shell.Windows()) {
            try {
                $url = [string]$window.LocationURL
                if ($url -and $url.StartsWith('file:///')) {
                    $path = ([Uri]$url).LocalPath.TrimEnd('\')
                    if ($path -ieq $shown) { $hit = $window; break }
                }
            }
            catch { }
        }
        Check 'an Explorer window is open on the folder' ([bool]$hit) "LocationURL of $shown"
        if ($hit) {
            $foreground = [ScreenRegionUi]::GetForegroundWindow()
            Check 'the window is in front (S0057 L1)' ($foreground -eq [IntPtr][int]$hit.HWND) "foreground $foreground, window $($hit.HWND)"
        }
    }
}
else {
    Write-Host '  [SKIP] the folder chord - run with -FolderChord (CyrFlip running, saving on)' -ForegroundColor DarkGray
}

# ---- 7. S0071 unattended: the annotation compose and the editor's OK/Cancel ------------------------
$Composer = T 'AnnotationComposer'
$ShapeType = T 'AnnotationShape'
$KindType = T 'AnnotationKind'
$EditorType = T 'AnnotationEditor'
$Palette = T 'AnnotationPalette'
$red = $Palette.GetProperty('Default', $bf).GetValue($null)
$white = [Drawing.Color]::FromArgb(255, 255, 255, 255)
# AnnotationKind.FilledRectangle (the order is the toolbar's: ellipse, filled ellipse, rectangle, filled rectangle...)
$filledRectangle = [Enum]::ToObject($KindType, 3)

$composeFrame = Call $Capture 'Grab' @($virtual)
try {
    $composeRegion = New-Object Drawing.Rectangle(($primary.X + 40), ($primary.Y + 40), 160, 120)
    $shape = [Activator]::CreateInstance($ShapeType, @($filledRectangle,
        [Drawing.Point]::new($composeRegion.X + 10, $composeRegion.Y + 10),
        [Drawing.Point]::new(($composeRegion.X + 150), ($composeRegion.Y + 110)), $red, 0))
    # A typed array: IReadOnlyList<AnnotationShape> takes an AnnotationShape[], not object[].
    $shapes = [Array]::CreateInstance($ShapeType, 1)
    $shapes.SetValue($shape, 0)
    $composed = Call $Composer 'Compose' @($composeFrame, $virtual, $composeRegion, $shapes, 3)
    Check 'the composed image is exactly the region''s size' ($composed.Width -eq 160 -and $composed.Height -eq 120) "$($composed.Width)x$($composed.Height)"

    $plain = Call $Capture 'Crop' @($composeFrame, $virtual, $composeRegion)
    $insideOk = $true; $outsideOk = $true
    for ($y = 20; $y -lt 100; $y += 10) {
        for ($x = 20; $x -lt 140; $x += 10) {
            if ($composed.GetPixel($x, $y).ToArgb() -eq $plain.GetPixel($x, $y).ToArgb()) { $insideOk = $false }
        }
    }
    foreach ($p in @(@(5, 5), @(155, 115), @(80, 5), @(5, 60))) {
        if ($composed.GetPixel($p[0], $p[1]).ToArgb() -ne $plain.GetPixel($p[0], $p[1]).ToArgb()) { $outsideOk = $false }
    }
    Check 'the annotated PNG differs from the plain crop inside the shape''s bounds' $insideOk ''
    Check 'the annotated PNG is the plain crop outside the shape''s bounds' $outsideOk ''
    $plain.Dispose()

    # The editor's OK/Cancel, without its windows: a synthetic frame, the two exits. (Show() opens
    # real per-monitor windows; the delivery path under test here does not need them.)
    $editorVirtual = New-Object Drawing.Rectangle(0, 0, 64, 48)
    $translate = [Func[string, string]] { param($s) $s }
    # Typed arrays for the ctor's IEnumerable<Rectangle> parameter - object[] will not convert.
    $monitors = [Array]::CreateInstance([Drawing.Rectangle], 1)
    $monitors.SetValue($editorVirtual, 0)
    # The ctor is invoked through its MethodInfo: the binder behind Activator.CreateInstance does not
    # match a Nullable<Rectangle> parameter.
    $editorCtor = $EditorType.GetConstructors($bf) | Where-Object { $_.GetParameters().Count -eq 6 } | Select-Object -First 1
    $editorArgs = { param($frameBitmap) , @([Drawing.Bitmap]$frameBitmap, $editorVirtual, $editorVirtual,
        $monitors, [IntPtr]::Zero, $translate) }
    $editorFrame = New-Object Drawing.Bitmap(64, 48)
    $g = [Drawing.Graphics]::FromImage($editorFrame); $g.Clear($white); $g.Dispose()
    $editor = $editorCtor.Invoke((& $editorArgs $editorFrame))
    $script:delivered = $null
    $editor.add_Delivered({ param($bmp) $script:delivered = $bmp })
    $drawn = [Activator]::CreateInstance($ShapeType, @($filledRectangle,
        [Drawing.Point]::new(8, 8), [Drawing.Point]::new(40, 30), $red, 0))
    $editor.Shapes.GetType().GetMethod('Add').Invoke($editor.Shapes, @($drawn))
    $editor.GetType().GetMethod('Ok', $bf).Invoke($editor, @())
    Check 'OK delivers a composed image' ($null -ne $script:delivered -and $script:delivered.Width -eq 64 -and $script:delivered.Height -eq 48) ''
    Check 'OK''s image carries the shape''s ink' ($null -ne $script:delivered -and $script:delivered.GetPixel(20, 18).R -gt 180 -and $script:delivered.GetPixel(20, 18).B -lt 150) ''
    if ($script:delivered) { $script:delivered.Dispose() }
    $editor.Dispose()

    $cancelFrame = New-Object Drawing.Bitmap(64, 48)
    $g = [Drawing.Graphics]::FromImage($cancelFrame); $g.Clear($white); $g.Dispose()
    $editor2 = $editorCtor.Invoke((& $editorArgs $cancelFrame))
    $script:delivered2 = $null; $script:cancelled2 = $false
    $editor2.add_Delivered({ param($bmp) $script:delivered2 = $bmp })
    $editor2.add_Cancelled({ $script:cancelled2 = $true })
    $editor2.GetType().GetMethod('Cancel', $bf).Invoke($editor2, @())
    Check 'Cancel delivers nothing' ($null -eq $script:delivered2 -and $script:cancelled2) ''
    $editor2.Dispose()
}
finally {
    $composeFrame.Dispose()
}

# ---- 8. the S0071 chord re-key through the real exe (opt-in, -Migration) ---------------------------
if ($Migration) {
    $regPath = 'HKCU:\Software\CyrFlip'
    if (Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue) {
        Write-Host '  [SKIP] the migration scene - CyrFlip is running; stop it first (a second launch just activates the first)' -ForegroundColor DarkGray
    }
    else {
        $backup = @{}
        foreach ($name in @('ScreenshotHotkey', 'ScreenshotFolderHotkey', 'ScreenshotKeyMigrationDone')) {
            $value = (Get-ItemProperty $regPath -ErrorAction SilentlyContinue).$name
            if ($null -ne $value) { $backup[$name] = $value }
        }
        $backupFile = Join-Path ([IO.Path]::GetTempPath()) ("CyrFlip-S0071-" + [Guid]::NewGuid().ToString('N') + ".json")
        $backup | ConvertTo-Json | Set-Content $backupFile
        $readBack = Get-Content $backupFile -Raw | ConvertFrom-Json
        Check 'the backup of the three values can be read back' ($null -ne $readBack) $backupFile
        if ($null -ne $readBack) {
            $now = $null
            try {
                Set-ItemProperty $regPath -Name 'ScreenshotHotkey' -Value 'Ctrl+PrintScreen'
                Remove-ItemProperty $regPath -Name 'ScreenshotKeyMigrationDone' -ErrorAction SilentlyContinue
                $started = Start-Process -FilePath $exe -PassThru
                $deadline = [DateTime]::UtcNow.AddSeconds(20)
                while ($null -eq $now -and [DateTime]::UtcNow -lt $deadline) {
                    Start-Sleep -Milliseconds 500
                    $candidate = Get-ItemProperty $regPath -ErrorAction SilentlyContinue
                    if ($null -ne $candidate -and $candidate.ScreenshotHotkey -eq 'Ctrl+F12' -and [int]$candidate.ScreenshotKeyMigrationDone -eq 1) { $now = $candidate }
                }
                Check 'the staged Ctrl+PrintScreen came back as Ctrl+F12 with the marker set' ([bool]$now) "ScreenshotHotkey=$(if ($now) { $now.ScreenshotHotkey } else { '<unchanged>' })"
                if (-not $started.HasExited) { Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue }
            }
            finally {
                # The way back: every value that was there is restored, the staged ones are removed.
                foreach ($name in @('ScreenshotHotkey', 'ScreenshotFolderHotkey', 'ScreenshotKeyMigrationDone')) {
                    if ($backup.ContainsKey($name)) { Set-ItemProperty $regPath -Name $name -Value $backup[$name] }
                    else { Remove-ItemProperty $regPath -Name $name -ErrorAction SilentlyContinue }
                }
                Remove-Item $backupFile -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
else {
    Write-Host '  [SKIP] the S0071 migration scene - run with -Migration (CyrFlip stopped)' -ForegroundColor DarkGray
}

# ---- 9. the S0071 full-capture chord (opt-in, -FullChord) -------------------------------------------
if ($FullChord) {
    Write-Host '  Press the full-capture chord now (default Ctrl+F11).' -ForegroundColor Cyan
    Read-Host '  Press Enter once you have pressed it'
    $pngFormat = [ScreenRegionUi]::RegisterClipboardFormatW('PNG')
    Check 'PNG is on the clipboard' ([ScreenRegionUi]::IsClipboardFormatAvailable($pngFormat)) ''
    Check 'CF_DIB is on the clipboard' ([ScreenRegionUi]::IsClipboardFormatAvailable(8)) ''
    # The full capture is the whole virtual screen; a rough spot check beats decoding the PNG here.
    Write-Host "  Expected image size (virtual screen): $($virtual.Width) x $($virtual.Height)" -ForegroundColor Cyan
}
else {
    Write-Host '  [SKIP] the full-capture chord - run with -FullChord (CyrFlip running; replaces the clipboard)' -ForegroundColor DarkGray
}

if ($InteropOnly) {
    if ($failures.Count) { Write-Host "`n$($failures.Count) check(s) failed." -ForegroundColor Red; exit 1 }
    Write-Host "`nAll unattended checks passed." -ForegroundColor Green
    exit 0
}

# ---- the human half -------------------------------------------------------------------------------
Write-Host "`nNow by hand (CyrFlip running):" -ForegroundColor Cyan
@(
    'Press the chord (default Ctrl+F12) and, separately, pick "Screen region capture" in the tray menu: the screen freezes and dims, the pointer is a cross.',
    'Press Ctrl+F11 (or pick "Full screen capture" in the tray): no overlay appears, and Ctrl+V pastes every display as one picture.',
    'Drag a rectangle, release: the overlay closes, the focus is back in the app you came from.',
    'Ctrl+V into Paint - the picture appears (CF_DIB).',
    'Ctrl+V into a browser text field, Telegram or Slack - the picture appears (PNG).',
    'Esc, a right click, and a click without a drag each cancel; the clipboard is unchanged.',
    'Hover a tooltip or open a menu, press the chord: the tooltip / menu is in the picture.',
    'The layout marker next to the caret is NOT in the picture.',
    'Two monitors with different scaling: a drag across both is one continuous picture, no seam.',
    'With saving on (the default now): the file appears in Pictures\Screenshots, named screenshot_yyMMdd_HHmmss.png.',
    'Point the folder at a pulled USB stick: the capture still reaches the clipboard and a balloon names Pictures\Screenshots.',
    'S0057, saving on, folder exists: the folder chord (default Ctrl+F10) opens the folder - and it is in front of the editor you came from.',
    'S0057, saving on, folder deleted (or not created yet): the chord shows the "no folder yet" balloon and no window opens.',
    'S0057, saving off: the same chord is not bound - the keys reach a text box in the foreground app, no window.',
    'S0057, folder on a removed drive or an offline share: the keyboard stays responsive while the chord is handled (the hook thread is not held); the balloon or Explorer''s own error appears.',
    'S0057, pressing the chord twice: two Explorer windows (D3); while the region overlay is up: nothing opens.',
    'S0057, inside an RDP client with "Defer to the remote desktop" on: the chord travels to the remote session, as every chord does.',
    'S0071: press Ctrl+Alt+F12 and drag a rectangle - the region shows at full brightness (outside dimmed) and a translucent toolbar sits near the pointer.',
    'S0071: the toolbar is draggable by any non-button part of its surface, and it never straddles monitor edges.',
    'S0071: draw an arrow, a red rectangle and a filled oval over a chart, press OK - Ctrl+V pastes exactly the picture with the annotations; the toolbar, the dimming and the outline are not in it.',
    'S0071: the eraser removes drawn shapes one drag at a time; "Clear" empties the picture back to the pristine frozen frame; Ctrl+Z undoes the last action.',
    'S0071: Cancel (or Esc) closes the editor - the clipboard is untouched, no file appeared, and the settings counter did not move.',
    'S0071: press Ctrl+Alt+F11 - the whole desktop freezes into the editor (no selection step); box one window, draw an arrow, OK.',
    'S0071: the 0.67-transparency look on the light theme, the dark theme and in high contrast (the eight swatches keep their colours).',
    'S0071: click another window (Alt+Tab, the Win key) while drawing - the editor cancels and nothing was delivered.',
    'S0071: switch "Draw on screen captures" off in settings - both annotate chords pass through to other apps, the editor never appears, the plain chords still capture.',
    'S0071: the annotate buttons on the settings page open the same editor as the chords; with the module off they are greyed out.',
    'S0071: every chord of the capture family (Ctrl+F10, Ctrl+F11, Ctrl+F12, Ctrl+Alt+F11, Ctrl+Alt+F12) shows its state on the "Hotkeys" overview page.',
    'S0071, typing on an AltGr layout (e.g. Polish Programmers): typing does not fire the Ctrl+Alt chords (no lost characters).',
    'S0071: an existing profile (no marker, ScreenshotHotkey = Ctrl+PrintScreen) upgrades once to Ctrl+F12; a deliberately re-bound chord is never touched.'
) | ForEach-Object { Write-Host "  [ ] $_" }

if ($failures.Count) { Write-Host "`n$($failures.Count) unattended check(s) failed." -ForegroundColor Red; exit 1 }
