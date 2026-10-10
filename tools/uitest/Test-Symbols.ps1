# The symbols window's two interop-bound facts (ticket S0056, section 13, desktop-driving half).
#
#   1. THE MACHINE CHECK - every character of the curated table can be drawn by some installed font.
#      The palette draws in Segoe UI Symbol and relies on Windows' font linking for the rest; a character
#      no font on THIS machine has would show as a tofu box in a cell. The set of fonts differs between
#      Windows 10 22H2 and 11, so run it on both. GetGlyphIndices is asked per font (it knows no linking),
#      so a character counts when any font of the candidate list has it.
#   2. THE CLIPBOARD WRITE - the real Win32Clipboard.TrySetText (the call the palette's click makes) puts
#      exactly the character on the clipboard: NBSP stays U+00A0, a combining mark stays alone, a
#      surrogate pair stays whole, nothing is appended. Read back as CF_UNICODETEXT.
#
# The human half stays a checklist at the end: press the chord and type "euro", Enter, Ctrl+V into
# Notepad, Word, Chrome, VS Code and an RDP session; click characters with an editor focused and confirm
# the foreground window never changed; open from the tray item and from the text menu and confirm the
# focus returns; dark theme, 125 % / 200 % scaling, a second monitor with another DPI; the hint on a
# machine with German and Polish layouts; 200 open/copy/close cycles with GDI/USER handle counts sampled
# before and after. CyrFlip ignores injected keystrokes by design, so a script cannot press the chord.
#
# Part 2 BORROWS YOUR CLIPBOARD for a moment. It refuses to start when the clipboard holds anything but
# plain text (a picture or a file list cannot be handed back from here), puts the text back at the end,
# and never reads CyrFlip's real data. Windows PowerShell 5.1, STA (the exe is .NET Framework):
#
#   powershell -NoProfile -STA -ExecutionPolicy Bypass -File tools\uitest\Test-Symbols.ps1
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$SkipClipboard
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Test-Symbols'

if ($PSVersionTable.PSEdition -eq 'Core' -or [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    Add-CheckFinding -Severity notverified -Name 'wrong-host' -Reason 'Run from Windows PowerShell 5.1 with -STA (the exe is .NET Framework and the clipboard wants an STA thread).'
    Complete-Check
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
if (-not ('SymbolsProbeNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SymbolsProbeNative {
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetGlyphIndicesW(IntPtr hdc, string text, int count, [Out] ushort[] indices, uint flags);
}
'@
}

$exe = Get-CyrFlipExe -Configuration $Configuration
if (-not (Test-Path $exe)) {
    Add-CheckFinding -Severity notverified -Name 'exe-missing' -Reason "No $Configuration build of CyrFlip.exe - build it first."
    Complete-Check
}
$asm = [Reflection.Assembly]::LoadFrom($exe)
$bindings = [Reflection.BindingFlags]'Static,Public,NonPublic'

# ---- 1. the machine check ----------------------------------------------------------------------------
$dataType = $asm.GetType('CyrFlip.SymbolsData')
$table = $dataType.GetProperty('Table', $bindings)
if ($null -eq $table) { $table = $dataType.GetField('Table', $bindings) }
$entries = @($table.GetValue($null))
if ($entries.Count -lt 300) {
    Add-CheckFinding -Severity fail -Name 'table-small' -Reason "The curated table has only $($entries.Count) entries."
}
Write-Host "Probing $($entries.Count) characters against the candidate fonts..." -ForegroundColor Cyan

# Segoe UI Symbol first (what the grid draws in), then the families Windows links to it.
$candidates = 'Segoe UI Symbol', 'Segoe UI', 'Segoe UI Emoji', 'Cambria Math', 'Arial', 'Consolas', 'Microsoft YaHei UI', 'MS Gothic', 'Nirmala UI'
$hdc = [SymbolsProbeNative]::CreateCompatibleDC([IntPtr]::Zero)
$fonts = @()
foreach ($name in $candidates) { try { $fonts += New-Object System.Drawing.Font($name, 14) } catch { } }
$missing = New-Object System.Collections.Generic.List[string]
try {
    foreach ($entry in $entries) {
        $text = [string]$entry.Text
        $drawable = $false
        foreach ($font in $fonts) {
            $hfont = $font.ToHfont()
            $old = [SymbolsProbeNative]::SelectObject($hdc, $hfont)
            $glyphs = New-Object 'UInt16[]' $text.Length
            [void][SymbolsProbeNative]::GetGlyphIndicesW($hdc, $text, $text.Length, $glyphs, 1)   # GGI_MARK_NONEXISTING_GLYPHS
            [void][SymbolsProbeNative]::SelectObject($hdc, $old)
            if (-not ($glyphs | Where-Object { $_ -eq 0xFFFF })) { $drawable = $true; break }
        }
        if (-not $drawable) { $missing.Add(('U+{0:X4} {1}' -f $entry.CodePoint, $entry.NameEn)) }
    }
}
finally { [void][SymbolsProbeNative]::DeleteDC($hdc); $fonts | ForEach-Object { $_.Dispose() } }

if ($missing.Count -gt 0) {
    Add-CheckFinding -Severity fail -Name 'no-font-draws-it' -Reason ("$($missing.Count) character(s) no candidate font on this machine can draw: " + (($missing | Select-Object -First 12) -join '; '))
}
else {
    Write-Host ("PASS every-glyph-drawable - " + "All $($entries.Count) characters are drawable on this machine ($([Environment]::OSVersion.Version)).") -ForegroundColor Green
}

# ---- 2. the clipboard write ---------------------------------------------------------------------------
if (-not $SkipClipboard -and (Get-Process CyrFlip -ErrorAction SilentlyContinue) -and
    ((Get-ItemProperty "HKCU:SoftwareCyrFlip" -Name EnableClipboardHistory -ErrorAction SilentlyContinue).EnableClipboardHistory -eq 1)) {
    # The history of a running CyrFlip records every copy, so ten test characters would land in the user's real history.
    Add-CheckFinding -Severity notverified -Name 'history-would-record' -Reason 'The clipboard history is on in a running CyrFlip: stop it or switch the history off, or run with -SkipClipboard.'
    $SkipClipboard = $true
}
if (-not $SkipClipboard) {
    $clip = $asm.GetType('CyrFlip.Win32Clipboard')
    $marks = [Enum]::Parse($asm.GetType('CyrFlip.TransientMarks'), 'None')
    $set = $clip.GetMethods($bindings) | Where-Object { $_.Name -eq 'TrySetText' -and $_.GetParameters().Count -eq 3 } | Select-Object -First 1
    $data = [System.Windows.Forms.Clipboard]::GetDataObject()
    $formats = @()
    if ($data) { $formats = @($data.GetFormats($false)) }
    $textOnly = @('UnicodeText', 'Text', 'System.String', 'OEMText', 'Locale')
    $foreign = @($formats | Where-Object { $textOnly -notcontains $_ })
    if ($foreign.Count -gt 0) {
        Add-CheckFinding -Severity notverified -Name 'clipboard-not-plain-text' -Reason ("The clipboard holds more than plain text ($($foreign -join ', ')); copy something plain first - this part will not overwrite a picture or a file list.")
    }
    else {
        $previous = if ($data -and $data.GetDataPresent([System.Windows.Forms.DataFormats]::UnicodeText)) { [string]$data.GetData([System.Windows.Forms.DataFormats]::UnicodeText) } else { $null }
        $samples = [ordered]@{
            'em dash'        = [string][char]0x2014
            'euro'           = [string][char]0x20AC
            'numero'         = [string][char]0x2116
            'NBSP'           = [string][char]0x00A0
            'narrow NBSP'    = [string][char]0x202F
            'combining acute'= [string][char]0x0301
            'zero width joiner' = [string][char]0x200D
            'soft hyphen'    = [string][char]0x00AD
            'one non-BMP'    = [char]::ConvertFromUtf32(0x1D538)
            'a Polish l'     = [string][char]0x0142
        }
        try {
            foreach ($name in $samples.Keys) {
                $text = $samples[$name]
                $ok = $set.Invoke($null, @($text, [uint32]0x0409, $marks))
                $back = [System.Windows.Forms.Clipboard]::GetText([System.Windows.Forms.TextDataFormat]::UnicodeText)
                if (-not $ok) { Add-CheckFinding -Severity fail -Name 'copy-refused' -Reason "TrySetText refused '$name'." }
                elseif ($back -ne $text) {
                    $got = ($back.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
                    $want = ($text.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
                    Add-CheckFinding -Severity fail -Name 'copy-not-exact' -Reason "'$name': wrote $want, the clipboard holds $got."
                }
            }
            Write-Host ("PASS clipboard-write-exact - " + "TrySetText wrote $($samples.Count) characters and CF_UNICODETEXT returned each one unchanged.") -ForegroundColor Green
        }
        finally {
            # Hand the user's text back (or leave the clipboard empty if it held none).
            try { if ($null -ne $previous) { [System.Windows.Forms.Clipboard]::SetText($previous) } else { [System.Windows.Forms.Clipboard]::Clear() } } catch { }
        }
    }
}

Write-Host @'

Human checklist - the part a script cannot press (tick each):
  [ ] The chord (Ctrl+Shift+F9 by default) opens the palette with the search box focused; "euro", Enter, Ctrl+V pastes in
      Notepad, Word, Chrome, VS Code and an RDP session.
  [ ] Clicking characters with an editor focused never changes the foreground window.
  [ ] Opened from the tray item and from the text context menu, the focus comes back to the previous window.
  [ ] Dark theme, 125 % and 200 % scaling, a second monitor with another DPI, the window dragged onto a monitor that is unplugged.
  [ ] On a machine with the German and the Polish layouts the hint says how to type a character (AltGr+...).
  [ ] 200 open/copy/close cycles with GDI/USER handle counts sampled before and after: no growth (Test-LongRun.ps1 shows how).
'@ -ForegroundColor Yellow

Complete-Check
