<#
.SYNOPSIS
The flip pipeline's clipboard hand-back (ticket S0009): the paste is delay-rendered and the restore
waits for the target's own read; a flip that changed nothing leaves the clipboard alone; a password
manager's markers, a Cut's move effect and the text's locale survive the restore.

.DESCRIPTION
Two halves, because only one of them can run unattended.

The first drives the real pipeline - reflection into the built exe, the real clipboard, a plain
WinForms text box in its own process as the paste target - and checks what xUnit cannot reach:

  1. delayed rendering works at all: the offer is announced without data, the target's Ctrl+V reaches
     CyrFlip's owner window as WM_RENDERFORMAT, the target gets the text, and CF_LOCALE is on the
     clipboard beside it (FP-1B, FP-8). If this scene fails, every flip would paste nothing;
  2. a whole case flip through ClipboardHandler: the target shows the result, the call returned long
     before the 2 s cap, and the user's clipboard - text plus the "do not record" markers of a
     password manager - is back byte for byte (FP-1, FP-2);

A clipboard monitor that fetches every change at once and ignores the markers (a VM's clipboard
sharing, an older clipboard manager) makes the target's own read unobservable; scene 1 names that
process in a NOTE, and flips on that machine fall back to the fixed wait - by design, not a failure.
  3. the chord with nothing selected: the clipboard is not rewritten at all - its sequence number does
     not move and a format the backup never carries survives (FP-3);
  4. a Cut of files keeps "Preferred DropEffect" through a flip (FP-7).

It BORROWS YOUR CLIPBOARD for a few seconds. What is on it is backed up first with CyrFlip's own
backup - the script refuses to start if that backup cannot be read - and handed back at the end,
exactly as a flip does. Diagnostics go to a temp folder, never to CyrFlip's real logs.

The second half is the checklist of what only a person can check (RDP, Word, Excel, VS Code, KeePass
and Win+V, Explorer, an ANSI-only app); CyrFlip ignores injected chords, so a script cannot press them.

Run it on an idle desktop, from Windows PowerShell (the exe is .NET Framework): synthesized input goes
to whatever owns the foreground at that instant.

.PARAMETER InteropOnly
Run only the unattended half.

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-ClipboardFlip.ps1 -InteropOnly
#>
[CmdletBinding()]
param(
    [switch]$InteropOnly,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi

if (-not ('ClipFlipUi' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ClipFlipUi {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, string l);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] public static extern int CountClipboardFormats();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormatW(string name);
    delegate bool EnumProc(IntPtr h, IntPtr p);

    // WM_GETTEXT / WM_SETTEXT are marshalled across processes by USER32 itself; EM_SETSEL carries
    // plain integers. So the target's text is read and staged without the clipboard under test.
    public static IntPtr EditOf(IntPtr top) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(top, (h, p) => {
            var sb = new StringBuilder(128); GetClassNameW(h, sb, sb.Capacity);
            if (sb.ToString().IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static string GetText(IntPtr edit) {
        var sb = new StringBuilder(4096);
        SendMessageW(edit, 0x000D, (IntPtr)sb.Capacity, sb);   // WM_GETTEXT
        return sb.ToString();
    }
    public static void SetSelectedText(IntPtr edit, string text) {
        SendMessageW(edit, 0x000C, IntPtr.Zero, text);          // WM_SETTEXT
        SendMessageW(edit, 0x00B1, IntPtr.Zero, (IntPtr)(-1));  // EM_SETSEL 0..end
    }
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
    # A value that went through parameter binding may arrive wrapped in a PSObject, which reflection
    # cannot convert; unwrap in place, so out-parameters still land in the caller's array.
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].psobject.BaseObject }
    }
    # The comma keeps a returned list or array whole instead of letting the pipeline unroll it.
    , $m.Invoke($target, $arguments)
}
function Prop($object, [string]$name) { $object.GetType().GetProperty($name, $bf).GetValue($object) }

$HandlerType = T 'ClipboardHandler'
$ClipboardType = T 'Win32Clipboard'
$FormatsType = T 'ClipboardFormats'
$OwnerType = T 'ClipboardOwner'
$OfferType = T 'PasteOffer'
$ReceiptType = T 'ClipboardHandler+PasteReceipt'

# The pipeline's diagnostics go to a scratch folder: this check must leave CyrFlip's real logs alone.
$logFolder = Join-Path ([IO.Path]::GetTempPath()) ("CyrFlip-S0009-" + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $logFolder)
(T 'DiagnosticLog').GetProperty('OverrideFolder', $bf).SetValue($null, $logFolder)

$CF_UNICODETEXT = [uint32]13; $CF_HDROP = [uint32]15; $CF_LOCALE = [uint32]16
function FormatId([string]$field) { [uint32]$FormatsType.GetField($field, $bf).GetValue($null) }
$ExcludeFromMonitor = FormatId 'ExcludeFromMonitor'
$CanIncludeInHistory = FormatId 'CanIncludeInHistory'
$PreferredDropEffect = FormatId 'PreferredDropEffect'
$Probe = [ClipFlipUi]::RegisterClipboardFormatW('CyrFlip S0009 probe format')

function New-Payloads {
    New-Object 'System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[uint32,byte[]]]'
}
function Add-Payload($list, [uint32]$format, [byte[]]$bytes) {
    $list.Add((New-Object 'System.Collections.Generic.KeyValuePair[uint32,byte[]]' -ArgumentList $format, $bytes))
}
function Unicode([string]$text) { , [byte[]](Call $ClipboardType 'UnicodeBytes' @($text)) }
function Dword([uint32]$value) { , [BitConverter]::GetBytes($value) }
# Everything staged here goes up marked "do not record", so neither CyrFlip's history nor Win+V keeps
# the test's clipboards. (The target's own Ctrl+C of the selection is an ordinary copy and is kept.)
function Set-Clip($payloads, [switch]$Unmarked) {
    if (-not $Unmarked) {
        Add-Payload $payloads $ExcludeFromMonitor ([byte[]](1))
        Add-Payload $payloads $CanIncludeInHistory (Dword 0)
    }
    if (-not (Call $ClipboardType 'Restore' (, $payloads))) { throw 'Could not stage the clipboard.' }
    Start-Sleep -Milliseconds 150
}
function Get-ClipBytes([uint32]$format) {
    $a = [object[]]@($format, $null, [int]::MaxValue)
    [void](Call $ClipboardType 'TryGetBytes' $a)
    , $a[1]
}
function Get-ClipText {
    $a = [object[]]@('')
    if (-not (Call $ClipboardType 'TryGetText' $a)) { return $null }
    $a[0]
}
function Same([byte[]]$a, [byte[]]$b) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    if ($a.Length -ne $b.Length) { return $false }
    for ($i = 0; $i -lt $a.Length; $i++) { if ($a[$i] -ne $b[$i]) { return $false } }
    $true
}
function Send-CtrlV {
    [CyrFlipUi]::keybd_event(0x11, 0, 0, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event(0x56, 0, 0, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event(0x56, 0, 2, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event(0x11, 0, 2, [IntPtr]::Zero)
}

# ---- 0. Your clipboard, kept aside ---------------------------------------------------------------
$userBackup = Call $HandlerType 'BackupClipboard' @()
if (Prop $userBackup 'Unreadable') {
    Write-Host 'Your clipboard could not be read (another program holds it). Nothing was touched - retry in a moment.' -ForegroundColor Yellow
    exit 2
}
if (-not (Prop $userBackup 'HasContent') -and [ClipFlipUi]::CountClipboardFormats() -gt 0) {
    # Only formats a flip never carries (HTML alone, a private format): the check could not hand them back.
    Write-Host 'Your clipboard holds only formats this check cannot hand back. Nothing was touched - copy some text first.' -ForegroundColor Yellow
    exit 2
}
# Everything the hand-back needs is resolved now, before a single scene runs, so a scene that throws
# cannot take the way back with it.
$userPayloads = Call $HandlerType 'RestorePayloads' @($userBackup)
$restoreMethod = $ClipboardType.GetMethod('Restore', $bf)
$clearMethod = $ClipboardType.GetMethod('TryClear', $bf)
if (-not $restoreMethod -or -not $clearMethod -or $userPayloads -isnot [System.Collections.IList]) {
    throw 'The hand-back could not be prepared; nothing was touched.'
}
Write-Host 'Your clipboard is backed up and will be handed back at the end.'
Write-Host ''

$target = $null
try {
    $target = Start-TargetWindow -Title 'CyrFlip clipboard flip check'
    $edit = [ClipFlipUi]::EditOf($target.Handle)
    if ($edit -eq [IntPtr]::Zero) { throw 'The target window has no edit control.' }
    $handler = [Activator]::CreateInstance($HandlerType)

    # ---- 1. Delayed rendering, the mechanism itself ---------------------------------------------
    Write-Host 'Delayed rendering (FP-1B, FP-8):'
    $sentinel = Unicode 'S0009 sentinel - the clipboard before the flip'
    $clip = New-Payloads; Add-Payload $clip $CF_UNICODETEXT $sentinel
    Set-Clip $clip
    $before = Call $HandlerType 'BackupClipboard' @()

    [ClipFlipUi]::SetSelectedText($edit, 'abc')
    if (-not (Set-WindowForeground -Handle $target.Handle)) { throw 'Target window never reached the foreground.' }
    $owner = $OwnerType.GetProperty('Shared', $bf).GetValue($null)
    Check 'the clipboard owner thread starts' ($null -ne $owner) ''
    if ($null -ne $owner) {
        $allMarks = [Enum]::Parse((T 'TransientMarks'), 'All')
        $offer = Call $OwnerType 'Offer' @('delayed-probe', [uint32]0x0419, $allMarks) $owner
        Check 'the offer takes the clipboard' ($null -ne $offer) ''
        if ($null -ne $offer) {
            Check 'the text is announced' ([ClipFlipUi]::IsClipboardFormatAvailable($CF_UNICODETEXT)) ''
            $locale = Get-ClipBytes $CF_LOCALE
            Check 'CF_LOCALE sits beside it' ((Same $locale (Dword 0x0419))) ("got " + $(if ($locale) { [BitConverter]::ToUInt32($locale, 0).ToString('X4') } else { 'none' }))
            Check 'the offer carries the do-not-record markers' ([ClipFlipUi]::IsClipboardFormatAvailable($ExcludeFromMonitor)) ''
            Start-Sleep -Milliseconds 300   # give an eager clipboard monitor the chance it would have in a flip
            $early = (Call $OfferType 'Wait' @(0) $offer).ToString()
            if ($early -eq 'RenderedEarly') {
                # Not a defect of the code: something on this machine fetches every clipboard change at
                # once and ignores the do-not-record markers. Flips then fall back to the fixed wait.
                Write-Host ("  [NOTE] fetched before the Ctrl+V by '{0}' - on this machine flips use the fixed wait" -f (Prop $offer 'RenderedBy')) -ForegroundColor Yellow
            }

            [void](Call $OfferType 'Arm' @() $offer)
            Send-CtrlV
            $state = (Call $OfferType 'Wait' @(3000) $offer).ToString()
            if ($early -ne 'RenderedEarly') {
                Check "the target's paste arrives as WM_RENDERFORMAT" ($state -eq 'Consumed') ("state $state, asked by '" + (Prop $offer 'RenderedBy') + "'")
            }
            Start-Sleep -Milliseconds 150
            $got = [ClipFlipUi]::GetText($edit)
            # The text can only have reached the clipboard through CyrFlip's WM_RENDERFORMAT handler.
            Check 'the target received the delay-rendered text' ($got -eq 'delayed-probe') "target holds '$got'"

            $receipt = Call $ReceiptType 'ByOwner' @($owner, (Prop $offer 'OwnSequence'))
            $action = (Call $HandlerType 'RestoreClipboard' @($before, $receipt)).ToString()
            Check 'the restore runs over our own paste' ($action -eq 'Restore') "action $action"
            Check 'the clipboard is the one from before' (Same (Unicode (Get-ClipText)) $sentinel) ''
        }
    }

    # ---- 2. A whole flip, the user's markers included ------------------------------------------
    Write-Host 'A case flip end to end (FP-1, FP-2):'
    $clip = New-Payloads
    Add-Payload $clip $CF_UNICODETEXT (Unicode 'hunter2 - a copied password')
    Set-Clip $clip   # adds ExcludeClipboardContentFromMonitorProcessing = 1 and CanIncludeInClipboardHistory = 0

    [ClipFlipUi]::SetSelectedText($edit, 'hello world')
    if (-not (Set-WindowForeground -Handle $target.Handle)) { throw 'Target window never reached the foreground.' }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $result = (Call $HandlerType 'FlipCase' @($false) $handler).ToString()
    $clock.Stop()
    Start-Sleep -Milliseconds 150
    Check 'the flip reports Flipped' ($result -eq 'Flipped') "result $result"
    Check 'the target shows the result' ([ClipFlipUi]::GetText($edit) -eq 'HELLO WORLD') ("target holds '" + [ClipFlipUi]::GetText($edit) + "'")
    Check 'the flip did not wait out the cap' ($clock.ElapsedMilliseconds -lt 1500) ("{0} ms (the local cap is 2000)" -f $clock.ElapsedMilliseconds)
    Check 'the password is back' ((Get-ClipText) -eq 'hunter2 - a copied password') ''
    Check 'its ExcludeClipboardContentFromMonitorProcessing is back' (Same (Get-ClipBytes $ExcludeFromMonitor) ([byte[]](1))) ''
    Check 'its CanIncludeInClipboardHistory = 0 is back' (Same (Get-ClipBytes $CanIncludeInHistory) (Dword 0)) ''

    # ---- 3. Nothing selected: the clipboard is not rewritten -----------------------------------
    Write-Host 'The chord with nothing selected (FP-3):'
    $clip = New-Payloads
    Add-Payload $clip $CF_UNICODETEXT (Unicode 'rich content stand-in')
    Add-Payload $clip $Probe ([byte[]](1, 2, 3, 4))
    Set-Clip $clip

    [ClipFlipUi]::SetSelectedText($edit, '')
    if (-not (Set-WindowForeground -Handle $target.Handle)) { throw 'Target window never reached the foreground.' }
    $seq = [CyrFlipUi]::GetClipboardSequenceNumber()
    $result = (Call $HandlerType 'FlipCase' @($false) $handler).ToString()
    Check 'the flip reports NoSelection' ($result -eq 'NoSelection') "result $result"
    Check 'the clipboard was not rewritten' ([CyrFlipUi]::GetClipboardSequenceNumber() -eq $seq) ''
    Check 'a format the backup never carries survives' (Same (Get-ClipBytes $Probe) ([byte[]](1, 2, 3, 4))) ''

    # ---- 4. A Cut of files stays a move ---------------------------------------------------------
    Write-Host 'A Cut of files through a flip (FP-7):'
    $file = Join-Path $logFolder 'cut-me.txt'
    Set-Content -Path $file -Value 'x'
    $pathBytes = [Text.Encoding]::Unicode.GetBytes($file + "`0`0")
    $dropFiles = New-Object byte[] (20 + $pathBytes.Length)
    [BitConverter]::GetBytes([int]20).CopyTo($dropFiles, 0)   # DROPFILES.pFiles
    [BitConverter]::GetBytes([int]1).CopyTo($dropFiles, 16)   # DROPFILES.fWide
    $pathBytes.CopyTo($dropFiles, 20)
    $clip = New-Payloads
    Add-Payload $clip $CF_HDROP $dropFiles
    Add-Payload $clip $PreferredDropEffect (Dword 2)          # DROPEFFECT_MOVE
    Set-Clip $clip

    [ClipFlipUi]::SetSelectedText($edit, 'hello')
    if (-not (Set-WindowForeground -Handle $target.Handle)) { throw 'Target window never reached the foreground.' }
    $result = (Call $HandlerType 'FlipCase' @($false) $handler).ToString()
    Check 'the flip reports Flipped' ($result -eq 'Flipped') "result $result"
    Check 'the files are back' (Same (Get-ClipBytes $CF_HDROP) $dropFiles) ''
    Check 'Preferred DropEffect is still a move' (Same (Get-ClipBytes $PreferredDropEffect) (Dword 2)) ''

    $log = Join-Path $logFolder 'clipboard-flip.log'
    if (Test-Path $log) {
        Write-Host ''
        Write-Host 'clipboard-flip.log:' -ForegroundColor DarkGray
        Get-Content $log | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    }
}
finally {
    # The hand-back comes first and uses only what was resolved before the scenes ran.
    $handedBack = $false
    for ($attempt = 0; $attempt -lt 5 -and -not $handedBack; $attempt++) {
        try {
            $handedBack = if ($userPayloads.Count -gt 0) { [bool]$restoreMethod.Invoke($null, (, $userPayloads)) }
                          else { [bool]$clearMethod.Invoke($null, @()) }
        }
        catch { $handedBack = $false }
        if (-not $handedBack) { Start-Sleep -Milliseconds 300 }
    }
    Write-Host ''
    if ($handedBack) { Write-Host 'Your clipboard has been handed back.' }
    else { Write-Host 'Your clipboard could NOT be handed back - its previous content is in Win+V and in CyrFlip''s history.' -ForegroundColor Red }
    if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
    try { [void]$OwnerType.GetMethod('ShutdownShared', $bf).Invoke($null, @()) } catch { }
    Remove-Item -Recurse -Force $logFolder -ErrorAction SilentlyContinue
}

if (-not $InteropOnly) {
    Write-Host ''
    Write-Host 'By hand, with CyrFlip running (a script cannot press a chord CyrFlip will act on):' -ForegroundColor Cyan
    @(
        'RDP: copy a distinctive word locally, flip a word in Notepad inside the remote session - the converted word appears, never the copied one.',
        'Word with a large document still loading: the same - the converted word, never the old clipboard.',
        'Excel: copy cells, press the chord in Notepad with nothing selected, paste into Excel - the cells paste as cells (marching ants intact). clipboard-flip.log shows no restore (S0032 FP2-1).',
        'Excel, a huge range (hundreds of thousands of cells): copy it, flip a word in Notepad - the flip goes through; clipboard-flip.log may say the picture was not carried (S0032 FP2-2).',
        'Ditto or CopyQ running (a monitor that ignores the markers): flip a word in a busy Word - the converted word appears, never the old clipboard; clipboard-flip.log names the monitor as an early render (S0032 FP2-3).',
        'Empty clipboard (Win+V > Clear all): flip a word in Notepad, then open Win+V / paste elsewhere - the clipboard is empty again (S0032 FP2-4).',
        'VS Code: caret in a line, no selection, the conversion chord - nothing changes (no duplicated line). Same in Notepad++ and Visual Studio.',
        'KeePass: copy a password, flip a word in Notepad, open Win+V - the password is not listed.',
        'Explorer: cut a file, flip a word, paste in another folder - the file moves, the original is gone.',
        'An ANSI-only app (e.g. an old Delphi/VB6 tool): converted Cyrillic pastes as Cyrillic, not as "?".',
        '"Press SHIFT to turn off Caps Lock": run Test-CapsSync.ps1 - its fourth scene covers it.'
    ) | ForEach-Object { Write-Host "  - $_" }
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'The clipboard hand-back behaves in every unattended scene.' -ForegroundColor Green
    exit 0
}
Write-Host ("Failed: " + ($failures -join ', ')) -ForegroundColor Red
exit 1
