# The "Горячие клавиши" settings page - the master switch and the overview of every chord - on a live app
# (tickets S0056 section 8 and S0062; it was the "Все комбинации" page before S0062).
#
# What xUnit cannot reach: the page in a real window with a real hook behind it. This script stages a
# known set of chords and switches, opens the settings window on the page, saves its screenshot and
# hands the human the checklist below. It never presses a chord for you - CyrFlip ignores injected
# keystrokes by design, so a synthetic Ctrl+Shift+F10 would prove nothing.
#
#   .\tools\uitest\Test-HotkeysOverview.ps1                 # stage, open, screenshot, restore
#   .\tools\uitest\Test-HotkeysOverview.ps1 -Interactive    # keep the staged settings until you press Enter
#
# It BORROWS THE REAL SETTINGS, so it is careful in this order:
#   1. every chord value, per-chord switch, module switch and both profile tables are read from
#      HKCU\Software\CyrFlip and written to a backup file;
#   2. the script REFUSES TO START when that backup cannot be written and read back;
#   3. the way back (the restore routine) is built before the first scene;
#   4. everything is put back in `finally`, with the app stopped while the values are written (a running
#      app writes its own state on exit), and the app is started again if it was running before.
# A name that did not exist before is deleted again, not left behind.
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$Interactive,
    [string]$OutDir = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts\uitest\hotkeys-overview')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Test-HotkeysOverview'

$key = 'HKCU:\Software\CyrFlip'
# Everything the page reads or a reset may write - and the module switches it must never change.
$names = @(
    'CaseHotkey', 'ClipboardHistoryHotkey', 'QuickNotesHotkey', 'ScreenshotHotkey', 'ScreenshotFolderHotkey',
    'TextMenuHotkey', 'SymbolsHotkey', 'ContextMenuChord',
    'EnableHotkeys', 'EnableCaseHotkey', 'EnableHistoryHotkey', 'EnableQuickNotesHotkey', 'EnableScreenshotHotkey',
    'EnableScreenshotFolderHotkey', 'EnableTextMenuHotkey', 'EnableSymbolsHotkey',
    'LayoutConversionProfiles', 'TranslateProfiles',
    'EnableTranslate', 'EnableScenarioLauncher', 'EnableQuickNotes', 'EnableContextMenu', 'EnableSymbols',
    'ScreenshotSaveEnabled', 'SettingsTab', 'SettingsPageId', 'SettingsGroupsState'
)

function Read-Settings {
    $snapshot = [ordered]@{}
    if (-not (Test-Path $key)) { return $snapshot }
    $item = Get-Item $key
    foreach ($name in $names) {
        if ($item.GetValueNames() -contains $name) {
            $snapshot[$name] = @{ Kind = [string]$item.GetValueKind($name); Value = $item.GetValue($name) }
        }
    }
    $snapshot
}

function Write-Settings($snapshot) {
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    foreach ($name in $names) {
        if ($snapshot.Contains($name)) {
            New-ItemProperty -Path $key -Name $name -Value $snapshot[$name].Value -PropertyType $snapshot[$name].Kind -Force | Out-Null
        }
        else { Remove-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue }
    }
}

if (-not (Test-Path (Get-CyrFlipExe -Configuration $Configuration))) {
    Add-CheckFinding -Severity notverified -Name 'exe-missing' -Reason "No $Configuration build of CyrFlip.exe - build it first."
    Complete-Check
}

# 1 + 2: the backup, written and read back before anything is touched.
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$backupPath = Join-Path $OutDir ('backup-' + (Get-Date -Format 'yyMMdd_HHmmss') + '.json')
$before = Read-Settings
try {
    ($before | ConvertTo-Json -Depth 4) | Set-Content -Path $backupPath -Encoding UTF8
    $readBack = Get-Content -Path $backupPath -Raw | ConvertFrom-Json
    if (@($readBack.PSObject.Properties).Count -ne $before.Count) { throw 'the backup does not hold every value' }
}
catch {
    Add-CheckFinding -Severity notverified -Name 'backup-failed' -Reason "Refusing to touch the settings: the backup could not be written and read back ($_)."
    Complete-Check
}
Write-Host "Backup of $($before.Count) value(s): $backupPath" -ForegroundColor Cyan

# 3: the way back, resolved before the first scene.
$wasRunning = [bool](Get-Process CyrFlip -ErrorAction SilentlyContinue)
$restore = {
    Stop-CyrFlipApp
    Start-Sleep -Milliseconds 600
    Write-Settings $before
    if ($wasRunning) { Start-CyrFlipApp -Configuration $Configuration | Out-Null }
}

$failures = @()
try {
    Stop-CyrFlipApp
    Start-Sleep -Milliseconds 600

    # Scene 1: a staged state the page has to tell the truth about.
    #   case flip  -> a chord of the user's own and its switch ON            (differs from the shipped chord)
    #   history    -> its shipped chord with the switch OFF                  (a switch-only difference)
    #   notes      -> module off (default) with the chord kept               ("module is off", a link)
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name 'EnableHotkeys' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'CaseHotkey' -Value 'Ctrl+Alt+Z' -Type String
    Set-ItemProperty -Path $key -Name 'EnableCaseHotkey' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'EnableHistoryHotkey' -Value 0 -Type DWord
    Set-ItemProperty -Path $key -Name 'EnableQuickNotes' -Value 0 -Type DWord
    # The page is remembered by its key (S0062: the stored key wins over a bare index).
    Set-ItemProperty -Path $key -Name 'SettingsPageId' -Value 'Hotkeys' -Type String
    Remove-ItemProperty -Path $key -Name 'SettingsGroupsState' -ErrorAction SilentlyContinue

    # Opened with /launcher-settings, so no mouse is needed (see Test-DarkTheme.ps1). The wait is
    # generous on purpose: a live profile's startup can take much longer than a clean one (measured
    # 23 s to the first window on 2026-10-14, the traffic-light module probing its targets at start),
    # and a settings window that opens at t=21 s is still a window that opened.
    Start-Process (Get-CyrFlipExe -Configuration $Configuration) -ArgumentList '/launcher-settings' | Out-Null
    $window = Wait-AppWindow -TimeoutSeconds 90
    if (-not $window) { throw 'the settings window did not open' }
    Start-Sleep -Milliseconds 800
    $shot = Save-WindowShot -Handle $window.Handle -Path (Join-Path $OutDir 'overview-scene1.png') -Restore
    Write-Host "Scene 1 screenshot: $($shot.Path) ($($shot.Width)x$($shot.Height))" -ForegroundColor Green
    Write-Host ("PASS scene-1-staged - " + "The page opened on the staged state; screenshot $($shot.Path).") -ForegroundColor Green

    Write-Host @'

Human checklist (the unattended half ends here - tick each against the window that is open now):
  [ ] The page "Горячие клавиши" is the second from the bottom (before "О программе"): a first group "Общие выключатели"
      (the master switch and "Уступать хоткеи ...") and then six groups; "Windows" is collapsed.
  [ ] "Исправить CapsLock" shows Ctrl+Alt+Z with its switch ON; its "По умолчанию" button is enabled.
  [ ] "Менеджер буфера" shows Ctrl+Shift+F10 with its switch OFF and the muted word "выключено".
  [ ] "Быстрые заметки" says "модуль выключен" and the words are a link to the notes page.
  [ ] One value, two places: untick the case-flip switch on "Исправление текста" (group "Регистр"), come back here
      - the row shows it off, and the other way round. Neither place fires a change twice. The master switch is one
      control on this page: untick it and every row says "горячие клавиши выключены"; "Исправить CapsLock" and
      "Менеджер буфера" are greyed on their own pages.
  [ ] Change a chord from here ("Изменить..."): a chord another action holds is refused with its name.
  [ ] Press the changed chord in Notepad: the LIVE hook acts on it (a real keypress, by you).
  [ ] Keyboard only: Tab into a row, Up/Down move between rows, Enter on "Изменить...", Space on a switch,
      Delete on a conversion/translation/scenario row clears its chord; focus survives every rebuild.
  [ ] "Сбросить все...": the confirmation counts what changes and names rows it cannot reset; Yes is red;
      Escape answers No. After Yes this page, the feature pages and the real chords all show the shipped values.
  [ ] A clash (give a conversion row the case flip's chord): both rows say "совпадает с: ...".
  [ ] A long language (de, hi), Arabic (right to left), 125 % / 200 % scaling, high contrast: nothing clipped.
'@ -ForegroundColor Yellow

    if ($Interactive) { [void](Read-Host 'Press Enter when the checklist is done - the settings are put back after that') }
}
catch {
    $failures += "$_"
}
finally {
    # 4: always put everything back, whatever happened above.
    try { & $restore; Write-Host 'Settings restored from the backup.' -ForegroundColor Cyan }
    catch { $failures += "RESTORE FAILED - the backup is $backupPath ($_)" }
}

foreach ($f in $failures) { Add-CheckFinding -Severity fail -Name 'hotkeys-overview-failure' -Reason $f }
Complete-Check
