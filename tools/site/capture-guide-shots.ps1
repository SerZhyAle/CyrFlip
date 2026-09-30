# Captures the live settings window for guide review. Run only in an interactive session.
# The caller must inspect every PNG for personal data before committing it.
param([string]$Exe = 'C:\GD\i\CyrFlip.exe', [string]$OutDir = 'docs/assets')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../uitest/CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Start-Process -FilePath $Exe -ArgumentList '/launcher-settings' -WindowStyle Hidden
$window = Find-AppWindow -TitleLike 'Настройки CyrFlip' -TimeoutSeconds 5
if (-not $window) { throw 'Settings window did not open' }
[void][CyrFlipUi]::SetForegroundWindow($window.Handle)
$key = 'HKCU:\Software\CyrFlip'
$original = [int](Get-ItemProperty $key -Name SettingsTab).SettingsTab
function Current-Tab { [int](Get-ItemProperty $key -Name SettingsTab).SettingsTab }
function Next-Tab { [System.Windows.Forms.SendKeys]::SendWait('^{TAB}'); Start-Sleep -Milliseconds 180 }
try {
    for ($i = 0; (Current-Tab) -ne 0 -and $i -lt 20; $i++) { Next-Tab }
    if ((Current-Tab) -ne 0) { throw 'Could not reach General tab' }
    foreach ($tab in @(0, 1, 3)) {
        for ($i = 0; (Current-Tab) -ne $tab -and $i -lt 20; $i++) { Next-Tab }
        if ((Current-Tab) -ne $tab) { throw "Could not reach tab $tab" }
        $path = Join-Path $OutDir ("guide-settings-{0}-ru.png" -f $tab)
        Save-WindowShot -Handle $window.Handle -Path $path | Out-Null
        Write-Output $path
    }
}
finally {
    for ($i = 0; (Current-Tab) -ne $original -and $i -lt 20; $i++) { Next-Tab }
}
