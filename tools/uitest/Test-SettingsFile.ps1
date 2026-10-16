<#
.SYNOPSIS
Operator-only S0055 settings transfer test. Uses the real General-page commands, preserves the
profile with CyrFlip's own export before the first scene, and restores registry/system launch
context in finally. No build or CI runner invokes it. The artifact folder contains local settings.
#>
[CmdletBinding()]
param([string]$ExePath, [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/settings-file/ui'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    $arguments = @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',$PSCommandPath,'-OutputDirectory',$OutputDirectory)
    if ($ExePath) { $arguments += @('-ExePath',$ExePath) }
    & powershell.exe @arguments
    exit $LASTEXITCODE
}
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms,UIAutomationClient,UIAutomationTypes
Enable-UiTestDpi
if (-not $ExePath) { $ExePath = Get-CyrFlipExe -Configuration Debug }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$OutputDirectory = Join-Path $OutputDirectory (Get-Date -Format 'yyyyMMdd-HHmmss')
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$assembly = [Reflection.Assembly]::LoadFrom($ExePath)
$translate = $assembly.GetType('CyrFlip.Localization').GetMethod('Translate')
$language = 'English'
function T([string]$source) { $translate.Invoke($null, @($script:language,$source)) }
$profilePath = 'Software\CyrFlip'
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($profilePath)
$original = @{}
if ($key) {
    try { foreach ($name in $key.GetValueNames()) { $original[$name] = @{ Value=$key.GetValue($name); Kind=$key.GetValueKind($name) } } }
    finally { $key.Dispose() }
}
$original | Export-Clixml -LiteralPath (Join-Path $OutputDirectory 'original-registry.xml')
$previousApps = @(Get-Process CyrFlip -ErrorAction SilentlyContinue | ForEach-Object { $_.Path } | Select-Object -Unique)
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$run = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runPath)
$runValue = if ($run) { $run.GetValue('CyrFlip') } else { $null }
$runKind = if ($null -ne $runValue) { $run.GetValueKind('CyrFlip') } else { [Microsoft.Win32.RegistryValueKind]::String }
if ($run) { $run.Dispose() }
$links = @()
foreach ($place in @('Programs','DesktopDirectory')) {
    $path = Join-Path ([Environment]::GetFolderPath($place)) 'CyrFlip.lnk'
    $saved = Join-Path $OutputDirectory ($place+'.lnk')
    $exists = Test-Path -LiteralPath $path
    if ($exists) { Copy-Item -LiteralPath $path -Destination $saved -Force }
    $links += @{ Path=$path; Saved=$saved; Exists=$exists }
}
function Stop-Subject {
    $apps = @(Get-Process CyrFlip -ErrorAction SilentlyContinue)
    Stop-CyrFlipApp
    foreach ($app in $apps) { if (-not $app.WaitForExit(5000)) { throw 'Previous CyrFlip process has not exited.' } }
    Start-Sleep -Milliseconds 300
}
$dataFolder = $assembly.GetType('CyrFlip.DataFolder').GetField('Current',[Reflection.BindingFlags]'Static,Public,NonPublic').GetValue($null)
$snapshotFolder = [IO.Path]::GetFullPath((Join-Path $dataFolder 'backups'))
$snapshotSaved = Join-Path $OutputDirectory 'undo-original'
[void][IO.Directory]::CreateDirectory($snapshotSaved)
$originalSnapshots = @()
if (Test-Path -LiteralPath $snapshotFolder) {
    $originalSnapshots = @(Get-ChildItem -LiteralPath $snapshotFolder -Filter '*settings-before-import-*.json' -File)
    foreach ($file in $originalSnapshots) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $snapshotSaved $file.Name) }
}
function Root($window) { [Windows.Automation.AutomationElement]::FromHandle($window.Handle) }
function Named($window,[string]$caption) {
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,$caption)
    (Root $window).FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
}
function Click($window,[string]$caption) {
    $control = Named $window $caption
    if (-not $control) { throw "Missing command: $caption" }
    Set-WindowForeground -Handle $window.Handle | Out-Null
    $handle = [IntPtr]$control.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { throw 'Command has no native handle.' }
    [void][CyrFlipUi]::PostMessageW($handle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 350
}
function Wait-Window([string]$caption) {
    $deadline = (Get-Date).AddSeconds(12)
    do {
        $window = Get-AppWindows | Where-Object { $_.Title -eq $caption } | Select-Object -First 1
        if ($window) { return $window }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw "Window missing: $caption"
}
function Open-Settings {
    $script:language = if ($original.ContainsKey('UiLanguage')) { [string](Get-ItemPropertyValue 'HKCU:\Software\CyrFlip' -Name UiLanguage) } else { 'English' }
    $subject = Start-Process -FilePath $ExePath -ArgumentList '/launcher-settings' -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(12)
    do {
        $settings = Get-AppWindows | Where-Object { $_.Title -eq (T 'CyrFlip Settings') } | Select-Object -First 1
        if ($settings) { break }
        $tour = Get-AppWindows | Where-Object { $_.Title -eq 'CyrFlip' } | Select-Object -First 1
        if ($tour -and (Named $tour (T 'Skip'))) { Click $tour (T 'Skip') }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if (-not $settings) { Get-AppWindows | Format-Table Title,Class,Width; Get-Process CyrFlip -ErrorAction SilentlyContinue | Format-Table Id,Path; Write-Host ('Expected: '+(T 'CyrFlip Settings')); if ($subject.HasExited) { Write-Host ('Subject exited: '+$subject.ExitCode) }; throw 'Settings did not open.' }
    Start-Sleep -Seconds 1
    $script:tabHandle = [IntPtr]::Zero
    $callback = [CyrFlipUi+EnumProc]{ param($handle,$unused)
        if ([CyrFlipUi]::Cls($handle) -like '*SysTabControl32*') { $script:tabHandle=$handle }
        return $true
    }
    [void][CyrFlipUi]::EnumChildWindows($settings.Handle,$callback,[IntPtr]::Zero)
    if ($script:tabHandle -eq [IntPtr]::Zero) { throw 'No native settings navigation.' }
    [void][CyrFlipUi]::PostMessageW($script:tabHandle,0x0100,[IntPtr]0x24,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    if (-not (Named $settings (T 'General'))) { throw 'General page is missing.' }
    return $settings
}
function File-Command($settings,[string]$command,[string]$path) {
    Click $settings (T $command)
    $deadline = (Get-Date).AddSeconds(10); $edit = $null; $dialog = $null
    do {
        foreach ($candidate in @(Get-AppWindows | Where-Object Class -EQ '#32770')) {
            $editId = if ($command -eq 'Import settings...') { '1148' } else { '1001' }
            $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty,$editId)
            $edit = (Root $candidate).FindAll([Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object { $_.Current.NativeWindowHandle -ne 0 -and [CyrFlipUi]::Cls([IntPtr]$_.Current.NativeWindowHandle) -eq 'Edit' } | Select-Object -First 1
            if ($edit) { $dialog=$candidate; break }
        }
        if (-not $edit) { Start-Sleep -Milliseconds 200 }
    } while (-not $edit -and (Get-Date) -lt $deadline)
    if (-not $edit) { throw 'File picker did not expose its filename edit.' }
    Set-WindowForeground -Handle $dialog.Handle | Out-Null
    $result = [IntPtr]::Zero
    [void][CyrFlipUi]::SendMessageTimeoutW([IntPtr]$edit.Current.NativeWindowHandle,0x000C,[IntPtr]::Zero,$path,0x0002,3000,[ref]$result)
    $okCondition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty,'1')
    $ok = (Root $dialog).FindAll([Windows.Automation.TreeScope]::Descendants,$okCondition) | Where-Object { $_.Current.NativeWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $ok) { throw 'File picker has no accept command.' }
    [void][CyrFlipUi]::PostMessageW([IntPtr]$ok.Current.NativeWindowHandle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}
function Export-Settings($settings,[string]$path) {
    File-Command $settings 'Export settings...' $path
    $result = Wait-Window 'CyrFlip'
    Click $result 'OK'
    if (-not (Test-Path -LiteralPath $path)) { throw 'CyrFlip export failed. Refusing to change settings.' }
    $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($json.format -ne 'CyrFlip Settings' -or $json.schemaVersion -ne 2) { throw 'Export is not a settings file.' }
    return $json
}
function Write-Scene($values) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($profilePath)
    try { foreach ($name in $values.Keys) { $key.SetValue($name,$values[$name],$(if ($values[$name] -is [string]) { [Microsoft.Win32.RegistryValueKind]::String } else { [Microsoft.Win32.RegistryValueKind]::DWord })) } }
    finally { $key.Dispose() }
}
function Import-Settings($settings,[string]$path) {
    File-Command $settings 'Import settings...' $path
    $preview = Wait-Window (T 'Settings transfer')
    Save-WindowShot -Handle $preview.Handle -Path (Join-Path $OutputDirectory 'preview.png')
    Click $preview (T 'Apply')
    Start-Sleep -Milliseconds 900
    if (Get-AppWindows | Where-Object Title -EQ (T 'Settings transfer')) { throw 'Preview did not apply.' }
}
function Compare-Settings($expected,$actual) {
    foreach ($section in @('settings','tables')) {
        foreach ($property in $expected.$section.PSObject.Properties) {
            $left = ConvertTo-Json -InputObject $property.Value -Depth 10 -Compress
            $right = ConvertTo-Json -InputObject $actual.$section.($property.Name) -Depth 10 -Compress
            if ($left -cne $right) { throw "Live export differs: $($property.Name)" }
        }
    }
}
function Tray-State {
    $icon = Get-TrayIcon -ErrorAction SilentlyContinue
    if (-not $icon) { throw 'No live tray indicator.' }
    return $icon.Name
}
$exported = $false
try {
    Stop-Subject
    $settings = Open-Settings
    $initialPath = Join-Path $OutputDirectory 'settings-original.json'
    $initial = Export-Settings $settings $initialPath
    $exported = $true
    Stop-Subject
    $desired = @{ UiLanguage='English'; Theme='dark'; CursorSize=48; EnableCursorChange=0; EnableCaretOverlay=1; CaretDotMode=1;
        ConvertSymbols=0; EnableLanguageSwitch=1; FlipCapsLockAfter=1; EnableHotkeys=1; EnableCaseHotkey=0; EnableHistoryHotkey=0;
        DeferToRemoteDesktop=1; KeepSystemAwake=0; KeepScreenOn=0; EnableClipboardHistory=0; EnableQuickNotes=0; ShowClipboardHistoryOnStartup=0;
        SymbolsHotkey='Ctrl+Shift+F24'; EnableSymbols=1 }
    Write-Scene $desired
    $settings = Open-Settings
    $desiredPath = Join-Path $OutputDirectory 'settings-desired.json'
    $expected = Export-Settings $settings $desiredPath
    Stop-Subject
    $scrambled = @{}; foreach ($name in $desired.Keys) {
        $scrambled[$name] = if ($name -eq 'Theme') { 'light' } elseif ($name -eq 'UiLanguage') { 'English' } elseif ($name -eq 'CursorSize') { 24 }
            elseif ($name -eq 'SymbolsHotkey' -or $name -eq 'EnableSymbols') { $desired[$name] }   # pinned in both scenes: with the module off the import candidate rebuilds the symbols chord from its default (Ctrl+Shift+F9), which would refuse the borrowed profile's row chords
            elseif ($name -in @('EnableClipboardHistory','EnableQuickNotes','KeepSystemAwake','KeepScreenOn','ShowClipboardHistoryOnStartup')) { 0 } else { 1-[int]$desired[$name] }
    }
    Write-Scene $scrambled
    $settings = Open-Settings
    $beforeImport = Export-Settings $settings (Join-Path $OutputDirectory 'settings-scrambled.json')
    Import-Settings $settings $desiredPath
    foreach ($name in $desired.Keys) {
        $actual = Get-ItemPropertyValue 'HKCU:\Software\CyrFlip' -Name $name
        if ($actual -cne $desired[$name]) { throw "Registry did not import: $name" }
    }
    $live = Export-Settings $settings (Join-Path $OutputDirectory 'settings-live.json')
    Compare-Settings $expected $live
    Click $settings (T 'Restore settings before import...')
    $undoPreview = Wait-Window (T 'Settings transfer')
    Click $undoPreview (T 'Apply')
    Start-Sleep -Milliseconds 800
    $undone = Export-Settings $settings (Join-Path $OutputDirectory 'settings-undone.json')
    Compare-Settings $beforeImport $undone
    Import-Settings $settings $desiredPath
    $trayLive = Tray-State
    Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutputDirectory 'live.png')
    Stop-Subject
    $settings = Open-Settings
    $fresh = Export-Settings $settings (Join-Path $OutputDirectory 'settings-fresh.json')
    Compare-Settings $live $fresh
    if ((Tray-State) -cne $trayLive) { throw 'Live tray indicator differs from a fresh process.' }
    Save-WindowShot -Handle $settings.Handle -Path (Join-Path $OutputDirectory 'fresh.png')
    $liveLuminance = Get-ImageLuminance -Path (Join-Path $OutputDirectory 'live.png')
    $freshLuminance = Get-ImageLuminance -Path (Join-Path $OutputDirectory 'fresh.png')
    if ([Math]::Abs($liveLuminance-$freshLuminance) -gt 0.025) { throw 'Live theme differs from a fresh process.' }
    Write-Host 'PASS: registry, live export, undo, tray indicator, theme and fresh-process state agree.'
    Write-Host 'Human checklist owed: refused chord, long-language interaction, high-contrast desktop and marker beside a real text caret.'
} catch {
    Write-Host $_.ScriptStackTrace
    throw
} finally {
    Stop-Subject
    # Restore even a failed startup/export: startup can migrate values and synchronize shell entries.
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($profilePath)
    try {
        foreach ($name in $key.GetValueNames()) { if (-not $original.ContainsKey($name)) { $key.DeleteValue($name,$false) } }
        foreach ($name in $original.Keys) { $key.SetValue($name,$original[$name].Value,$original[$name].Kind) }
    } finally { $key.Dispose() }
    $run = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($runPath)
    try { if ($null -eq $runValue) { $run.DeleteValue('CyrFlip',$false) } else { $run.SetValue('CyrFlip',$runValue,$runKind) } }
    finally { $run.Dispose() }
    foreach ($link in $links) {
        if ($link.Exists) { Copy-Item -LiteralPath $link.Saved -Destination $link.Path -Force }
        elseif (Test-Path -LiteralPath $link.Path) { Remove-Item -LiteralPath $link.Path -Force }
    }
    if (Test-Path -LiteralPath $snapshotFolder) {
        foreach ($file in @(Get-ChildItem -LiteralPath $snapshotFolder -Filter '*settings-before-import-*.json' -File)) {
            if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($file.FullName)) -ne $snapshotFolder) { throw 'Snapshot restore escaped its folder.' }
            Remove-Item -LiteralPath $file.FullName -Force
        }
    }
    foreach ($file in $originalSnapshots) { Copy-Item -LiteralPath (Join-Path $snapshotSaved $file.Name) -Destination (Join-Path $snapshotFolder $file.Name) -Force }
    foreach ($app in $previousApps) { Start-Process -FilePath $app -WindowStyle Hidden | Out-Null }
    Write-Host 'Original registry settings and launch context restored.'
}
