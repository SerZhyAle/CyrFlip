<#
.SYNOPSIS
The open exchange file's import cost (ticket S0061, scene SC23, section 5.5 RS / LK): how long the shipped
reader and the merge planner take on a valid file up to the 25 MB cap, and how much managed memory the
read result holds, measured on the built exe.

.DESCRIPTION
The import of a "CyrFlip Exchange Text v1" file runs its read, parse and plan on the pool behind
BusyDialog (S0030 HT-2), so a large file never holds the hooks' thread by construction; what this scene
leaves open is the size of the wait the dialog covers (B-5: an action over 400 ms shows progress - the
dialog appears after 40 ms) and the memory one import costs. This script measures both on the shipped
code, with no window, no clipboard and no profile:

  * files  - written by the fault stager's ExchangeText (the writer's own framing), into a throwaway
             %TEMP%\CyrFlip-exchangeimport-* folder deleted at the end:
               - "large-<n>MB": whole history entries of 60 000 characters landing exactly on n MB
                 (25 MB is the cap itself - accepted; 25 MB + 1 is refused, ER-3 already holds that row);
               - "many-10000": 10 000 short history entries, the object cap - the per-block cost;
               - "notes-2000": 2 000 notes with ids, 4 KB bodies - the note path of the planner;
  * read   - CyrFlipExchangeReader.ReadFile, timed, in Windows PowerShell 5.1 (the exe is net48);
  * heap   - managed heap after a forced collection, before the read and while the result is held;
  * plan   - CyrFlipExchangeMerge.PlanClipboard (against an empty history) and PlanNotes (against no
             local notes), timed - the preview the dialog shows and the apply re-plans.

The run is a measurement: B-5 is reported per file (over 400 ms = the BusyDialog path covers it, which is
the design), not judged. Exit 0 when every file was read whole (no Fatal, no problem, every block valid)
and planned in full; 1 when a valid file was refused, lost a block, or the planner dropped an entry; 2
when it could not run (no build, Windows PowerShell missing). The last stdout line is the verdict line
(CHECK-VERDICT). The NB axis of SC23 - the hooks' thread during a real import through the dialog - is a
live run with the stall sampler and is not decided here.

Wall time on machine A for the default set is a few seconds plus the file writing (about 60 MB in all).

.EXAMPLE
pwsh -File tools\uitest\Measure-ExchangeImport.ps1
.EXAMPLE
pwsh -Command "& .\tools\uitest\Measure-ExchangeImport.ps1 -SizesMb 1,25 -OutJson temp\S0061\sc23.json"   # an array needs -Command, -File passes '1,25' as one string
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 25)]
    [int[]]$SizesMb = @(1, 5, 25),
    [switch]$SkipShapes,
    [string]$ExePath,
    [string]$OutJson
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Measure-ExchangeImport'

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

Import-Module (Join-Path $PSScriptRoot 'CyrFlip.FaultStager.psm1') -Force
$null = Get-AuditExchangeId 'warm-up'   # compiles the stager's C# types (CyrFlipFault.ExchangeText)

$cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
Write-Host "machine: $env:COMPUTERNAME | $cpu | $ramGb GB | Windows $([Environment]::OSVersion.Version)"
Write-Host "exe:     $exe $((Get-Item $exe).VersionInfo.FileVersion)"

$marker = '# CyrFlip Exchange Text v1'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$work = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-exchangeimport-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$total = [Diagnostics.Stopwatch]::StartNew()

try {
    # name -> expected blocks; the files themselves go to $work.
    $files = [ordered]@{}
    # Whole entries of 60 000 characters, then one shorter entry for the remainder, so the file is at most
    # n MB and every entry stays under the history cap (128 KB of UTF-16, 65 536 characters). The stager's
    # ExchangeText.Oversize lands on the byte but may end in an entry over that cap; it is not used here.
    $chunk = 60000
    $empty = $utf8.GetByteCount([CyrFlipFault.ExchangeText]::Build($marker, $null, $null))
    $probeBlock = New-Object CyrFlipFault.ExchangeBlock
    $probeBlock.Text = 'p' * $chunk
    $one = $utf8.GetByteCount([CyrFlipFault.ExchangeText]::Build($marker, $null, @($probeBlock))) - $empty
    foreach ($mb in ($SizesMb | Sort-Object -Unique)) {
        $target = [long]$mb * 1024 * 1024
        $count = [int][math]::Floor(($target - $empty) / $one)
        $blocks = [Collections.Generic.List[CyrFlipFault.ExchangeBlock]]::new()
        for ($i = 0; $i -lt $count; $i++) {
            $b = New-Object CyrFlipFault.ExchangeBlock
            $b.Text = ('p' * ($chunk - 6)) + $i.ToString('D6')
            $blocks.Add($b)
        }
        $rest = $target - $empty - [long]$count * $one - ($one - $chunk)
        if ($rest -gt 0) {
            $b = New-Object CyrFlipFault.ExchangeBlock
            $b.Text = 'q' * $rest
            $blocks.Add($b)
        }
        $text = [CyrFlipFault.ExchangeText]::Build($marker, $null, $blocks.ToArray())
        $name = "large-$($mb)MB.txt"
        [IO.File]::WriteAllText((Join-Path $work $name), $text, $utf8)
        $files[$name] = ([regex]::Matches($text, '(?m)^## Clipboard$')).Count
        $text = $null
    }
    if (-not $SkipShapes) {
        $text = [CyrFlipFault.ExchangeText]::Build($marker, $null, [CyrFlipFault.ExchangeText]::Numbered(10000))
        [IO.File]::WriteAllText((Join-Path $work 'many-10000.txt'), $text, $utf8)
        $files['many-10000.txt'] = 10000
        $body = ('note line of the audit corpus ' * 140).Substring(0, 4096)
        $notes = [CyrFlipFault.ExchangeBlock[]]@(for ($i = 0; $i -lt 2000; $i++) {
            $b = New-Object CyrFlipFault.ExchangeBlock
            $b.Id = [guid]::NewGuid().ToString()
            $b.Title = "audit note $i"
            $b.Body = $body
            $b
        })
        $text = [CyrFlipFault.ExchangeText]::Build($marker, $notes, $null)
        [IO.File]::WriteAllText((Join-Path $work 'notes-2000.txt'), $text, $utf8)
        $files['notes-2000.txt'] = 2000
        $text = $null; $notes = $null
    }
    Write-Host ("files:   {0} written in {1:F1} s" -f $files.Count, $total.Elapsed.TotalSeconds)

    # Runs in Windows PowerShell 5.1. One line per file:
    # name|bytes|readMs|fatal|problems|notes|clips|heapBefore|heapHeld|planMs|clipActions|noteActions
    $probe = @'
param($Exe, $Work, [string]$NameList)
$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$bf = [Reflection.BindingFlags]'Static,Public,NonPublic'
$tReader = $asm.GetType('CyrFlip.CyrFlipExchangeReader', $true)
$tMerge  = $asm.GetType('CyrFlip.CyrFlipExchangeMerge', $true)
$tReport = $asm.GetType('CyrFlip.ExchangeMergeReport', $true)
$tEntry  = $asm.GetType('CyrFlip.ClipboardHistoryEntry', $true)
$tNote   = $asm.GetType('CyrFlip.QuickNote', $true)
$read = $tReader.GetMethod('ReadFile', $bf)
$planClips = $tMerge.GetMethod('PlanClipboard', $bf)
$planNotes = $tMerge.GetMethod('PlanNotes', $bf)
$findType = [Func[string,object]].GetGenericTypeDefinition().MakeGenericType(@([string], $tEntry))
$find = [Management.Automation.LanguagePrimitives]::ConvertTo({ param($id) $null }, $findType)
$noLocal = [Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType(@($tNote)))
# Warm the JIT on a tiny file so the first measured read is not the compile.
$warm = Join-Path $Work 'warm.txt'
[IO.File]::WriteAllText($warm, "# CyrFlip Exchange Text v1`nExported-At: 2026-10-29T12:00:00Z`n`n# Quick notes`n`n# Clipboard history`n", (New-Object Text.UTF8Encoding($false)))
$null = $read.Invoke($null, @([string]$warm))
foreach ($name in $NameList.Split('|')) {
    $path = Join-Path $Work $name
    [GC]::Collect(); [GC]::WaitForPendingFinalizers(); [GC]::Collect()
    $before = [GC]::GetTotalMemory($true)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = $read.Invoke($null, @([string]$path))
    $readMs = $sw.Elapsed.TotalMilliseconds
    $held = [GC]::GetTotalMemory($true)
    $fatal = if ($null -ne $r.Fatal) { $r.Fatal.ToString() } else { '' }
    $problems = @($r.Problems | ForEach-Object { $_.Kind.ToString() } | Sort-Object -Unique) -join ','
    $report = [Activator]::CreateInstance($tReport, $true)
    $sw.Restart()
    $ca = $planClips.Invoke($null, @($find, $r.Clipboard, $report))
    $na = $planNotes.Invoke($null, @($noLocal, $r.Notes, $false, [DateTime]::UtcNow, $report))
    $planMs = $sw.Elapsed.TotalMilliseconds
    '{0}|{1}|{2:F1}|{3}|{4}|{5}|{6}|{7}|{8}|{9:F1}|{10}|{11}' -f $name, (Get-Item $path).Length, $readMs, $fatal, $problems,
        $r.Notes.Count, $r.Clipboard.Count, $before, $held, $planMs, $ca.Count, $na.Count
    $r = $null; $ca = $null; $na = $null
}
'@
    $probeFile = Join-Path $work 'import-probe.ps1'
    Set-Content -Path $probeFile -Value $probe -Encoding UTF8
    $lines = & $ps51 -NoProfile -ExecutionPolicy Bypass -File $probeFile -Exe $exe -Work $work -NameList (@($files.Keys) -join '|') 2>&1
    if ($LASTEXITCODE -ne 0) {
        Add-CheckFinding -Severity fail -Name 'probe-run' -Reason ("the probe failed: " + (($lines | Select-Object -Last 6) -join ' | '))
        Complete-Check
    }

    $rows = foreach ($l in $lines) {
        $p = "$l".Split('|')
        if ($p.Count -ne 12 -or -not $files.Contains($p[0])) { continue }
        [pscustomobject]@{
            File = $p[0]; Bytes = [long]$p[1]; ReadMs = [double]$p[2]; Fatal = $p[3]; Problems = $p[4]
            Notes = [int]$p[5]; Clips = [int]$p[6]; HeldBytes = [long]$p[8] - [long]$p[7]
            PlanMs = [double]$p[9]; ClipActions = [int]$p[10]; NoteActions = [int]$p[11]
            Expected = [int]$files[$p[0]]
        }
    }

    Write-Host ''
    Write-Host ('{0,-16} {1,12} {2,10} {3,10} {4,8} {5,12} {6,8}' -f 'file', 'bytes', 'read ms', 'plan ms', 'blocks', 'held MB', 'B-5')
    foreach ($r in $rows) {
        $wait = $r.ReadMs + $r.PlanMs
        $b5 = if ($wait -gt 400) { 'dialog' } else { 'quiet' }
        Write-Host ('{0,-16} {1,12:N0} {2,10:F1} {3,10:F1} {4,8} {5,12:F1} {6,8}' -f $r.File, $r.Bytes, $r.ReadMs, $r.PlanMs, ($r.Notes + $r.Clips), ($r.HeldBytes / 1MB), $b5)
        if ($r.Fatal -or $r.Problems) {
            Add-CheckFinding -Severity fail -Name "refused-$($r.File)" -Reason "expected: Accepted, $($r.Expected) blocks | actual: Fatal='$($r.Fatal)' Problems='$($r.Problems)'"
        }
        elseif (($r.Notes + $r.Clips) -ne $r.Expected) {
            Add-CheckFinding -Severity fail -Name "blocks-$($r.File)" -Reason "expected: $($r.Expected) blocks | actual: $($r.Notes + $r.Clips)"
        }
        elseif (($r.ClipActions + $r.NoteActions) -ne $r.Expected) {
            Add-CheckFinding -Severity fail -Name "plan-$($r.File)" -Reason "expected: $($r.Expected) planned additions into an empty profile | actual: $($r.ClipActions + $r.NoteActions)"
        }
    }
    $missing = @($files.Keys | Where-Object { $_ -notin @($rows | ForEach-Object File) })
    if ($missing.Count) {
        Add-CheckFinding -Severity fail -Name 'files-missing' -Reason "no measurement for $($missing -join ', ')"
    }

    $big = $rows | Where-Object { $_.File -like 'large-*' } | Sort-Object Bytes | Select-Object -Last 1
    if ($big) {
        Write-Host ("slope:   {0:F1} ms read per MB, {1:F2} MB held per MB of file ({2})" -f ($big.ReadMs / ($big.Bytes / 1MB)), ($big.HeldBytes / $big.Bytes), $big.File)
    }
    if ($OutJson) {
        $dir = Split-Path -Parent $OutJson
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
        [ordered]@{
            scene = 'SC23'; machine = $env:COMPUTERNAME; cpu = $cpu; ramGb = $ramGb
            exe = (Get-Item $exe).VersionInfo.FileVersion; measuredAt = (Get-Date).ToUniversalTime().ToString('o')
            rows = @($rows)
        } | ConvertTo-Json -Depth 4 | Set-Content -Path $OutJson -Encoding UTF8
        Write-Host "json:    $OutJson"
    }
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("total {0:F1} s" -f $total.Elapsed.TotalSeconds)
}

Complete-Check
