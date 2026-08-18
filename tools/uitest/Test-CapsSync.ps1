<#
.SYNOPSIS
"Synchronize CapsLock after case correction": the key must end up matching the corrected text, not
merely change - on when the text ends in a capital, off when it ends in a small letter.

.DESCRIPTION
Two halves, because only one of them can run unattended.

The first is the interop fact the feature rests on. Windows has no API that sets a lock state; it can
only be toggled, so CyrFlip reads CapsLock first and sends the keystroke only when the reading differs
from what the text asks for. That reading is taken on the clipboard worker - a thread with no message
queue - and GetKeyState is documented as answering per-thread. If it went stale there the result would
be inverted rather than absent, in exactly half the cases, which is the kind of bug that looks like
"sometimes it does the opposite". This half runs on its own and restores the key it borrows.

The second is end-to-end and needs a human at the keyboard: CyrFlip ignores injected keystrokes
(LLKHF_INJECTED), so a synthesized Ctrl+Shift+F11 would prove nothing at all. The script stages each
scene - text in an editable window, everything selected, CapsLock in a known state - and asks you to
press the chord; it then waits for the clipboard to move (that is the operation happening) and checks
both the text and the key.

The scenes are chosen so that a blind toggle - what this replaced - fails the first one:

  | CapsLock before | typed         | corrected     | CapsLock after |
  | off             | hELLO wORLD   | Hello World   | off (unchanged)|   <- a toggle turned it on
  | off             | hello world   | HELLO WORLD   | on             |
  | on              | hELLO wORLD   | Hello World   | off            |

.PARAMETER InteropOnly
Run only the unattended half.

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-CapsSync.ps1
.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-CapsSync.ps1 -InteropOnly
#>
[CmdletBinding()]
param(
    [switch]$InteropOnly,
    [string]$Chord = 'Ctrl+Shift+F11',
    [int]$ChordTimeoutSeconds = 60,
    [int]$SettleMs = 260
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi

$VK_CONTROL = 0x11; $VK_A = 0x41; $VK_C = 0x43; $VK_V = 0x56
$KEYUP = 2

$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}{2}" -f $mark, $name, $(if ($detail) { " - $detail" } else { '' })) -ForegroundColor $colour
    if (-not $ok) { $script:failures += $name }
}

function Send-Chord([int]$vk) {
    [CyrFlipUi]::keybd_event([byte]$VK_CONTROL, 0, 0, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event([byte]$vk, 0, 0, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event([byte]$vk, 0, $KEYUP, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event([byte]$VK_CONTROL, 0, $KEYUP, [IntPtr]::Zero)
    Start-Sleep -Milliseconds $SettleMs
}

# ---- 1. The interop fact, unattended -----------------------------------------------------------
Write-Host 'CapsLock state, read from a thread with no message queue:'
$original = Get-CapsLockState
try {
    foreach ($state in @($true, $false)) {
        if (-not (Set-CapsLockState -On $state)) { throw "Could not put CapsLock into '$state'." }
        $onThread = Get-CapsLockState
        $offThread = Get-CapsLockState -OffThread
        Check "CapsLock=$state is seen off-thread" ($offThread -eq $state) "this thread = $onThread, worker thread = $offThread"
    }
}
finally {
    [void](Set-CapsLockState -On $original)
}

if ($InteropOnly) {
    Write-Host ''
    if ($failures.Count -eq 0) {
        Write-Host 'The reading CyrFlip bases the decision on is honest on a queue-less thread.' -ForegroundColor Green
        exit 0
    }
    Write-Host ("Failed: " + ($failures -join ', ')) -ForegroundColor Red
    exit 1
}

# ---- 2. End-to-end, with a human pressing the chord ---------------------------------------------
$scenes = @(
    [pscustomobject]@{ Name = 'ends lower, key already off - left alone'; Before = $false; Typed = 'hELLO wORLD'; Expect = 'Hello World'; After = $false }
    [pscustomobject]@{ Name = 'ends upper - key switched on';            Before = $false; Typed = 'hello world'; Expect = 'HELLO WORLD'; After = $true }
    [pscustomobject]@{ Name = 'ends lower, key was on - switched off';   Before = $true;  Typed = 'hELLO wORLD'; Expect = 'Hello World'; After = $false }
)

Write-Host ''
Write-Host 'Before running the rest: CyrFlip must be running, with' -NoNewline
Write-Host ' Settings -> General -> "Synchronize CapsLock after case correction" ' -ForegroundColor Yellow -NoNewline
Write-Host 'ticked.'
Write-Host "The case-flip chord is assumed to be $Chord (change it with -Chord if you rebound it)."
Write-Host ''

$target = $null
try {
    $target = Start-TargetWindow -Title 'CyrFlip caps-sync check'
    foreach ($scene in $scenes) {
        Write-Host ("Scene: {0}" -f $scene.Name) -ForegroundColor Cyan
        if (-not (Set-WindowForeground -Handle $target.Handle)) { throw 'Target window never reached the foreground.' }

        # Stage the text through the clipboard: typing it would be at the mercy of the very
        # CapsLock state this check is about.
        Send-Chord $VK_A
        Set-Clipboard -Value $scene.Typed
        Start-Sleep -Milliseconds $SettleMs
        Send-Chord $VK_V
        Send-Chord $VK_A

        if (-not (Set-CapsLockState -On $scene.Before)) { throw 'Could not stage the CapsLock state.' }
        $seq = Get-ClipboardSequence

        Write-Host ("  CapsLock is {0}; the window holds '{1}', selected." -f $(if ($scene.Before) { 'ON' } else { 'off' }), $scene.Typed)
        Write-Host ("  Press {0} now - in that window, without clicking anywhere else." -f $Chord) -ForegroundColor Yellow

        $deadline = (Get-Date).AddSeconds($ChordTimeoutSeconds)
        while ((Get-ClipboardSequence) -eq $seq -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 120 }
        if ((Get-ClipboardSequence) -eq $seq) {
            Check $scene.Name $false "no chord seen within $ChordTimeoutSeconds s"
            continue
        }
        Start-Sleep -Milliseconds 900   # let the paste, the clipboard restore and the CapsLock keystroke land

        $caps = Get-CapsLockState
        Send-Chord $VK_A
        Send-Chord $VK_C
        $text = Get-Clipboard -Raw
        if ($null -ne $text) { $text = $text.TrimEnd("`r", "`n") }

        Check ("{0} - text" -f $scene.Name) ($text -eq $scene.Expect) "got '$text', expected '$($scene.Expect)'"
        Check ("{0} - CapsLock" -f $scene.Name) ($caps -eq $scene.After) ("got {0}, expected {1}" -f $caps, $scene.After)
    }
}
finally {
    if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
    [void](Set-CapsLockState -On $original)
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'CapsLock follows the corrected text in all three scenes.' -ForegroundColor Green
    exit 0
}
Write-Host ("Failed: " + ($failures -join ', ')) -ForegroundColor Red
exit 1
