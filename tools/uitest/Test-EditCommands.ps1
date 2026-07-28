<#
.SYNOPSIS
Replays the exact key sequences ClipboardHandler.SendEditCommand injects (Copy / Cut / Paste) into a
real editable text box and checks that each one did what it promises.

.DESCRIPTION
The text context menu's first three items do not transform anything themselves: they synthesize
Ctrl+C / Ctrl+X / Ctrl+V into the window under the menu. When one of them "does nothing" there are
only two possible causes - the sequence never reaches the application, or the application legitimately
refuses it (a read-only field cannot cut). This check removes the first cause from the picture by
driving a plain WinForms TextBox, where all three must work.

Cut is verified twice over, because that is the item whose failure is visible:
  1. the text reaches the clipboard  -> Ctrl+X was delivered;
  2. the box is left empty           -> it was a cut and not a copy.

Deliberately outside dotnet test / CI, like every script here: it steals the focus and injects input.

Run it on an idle desktop. Synthesized input goes to whatever owns the foreground at that instant, so
a session where someone is clicking around produces failures that say nothing about the code - and
"Target window never reached the foreground" means exactly that, not a defect.

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-EditCommands.ps1
#>
[CmdletBinding()]
param([int]$SettleMs = 220)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi

$VK_SHIFT = 0x10; $VK_CONTROL = 0x11; $VK_MENU = 0x12; $VK_LWIN = 0x5B; $VK_RWIN = 0x5C
$VK_A = 0x41; $VK_C = 0x43; $VK_V = 0x56; $VK_X = 0x58
$KEYUP = 2

function Send-Key([int]$vk, [bool]$up) {
    [CyrFlipUi]::keybd_event([byte]$vk, 0, $(if ($up) { $KEYUP } else { 0 }), [IntPtr]::Zero)
}

# The shape ClipboardHandler uses: drop the modifiers that would corrupt the chord (the hotkey is
# still held when a real flip fires), then drive Ctrl ourselves. Copied deliberately, not simplified.
function Send-EditCommand([int]$vk) {
    Send-Key $VK_SHIFT   $true
    Send-Key $VK_MENU    $true
    Send-Key $VK_LWIN    $true
    Send-Key $VK_RWIN    $true
    Send-Key $VK_CONTROL $false
    Send-Key $vk         $false
    Send-Key $vk         $true
    Send-Key $VK_CONTROL $true
    Start-Sleep -Milliseconds $SettleMs
}

function Send-SelectAll {
    Send-Key $VK_CONTROL $false; Send-Key $VK_A $false
    Send-Key $VK_A $true;        Send-Key $VK_CONTROL $true
    Start-Sleep -Milliseconds $SettleMs
}

$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("  [{0}] {1}{2}" -f $mark, $name, $(if ($detail) { " - $detail" } else { '' }))
    if (-not $ok) { $script:failures += $name }
}

$marker = "cyrflip-edit-check-$(Get-Random)"
$target = $null
try {
    Write-Host 'Opening an editable target window..'
    $target = Start-TargetWindow -Title 'CyrFlip edit-command check'

    # --- Paste -----------------------------------------------------------------------------
    Set-Clipboard -Value $marker
    Start-Sleep -Milliseconds $SettleMs
    Send-EditCommand $VK_V

    # --- Cut -------------------------------------------------------------------------------
    Send-SelectAll
    Set-Clipboard -Value 'sentinel-before-cut'
    Start-Sleep -Milliseconds $SettleMs
    Send-EditCommand $VK_X
    $afterCut = Get-Clipboard -Raw
    Check 'Ctrl+X reaches the application' ($afterCut -eq $marker) "clipboard = '$afterCut'"

    # An empty box copies nothing, so the sentinel survives - that is how "the text really went away"
    # is told apart from "Ctrl+X behaved like Ctrl+C".
    Set-Clipboard -Value 'sentinel-after-cut'
    Start-Sleep -Milliseconds $SettleMs
    Send-SelectAll
    Send-EditCommand $VK_C
    $afterCopy = Get-Clipboard -Raw
    Check 'Ctrl+X emptied the field' ($afterCopy -eq 'sentinel-after-cut') "clipboard = '$afterCopy'"

    # --- Copy ------------------------------------------------------------------------------
    Set-Clipboard -Value $marker
    Start-Sleep -Milliseconds $SettleMs
    Send-EditCommand $VK_V
    Send-SelectAll
    Set-Clipboard -Value 'sentinel-before-copy'
    Start-Sleep -Milliseconds $SettleMs
    Send-EditCommand $VK_C
    $copied = Get-Clipboard -Raw
    Check 'Ctrl+C reaches the application' ($copied -eq $marker) "clipboard = '$copied'"
}
finally {
    if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'All edit commands work against a plain editable field.' -ForegroundColor Green
    Write-Host 'If one of them does nothing in a real app, that app is refusing it (a read-only field cannot cut).'
    exit 0
}
Write-Host ("Failed: " + ($failures -join ', ')) -ForegroundColor Red
exit 1
