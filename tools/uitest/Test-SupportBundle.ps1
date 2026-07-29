# "Send logs to the author", end to end - the part xUnit cannot reach.
#
# SupportBundleTests proves what goes into the archive and MailSenderTests proves which rung of the
# ladder answers which MAPI code. Neither can prove the one thing that matters at the end: that a
# real mail client opened with the archive actually attached. That needs a human at the keyboard, so
# this script does the mechanical half and tells you exactly what to look at for the rest.
#
#   .\tools\uitest\Test-SupportBundle.ps1              # you press the button, it checks the result
#   .\tools\uitest\Test-SupportBundle.ps1 -NoUi        # it builds the bundle itself, no clicking
#
# -NoUi drives SupportBundle.CreateDefault in the built exe by reflection, against the real log
# folder and the real registry - so the disk half (whitelist, tail, report, retention) is verifiable
# unattended, and the mail half stays the human's. It runs that part through Windows PowerShell,
# because loading a net48 WinExe into pwsh 7 is not something to rely on.
#
# What it does:
#   1. reports whether a clipboard-history.log exists at all - if it does not, this run cannot prove
#      the exclusion rule and says so instead of printing a green tick for a vacuous check;
#   2. waits for a new archive to appear in the reports folder while you press the button;
#   3. opens the archive and checks the whole listing: report.txt present, clipboard-history.log
#      absent, truncation markers where a file was cut, and no more than five archives left over;
#   4. prints report.txt's head so you can see what is about to leave the machine.
#
# It deliberately never injects a canary into clipboard-history.log: that file is the user's real
# clipboard history, and writing test junk into it would corrupt what the app reads back.
[CmdletBinding()]
param(
    [int]$WaitSeconds = 180,
    [switch]$NoUi,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell needs the assembly loaded by name; pwsh 7 already has ZipFile in the runtime
# and answers "cannot find the type" to the same call, so a failure here is not fatal.
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }

$packaged = $false
$logDir = Join-Path $env:LOCALAPPDATA 'CyrFlip'
if (-not (Test-Path $logDir) -and (Test-Path (Join-Path $env:ProgramData 'CyrFlip'))) {
    $logDir = Join-Path $env:ProgramData 'CyrFlip'
    $packaged = $true
}
$reportsDir = Join-Path $logDir 'reports'

"log folder        : $logDir$(if ($packaged) { '  (MSIX layout)' })"
"reports folder    : $reportsDir"

$history = Join-Path $logDir 'clipboard-history.log'
if (Test-Path $history) {
    $size = (Get-Item $history).Length
    "clipboard history : present ($size bytes) - the exclusion check below is meaningful"
} else {
    Write-Warning 'clipboard-history.log is absent: the exclusion check cannot fail in this run.'
    Write-Warning 'Enable the clipboard manager, copy something, then run this script again.'
}

$before = @()
if (Test-Path $reportsDir) { $before = @(Get-ChildItem $reportsDir -Filter 'CyrFlip-logs-*.zip') }
"archives before   : $($before.Count)"

''
if ($NoUi) {
    $exe = Join-Path $PSScriptRoot "..\..\src\CyrFlip\bin\$Configuration\net48\CyrFlip.exe"
    $exe = [System.IO.Path]::GetFullPath($exe)
    if (-not (Test-Path $exe)) { throw "Build it first - not found: $exe" }
    "driving           : $exe (reflection, no UI)"

    # SupportBundle is internal, so this goes through reflection; AppConfig.Load() is what the live
    # app hands it, registry and all, which is the point of running against the real machine.
    $driver = @"
`$ErrorActionPreference = 'Stop'
`$asm = [Reflection.Assembly]::LoadFrom('$exe')
`$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
`$config = `$asm.GetType('CyrFlip.AppConfig').GetMethod('Load', `$flags).Invoke(`$null, @())
`$result = `$asm.GetType('CyrFlip.SupportBundle').GetMethod('CreateDefault', `$flags).Invoke(`$null, @(`$config, [DateTime]::Now))
`$result.GetType().GetField('ArchivePath').GetValue(`$result)
"@
    $driverFile = Join-Path ([System.IO.Path]::GetTempPath()) ('cyrflip-bundle-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    [System.IO.File]::WriteAllText($driverFile, $driver, (New-Object System.Text.UTF8Encoding($false)))
    try {
        $created = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $driverFile
        if ($LASTEXITCODE -ne 0) { throw "the driver exited with $LASTEXITCODE" }
    } finally {
        Remove-Item $driverFile -ErrorAction SilentlyContinue
    }
    $fresh = Get-Item ($created | Select-Object -Last 1)
} else {
    'Now in CyrFlip: tray -> Settings -> "About and extras" -> "Send logs to the author..".'
    'Leave the dialog open when it appears - this script only needs the archive on disk.'
    ''

    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $fresh = $null
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $reportsDir) {
            $now = @(Get-ChildItem $reportsDir -Filter 'CyrFlip-logs-*.zip')
            $new = @($now | Where-Object { $before.FullName -notcontains $_.FullName })
            if ($new.Count -gt 0) { $fresh = $new | Sort-Object LastWriteTimeUtc | Select-Object -Last 1; break }
        }
        Start-Sleep -Milliseconds 500
    }

    if ($null -eq $fresh) { throw "No new archive appeared in $reportsDir within $WaitSeconds s." }
}

''
"archive           : $($fresh.Name)"
"size              : $([math]::Round($fresh.Length / 1KB, 1)) KB"

$zip = [System.IO.Compression.ZipFile]::OpenRead($fresh.FullName)
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    'contents          :'
    foreach ($entry in $zip.Entries) {
        "                    {0,-26} {1,8} bytes" -f $entry.FullName, $entry.Length
    }

    $failures = New-Object System.Collections.Generic.List[string]
    if ($names -notcontains 'report.txt') { $failures.Add('report.txt is missing from the archive') }
    if ($names -contains 'clipboard-history.log') { $failures.Add('CLIPBOARD HISTORY IS IN THE ARCHIVE - hard rule broken') }

    # A truncated file has to say so in its own first line; a silently shortened log reads as complete.
    foreach ($entry in $zip.Entries) {
        if ($entry.Length -lt 400KB) { continue }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $first = $reader.ReadLine() } finally { $reader.Dispose() }
        if ($first -notlike '--- truncated:*') {
            $failures.Add("$($entry.FullName) is at the size cap but carries no truncation marker")
        }
    }

    $report = $zip.GetEntry('report.txt')
    if ($report) {
        $reader = New-Object System.IO.StreamReader($report.Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        ''
        'report.txt (head) :'
        ($text -split "`r?`n" | Select-Object -First 12) | ForEach-Object { "                    $_" }
    }
} finally {
    $zip.Dispose()
}

$after = @(Get-ChildItem $reportsDir -Filter 'CyrFlip-logs-*.zip')
"archives after    : $($after.Count)"
if ($after.Count -gt 5) { $failures.Add("retention kept $($after.Count) archives, expected at most 5") }

''
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    throw "$($failures.Count) check(s) failed."
}
Write-Host 'PASS: archive contents, truncation markers and retention are as specified.' -ForegroundColor Green

''
if ($NoUi) {
    'This run covered the disk half only - no dialog was shown and no message was prepared.'
    'Run the script without -NoUi and press the button to cover the list below.'
}
'Left for your eyes - nothing in this repo can assert it:'
'  1. press "Create the message" and confirm the compose window opened at all;'
'  2. the recipient is sza@ukr.net and the subject names the version, the OS and the UI language;'
'  3. THE ARCHIVE IS ATTACHED. If instead you got the "does not accept an attachment from a link"'
'     notice, this machine has no Simple MAPI client (new Outlook and webmail do not register one)'
'     and the mailto: rung answered - that is the designed fallback, not a bug;'
'  4. nothing was sent until you pressed Send yourself.'
