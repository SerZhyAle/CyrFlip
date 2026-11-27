<#
.SYNOPSIS
Dump-and-diff of the runtime quality audit (ticket CYRFLP-0061, spec 5.4 "Dump-and-diff"): names the managed
type and the kernel handle type that grow while a scene is looped, on a live, STAGED CyrFlip copy.

.DESCRIPTION
The resource sampler (CyrFlip.AuditRig.psm1) says THAT a curve climbs - baseline A recorded kernel handles
+22..+45 per 1000 events and private bytes +7 MB per 1000 CapsLock toggles, both unexplained. This script
says WHAT climbs:

  1. stage a throwaway profile (Enter-AuditProfile: the user's HKCU\Software\CyrFlip, Run value and running
     instance are saved first and put back in finally; the user's data folder is never opened);
  2. start the staged copy, wait for it to publish its layout, run -Warmup iterations of the scene;
  3. take full dump 1 with cdb (non-invasive attach, `.dump /ma`), run -Iterations more iterations,
     take full dump 2;
  4. read each dump with cdb + SOS (`.loadby sos clr`): `!dumpheap -stat` (live objects only unless
     -IncludeDead) and `!handle 0 1` (kernel handles by type);
  5. diff both tables by type and write the deltas to CSV, the largest first;
  6. DELETE both dumps (spec 3.2 privacy rule: a dump is taken only from a throwaway profile and deleted
     after the diff) unless -KeepDumps.

Scenes (the drivers of Measure-AuditScenes.ps1, one iteration each):
  SC13      one layout switch of a throwaway target window, 100 ms apart
  SC17      one CapsLock toggle, 60 ms apart (the CapsLock state is put back in finally)
  SC38      settings window opened over the launcher pipe and closed (WM_CLOSE = hide)
  IDLE      one second of nothing (the idle wake-ups of F-10)

No clipboard scene on purpose: a full dump holds every string the process saw, so nothing the user ever
copied may be in it - the staged profile has the clipboard history off and no scene touches the clipboard.
The dumps and the CSVs stay under -OutDir on this machine; nothing is uploaded anywhere.

A type is a FINDING when, between the two dumps, its instance count grew by at least
-MinPerIteration x -Iterations (default: one object per iteration) - a managed type by `!dumpheap`, a kernel
handle type by `!handle`. A one-off allocation (a cache filled once) stays below that line when -Warmup
covers it.

Exit codes (CHECK-VERDICT): 0 no type grew past the line, 1 at least one type did, 2 could not verify
(cdb not found, not built, did not start, a dump or SOS failed).

Needs the Debugging Tools for Windows (cdb.exe, x64 - a net48 AnyCPU process is 64-bit here); the CLR's own
SOS ships with .NET Framework 4.8, nothing is downloaded. Desktop-bound (a target window, the CapsLock key,
the settings window) and minutes long - an owner-named or release run.

.EXAMPLE
pwsh -File tools\uitest\Measure-HeapDiff.ps1 -Scene SC17 -Iterations 1000
pwsh -File tools\uitest\Measure-HeapDiff.ps1 -Scene SC13 -Iterations 500 -Warmup 100 -KeepDumps
pwsh -File tools\uitest\Measure-HeapDiff.ps1 -Scene IDLE -Iterations 600
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('SC13', 'SC17', 'SC38', 'IDLE')][string]$Scene,
    # Iterations between the two dumps.
    [ValidateRange(1, 100000)][int]$Iterations = 500,
    # Iterations before the first dump, so one-off caches are already filled.
    [ValidateRange(0, 100000)][int]$Warmup = 50,
    # A type is reported when its count grew by at least this many objects per iteration.
    [double]$MinPerIteration = 1.0,
    # Count dead (not yet collected) objects too; by default only live ones (`!dumpheap -stat -live`).
    [switch]$IncludeDead,
    [switch]$KeepDumps,
    [string]$CdbPath,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$ExePath,
    [string]$OutDir,
    [int]$Top = 20
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.AuditRig.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject "uitest/Measure-HeapDiff $Scene"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $OutDir) { $OutDir = Join-Path $repo "artifacts\audit\S0061\heapdiff-$Scene-$stamp" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Enable-UiTestDpi

function Find-Cdb {
    if ($CdbPath) { if (Test-Path $CdbPath) { return (Resolve-Path $CdbPath).Path }; return $null }
    $cmd = Get-Command cdb.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $root) { continue }
        $candidate = Join-Path $root 'Windows Kits\10\Debuggers\x64\cdb.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    $null
}

function Invoke-Cdb([string]$Cdb, [string[]]$Target, [string[]]$Commands, [string]$Name) {
    # Commands go through -cf (a file), never -c with ';' - cdb splits a -c string in surprising places
    # (memory note "CyrFlip crash-dump analysis").
    $script = Join-Path $OutDir "$Name.cdb"
    $log = Join-Path $OutDir "$Name.log"
    Set-Content -Path $script -Value $Commands -Encoding ascii
    $cdbArgs = @($Target) + @('-cf', $script, '-logo', $log)
    & $Cdb @cdbArgs 2>&1 | Out-Null
    $code = $LASTEXITCODE
    Remove-Item $script -ErrorAction SilentlyContinue
    [pscustomobject]@{ ExitCode = $code; Log = $log; Text = if (Test-Path $log) { Get-Content $log -Raw } else { '' } }
}

function New-Dump([string]$Cdb, [int]$ProcessId, [string]$Path) {
    $r = Invoke-Cdb $Cdb @('-pv', '-p', "$ProcessId") @(".dump /ma /o `"$Path`"", 'qd') ([IO.Path]::GetFileNameWithoutExtension($Path) + '-take')
    if (-not (Test-Path $Path) -or (Get-Item $Path).Length -lt 1MB) {
        throw "cdb did not write the dump $Path (exit $($r.ExitCode); log $($r.Log))"
    }
}

function Read-Dump([string]$Cdb, [string]$Path) {
    $heapCmd = if ($IncludeDead) { '!dumpheap -stat' } else { '!dumpheap -stat -live' }
    $r = Invoke-Cdb $Cdb @('-z', $Path) @('.loadby sos clr', '.echo ==HEAP==', $heapCmd, '.echo ==HANDLES==', '!handle 0 1', '.echo ==END==', 'q') ([IO.Path]::GetFileNameWithoutExtension($Path) + '-read')
    $text = $r.Text
    $heapStart = $text.IndexOf('==HEAP==')
    $handleStart = $text.IndexOf('==HANDLES==')
    $end = $text.IndexOf('==END==')
    if ($heapStart -lt 0 -or $handleStart -lt 0 -or $end -lt 0) { throw "cdb did not run the read script on $Path (log $($r.Log))" }
    $heapText = $text.Substring($heapStart, $handleStart - $heapStart)
    $handleText = $text.Substring($handleStart, $end - $handleStart)

    $heap = @{}
    foreach ($line in $heapText -split '\r?\n') {
        if ($line -match '^\s*([0-9a-fA-F`]{8,17})\s+([\d,]+)\s+([\d,]+)\s+(.+?)\s*$') {
            $type = $Matches[4]
            $count = [long]($Matches[2] -replace ',', '')
            $size = [long]($Matches[3] -replace ',', '')
            if ($heap.ContainsKey($type)) { $heap[$type].Count += $count; $heap[$type].Size += $size }
            else { $heap[$type] = [pscustomobject]@{ Count = $count; Size = $size } }
        }
    }
    if ($heap.Count -eq 0) { throw "SOS returned no heap statistics for $Path - is the CLR's SOS loadable? (log $($r.Log))" }

    $handles = @{}
    foreach ($line in $handleText -split '\r?\n') {
        if ($line -match '^\s*Type\s+(\S.*?)\s*$') {
            $t = $Matches[1]
            if ($handles.ContainsKey($t)) { $handles[$t]++ } else { $handles[$t] = 1 }
        }
    }
    [pscustomobject]@{ Heap = $heap; Handles = $handles }
}

function Compare-Tables($Before, $After, [string]$Kind) {
    $rows = New-Object System.Collections.Generic.List[object]
    $keys = @($Before.Keys) + @($After.Keys) | Sort-Object -Unique
    foreach ($k in $keys) {
        if ($Kind -eq 'managed') {
            $b = $Before[$k]; $a = $After[$k]
            $bc = if ($b) { $b.Count } else { 0 }; $ac = if ($a) { $a.Count } else { 0 }
            $bs = if ($b) { $b.Size } else { 0 }; $as = if ($a) { $a.Size } else { 0 }
        }
        else {
            $bc = [long]($Before[$k]); $ac = [long]($After[$k]); $bs = 0; $as = 0
        }
        if ($ac -eq $bc -and $as -eq $bs) { continue }
        $rows.Add([pscustomobject]@{
                Kind = $Kind; Type = $k; CountBefore = $bc; CountAfter = $ac; CountDelta = $ac - $bc
                SizeBefore = $bs; SizeAfter = $as; SizeDelta = $as - $bs
                PerIteration = [Math]::Round(($ac - $bc) / [double]$Iterations, 3)
            })
    }
    $rows.ToArray()
}

$cdb = Find-Cdb
if (-not $cdb) {
    Add-CheckFinding -Severity notverified -Name 'cdb' -Reason 'cdb.exe not found - install the Debugging Tools for Windows (Windows SDK) or pass -CdbPath'
    Complete-Check
}

$values = @{}
if ($Scene -eq 'SC38') { $values = @{ EnableScenarioLauncher = 1; LauncherFirstEnableDone = 1 } }
$profile = $null
$target = $null
$capsWas = $null
$dumps = @()
try {
    $profile = Enter-AuditProfile -Values $values -Configuration $Configuration -ExePath $ExePath
    $inst = Start-AuditInstance -Profile $profile
    $layout = Join-Path $profile.DataFolder 'layout.txt'
    if ((Wait-AuditCondition -TimeoutMs 20000 -PollMs 30 -Condition { Test-Path $layout }) -lt 0) {
        throw 'the staged copy never published a layout within 20 s'
    }
    Start-Sleep -Seconds 2
    $procId = $inst.Id

    switch ($Scene) {
        'SC13' { $target = Start-TargetWindow -Title 'CyrFlip audit target' }
        'SC17' { $capsWas = Get-CapsLockState }
    }
    $step = {
        switch ($Scene) {
            'SC13' { [void](Switch-WindowLayout -Handle $target.Handle); Start-Sleep -Milliseconds 100 }
            'SC17' { [void](Set-CapsLockState -On (-not (Get-CapsLockState))); Start-Sleep -Milliseconds 60 }
            'SC38' {
                if (-not (Send-AuditPipeCommand -Line '/launcher-settings')) { throw 'the launcher pipe does not answer' }
                if ((Wait-AuditCondition -TimeoutMs 8000 -PollMs 10 -Condition { @(Get-AppWindows | Where-Object { $_.ProcessId -eq $procId -and $_.Width -ge 300 }).Count -gt 0 }) -lt 0) {
                    throw 'the settings window did not appear within 8 s'
                }
                Start-Sleep -Milliseconds 150
                $win = Get-AppWindows | Where-Object { $_.ProcessId -eq $procId -and $_.Width -ge 300 } | Select-Object -First 1
                if ($win) { [void][CyrFlipUi]::PostMessageW($win.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }   # WM_CLOSE: close means hide
                [void](Wait-AuditCondition -TimeoutMs 3000 -PollMs 10 -Condition { @(Get-AppWindows | Where-Object { $_.ProcessId -eq $procId -and $_.Width -ge 300 }).Count -eq 0 })
            }
            'IDLE' { Start-Sleep -Seconds 1 }
        }
    }

    Write-Host "Warm-up: $Warmup iterations of $Scene"
    for ($i = 0; $i -lt $Warmup; $i++) { & $step }
    if ($Scene -eq 'SC17' -and $null -ne $capsWas) { [void](Set-CapsLockState -On $capsWas) }
    Start-Sleep -Milliseconds 800
    $s1 = Get-AuditResourceSample -ProcessId $procId
    $dump1 = Join-Path $OutDir 'before.dmp'
    $dumps += $dump1
    New-Dump $cdb $procId $dump1
    Write-Host ("Dump 1: {0:N1} MB" -f ((Get-Item $dump1).Length / 1MB))

    Write-Host "Measured: $Iterations iterations of $Scene"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $Iterations; $i++) { & $step }
    if ($Scene -eq 'SC17' -and $null -ne $capsWas) { [void](Set-CapsLockState -On $capsWas) }
    Start-Sleep -Milliseconds 800
    $s2 = Get-AuditResourceSample -ProcessId $procId
    if (-not $s2) { throw "the process $procId disappeared during the scene" }
    $dump2 = Join-Path $OutDir 'after.dmp'
    $dumps += $dump2
    New-Dump $cdb $procId $dump2
    Write-Host ("Dump 2: {0:N1} MB after {1:N0} s" -f ((Get-Item $dump2).Length / 1MB), $watch.Elapsed.TotalSeconds)
}
catch {
    Add-CheckFinding -Severity notverified -Name 'scene' -Reason ("{0} (script line {1})" -f $_.Exception.Message, $_.InvocationInfo.ScriptLineNumber)
}
finally {
    if ($Scene -eq 'SC17' -and $null -ne $capsWas) { try { [void](Set-CapsLockState -On $capsWas) } catch { } }
    if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
    Exit-AuditProfile -Profile $profile
}

if ($dumps.Count -eq 2 -and (Test-Path $dumps[0]) -and (Test-Path $dumps[1])) {
    try {
        $before = Read-Dump $cdb $dumps[0]
        $after = Read-Dump $cdb $dumps[1]
        $managed = @(Compare-Tables $before.Heap $after.Heap 'managed')
        $kernel = @(Compare-Tables $before.Handles $after.Handles 'handle')
        $all = @($managed) + @($kernel)
        $csv = Join-Path $OutDir 'heapdiff.csv'
        $all | Sort-Object -Property @{ Expression = 'Kind' }, @{ Expression = { [Math]::Abs($_.SizeDelta) }; Descending = $true }, @{ Expression = { [Math]::Abs($_.CountDelta) }; Descending = $true } |
            Export-Csv -Path $csv -NoTypeInformation -Encoding utf8

        $line = $MinPerIteration * $Iterations
        $grown = @($all | Where-Object { $_.CountDelta -ge $line })
        Write-Host ''
        Write-Host "Managed types, largest size growth first (top $Top):"
        $managed | Sort-Object SizeDelta -Descending | Select-Object -First $Top Type, CountDelta, SizeDelta, PerIteration | Format-Table -AutoSize | Out-String | Write-Host
        Write-Host 'Kernel handle types that changed:'
        $kernel | Sort-Object CountDelta -Descending | Select-Object Type, CountBefore, CountAfter, CountDelta, PerIteration | Format-Table -AutoSize | Out-String | Write-Host
        if ($s1 -and $s2) {
            Write-Host ("Sampler between the dumps: handles {0} -> {1}, private {2:N1} -> {3:N1} MB, heap counter {4:N1} -> {5:N1} MB" -f `
                    $s1.Handles, $s2.Handles, ($s1.PrivateBytes / 1MB), ($s2.PrivateBytes / 1MB), ($s1.ManagedHeap / 1MB), ($s2.ManagedHeap / 1MB))
        }
        Write-Host "Deltas: $csv"
        $summary = [ordered]@{
            scene = $Scene; iterations = $Iterations; warmup = $Warmup; live = (-not $IncludeDead); minPerIteration = $MinPerIteration
            machine = Get-AuditMachine; before = $s1; after = $s2
            grown = @($grown | Select-Object Kind, Type, CountDelta, SizeDelta, PerIteration)
        }
        $summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $OutDir 'heapdiff.json') -Encoding utf8
        foreach ($g in $grown) {
            Add-CheckFinding -Severity fail -Name "growth-$($g.Kind)" -Reason ("{0} {1}: +{2} objects over {3} iterations ({4} per iteration), {5} bytes" -f $Scene, $g.Type, $g.CountDelta, $Iterations, $g.PerIteration, $g.SizeDelta)
        }
    }
    catch {
        Add-CheckFinding -Severity notverified -Name 'read-dump' -Reason $_.Exception.Message
    }
}

if (-not $KeepDumps) {
    foreach ($d in $dumps) { Remove-Item $d -Force -ErrorAction SilentlyContinue }
    if ($dumps.Count) { Write-Host 'Dumps deleted (pass -KeepDumps to keep them for a manual !gcroot).' }
}

Write-Host ''
Complete-Check
