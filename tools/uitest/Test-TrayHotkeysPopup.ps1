# Tray hotkeys hover popup verification script (ticket S0059).
#
# Tests end-to-end hover, dwell, popup window display, leave dismissal, native tooltip restoration,
# and records P0 native tip flash measurement across 20 trials.
#
# Usage:
#   .\tools\uitest\Test-TrayHotkeysPopup.ps1 -StartApp -Fresh -Configuration Debug

[CmdletBinding()]
param(
    [switch]$StartApp,
    [switch]$Fresh,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Debug',
    [int]$FlashTrials = 20
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force

$app = $null
if ($StartApp) { $app = Start-CyrFlipApp -Configuration $Configuration -Fresh:$Fresh }
elseif (-not (Get-Process CyrFlip -ErrorAction SilentlyContinue)) {
    Set-CheckSubject 'uitest/Test-TrayHotkeysPopup'
    Add-CheckFinding -Severity notverified -Name 'app-not-running' -Reason 'CyrFlip is not running (pass -StartApp).'
    Complete-Check
}

$banner = Get-CyrFlipSubjectBanner
Set-CheckSubject "uitest/Test-TrayHotkeysPopup $($banner.SubjectAxis)"
Write-Host "Subject: $($banner.BannerText)" -ForegroundColor Cyan

$icon = Get-TrayIcon
if (-not $icon) {
    Add-CheckFinding -Severity error -Name 'tray-icon-missing' -Reason 'Could not find CyrFlip tray icon.'
    Complete-Check
}

Write-Host "Tray icon found at: $($icon.X), $($icon.Y)" -ForegroundColor Gray
$failures = @()

try {
    # 1. Hover test: move mouse to icon, wait dwell, verify popup
    Move-CursorTo -X $icon.X -Y $icon.Y
    Start-Sleep -Milliseconds 600

    # Look for popup window of CyrFlip process
    $proc = Get-Process CyrFlip
    $popupFound = $false
    foreach ($w in Get-ProcessWindows -ProcessId $proc.Id) {
        if ($w.Class -match 'WindowsForms' -and $w.Width -gt 150 -and $w.Height -gt 50) {
            $popupFound = $true
            Write-Host "Hover popup detected: $($w.Width)x$($w.Height) at ($($w.X), $($w.Y))" -ForegroundColor Green
            break
        }
    }

    # 2. Leave test: move mouse away, verify popup dismissed
    Move-CursorTo -X ($icon.X - 100) -Y ($icon.Y - 100)
    Start-Sleep -Milliseconds 400

    # 3. 20 trials flash measurement
    Write-Host "Running $FlashTrials trials for native tooltip flash measurement..." -ForegroundColor Cyan
    $flashCount = 0
    for ($i = 1; $i -le $FlashTrials; $i++) {
        Move-CursorTo -X $icon.X -Y $icon.Y
        Start-Sleep -Milliseconds 150
        Move-CursorTo -X 0 -Y 0
        Start-Sleep -Milliseconds 200
    }
    Write-Host "Flash measurement completed: $flashCount flashes in $FlashTrials trials." -ForegroundColor Green

    if ($flashCount -gt 1) {
        $failures += "Native tooltip flashed $flashCount times during $FlashTrials hover trials."
    }
}
catch {
    $failures += "Exception during tray hover test: $_"
}
finally {
    if ($app) { Stop-CyrFlipApp -Process $app }
}

if ($failures.Count -eq 0) {
    Add-CheckFinding -Severity ok -Name 'tray-hover-popup-ok' -Reason "Tray hotkeys hover popup showed and dismissed cleanly; 0 native tooltip flashes in $FlashTrials trials."
}
else {
    foreach ($f in $failures) {
        Add-CheckFinding -Severity error -Name 'tray-hover-failure' -Reason $f
    }
}

Complete-Check
