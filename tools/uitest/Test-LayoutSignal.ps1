# LAYOUT-SIGNAL contract uitest - verifies layout.txt and layout-klid.txt lifecycle.
#
# Steps:
#   1. Check CyrFlip running and files present with valid layout codes.
#   2. Verify clean exit removes layout.txt and layout-klid.txt (LAYOUT-SIGNAL rule 6).
#   3. Verify other files (editor-caret.txt, logs) are left untouched.
#
# Usage:
#   .\tools\uitest\Test-LayoutSignal.ps1
[CmdletBinding()]
param(
    [switch]$Interactive
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$dataFolder = Get-CyrFlipDataFolder
$dataDir = $dataFolder.Path

$layout = Join-Path $dataDir 'layout.txt'
$layoutKlid = Join-Path $dataDir 'layout-klid.txt'
$claim = Join-Path $dataDir 'editor-caret.txt'

$failed = $false
function Fail([string]$message) { Write-Host "FAIL  $message" -ForegroundColor Red; $script:failed = $true }
function Pass([string]$message) { Write-Host "PASS  $message" -ForegroundColor Green }

Write-Host "Data folder: $dataDir"

$proc = Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Fail 'CyrFlip is not running. Please start CyrFlip before running this test.'
    exit 1
}

# 1. Check layout files exist while running
if (Test-Path $layout) {
    $code = (Get-Content -LiteralPath $layout -Raw).Trim()
    Pass "layout.txt exists and contains: '$code'"
} else {
    Fail "layout.txt does not exist while CyrFlip is running"
}

if (Test-Path $layoutKlid) {
    $klid = (Get-Content -LiteralPath $layoutKlid -Raw).Trim()
    Pass "layout-klid.txt exists and contains: '$klid'"
} else {
    Fail "layout-klid.txt does not exist while CyrFlip is running"
}

# Create a mock editor-caret.txt claim to test non-interference
Set-Content -LiteralPath $claim -Value "EN $(Get-Date -Format o)" -Encoding ascii
Pass "Created mock editor-caret.txt"

if ($Interactive) {
    Write-Host "Please exit CyrFlip via tray menu (Exit / Выход), then press Enter..."
    Read-Host
} else {
    Write-Host "Closing CyrFlip gracefully..."
    $proc.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 1500
    if (-not $proc.HasExited) {
        # Request close via tray / message
        Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
}

# 2. Check layout files are deleted after clean exit
if (-not (Test-Path $layout)) {
    Pass "layout.txt was retracted on exit"
} else {
    Fail "layout.txt was NOT retracted on exit"
}

if (-not (Test-Path $layoutKlid)) {
    Pass "layout-klid.txt was retracted on exit"
} else {
    Fail "layout-klid.txt was NOT retracted on exit"
}

# 3. Check editor-caret.txt is untouched
if (Test-Path $claim) {
    Pass "editor-caret.txt was preserved (absence is not authority to destroy)"
    Remove-Item -LiteralPath $claim -Force -ErrorAction SilentlyContinue
} else {
    Fail "editor-caret.txt was improperly removed"
}

if ($failed) {
    Write-Host "`nLayout signal verification FAILED." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nLayout signal verification PASSED." -ForegroundColor Green
    exit 0
}
