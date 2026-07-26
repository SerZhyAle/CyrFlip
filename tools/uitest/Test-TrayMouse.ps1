# Tray mouse behaviour, end to end - the part xUnit cannot reach.
#
#   single left click  -> the layout of the LAST ACTIVE window advances (the click itself has
#                         already moved the focus to the taskbar, so a target window is opened
#                         first and never re-focused afterwards);
#   double left click  -> Settings opens (also the sanity check that the synthesized click
#                         reaches the NotifyIcon at all - if this fails, nothing else means
#                         anything).
#
#   .\tools\uitest\Test-TrayMouse.ps1                  # against an already running CyrFlip
#   .\tools\uitest\Test-TrayMouse.ps1 -StartApp -Fresh # build output, restarted first
#
# Needs at least two installed keyboard layouts, and the tray icon must be visible on the
# taskbar (not hidden behind the chevron).
[CmdletBinding()]
param(
    [switch]$StartApp,
    [switch]$Fresh,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$SkipDoubleClick
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force

$layouts = @(Get-InstalledLayouts)
"installed layouts : " + (($layouts | ForEach-Object Klid) -join ', ')
if ($layouts.Count -lt 2) { throw "Need at least two installed layouts to see a switch." }

$app = $null
if ($StartApp) { $app = Start-CyrFlipApp -Configuration $Configuration -Fresh:$Fresh }
elseif (-not (Get-Process CyrFlip -ErrorAction SilentlyContinue)) { throw "CyrFlip is not running (pass -StartApp)." }

$icon = Get-TrayIcon
"tray icon         : $($icon.X),$($icon.Y)  '$($icon.Name -replace '\r?\n', ' | ')'"

$target = Start-TargetWindow
$before = Get-WindowLayout -Handle $target.Handle
"target window     : $($target.Handle)  layout $($before.Klid)"

$failures = @()

# Both clicks happen without re-focusing the target window in between, and the layout is read
# while it stays in the background. That is not a shortcut, it is the only honest measurement:
# re-activating the window makes Windows re-apply the system-wide current layout to it (verified
# on 2026-07-26 - a switch applied while the window is in the *foreground* does survive, one
# applied to a background window does not), and the tray click is by definition delivered while
# the window is in the background, because clicking the notification area focuses the taskbar.
# Re-focusing between clicks therefore resets the layout and every click reads as "RU -> EN".
function Invoke-TrayClickOnTarget([int]$Count = 1) {
    Invoke-TrayClick -Count $Count -Verbose:($VerbosePreference -eq 'Continue')
    Start-Sleep -Seconds 2                     # the single click is deferred by DoubleClickTime
}

try {
    # --- single click -------------------------------------------------------------------
    Invoke-TrayClickOnTarget
    $after = Get-WindowLayout -Handle $target.Handle
    "after 1 click     : $($after.Klid)"
    if ($after.Hkl -eq $before.Hkl) { $failures += "single click did not switch the layout ($($before.Klid) unchanged)" }

    Invoke-TrayClickOnTarget
    $after2 = Get-WindowLayout -Handle $target.Handle
    "after 2 clicks    : $($after2.Klid)"
    if ($layouts.Count -eq 2 -and $after2.Hkl -ne $before.Hkl) { $failures += "second click did not come back to $($before.Klid)" }

    # Informational, not a pass/fail: what the user gets after clicking back into their window.
    if (Set-WindowForeground -Handle $target.Handle) {
        Start-Sleep -Milliseconds 600
        $refocused = Get-WindowLayout -Handle $target.Handle
        "after refocus     : $($refocused.Klid)  (Windows re-applies the system layout to a window it activates)"
    }

    # --- double click -------------------------------------------------------------------
    if (-not $SkipDoubleClick) {
        if (Wait-AppWindow -TimeoutSeconds 0) {
            "settings          : already open, skipping the double-click check"
        }
        else {
            Invoke-TrayClickOnTarget -Count 2
            $settings = Wait-AppWindow -TimeoutSeconds 8
            if ($settings) { "settings opened   : '$($settings.Title)' ($($settings.Width)x$($settings.Height))" }
            else { $failures += "double click did not open Settings" }
        }
    }
}
finally {
    Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue
    if ($app) { Stop-CyrFlipApp }
}

""
if ($failures.Count -eq 0) { "RESULT: PASS"; exit 0 }
"RESULT: FAIL"
$failures | ForEach-Object { "  - $_" }
exit 1
