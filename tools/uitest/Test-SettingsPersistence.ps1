<#
.SYNOPSIS
Owner-run S0072 G6 evidence collector. Run in a disposable Windows test account.
.DESCRIPTION
Exercises the real distribution operations and import preview. Never run by CI.
The packaged pass deliberately removes the package (including recorded content).
Export the original settings before starting; the finally block asks you to restore
that file through the app. Evidence files contain private hosts and program paths.
.EXAMPLE
./Test-SettingsPersistence.ps1 -Channel Unpackaged -ExePath ./bin/CyrFlip.exe -ReplacementExe ./artifacts/CyrFlip.exe -OriginalSettings ./original.json
.EXAMPLE
./Test-SettingsPersistence.ps1 -Channel Packaged -PackageName CyrFlip -InstallPackage ./old.msix -UpdatePackage ./new.msix -OriginalSettings ./original.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Unpackaged','Packaged')][string]$Channel,
    [Parameter(Mandatory)][string]$OriginalSettings,
    [string]$ExePath,
    [string]$ReplacementExe,
    [string]$PackageName,
    [string]$InstallPackage,
    [string]$UpdatePackage,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/settings-persistence')
)
$ErrorActionPreference = 'Stop'
$OriginalSettings = (Resolve-Path -LiteralPath $OriginalSettings).Path
$original = Get-Content -LiteralPath $OriginalSettings -Raw -Encoding UTF8 | ConvertFrom-Json
if ($original.format -ne 'CyrFlip Settings') { throw 'Export the original profile before running this script.' }
if ($Channel -eq 'Unpackaged') {
    $ExePath = (Resolve-Path -LiteralPath $ExePath).Path
    $ReplacementExe = (Resolve-Path -LiteralPath $ReplacementExe).Path
} else {
    $InstallPackage = (Resolve-Path -LiteralPath $InstallPackage).Path
    $UpdatePackage = (Resolve-Path -LiteralPath $UpdatePackage).Path
    if (-not $PackageName) { throw 'PackageName must be the exact package identity name.' }
}
$OutputDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) (Get-Date -Format 'yyyyMMdd-HHmmss')
[void][IO.Directory]::CreateDirectory($OutputDirectory)
Copy-Item -LiteralPath $OriginalSettings -Destination (Join-Path $OutputDirectory 'original.json')
$backupFolder = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CyrFlip/Backups'
$scenarioFolder = Join-Path $env:APPDATA 'CyrFlip/Scenarios'
$profileCaptured = $false
function Save-FolderFiles([string]$source, [string]$destination, [string]$filter) {
    [void][IO.Directory]::CreateDirectory($destination)
    if (Test-Path -LiteralPath $source) {
        Get-ChildItem -LiteralPath $source -Filter $filter -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination $_.Name)
        }
    }
}
function Restore-FolderFiles([string]$source, [string]$destination, [string]$filter) {
    $target = [IO.Path]::GetFullPath($destination).TrimEnd('\','/')
    [void][IO.Directory]::CreateDirectory($target)
    Get-ChildItem -LiteralPath $target -Filter $filter -File | ForEach-Object {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($_.FullName)) -ne $target) { throw 'Restore escaped its named folder.' }
        Remove-Item -LiteralPath $_.FullName -Force
    }
    Get-ChildItem -LiteralPath $source -Filter $filter -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name)
    }
}
function Checkpoint([string]$instruction) {
    Write-Host $instruction
    [void](Read-Host 'Complete this step, then press Enter')
}
function LatestBackup {
    $file = Get-ChildItem -LiteralPath $backupFolder -Filter 'CyrFlip-settings-*.json' -File |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $file) { throw 'No Documents backup found. Enable backups and close CyrFlip cleanly.' }
    $doc = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($doc.schemaVersion -ne 2) { throw 'Expected a v2 backup.' }
    return @{ File=$file; Document=$doc }
}
function Signals($doc) {
    # These are the three deliberately edited, stable settings. Geometry changes when windows open.
    [ordered]@{
        Chord = $doc.settings.CaseHotkey
        Targets = $doc.tables.TrafficLightTargets
        Scenarios = $doc.tables.scenarios
    } | ConvertTo-Json -Depth 30 -Compress
}
function Evidence([string]$stage, $expected) {
    $backup = LatestBackup
    if ($backup.File.LastWriteTimeUtc -le $script:lastBackupWrite) { throw "$stage did not produce a new backup after clean exit." }
    $script:lastBackupWrite = $backup.File.LastWriteTimeUtc
    $actual = Signals $backup.Document
    Copy-Item -LiteralPath $backup.File.FullName -Destination (Join-Path $OutputDirectory ($stage+'.json'))
    if ($actual -cne $expected) { throw "$stage changed the chord, targets or scenarios." }
    "$Channel $stage PASS: CaseHotkey, TrafficLightTargets, scenarios; backup=$($backup.File.Name)" |
        Tee-Object -FilePath (Join-Path $OutputDirectory 'evidence.txt') -Append | Write-Host
    if ($Channel -eq 'Unpackaged') {
        Get-ItemProperty -LiteralPath 'HKCU:\Software\CyrFlip' |
            Select-Object CaseHotkey,TrafficLightTargets | Export-Clixml -LiteralPath (Join-Path $OutputDirectory ($stage+'-registry.xml'))
        Get-ChildItem -LiteralPath (Join-Path $env:APPDATA 'CyrFlip/Scenarios') -Filter '*.xml' |
            Select-Object Name,Length | Export-Clixml -LiteralPath (Join-Path $OutputDirectory ($stage+'-scenario-files.xml'))
    }
}
try {
    Checkpoint 'Use a disposable test account. Confirm its original profile is exported and no recorded content needs preserving.'
    if ($Channel -eq 'Unpackaged') {
        Checkpoint 'Close every CyrFlip instance before the backup-first profile capture.'
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\CyrFlip')
        if (-not $key) { throw 'Expected the existing profile you exported in OriginalSettings.' }
        $registryBefore = @{}
        try {
            foreach ($name in $key.GetValueNames()) {
                $registryBefore[$name] = @{ Value=$key.GetValue($name); Kind=$key.GetValueKind($name) }
            }
        } finally { $key.Dispose() }
        $registryBefore | Export-Clixml -LiteralPath (Join-Path $OutputDirectory 'registry-before.xml')
        Save-FolderFiles $scenarioFolder (Join-Path $OutputDirectory 'scenarios-before') '*.xml'
        Save-FolderFiles $backupFolder (Join-Path $OutputDirectory 'backups-before') 'CyrFlip-settings-*.json'
        Copy-Item -LiteralPath $ExePath -Destination (Join-Path $OutputDirectory 'CyrFlip-before.exe')
        $profileCaptured = $true
    }
    if ($Channel -eq 'Packaged') {
        Add-AppxPackage -Path $InstallPackage
        Checkpoint 'Launch the installed Store package from Start.'
    } else { Start-Process -FilePath $ExePath -WindowStyle Hidden }
    Checkpoint 'Set a distinctive case shortcut, add a network target and a launcher scenario. Enable backups. Close CyrFlip cleanly.'
    $baseline = LatestBackup
    if (-not $baseline.Document.settings.CaseHotkey -or -not $baseline.Document.tables.TrafficLightTargets.Count -or -not $baseline.Document.tables.scenarios.Count) {
        throw 'The baseline must contain a chord, target and scenario.'
    }
    Copy-Item -LiteralPath $baseline.File.FullName -Destination (Join-Path $OutputDirectory 'baseline.json')
    $script:lastBackupWrite = $baseline.File.LastWriteTimeUtc
    $expected = Signals $baseline.Document
    if ($Channel -eq 'Unpackaged') {
        Copy-Item -LiteralPath $ReplacementExe -Destination $ExePath -Force
        Start-Process -FilePath $ExePath -WindowStyle Hidden
        Checkpoint 'Inspect the chord, target and scenario after redeploy. Close CyrFlip cleanly.'
        Evidence 'reinstall' $expected
    } else {
        Add-AppxPackage -Path $UpdatePackage
        Checkpoint 'Launch the updated package; inspect all three settings, then close CyrFlip cleanly.'
        Evidence 'update' $expected
        $package = @(Get-AppxPackage -Name $PackageName)
        if ($package.Count -ne 1) { throw 'Expected exactly one package with the supplied identity.' }
        Remove-AppxPackage -Package $package[0].PackageFullName
        if (-not (Test-Path -LiteralPath $baseline.File.FullName)) { throw 'Documents backup disappeared on removal.' }
        Add-AppxPackage -Path $UpdatePackage
        Checkpoint 'Launch the reinstalled package. After the welcome tour choose Not now. Close and restart: confirm the restore offer returns, then choose Restore and Apply in the preview. Inspect all three settings and close cleanly.'
        Evidence 'uninstall-restore' $expected
    }
} finally {
    if ($profileCaptured) {
        Start-Process -FilePath $ExePath -ArgumentList '/exit' -WindowStyle Hidden -Wait
        Checkpoint 'Confirm CyrFlip has exited so the original profile can be restored.'
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\CyrFlip')
        try {
            foreach ($name in $key.GetValueNames()) { if (-not $registryBefore.ContainsKey($name)) { $key.DeleteValue($name) } }
            foreach ($name in $registryBefore.Keys) { $key.SetValue($name,$registryBefore[$name].Value,$registryBefore[$name].Kind) }
        } finally { $key.Dispose() }
        Restore-FolderFiles (Join-Path $OutputDirectory 'scenarios-before') $scenarioFolder '*.xml'
        Restore-FolderFiles (Join-Path $OutputDirectory 'backups-before') $backupFolder 'CyrFlip-settings-*.json'
        Copy-Item -LiteralPath (Join-Path $OutputDirectory 'CyrFlip-before.exe') -Destination $ExePath -Force
        Write-Host "Original registry, scenarios, backups and executable restored. Private evidence: $OutputDirectory"
    } elseif ($Channel -eq 'Packaged') {
        Checkpoint "Restore the original settings through General > Import settings using $OriginalSettings. Review opt-in recording switches and restore their original values. Close the app. Evidence is private: $OutputDirectory"
    }
}
