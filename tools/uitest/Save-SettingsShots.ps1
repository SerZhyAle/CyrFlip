# Opens the settings window and saves a PNG of every tab - the fastest way to eyeball a layout
# or localization change (clipped captions, an overflowing table column, an untranslated label)
# without driving the UI by hand.
#
#   .\tools\uitest\Save-SettingsShots.ps1                       # every tab -> .\artifacts\uitest
#   .\tools\uitest\Save-SettingsShots.ps1 -Tab 4 -OutDir C:\tmp # one tab
#
# Tabs are cycled with Ctrl+Tab and the position is read back from HKCU\Software\CyrFlip
# \SettingsTab, which the app writes on every SelectedIndexChanged. That makes the walk
# deterministic (the window reopens on whatever tab was last left open) and self-measuring: the
# walk ends when the index wraps to 0, so nothing here has to know how many tabs this build has.
# The UIA tree is no help - the owner-drawn TabControl exposes no TabItem elements.
[CmdletBinding()]
param(
    [switch]$StartApp,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [int]$Tab = -1,
    [int]$MaxTabs = 32,
    [string]$OutDir = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts\uitest')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms

function Get-ActiveTab {
    [int](Get-ItemProperty 'HKCU:\Software\CyrFlip' -Name SettingsTab -ErrorAction SilentlyContinue).SettingsTab
}
function Step-Tab([switch]$Back) {
    [System.Windows.Forms.SendKeys]::SendWait($(if ($Back) { '^+{TAB}' } else { '^{TAB}' }))
    Start-Sleep -Milliseconds 350
}

if ($StartApp -and -not (Get-Process CyrFlip -ErrorAction SilentlyContinue)) {
    Start-CyrFlipApp -Configuration $Configuration | Out-Null
}

$settings = Wait-AppWindow -TimeoutSeconds 0
if (-not $settings) {
    Invoke-TrayClick -Count 2                    # the tray double click is how a user opens it
    $settings = Wait-AppWindow -TimeoutSeconds 10
}
if (-not $settings) { throw "Settings window did not open." }
"settings window   : '$($settings.Title)' $($settings.Width)x$($settings.Height)"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Ctrl+Tab needs the window focused; the screenshot itself does not.
[void][CyrFlipUi]::SetForegroundWindow($settings.Handle)
Start-Sleep -Milliseconds 500

# Rewind to the first tab so the numbering means something.
for ($guard = 0; (Get-ActiveTab) -ne 0 -and $guard -lt $MaxTabs; $guard++) { Step-Tab -Back }
if ((Get-ActiveTab) -ne 0) { throw "Could not reach the first tab (is the settings window focused?)" }

$shot = $null
for ($i = 0; $i -lt $MaxTabs; $i++) {
    $current = Get-ActiveTab
    if ($i -gt 0 -and $current -eq 0) { break }          # wrapped round: every tab is done
    if ($Tab -lt 0 -or $Tab -eq $current) {
        $path = Join-Path $OutDir ("settings-tab{0:d2}.png" -f $current)
        $shot = Save-WindowShot -Handle $settings.Handle -Path $path
        "  tab $current -> $($shot.Path)  ($($shot.Width)x$($shot.Height))"
        if ($Tab -ge 0) { break }
    }
    Step-Tab
}

if (-not $shot) { throw "Tab $Tab was never reached." }
"done: $OutDir"
