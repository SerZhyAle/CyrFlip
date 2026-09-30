# Capture current UI evidence for the three translated setup guides.
# Review the output for personal data before committing it.
param([string]$OutDir = 'docs/assets')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../uitest/CyrFlip.UiTest.psm1') -Force
$key = 'HKCU:\Software\CyrFlip'
$originalLanguage = (Get-ItemProperty $key -Name UiLanguage -ErrorAction SilentlyContinue).UiLanguage
$originalTab = (Get-ItemProperty $key -Name SettingsTab -ErrorAction SilentlyContinue).SettingsTab
$originalProcess = Get-Process CyrFlip -ErrorAction SilentlyContinue | Select-Object -First 1
$originalExe = if ($originalProcess) { $originalProcess.Path } else { $null }
$exe = if ($originalExe) { $originalExe } else { Get-CyrFlipExe -Configuration Release }
$languages = @(
    @{ Code = 'en'; Name = 'English' },
    @{ Code = 'ru'; Name = 'Русский' },
    @{ Code = 'uk'; Name = 'Українська' }
)
try {
    foreach ($language in $languages) {
        Stop-CyrFlipApp
        New-ItemProperty $key -Name UiLanguage -Value $language.Name -PropertyType String -Force | Out-Null
        foreach ($tab in @(0, 1, 3)) {
            Stop-CyrFlipApp
            New-ItemProperty $key -Name SettingsTab -Value $tab -PropertyType DWord -Force | Out-Null
            Start-Process -FilePath $exe -ArgumentList '/launcher-settings' -WindowStyle Hidden
            $window = Wait-AppWindow -MinWidth 800 -TimeoutSeconds 20
            if (-not $window) { throw "Settings window did not open for $($language.Code), tab $tab" }
            Start-Sleep -Milliseconds 350
            $path = Join-Path $OutDir ("guide-settings-{0}-{1}.png" -f $tab, $language.Code)
            Save-WindowShot -Handle $window.Handle -Path $path | Out-Null
            Write-Output $path
        }
    }
}
finally {
    Stop-CyrFlipApp
    if ($null -eq $originalLanguage) { Remove-ItemProperty $key -Name UiLanguage -ErrorAction SilentlyContinue }
    else { New-ItemProperty $key -Name UiLanguage -Value $originalLanguage -PropertyType String -Force | Out-Null }
    if ($null -eq $originalTab) { Remove-ItemProperty $key -Name SettingsTab -ErrorAction SilentlyContinue }
    else { New-ItemProperty $key -Name SettingsTab -Value $originalTab -PropertyType DWord -Force | Out-Null }
    if ($originalExe) { Start-Process -FilePath $originalExe -WindowStyle Hidden }
}
