<#
.SYNOPSIS
The clipboard history's size curve (ticket S0061, scene SC22, section 5.5 RS-4 / LK-5): how replay,
search and the erase of one entry grow with the number of entries, measured on the shipped code.

.DESCRIPTION
The history is unbounded by decision, so the deliverable is the shape of the curve, not a cap. This
script generates a synthetic journal of N entries through the built exe's own ClipboardHistoryJournal
(DPAPI, the cipher the app uses - QuickNotesCipher.Dpapi), then, for each N:

  * stage   - N "add" records appended and drained (PowerShell drives the loop, so this is staging
              cost, not product cost; reported for planning only);
  * load    - one ClipboardHistoryJournal.Load into a fresh ClipboardHistoryOrder, timed: the replay
              the app runs on the pool (at start, and since S0082 also for the manager window and the
              exchange file while the history is off);
  * heap    - managed heap after a forced collection, before and after the load, per entry;
  * search  - ClipboardHistorySearch.Matches over every entry for a query that matches none and one
              that matches all, in a compiled loop (the search window's background pass);
  * erase   - one "delete" record appended and drained: the whole-file rewrite of S0031 CH2-5.

Nothing of the user's is touched: no clipboard, no registry, no real history. The journal lives in a
throwaway %TEMP%\CyrFlip-historycurve-* folder that is deleted at the end; every text is synthetic.

The run is a measurement, not a budget (D2 is open): exit 0 when every size was measured and replayed
whole, 1 when a replay lost or skipped an entry or the erase did not remove it, 2 when it could not
run (no build, Windows PowerShell missing). The last stdout line is the verdict line (CHECK-VERDICT).

Wall time on machine A for the default sizes is a few seconds; 100000 is opt-in (-Sizes) and takes
tens of seconds, mostly DPAPI.

.EXAMPLE
pwsh -File tools\uitest\Measure-HistoryJournalCurve.ps1
.EXAMPLE
pwsh -File tools\uitest\Measure-HistoryJournalCurve.ps1 -Sizes 100,1000,10000,100000 -OutJson temp\S0061\sc22.json
#>
[CmdletBinding()]
param(
    [int[]]$Sizes = @(100, 1000, 10000),
    [ValidateRange(16, 65536)]
    [int]$TextLength = 200,
    [string]$ExePath,
    [string]$OutJson
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Measure-HistoryJournalCurve'

$exe = if ($ExePath) { $ExePath } else { Join-Path $repo 'src\CyrFlip\bin\Release\net48\CyrFlip.exe' }
if (-not (Test-Path $exe)) {
    Add-CheckFinding -Severity notverified -Name 'not-built' -Reason "no build at $exe"
    Complete-Check
}
$exe = (Resolve-Path $exe).Path
$ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path $ps51)) {
    Add-CheckFinding -Severity notverified -Name 'no-windows-powershell' -Reason 'the net48 exe is loaded by Windows PowerShell 5.1, which is missing'
    Complete-Check
}

$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
$build = [Environment]::OSVersion.Version.ToString()
Write-Host "machine: $env:COMPUTERNAME | $cpu | $ramGb GB | Windows $build"
Write-Host "exe:     $exe $((Get-Item $exe).VersionInfo.FileVersion)"

$work = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-historycurve-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null

# Runs in Windows PowerShell 5.1 (the exe is net48 and uses System.Web.Extensions). One line per size:
# N|stageMs|loadMs|skipped|count|fileBytes|heapBytes|missMs|hitMs|hitCount|eraseMs|countAfterErase
$probe = @'
param($Exe, $Work, [string]$SizeList, [int]$TextLength)
$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$bf = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
$tJournal = $asm.GetType('CyrFlip.ClipboardHistoryJournal', $true)
$tRecord  = $asm.GetType('CyrFlip.ClipboardHistoryJournal+Record', $true)
$tOrder   = $asm.GetType('CyrFlip.ClipboardHistoryOrder', $true)
$tEntry   = $asm.GetType('CyrFlip.ClipboardHistoryEntry', $true)
$tCipherI = $asm.GetType('CyrFlip.IQuickNotesCipher', $true)
$tCipher  = $asm.GetType('CyrFlip.QuickNotesCipher', $true)
$tService = $asm.GetType('CyrFlip.ClipboardHistoryService', $true)
$tSearch  = $asm.GetType('CyrFlip.ClipboardHistorySearch', $true)

$cipher  = $tCipher.GetField('Dpapi', $bf).GetValue($null)
$hash    = $tService.GetMethod('Hash', $bf)
$ctor    = $tJournal.GetConstructor($bf, $null, [Type[]]@([string], $tCipherI, [Action]), $null)
$append  = $tJournal.GetMethod('Append', $bf)
$drain   = $tJournal.GetMethod('Drain', $bf)
$load    = $tJournal.GetMethod('Load', $bf)
$dispose = $tJournal.GetMethod('Dispose', $bf)
$fAction = $tRecord.GetField('Action'); $fUuid = $tRecord.GetField('Uuid'); $fCreated = $tRecord.GetField('CreatedAt')
$fText = $tRecord.GetField('Text'); $fApp = $tRecord.GetField('SourceApp'); $fTitle = $tRecord.GetField('SourceTitle')
$pCount = $tOrder.GetProperty('Count')
$mFind  = $tOrder.GetMethod('Find', $bf)
$pEntries = $tOrder.GetProperty('Entries')

# The search loop runs compiled, so the per-entry cost is the product's and not PowerShell's.
Add-Type -TypeDefinition @"
using System;
using System.Collections;
public static class AuditHistoryLoop {
    public static int Count(IEnumerable items, Func<object, string, bool> match, string query) {
        int n = 0;
        foreach (object o in items) if (match(o, query)) n++;
        return n;
    }
}
"@
$E = [Linq.Expressions.Expression]
$pe = $E::Parameter([object], 'e'); $pq = $E::Parameter([string], 'q')
$call = $E::Call($tSearch.GetMethod('Matches', $bf), $E::Convert($pe, $tEntry), $pq)
$match = $E::Lambda([Func[object, string, bool]], $call, [Linq.Expressions.ParameterExpression[]]@($pe, $pq)).Compile()

function New-Record([string]$action, [string]$uuid, [long]$ticks, [string]$text) {
    $r = [Activator]::CreateInstance($tRecord)
    $fAction.SetValue($r, $action); $fUuid.SetValue($r, $uuid); $fCreated.SetValue($r, $ticks)
    if ($action -eq 'add') { $fText.SetValue($r, $text); $fApp.SetValue($r, 'audit.exe'); $fTitle.SetValue($r, 'audit window') }
    $r
}

$filler = ('lorem ipsum dolor sit amet ' * [math]::Ceiling($TextLength / 27 + 1))
$t0 = [DateTime]::UtcNow.Ticks
foreach ($n in ($SizeList -split ',' | ForEach-Object { [int]$_ })) {
    $path = Join-Path $Work ("history-$n.log")
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $j = $ctor.Invoke(@([string]$path, $cipher, $null))
    $uuids = New-Object 'System.Collections.Generic.List[string]'
    for ($i = 0; $i -lt $n; $i++) {
        $head = "audit synthetic history entry $i "
        $text = ($head + $filler).Substring(0, $TextLength)
        $uuid = [string]$hash.Invoke($null, @($text))
        $uuids.Add($uuid)
        $append.Invoke($j, @((New-Record 'add' $uuid ($t0 + $i) $text)))
    }
    if (-not $drain.Invoke($j, @([TimeSpan]::FromMinutes(10)))) { throw "staging did not drain at N=$n" }
    $dispose.Invoke($j, $null)
    $stageMs = $sw.Elapsed.TotalMilliseconds
    $fileBytes = (Get-Item $path).Length

    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    $heap0 = [GC]::GetTotalMemory($true)
    $order = [Activator]::CreateInstance($tOrder, $true)
    $j2 = $ctor.Invoke(@([string]$path, $cipher, $null))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $skipped = [int]$load.Invoke($j2, @($order))
    $loadMs = $sw.Elapsed.TotalMilliseconds
    $heap1 = [GC]::GetTotalMemory($true)
    $count = [int]$pCount.GetValue($order)

    $entries = $pEntries.GetValue($order)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $missCount = [AuditHistoryLoop]::Count($entries, $match, 'zq-audit-absent')
    $missMs = $sw.Elapsed.TotalMilliseconds
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $hitCount = [AuditHistoryLoop]::Count($entries, $match, 'SYNTHETIC history')
    $hitMs = $sw.Elapsed.TotalMilliseconds
    if ($missCount -ne 0) { throw "a query that matches nothing matched $missCount entries at N=$n" }

    $victim = $uuids[[int]($n / 2)]
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $append.Invoke($j2, @((New-Record 'delete' $victim ($t0 + $n) $null)))
    if (-not $drain.Invoke($j2, @([TimeSpan]::FromMinutes(10)))) { throw "the erase did not drain at N=$n" }
    $eraseMs = $sw.Elapsed.TotalMilliseconds
    $dispose.Invoke($j2, $null)

    $after = [Activator]::CreateInstance($tOrder, $true)
    $j3 = $ctor.Invoke(@([string]$path, $cipher, $null))
    [void]$load.Invoke($j3, @($after))
    $dispose.Invoke($j3, $null)
    $countAfter = [int]$pCount.GetValue($after)
    if ($null -ne $mFind.Invoke($after, @($victim))) { $countAfter = -1 }

    '{0}|{1:F0}|{2:F1}|{3}|{4}|{5}|{6}|{7:F2}|{8:F2}|{9}|{10:F1}|{11}' -f $n, $stageMs, $loadMs, $skipped, $count,
        $fileBytes, ($heap1 - $heap0), $missMs, $hitMs, $hitCount, $eraseMs, $countAfter
    $order = $null; $after = $null; $entries = $null
    Remove-Item -Path $path -Force
}
'@

$rows = @()
$total = [Diagnostics.Stopwatch]::StartNew()
try {
    $probeFile = Join-Path $work 'history-probe.ps1'
    Set-Content -Path $probeFile -Value $probe -Encoding UTF8
    $lines = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $probeFile -Exe $exe -Work $work -SizeList ($Sizes -join ',') -TextLength $TextLength 2>&1
    if ($LASTEXITCODE -ne 0) {
        Add-CheckFinding -Severity fail -Name 'probe-run' -Reason ("the probe failed: " + (($lines | Select-Object -Last 6) -join ' | '))
    }
    foreach ($l in $lines) {
        $f = "$l".Split('|')
        if ($f.Count -ne 12) { continue }
        $rows += [pscustomobject]@{
            Entries = [int]$f[0]; StageMs = [double]$f[1]; LoadMs = [double]$f[2]; Skipped = [int]$f[3]; Loaded = [int]$f[4]
            FileBytes = [long]$f[5]; HeapBytes = [long]$f[6]; SearchMissMs = [double]$f[7]; SearchHitMs = [double]$f[8]
            HitCount = [int]$f[9]; EraseMs = [double]$f[10]; AfterErase = [int]$f[11]
        }
    }
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host ('{0,8} {1,9} {2,9} {3,11} {4,11} {5,10} {6,10} {7,9} {8,7}' -f 'entries', 'load ms', 'us/entry', 'file B/ent', 'heap B/ent', 'miss ms', 'hit ms', 'erase ms', 'stage s')
foreach ($r in $rows) {
    Write-Host ('{0,8} {1,9:F1} {2,9:F1} {3,11:F0} {4,11:F0} {5,10:F2} {6,10:F2} {7,9:F1} {8,7:F1}' -f $r.Entries, $r.LoadMs,
        (1000 * $r.LoadMs / $r.Entries), ($r.FileBytes / $r.Entries), ($r.HeapBytes / $r.Entries), $r.SearchMissMs, $r.SearchHitMs,
        $r.EraseMs, ($r.StageMs / 1000))
    $expected = $r.Entries
    if ($r.Skipped -ne 0 -or $r.Loaded -ne $expected) {
        Add-CheckFinding -Severity fail -Name "replay-$($r.Entries)" -Reason "expected: $expected loaded, 0 skipped | actual: $($r.Loaded) loaded, $($r.Skipped) skipped"
    }
    if ($r.HitCount -ne $expected) {
        Add-CheckFinding -Severity fail -Name "search-$($r.Entries)" -Reason "expected: $expected matches | actual: $($r.HitCount)"
    }
    if ($r.AfterErase -ne $expected - 1) {
        Add-CheckFinding -Severity fail -Name "erase-$($r.Entries)" -Reason "expected: $($expected - 1) entries after the erase, the victim gone | actual: $($r.AfterErase) (-1 = victim still present)"
    }
}
$missing = @($Sizes | Where-Object { $_ -notin $rows.Entries })
if ($missing.Count -gt 0) {
    Add-CheckFinding -Severity fail -Name 'sizes-missing' -Reason "no measurement for N = $($missing -join ', ')"
}
if ($rows.Count -ge 2) {
    $a = $rows[0]; $b = $rows[-1]
    $ratio = ($b.LoadMs / $b.Entries) / [math]::Max(0.001, ($a.LoadMs / $a.Entries))
    Write-Host ("load cost per entry, N={0} against N={1}: x{2:F2} (1 = linear in N)" -f $b.Entries, $a.Entries, $ratio)
}
Write-Host ("total {0:F1} s" -f $total.Elapsed.TotalSeconds)

if ($OutJson) {
    $dir = Split-Path -Parent $OutJson
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    [pscustomobject]@{
        scene = 'SC22'; ticket = 'S0061'; measured = (Get-Date).ToString('s'); machine = $env:COMPUTERNAME; cpu = $cpu
        ramGb = $ramGb; windows = $build; exe = (Get-Item $exe).VersionInfo.FileVersion; textLength = $TextLength; rows = $rows
    } | ConvertTo-Json -Depth 4 | Set-Content -Path $OutJson -Encoding UTF8
}

Complete-Check
