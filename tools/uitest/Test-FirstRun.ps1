<#
.SYNOPSIS
Operator-only first-run test (S0055). Backs up HKCU\Software\CyrFlip before touching it,
captures all five pages, checks immediate marking and General-page handoff, then restores.
No CI/build runner calls this script. Packaged-build/caret observations are separate checks.
#>
[CmdletBinding()]
param(
    [ValidateSet('ru', 'ar')][string]$Language = 'ru',
    [string]$ExePath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/first-run')
)
$ErrorActionPreference = 'Stop'
# WinForms UIA's legacy bridge is available in Windows PowerShell 5.1, not pwsh 7.
if ($PSVersionTable.PSEdition -eq 'Core') {
    $arguments = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
        '-Language', $Language, '-OutputDirectory', $OutputDirectory)
    if ($ExePath) { $arguments += @('-ExePath', $ExePath) }
    & powershell.exe @arguments
    exit $LASTEXITCODE
}
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Enable-UiTestDpi
if (-not $ExePath) { $ExePath = Get-CyrFlipExe }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$sourceExe = $ExePath
# A fresh staging directory omits legacy config.json and suppresses shortcut creation.
$subjectFolder = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlipFirstRun-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($subjectFolder)
$ExePath = Join-Path $subjectFolder 'CyrFlip.exe'
Copy-Item -LiteralPath $sourceExe -Destination $ExePath
if (Test-Path -LiteralPath ($sourceExe + '.config')) { Copy-Item -LiteralPath ($sourceExe + '.config') -Destination ($ExePath + '.config') }
$backup = Join-Path $OutputDirectory ("profile-" + [Guid]::NewGuid().ToString('N') + '.reg')
$regPath = 'HKCU\Software\CyrFlip'
$providerPath = 'HKCU:\Software\CyrFlip'
$hadKey = Test-Path -LiteralPath $providerPath
# No process is stopped and no key is touched until an on-disk backup has been verified.
if ($hadKey) {
    & reg.exe export $regPath $backup /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Registry export failed; the profile was not touched.' }
} else {
    [IO.File]::WriteAllText($backup, "Windows Registry Editor Version 5.00`r`n`r`n", [Text.Encoding]::Unicode)
}
if (-not (Test-Path -LiteralPath $backup) -or (Get-Item -LiteralPath $backup).Length -lt 20) {
    throw 'No verified registry backup; refusing to start.'
}
$previousApps = @(Get-Process CyrFlip -ErrorAction SilentlyContinue | ForEach-Object { $_.Path } | Select-Object -Unique)
$legacy = Join-Path $env:APPDATA 'CyrFlip/config.json'
if (Test-Path -LiteralPath $legacy) { throw "A roaming legacy config would suppress the tour: $legacy. Profile was not touched." }
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
try {
    $runValue = if ($runKey) { $runKey.GetValue('CyrFlip') } else { $null }
    $runKind = if ($null -ne $runValue) { $runKey.GetValueKind('CyrFlip') } else { [Microsoft.Win32.RegistryValueKind]::String }
} finally { if ($runKey) { $runKey.Dispose() } }
@{ Value = $runValue; Kind = [int]$runKind } | Export-Clixml -LiteralPath ($backup + '.startup.xml')
$shortcutBackups = @()
foreach ($place in @('Programs','DesktopDirectory')) {
    $link = Join-Path ([Environment]::GetFolderPath($place)) 'CyrFlip.lnk'
    $saved = $backup + ".$place.lnk"
    $exists = Test-Path -LiteralPath $link
    if ($exists) { Copy-Item -LiteralPath $link -Destination $saved -ErrorAction Stop }
    $shortcutBackups += @{ Path = $link; Backup = $saved; Exists = $exists }
}

function Start-Subject {
    $proc = Start-Process -FilePath $ExePath -WindowStyle Hidden -PassThru
    Write-Host (Get-CyrFlipSubjectBanner -ExePath $ExePath -ProcessId $proc.Id).BannerText
    return $proc
}
function Get-Tour {
    $window = Wait-AppWindow -TimeoutSeconds 10
    if (-not $window -or $window.Title -ne 'CyrFlip') { throw 'Welcome tour did not appear.' }
    return $window
}
function Ui-Root($window) { [Windows.Automation.AutomationElement]::FromHandle($window.Handle) }
function Ui-Button($window, [string]$caption) {
    # The themed owner-drawn WinForms button is exposed as a Pane by the legacy bridge.
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $caption)
    $button = (Ui-Root $window).FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $button) {
        $all = (Ui-Root $window).FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
        foreach ($item in $all) { Write-Host ($item.Current.ControlType.ProgrammaticName + ': ' + $item.Current.Name) }
        throw "Missing button: $caption"
    }
    Set-WindowForeground -Handle $window.Handle | Out-Null
    # Enter activates the tour's recommended answer, including with focus in the language box.
    [Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 250
}
function Assert-Quiet {
    Start-Sleep -Seconds 2
    if (Get-AppWindows | Where-Object Width -GE 300) { throw 'An existing profile unexpectedly opened a window.' }
}

try {
    Stop-CyrFlipApp
    if (Test-Path -LiteralPath $providerPath) { Remove-Item -LiteralPath $providerPath -Recurse -Force }
    $subject = Start-Subject
    $tour = Get-Tour
    if ((Get-ItemPropertyValue -LiteralPath $providerPath -Name WelcomeShown) -ne 1) { throw 'Marker not written on page 1.' }
    Start-Process -FilePath $ExePath -ArgumentList '/launcher-settings' -WindowStyle Hidden -Wait
    Start-Sleep -Milliseconds 300
    if (@(Get-AppWindows | Where-Object Width -GE 300).Count -ne 1) { throw 'A settings request escaped the modal tour.' }
    $comboCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ComboBox)
    $combo = (Ui-Root $tour).FindFirst([Windows.Automation.TreeScope]::Descendants, $comboCondition)
    if (-not $combo) { throw 'No interface-language control.' }
    Set-WindowForeground -Handle $tour.Handle | Out-Null
    $combo.SetFocus()
    # End selects Chinese in one gesture; the Arabic initial then selects Arabic directly.
    # Each selection rebuilds the control, so reacquire it between gestures.
    $keys = if ($Language -eq 'ar') { '{END}' } else { '{HOME}' }
    [Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 600
    if ($Language -eq 'ar') {
        # Jump directly by the endonym's first character, rather than traversing eight rebuilds.
        $tour = Get-Tour
        $combo = (Ui-Root $tour).FindFirst([Windows.Automation.TreeScope]::Descendants, $comboCondition)
        $combo.SetFocus()
        [Windows.Forms.SendKeys]::SendWait('ا')
        Start-Sleep -Milliseconds 600
    }
    $tour = Get-Tour
    $next = if ($Language -eq 'ru') { 'Далее' } else { 'التالي' }
    for ($page = 1; $page -le 5; $page++) {
        Save-WindowShot -Handle $tour.Handle -Path (Join-Path $OutputDirectory "$Language-page-$page.png")
        if ($page -lt 5) { Ui-Button $tour $next }
    }
    Ui-Button $tour $(if ($Language -eq 'ru') { 'Открыть настройки' } else { 'فتح الإعدادات' })
    $settings = Wait-AppWindow
    if (-not $settings -or $settings.Title -eq 'CyrFlip') { throw 'Settings did not open after the tour.' }
    if ((Get-ItemPropertyValue -LiteralPath $providerPath -Name SettingsTab) -ne 0) { throw 'Settings did not land on General.' }
    if (@(Get-AppWindows | Where-Object Width -GE 300).Count -ne 1) { throw 'The settings handoff opened more than one window.' }
    Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutputDirectory "$Language-settings.png")
    Stop-CyrFlipApp
    $subject = Start-Subject
    Assert-Quiet
    Stop-CyrFlipApp
    # A missing marker on an existing key identifies an upgrade, not a clean profile.
    Remove-ItemProperty -LiteralPath $providerPath -Name WelcomeShown
    $subject = Start-Subject
    Assert-Quiet
    Stop-CyrFlipApp
    New-ItemProperty -LiteralPath $providerPath -Name WelcomeShown -Value 0 -PropertyType DWord -Force | Out-Null
    $subject = Start-Subject
    $tour = Get-Tour
    if ((Get-ItemPropertyValue -LiteralPath $providerPath -Name WelcomeShown) -ne 1) { throw 'Replay did not mark the tour.' }
    Set-WindowForeground -Handle $tour.Handle | Out-Null
    [Windows.Forms.SendKeys]::SendWait('{ESC}')
    Assert-Quiet
    Write-Host "PASS: $Language first-run, five pages, immediate marker, General handoff, upgrade suppression, explicit replay."
} catch {
    Write-Host $_.ScriptStackTrace
    throw
} finally {
    Stop-CyrFlipApp
    if (Test-Path -LiteralPath $providerPath) { Remove-Item -LiteralPath $providerPath -Recurse -Force }
    if ($hadKey) {
        & reg.exe import $backup | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Profile restore failed. Backup retained at $backup" }
    }
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
    try {
        if ($null -ne $runValue) { $runKey.SetValue('CyrFlip', $runValue, $runKind) }
        else { $runKey.DeleteValue('CyrFlip', $false) }
    } finally { $runKey.Dispose() }
    foreach ($link in $shortcutBackups) {
        if ($link.Exists) { Copy-Item -LiteralPath $link.Backup -Destination $link.Path -Force }
        elseif (Test-Path -LiteralPath $link.Path) { Remove-Item -LiteralPath $link.Path -Force }
    }
    foreach ($app in $previousApps) { Start-Process -FilePath $app -WindowStyle Hidden | Out-Null }
    Write-Host "Profile restored; backup retained at $backup"
}
