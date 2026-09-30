# Keyboard hook and chord fixes - the checks xUnit cannot reach (ticket S0004).
#
# ChordMatcherTests, KeyInjectionTests, KeyboardHookTests, HotkeyRulesTests and ChordRegistryTests
# prove the decisions. What they cannot prove is what a real application does with the keys CyrFlip
# injects, and every scene below needs PHYSICAL keys: CyrFlip never fires a chord on injected input,
# by design, so a synthesized chord would prove nothing. The script therefore walks a human through
# the scenes and records a verdict for each.
#
#   .\tools\uitest\Test-HooksAndChords.ps1                 # every scene
#   .\tools\uitest\Test-HooksAndChords.ps1 -InteropOnly    # unattended: the scan codes KeyInjection sends
#
# -InteropOnly checks that MapVirtualKey resolves every side-specific modifier to its own scan code -
# the right Shift to 0x36, not 0x2A - since a generic or zero scan code is exactly how the old
# injection released the left key while the right one was held.
[CmdletBinding()]
param([switch]$InteropOnly)

$ErrorActionPreference = 'Stop'

Add-Type -Namespace CyrFlipUiTest -Name Keys -MemberDefinition @'
[DllImport("user32.dll")] public static extern uint MapVirtualKey(uint uCode, uint uMapType);
'@

$failed = $false
function Fail([string]$message) { Write-Host "FAIL  $message" -ForegroundColor Red; $script:failed = $true }
function Pass([string]$message) { Write-Host "PASS  $message" -ForegroundColor Green }

$expected = [ordered]@{ LCtrl = @(0xA2, 0x1D); RCtrl = @(0xA3, 0x1D); LShift = @(0xA0, 0x2A); RShift = @(0xA1, 0x36);
    LAlt = @(0xA4, 0x38); RAlt = @(0xA5, 0x38); LWin = @(0x5B, 0x5B); RWin = @(0x5C, 0x5C) }
foreach ($name in $expected.Keys) {
    $vk, $scan = $expected[$name]
    $got = [CyrFlipUiTest.Keys]::MapVirtualKey($vk, 0) -band 0xFF
    if ($got -eq $scan) { Pass ("{0,-6} vk 0x{1:X2} -> scan 0x{2:X2}" -f $name, $vk, $got) }
    else { Fail ("{0,-6} vk 0x{1:X2} -> scan 0x{2:X2}, expected 0x{3:X2}" -f $name, $vk, $got, $scan) }
}

if ($InteropOnly) {
    if ($failed) { exit 1 }
    exit 0
}

if (@(Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue).Count -eq 0) {
    Fail 'CyrFlip is not running - start it first'
    exit 1
}

$scenes = @(
    'KC-1  Chrome: select a word, hold LEFT Ctrl + RIGHT Shift, press F12. The word is converted and DevTools does NOT open.',
    'KC-1  Word or Notepad: hold Ctrl+Shift, tap F12 twice. Converted, then converted back - no "Save As" dialog.',
    'KC-1  Notepad: select a word, RIGHT Ctrl + Shift + F12, release everything, type "a". An "a" appears (not a shortcut).',
    'KC-1  Hold a launcher scenario chord for two seconds. The scenario starts ONCE.',
    'KC-4  Install US + US-International, turn on "Ctrl+Shift switches layout". Select text, Ctrl+Shift+F11. The layout label does NOT change.',
    'KC-4  Give a scenario an Alt+F9 chord, press it in Notepad. Notepad''s menu bar does NOT get the focus.',
    'KC-5  Polish (Programmers) installed, quick notes on with Ctrl+Alt+N: AltGr+N in Notepad types "ń".',
    'KC-5  Hotkey dialog: Shift+A, Ctrl+C and (with German installed) Ctrl+Alt+E are each refused with a reason; OK stays grey.',
    'KC-2  Hotkey dialog: Ctrl+Shift+PageUp is accepted and shown as Ctrl+Shift+PageUp after reopening Settings.',
    'KC-6  Untick the case flip, then try to give its chord to a conversion row. Refused, naming the case flip as the owner.',
    'KC-7  Ctrl + right-press over a normal window, release over Task Manager (elevated), wait 4 s, right-click elsewhere: the application''s own menu opens.',
    'KC-8  Hotkeys master switch OFF, translate the clipboard from the tray, press Esc while it streams: the translation stops.',
    'KC2-1 Hold Ctrl+Alt, press Del, choose Cancel. At once, in Notepad: a lone F12 does NOT convert; select a word, Ctrl+Shift+F12 converts it; afterwards typing "a" types "a" (no Ctrl stuck down).',
    'KC2-1 Notepad: select a word, hold Ctrl+Shift and tap F12 twice quickly. Converted, then converted back (the held Shift survives our own release).',
    'KC2-2 Control Panel > Keyboard > Repeat delay = Long. Hold a launcher scenario chord for two seconds: the scenario starts ONCE.',
    'KC2-3 Context menu chord Alt+RightClick: open it over Notepad, press Esc, release Alt. Notepad''s menu bar does NOT get the focus. (With a Chinese IME and Shift+RightClick: the IME mode does NOT toggle.)',
    'KC2-4 Settings > translation row > change chord, press Ctrl+Shift+F12 (the EN-RU conversion chord). The dialog shows Ctrl+Shift+F12 and no conversion runs; OK is refused naming the conversion row as the owner.'
)

$results = @()
foreach ($scene in $scenes) {
    Write-Host ''
    Write-Host $scene -ForegroundColor Cyan
    $answer = Read-Host 'Result? [y = as described / n = not / s = skip]'
    switch ($answer.Trim().ToLowerInvariant()) {
        'y' { Pass $scene.Substring(0, 5); $results += [pscustomobject]@{ Scene = $scene; Verdict = 'pass' } }
        'n' { Fail $scene.Substring(0, 5); $results += [pscustomobject]@{ Scene = $scene; Verdict = 'FAIL' } }
        default { Write-Host 'SKIP' -ForegroundColor Yellow; $results += [pscustomobject]@{ Scene = $scene; Verdict = 'skipped' } }
    }
}

Write-Host ''
$results | Format-Table -AutoSize -Wrap
if ($failed) { exit 1 }
exit 0
