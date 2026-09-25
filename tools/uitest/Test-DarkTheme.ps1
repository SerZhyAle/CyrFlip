# Live check of the app theme (ticket S0020 section 8) - what xUnit cannot reach: the dark title bar
# (DWM), the dark native parts (scroll bars, the page list), the tray menu drawn by the dark renderer,
# and - with -FollowWindows - an open window following Windows' own light/dark switch while it runs.
#
#   .\tools\uitest\Test-DarkTheme.ps1                    # CyrFlip's own mode: dark, then light
#   .\tools\uitest\Test-DarkTheme.ps1 -FollowWindows     # also flips Windows' AppsUseLightTheme live
#   .\tools\uitest\Test-DarkTheme.ps1 -Pages 0,7,8       # those settings pages (a combo, text boxes, a list)
#   .\tools\uitest\Test-DarkTheme.ps1 -TrayMenu         # also the tray menu (right-clicks the tray icon)
#
# The settings window is opened with the launcher's /launcher-settings command, so without -TrayMenu
# nothing here moves the mouse; the window does come to the front for the few seconds it is captured.
#
# Each step saves a PNG under artifacts\uitest\theme and judges it by its mean luminance (dark < 0.15,
# light > 0.5) - language-independent, and a themed window is mostly background. CyrFlip reads its
# theme at start, so the mode steps restart the app; the -FollowWindows step does not, which is the
# point of it. The app's Theme value and Windows' AppsUseLightTheme are put back in `finally`, and the
# app is left stopped if it was not running before.
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$FollowWindows,
    [switch]$TrayMenu,        # also right-click the tray icon and capture the menu (moves the mouse)
    [string[]]$Pages = @('-1'),   # settings pages to capture (index); -1 = whichever page the window reopens on
    [string]$OutDir = ''      # default: artifacts\uitest\theme (Windows PowerShell has no $PSScriptRoot in param defaults)
)
if (-not $OutDir) { $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts\uitest\theme' }
# -File hands "0,7,8" over as one string.
$Pages = @($Pages | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { [int]$_.Trim() })

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$appKey = 'HKCU:\Software\CyrFlip'
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$originalTheme = (Get-ItemProperty $appKey -Name Theme -ErrorAction SilentlyContinue).Theme
$originalTab = (Get-ItemProperty $appKey -Name SettingsTab -ErrorAction SilentlyContinue).SettingsTab
$originalApps = (Get-ItemProperty $personalize -Name AppsUseLightTheme -ErrorAction SilentlyContinue).AppsUseLightTheme
$wasRunning = [bool](Get-Process CyrFlip -ErrorAction SilentlyContinue)
$results = New-Object System.Collections.Generic.List[object]

function Judge([string]$name, [string]$path, [string]$expect, [double]$top = 1.0) {
    $lum = Get-ImageLuminance -Path $path -TopFraction $top
    $ok = if ($expect -eq 'dark') { $lum -lt 0.15 } else { $lum -gt 0.5 }
    $results.Add([pscustomobject]@{ Step = $name; Expected = $expect; Luminance = $lum; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Shot = $path })
    "{0,-34} {1,-5} luminance {2,5}  {3}" -f $name, $expect, $lum, $(if ($ok) { 'PASS' } else { 'FAIL' })
}

function Open-Settings {
    # Started with /launcher-settings (see Start-InMode) the app opens the window itself - no mouse.
    $w = Wait-AppWindow -TimeoutSeconds 15
    if (-not $w) { throw 'The settings window did not open.' }
    [void][CyrFlipUi]::SetForegroundWindow($w.Handle)
    Start-Sleep -Milliseconds 700
    $w
}

function Save-TrayMenu([string]$path) {
    $before = @(Get-AppWindows | ForEach-Object { $_.Handle })
    Invoke-TrayClick -Button Right
    Start-Sleep -Milliseconds 700
    $menu = Get-AppWindows | Where-Object { $before -notcontains $_.Handle -and $_.Height -gt 60 } | Select-Object -First 1
    if (-not $menu) { Write-Warning 'The tray menu did not open - is the icon in the hidden-icons flyout?'; return $null }
    $shot = Save-WindowShot -Handle $menu.Handle -Path $path
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 300
    $shot
}

function Start-InMode([string]$mode) {
    Set-ItemProperty $appKey -Name Theme -Value $mode -Type String
    Stop-CyrFlipApp
    # The Jump List's "manage scenarios" command: with no instance running it starts the app and opens
    # the settings window on start, so no tray icon has to be found and clicked.
    Start-Process (Get-CyrFlipExe -Configuration $Configuration) -ArgumentList '/launcher-settings' | Out-Null
}

try {
    foreach ($mode in 'dark', 'light') {
        foreach ($pageIndex in $Pages) {
            # The window reopens on the page it was last left on: that is how a page is chosen here
            # without a key or a click.
            if ($pageIndex -ge 0) { Set-ItemProperty $appKey -Name SettingsTab -Value $pageIndex -Type DWord }
            Start-InMode $mode
            $settings = Open-Settings
            $suffix = if ($pageIndex -ge 0) { "-page$pageIndex" } else { '' }
            $shot = Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutDir "settings-$mode$suffix.png")
            Judge "settings window ($mode mode$suffix)" $shot.Path $mode
            # The frame is in the shot: its top band is the title bar DWM paints (S0020 v0.2 item 5).
            Judge "title bar ($mode mode$suffix)" $shot.Path $mode 0.03
        }
        if ($TrayMenu) {
            $menu = Save-TrayMenu (Join-Path $OutDir "tray-menu-$mode.png")
            if ($menu) { Judge "tray menu ($mode mode)" $menu.Path $mode }
        }
    }

    if ($FollowWindows) {
        # Windows light, CyrFlip following it; then Windows dark with the window left open.
        Set-ItemProperty $personalize -Name AppsUseLightTheme -Value 1 -Type DWord
        Send-SettingChange
        Start-InMode 'system'
        $settings = Open-Settings
        $shot = Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutDir 'follow-1-windows-light.png')
        Judge 'system mode, Windows light' $shot.Path 'light'

        Set-ItemProperty $personalize -Name AppsUseLightTheme -Value 0 -Type DWord
        Send-SettingChange
        Start-Sleep -Seconds 2                      # the acceptance line: repainted within 2 s
        $shot = Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutDir 'follow-2-windows-dark.png')
        Judge 'Windows switched to dark (live)' $shot.Path 'dark'

        Set-ItemProperty $personalize -Name AppsUseLightTheme -Value 1 -Type DWord
        Send-SettingChange
        Start-Sleep -Seconds 2
        $shot = Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutDir 'follow-3-windows-light.png')
        Judge 'Windows switched back to light' $shot.Path 'light'
    }
}
finally {
    if ($null -ne $originalTab) { Set-ItemProperty $appKey -Name SettingsTab -Value $originalTab -Type DWord }
    if ($null -eq $originalTheme) { Remove-ItemProperty $appKey -Name Theme -ErrorAction SilentlyContinue }
    else { Set-ItemProperty $appKey -Name Theme -Value $originalTheme -Type String }
    if ($FollowWindows) {
        if ($null -eq $originalApps) { Remove-ItemProperty $personalize -Name AppsUseLightTheme -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $personalize -Name AppsUseLightTheme -Value $originalApps -Type DWord }
        Send-SettingChange
    }
    Stop-CyrFlipApp
    if ($wasRunning) { Write-Warning 'CyrFlip was running before this check and has been stopped - start it again.' }
}

$results | Format-Table -AutoSize | Out-String | Write-Host
"Manual checklist (things a luminance cannot judge): the text context menu over a selection, a text box's"
"Cut/Copy/Paste menu, a combo box drop-down, the quick-notes editor's scroll bars, and a high contrast theme."
$failed = @($results | Where-Object Result -eq 'FAIL').Count
if ($failed -gt 0) { Write-Error "$failed step(s) failed - see the shots in $OutDir"; exit 1 }
"PASS: $($results.Count) step(s), shots in $OutDir"
