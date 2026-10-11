<#
.SYNOPSIS
The scene runner of the runtime quality audit (ticket S0061): measures a staged, throwaway CyrFlip from
outside, scene by scene, and writes one JSON per scene.

.DESCRIPTION
Every scene runs against a STAGED profile (CyrFlip.AuditRig.psm1): a copy of the build started from
%TEMP% with its own LOCALAPPDATA, a fresh HKCU\Software\CyrFlip, and the user's key, autostart value and
running instance saved first and put back in finally (Restore-AuditProfile is the rescue for a run that
died). The user's data folder, journals and history are never opened.

Scenes (ids are the catalogue's, PLAN/S0061_runtime-quality-audit/scenes.json):
  SC01  cold start to hooks-ready: how long the process is unreachable, when the tray icon and the layout
        signal appear, and how long the machine's keyboard waited on CyrFlip (the key probe).
  SC02  exit and restart x N: graceful exit time, files left behind, a restart that does not come up.
  SC13  layout-switch storm: 100 switches in 10 s per round, resources per round.
  SC17  CapsLock on / off storm.
  SC21  copy storm: synthetic text onto the clipboard with the history on. Borrows the clipboard text.
  SC38  settings window open / close, with the launcher on (the pipe needs it).
  SC44  idle for -IdleMinutes with the defaults.
  SC45  idle with every module on except the cursor replacement (a global side effect the rig will not
        risk on a machine somebody is typing on).

Two probes run through every scene: the UI ping (WM_NULL to the UI thread, 10 ms) and the key probe (how
long a no-op injected key takes to leave SendInput - what ANOTHER process feels of the hooks).

Budgets (spec 5.6) are hypotheses until D2: a crossing is an ADVISORY here, never a failure; a scene that
could not run (the app did not start, died, no tray icon) is a failure.

.EXAMPLE
pwsh -File tools\uitest\Measure-AuditScenes.ps1 -Scene SC01 -Runs 10
pwsh -File tools\uitest\Measure-AuditScenes.ps1 -Scene SC44 -IdleMinutes 10
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('SC01', 'SC02', 'SC13', 'SC17', 'SC21', 'SC38', 'SC44', 'SC45', 'CTRL')][string[]]$Scene,
    [int]$Runs = 10,
    [int]$Rounds = 10,
    [int]$PerRound = 0,
    [int]$IdleMinutes = 10,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$ExePath,
    [string]$OutDir,
    [switch]$AcceptClipboardLoss,
    # Extra registry values for the staged profile, "Name=Value" (DWORD when numeric) - the bisecting
    # lever: run SC01 with EnableCaretOverlay=0 and see whether a gap goes away.
    [string[]]$Set = @(),
    # The weak-environment stand-in (S0061 D6): this many percent busy on every logical core for the
    # scene's duration. 0 = none. Reported in the result.
    [int]$LoadPercent = 0,
    # Idle scenes only: no latency probes. The probes themselves wake the app (a ping and a hook round
    # trip every 50 ms) - run once with and once without to see what is the app's own.
    [switch]$NoProbes
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.AuditRig.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject ('uitest/Measure-AuditScenes ' + ($Scene -join ','))
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts\audit\S0061' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
Enable-UiTestDpi

# Proposed budgets of spec 5.6 (D2 pending): a crossing is reported, not failed.
$Budget = @{ GapMs = 150; StartupTrayMs = 1000; SettingsWarmMs = 300; SettingsColdMs = 800; IdleCpuPercent = 0.5; PrivateMb = 50 }

function Median([double[]]$v) { if ($v.Count -eq 0) { return $null }; $s = $v | Sort-Object; $s[[int][Math]::Floor(($s.Count - 1) / 2)] }
function Round1($x) { if ($null -eq $x) { $null } else { [Math]::Round([double]$x, 1) } }

function Get-StatsObject($Stats) {
    [ordered]@{
        n = $Stats.Count; medianMs = Round1 $Stats.Median; p95Ms = Round1 $Stats.P95; p99Ms = Round1 $Stats.P99; maxMs = Round1 $Stats.Max
        over100 = $Stats.Over100; over250 = $Stats.Over250; over300 = $Stats.Over300; loopJitterMaxMs = Round1 $Stats.LoopJitterMaxMs
        worstAtMs = @($Stats.WorstAtMs | Select-Object -First 5 | ForEach-Object { Round1 $_ })
        worstMs = @($Stats.WorstMs | Select-Object -First 5 | ForEach-Object { Round1 $_ })
    }
}

function Save-SceneResult([string]$Id, $Machine, $Body) {
    $result = [ordered]@{ scene = $Id; machine = $Machine; loadPercent = $LoadPercent; registryOverrides = @($Set); result = $Body }
    $path = Join-Path $OutDir "$Id-$stamp.json"
    $result | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
    Write-Host "  -> $path" -ForegroundColor DarkGray
}

function Wait-InstanceReady($Profile, [int]$TimeoutMs) {
    # "Ready" = the indicator published its first layout (layout.txt) - the message loop is running and
    # the launcher pipe exists. NOT the tray icon: the notification area keeps a ghost entry for a process
    # that just exited, so an MSAA look-up reports an icon ~20 ms after a launch (the first SC02 run sent
    # /exit into a pipe that did not exist yet and read it as "exit does not work").
    $layout = Join-Path $Profile.DataFolder 'layout.txt'
    Wait-AuditCondition -TimeoutMs $TimeoutMs -PollMs 30 -Condition { Test-Path $layout }
}

function Add-BudgetAdvisory([string]$Name, [string]$Text) { Add-CheckFinding -Severity advisory -Name $Name -Reason $Text }

# A probe whose OWN cadence slipped by this much was starved by the machine, not by the app: the gaps it
# reports in that scene are not the app's. (The first idle run was contaminated by a build running beside
# it - loop jitter 974 ms beside a "max 897 ms" gap - which is exactly what this rule exists to say.)
$JitterInvalidMs = 100
function Test-ProbeValid([string]$Scene, $Stats, [string]$Which) {
    if ($Stats -and $Stats.LoopJitterMaxMs -gt $JitterInvalidMs) {
        Add-CheckFinding -Severity advisory -Name 'contaminated' -Reason ("{0} : the {1} probe's own cadence slipped by {2:F0} ms - the machine was busy with something else; this scene's gaps are NOT the app's, re-run it on a quiet machine" -f $Scene, $Which, $Stats.LoopJitterMaxMs)
        return $false
    }
    $true
}

# ---------------------------------------------------------------------------------------------------
$exeForBanner = if ($ExePath) { $ExePath } else { Get-CyrFlipExe -Configuration $Configuration }
$machine = Get-AuditMachine -ExePath $exeForBanner
Write-Host "Machine: $($machine.Cpu), $($machine.Logical) threads, $($machine.RamGb) GB, $($machine.Windows), monitors $($machine.Monitors -join ' ')" -ForegroundColor Cyan
if ($machine.Build) { Write-Host "Build  : $($machine.Build.ProductVersion)" -ForegroundColor Cyan }

foreach ($id in $Scene) {
    Write-Host ''
    Write-Host "=== $id ===" -ForegroundColor Cyan
    $values = @{}
    $stageScenario = $false
    switch ($id) {
        'SC21' { $values = @{ EnableClipboardHistory = 1; ShowClipboardHistoryOnStartup = 0 } }
        'SC38' { $values = @{ EnableScenarioLauncher = 1; LauncherFirstEnableDone = 1 } }
        'SC45' {
            $values = @{
                EnableClipboardHistory = 1; ShowClipboardHistoryOnStartup = 0; EnableScenarioLauncher = 1; LauncherFirstEnableDone = 1
                EnableTranslate = 1; TranslateSeeded = 1; EnableContextMenu = 1; EnableQuickNotes = 1; QuickNotesNoticeShown = 1
                EnableSymbols = 1; ScreenshotSaveEnabled = 0
            }
        }
    }

    foreach ($pair in $Set) {
        $name, $value = $pair -split '=', 2
        $values[$name.Trim()] = if ($value -match '^-?\d+$') { [int]$value } else { $value }
    }
    $profile = $null
    $clipboardBackup = $null
    $load = $null
    try {
        if ($id -eq 'SC21') {
            $text = Get-Clipboard -Raw -ErrorAction SilentlyContinue
            $clipboardBackup = [pscustomobject]@{ Text = $text }
            if (-not $AcceptClipboardLoss) {
                # Text is restored; a picture or a file list cannot be handed back by this script.
                if (-not ('CyrFlipAuditScene.ClipFmt' -as [type])) {
                    Add-Type -Namespace CyrFlipAuditScene -Name ClipFmt -MemberDefinition '[DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);'
                }
                $nonText = @(2, 8, 15, 17, 6 | Where-Object { [CyrFlipAuditScene.ClipFmt]::IsClipboardFormatAvailable([uint32]$_) })
                if ($nonText.Count -gt 0) {
                    Add-CheckFinding -Severity notverified -Name 'clipboard-not-text' -Reason 'the clipboard holds a picture or files that this scene cannot hand back; copy some plain text first or pass -AcceptClipboardLoss'
                    continue
                }
            }
        }

        $profile = Enter-AuditProfile -Values $values -Configuration $Configuration -ExePath $ExePath
        Write-Host "profile: $($profile.Root)" -ForegroundColor DarkGray
        if ($LoadPercent -gt 0) {
            $load = Start-AuditLoad -Percent $LoadPercent
            Write-Host "synthetic CPU load: $LoadPercent % on every logical core" -ForegroundColor Yellow
        }

        switch ($id) {

            'SC01' {
                $runsOut = New-Object System.Collections.Generic.List[object]
                for ($r = 1; $r -le $Runs; $r++) {
                    $key = Start-AuditProbe -Kind Key -IntervalMs 10
                    $inst = Start-AuditInstance -Profile $profile
                    $ui = Start-AuditProbe -Kind Ui -ProcessId $inst.Id -IntervalMs 10
                    $launch = $inst.LaunchedUtc
                    $layoutFile = Join-Path $profile.DataFolder 'layout.txt'
                    $trayMs = -1; $layoutMs = -1
                    $until = (Get-Date).AddSeconds(20)
                    while ((Get-Date) -lt $until -and ($trayMs -lt 0 -or $layoutMs -lt 0)) {
                        if ($inst.Process.HasExited) { break }
                        if ($layoutMs -lt 0 -and (Test-Path $layoutFile)) { $layoutMs = ([DateTime]::UtcNow - $launch).TotalMilliseconds }
                        if ($trayMs -lt 0 -and @(Get-TrayIcons | Where-Object { $_.Name -like '*CyrFlip*' }).Count -gt 0) { $trayMs = ([DateTime]::UtcNow - $launch).TotalMilliseconds }
                        Start-Sleep -Milliseconds 25
                    }
                    Start-Sleep -Seconds 3   # watch what the first seconds of normal life look like too
                    $alive = -not $inst.Process.HasExited
                    $uiStats = Stop-AuditProbe $ui
                    $keyStats = Stop-AuditProbe $key
                    $first = $ui.Samples()
                    $firstAnswer = if ($first[0].Length -gt 0) { ($ui.OffsetMs($launch) * -1) + $first[0][0] + $first[1][0] } else { -1 }
                    $keyOffset = $key.OffsetMs($launch) * -1
                    $keyStalls = @()
                    $ks = $key.Samples()
                    for ($i = 0; $i -lt $ks[0].Length; $i++) { if ($ks[1][$i] -gt 50) { $keyStalls += [pscustomobject]@{ atMs = Round1 ($ks[0][$i] + $keyOffset); ms = Round1 $ks[1][$i] } } }
                    $exitMs = Stop-AuditInstance -Profile $profile
                    $row = [ordered]@{
                        run = $r; cold = ($r -eq 1); alive = $alive
                        firstWindowMs = Round1 (($uiStats.FirstWindowAtMs) + ($ui.OffsetMs($launch) * -1))
                        firstAnswerMs = Round1 $firstAnswer; trayMs = Round1 $trayMs; layoutFileMs = Round1 $layoutMs
                        uiMaxMs = Round1 $uiStats.Max; uiOver100 = $uiStats.Over100; uiOver250 = $uiStats.Over250
                        # when and for how long, relative to the launch: the second gap is the one to attribute
                        uiGaps = @(for ($g = 0; $g -lt $uiStats.WorstMs.Count; $g++) {
                                if ($uiStats.WorstMs[$g] -gt 100) { [pscustomobject]@{ atMs = Round1 ($uiStats.WorstAtMs[$g] + ($ui.OffsetMs($launch) * -1)); ms = Round1 $uiStats.WorstMs[$g] } } })
                        keyMaxMs = Round1 $keyStats.Max; keyStallsOver50 = @($keyStalls).Count
                        keyStallTotalMs = Round1 (($keyStalls | Measure-Object ms -Sum).Sum)
                        keyStalls = @($keyStalls | Select-Object -First 6)
                        exitMs = $exitMs
                    }
                    $runsOut.Add($row)
                    Write-Host ("  run {0,2}{1}: first answer {2,6} ms, tray {3,6} ms, layout.txt {4,6} ms; ui max {5,6} ms, key max {6,6} ms (stalls>50: {7})" -f `
                            $r, $(if ($r -eq 1) { ' cold' } else { '     ' }), $row.firstAnswerMs, $row.trayMs, $row.layoutFileMs, $row.uiMaxMs, $row.keyMaxMs, $row.keyStallsOver50)
                    if (-not $alive -or $trayMs -lt 0) {
                        Add-CheckFinding -Severity fail -Name 'startup-failed' -Reason "run ${r}: the app $(if (-not $alive) { 'exited' } else { 'never showed its tray icon' }) within 20 s"
                    }
                }
                $warm = @($runsOut | Where-Object { -not $_.cold -and $_.trayMs -ge 0 })
                $summary = [ordered]@{
                    warmRuns = $warm.Count
                    medianFirstAnswerMs = Median ([double[]]@($warm | ForEach-Object { $_.firstAnswerMs }))
                    medianTrayMs = Median ([double[]]@($warm | ForEach-Object { $_.trayMs }))
                    medianLayoutFileMs = Median ([double[]]@($warm | ForEach-Object { $_.layoutFileMs }))
                    worstKeyStallMs = ($runsOut | Measure-Object keyMaxMs -Maximum).Maximum
                    coldKeyStallMs = $runsOut[0].keyMaxMs
                }
                Write-Host ("  warm medians: first answer {0} ms, tray {1} ms, layout.txt {2} ms; worst key stall {3} ms (cold run {4} ms)" -f `
                        $summary.medianFirstAnswerMs, $summary.medianTrayMs, $summary.medianLayoutFileMs, $summary.worstKeyStallMs, $summary.coldKeyStallMs)
                # trayMs is recorded but NOT judged: the notification area keeps a ghost entry for an app that
                # just exited, so an MSAA look-up finds an icon within ~20 ms of a launch that has not yet
                # shown one. The first answered ping (message loop running) is the usable-time measure.
                if ($summary.medianFirstAnswerMs -gt $Budget.StartupTrayMs) { Add-BudgetAdvisory 'B-2' "the UI thread first answers a message $($summary.medianFirstAnswerMs) ms after launch (proposed budget $($Budget.StartupTrayMs) ms)" }
                if ($summary.worstKeyStallMs -gt $Budget.GapMs) { Add-BudgetAdvisory 'B-1' "startup left the keyboard waiting $($summary.worstKeyStallMs) ms (proposed budget $($Budget.GapMs) ms)" }
                $body = [ordered]@{ summary = $summary; runs = $runsOut.ToArray() }
                Save-SceneResult -Id $id -Machine $machine -Body $body
            }

            'SC02' {
                $cycles = New-Object System.Collections.Generic.List[object]
                for ($r = 1; $r -le $Runs; $r++) {
                    $inst = Start-AuditInstance -Profile $profile
                    $tray = Wait-InstanceReady $profile 20000
                    Start-Sleep -Milliseconds 300   # the pipe's listener thread is a beat behind the first layout
                    $signal = Join-Path $profile.DataFolder 'layout.txt'
                    $hadSignal = Test-Path $signal
                    $exitMs = Stop-AuditInstance -Profile $profile
                    Start-Sleep -Milliseconds 200
                    $left = @(Get-ChildItem $profile.DataFolder -Filter '*.tmp' -ErrorAction SilentlyContinue)
                    $row = [ordered]@{
                        run = $r; trayMs = $tray; exitMs = $exitMs; signalRetracted = ($hadSignal -and -not (Test-Path $signal))
                        tmpLeft = $left.Count; processGone = -not (Get-Process -Id $inst.Id -ErrorAction SilentlyContinue)
                    }
                    $cycles.Add($row)
                    if ($tray -lt 0) { Add-CheckFinding -Severity fail -Name 'restart-failed' -Reason "cycle ${r}: no tray icon after 20 s" }
                    if ($exitMs -lt 0) { Add-CheckFinding -Severity fail -Name 'exit-not-graceful' -Reason "cycle ${r}: /exit did not end the app within 4 s (killed)" }
                    if ($hadSignal -and -not $row.signalRetracted) { Add-CheckFinding -Severity fail -Name 'signal-not-retracted' -Reason "cycle ${r}: layout.txt survived a graceful exit" }
                }
                $ok = @($cycles | Where-Object { $_.exitMs -ge 0 })
                Write-Host ("  {0} cycles; graceful {1}; median exit {2} ms, worst {3} ms; tmp files left: {4}" -f `
                        $cycles.Count, $ok.Count, (Median ([double[]]@($ok | ForEach-Object { $_.exitMs }))), (($ok | Measure-Object exitMs -Maximum).Maximum), (($cycles | Measure-Object tmpLeft -Sum).Sum))
                $body = [ordered]@{ cycles = $cycles.ToArray() }
                Save-SceneResult -Id $id -Machine $machine -Body $body
            }

            { $_ -in 'SC13', 'SC17', 'SC21', 'SC38' } {
                $inst = Start-AuditInstance -Profile $profile
                if ((Wait-InstanceReady $profile 20000) -lt 0) { Add-CheckFinding -Severity fail -Name 'startup-failed' -Reason "$id : the indicator never published a layout within 20 s"; continue }
                Start-Sleep -Seconds 2
                $procId = $inst.Id
                $ui = Start-AuditProbe -Kind Ui -ProcessId $procId -IntervalMs 10
                $key = Start-AuditProbe -Kind Key -IntervalMs 10
                $samples = New-Object System.Collections.Generic.List[object]
                $extra = [ordered]@{}
                $target = $null
                $capsWas = $null
                $latencies = New-Object System.Collections.Generic.List[double]
                try {
                    switch ($id) {
                        'SC13' { $target = Start-TargetWindow -Title 'CyrFlip audit target'; $per = if ($PerRound) { $PerRound } else { 100 } }
                        'SC17' { $capsWas = Get-CapsLockState; $per = if ($PerRound) { $PerRound } else { 100 } }
                        'SC21' { $per = if ($PerRound) { $PerRound } else { 200 } }
                        'SC38' { $per = if ($PerRound) { $PerRound } else { 10 } }
                    }
                    for ($round = 1; $round -le $Rounds; $round++) {
                        $roundWatch = [Diagnostics.Stopwatch]::StartNew()
                        for ($i = 1; $i -le $per; $i++) {
                            switch ($id) {
                                'SC13' { [void](Switch-WindowLayout -Handle $target.Handle); Start-Sleep -Milliseconds 100 }
                                'SC17' { [void](Set-CapsLockState -On (-not (Get-CapsLockState))); Start-Sleep -Milliseconds 60 }
                                'SC21' {
                                    Set-Clipboard -Value ("audit-synthetic-{0}-{1}-{2}" -f $round, $i, ('x' * (20 + ($i % 50))))
                                    Start-Sleep -Milliseconds 45
                                }
                                'SC38' {
                                    $sw = [Diagnostics.Stopwatch]::StartNew()
                                    if (-not (Send-AuditPipeCommand -Line '/launcher-settings')) { throw 'the launcher pipe does not answer' }
                                    $w = Wait-AuditCondition -TimeoutMs 8000 -PollMs 10 -Condition { @(Get-AppWindows | Where-Object { $_.ProcessId -eq $procId -and $_.Width -ge 300 }).Count -gt 0 }
                                    if ($w -lt 0) { throw 'the settings window did not appear within 8 s' }
                                    $latencies.Add($sw.Elapsed.TotalMilliseconds)
                                    Start-Sleep -Milliseconds 150
                                    $win = Get-AppWindows | Where-Object { $_.ProcessId -eq $procId -and $_.Width -ge 300 } | Select-Object -First 1
                                    if ($win) { [void][CyrFlipUi]::PostMessageW($win.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }   # WM_CLOSE: close means hide
                                    [void](Wait-AuditCondition -TimeoutMs 3000 -PollMs 10 -Condition { @(Get-AppWindows | Where-Object { $_.ProcessId -eq $procId -and $_.Width -ge 300 }).Count -eq 0 })
                                }
                            }
                        }
                        Start-Sleep -Milliseconds 600
                        $s = Get-AuditResourceSample -ProcessId $procId
                        if (-not $s) { throw "the process $procId disappeared in round $round" }
                        $s | Add-Member -NotePropertyName Round -NotePropertyValue $round
                        $samples.Add($s)
                        Write-Host ("  round {0,2}: {1,5:N1} s  GDI {2,4}  USER {3,4}  handles {4,4}  threads {5,3}  private {6,6:N1} MB  heap {7,6:N1} MB" -f `
                                $round, $roundWatch.Elapsed.TotalSeconds, $s.Gdi, $s.User, $s.Handles, $s.Threads, ($s.PrivateBytes / 1MB), ($s.ManagedHeap / 1MB))
                    }
                    if ($id -eq 'SC21') {
                        $journal = Join-Path $profile.DataFolder 'clipboard-history.log'
                        if (Test-Path $journal) {
                            $extra.journalBytes = (Get-Item $journal).Length
                            $extra.journalLines = @([IO.File]::ReadLines($journal) | Where-Object { $_ }).Count
                        }
                        $extra.copiesSent = $Rounds * $per
                    }
                    if ($id -eq 'SC38' -and $latencies.Count -gt 0) {
                        $lat = [double[]]$latencies.ToArray()
                        $extra.openFirstMs = Round1 $lat[0]
                        $extra.openWarmMedianMs = Round1 (Median ([double[]]@($lat | Select-Object -Skip 1)))
                        $extra.openWorstMs = Round1 (($lat | Measure-Object -Maximum).Maximum)
                        Write-Host ("  settings visible after: first {0} ms, warm median {1} ms, worst {2} ms" -f $extra.openFirstMs, $extra.openWarmMedianMs, $extra.openWorstMs)
                        if ($extra.openWarmMedianMs -gt $Budget.SettingsWarmMs) { Add-BudgetAdvisory 'B-3' "settings warm open median $($extra.openWarmMedianMs) ms (proposed budget $($Budget.SettingsWarmMs) ms)" }
                    }
                }
                finally {
                    if ($id -eq 'SC17' -and $null -ne $capsWas) { [void](Set-CapsLockState -On $capsWas) }
                    if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
                }
                $uiStats = Stop-AuditProbe $ui (Join-Path $OutDir "$id-$stamp-ui.csv")
                $keyStats = Stop-AuditProbe $key (Join-Path $OutDir "$id-$stamp-key.csv")
                $growth = @(Test-AuditGrowth -Samples $samples -WarmupShare 0.2)
                Write-Host (Format-AuditStats $uiStats '  ui ping  ')
                Write-Host (Format-AuditStats $keyStats '  key probe')
                $growth | Format-Table Property, First, Last, Slope, BandLow, BandHigh, FittedTotal, Verdict -AutoSize | Out-String | Write-Host
                foreach ($g in $growth | Where-Object { $_.Verdict -eq 'Growth' }) {
                    Add-CheckFinding -Severity advisory -Name "growth-$($g.Property.ToLower())" -Reason "$id : $($g.Property) grows $($g.Slope) per round (band $($g.BandLow)..$($g.BandHigh)), fitted total $($g.FittedTotal) over $($g.N) rounds"
                }
                $valid = (Test-ProbeValid $id $uiStats 'UI') -and (Test-ProbeValid $id $keyStats 'key')
                if ($valid) {
                    if ($uiStats.Max -gt $Budget.GapMs) { Add-BudgetAdvisory 'B-1' "$id : UI thread unreachable for $([int]$uiStats.Max) ms at worst (proposed budget $($Budget.GapMs) ms)" }
                    if ($keyStats.Max -gt $Budget.GapMs) { Add-BudgetAdvisory 'B-1' "$id : another process's keystroke waited $([int]$keyStats.Max) ms at worst (proposed budget $($Budget.GapMs) ms)" }
                }
                Save-SceneResult $id $machine ([ordered]@{
                        rounds = $Rounds; perRound = $per; valid = $valid; ui = Get-StatsObject $uiStats; key = Get-StatsObject $keyStats
                        samples = @($samples | ForEach-Object { [ordered]@{ round = $_.Round; gdi = $_.Gdi; user = $_.User; handles = $_.Handles; threads = $_.Threads; privateMb = [Math]::Round($_.PrivateBytes / 1MB, 2); heapMb = if ($_.ManagedHeap -ge 0) { [Math]::Round($_.ManagedHeap / 1MB, 2) } else { $null } } })
                        growth = @($growth); extra = $extra
                    })
            }

            'CTRL' {
                # The control: the key probe alone, with NO CyrFlip running (the user's own instance was
                # stopped by the staging; the staged copy is not started). Whatever stalls the keyboard
                # path here is not CyrFlip's - it is every other hook owner on the machine, the system
                # and the antivirus. A key-probe gap in any other scene means nothing until it is read
                # against this.
                $key = Start-AuditProbe -Kind Key -IntervalMs 50
                Start-Sleep -Seconds ($IdleMinutes * 60)
                $keyStats = Stop-AuditProbe $key (Join-Path $OutDir "CTRL-$stamp-key.csv")
                Write-Host (Format-AuditStats $keyStats '  key probe (no CyrFlip)')
                Save-SceneResult $id $machine ([ordered]@{ minutes = $IdleMinutes; key = Get-StatsObject $keyStats; valid = ($keyStats.LoopJitterMaxMs -le $JitterInvalidMs) })
            }

            { $_ -in 'SC44', 'SC45' } {
                $inst = Start-AuditInstance -Profile $profile
                if ((Wait-InstanceReady $profile 20000) -lt 0) { Add-CheckFinding -Severity fail -Name 'startup-failed' -Reason "$id : the indicator never published a layout within 20 s"; continue }
                Start-Sleep -Seconds 5   # let the start-up work finish; idle is what is measured
                $procId = $inst.Id
                $ui = $null; $key = $null
                if (-not $NoProbes) {
                    $ui = Start-AuditProbe -Kind Ui -ProcessId $procId -IntervalMs 50
                    $key = Start-AuditProbe -Kind Key -IntervalMs 50
                }

                $counters = @()
                try {
                    $cat = New-Object System.Diagnostics.PerformanceCounterCategory 'Thread'
                    foreach ($instName in $cat.GetInstanceNames() | Where-Object { $_ -like 'CyrFlip/*' -or $_ -like 'CyrFlip#*/*' }) {
                        $c = New-Object System.Diagnostics.PerformanceCounter 'Thread', 'Context Switches/sec', $instName, $true
                        [void]$c.NextValue(); $counters += $c
                    }
                }
                catch { Write-Warning "context-switch counters unavailable: $($_.Exception.Message)" }

                $first = Get-AuditResourceSample -ProcessId $procId
                $series = New-Object System.Collections.Generic.List[object]
                $switches = New-Object System.Collections.Generic.List[double]
                $end = (Get-Date).AddMinutes($IdleMinutes)
                $last = $first
                while ((Get-Date) -lt $end) {
                    Start-Sleep -Seconds 5
                    $s = Get-AuditResourceSample -ProcessId $procId
                    if (-not $s) { throw "the process $procId disappeared during the idle watch" }
                    $series.Add($s)
                    $cs = 0.0
                    foreach ($c in $counters) { try { $cs += $c.NextValue() } catch { } }
                    $switches.Add($cs)
                    $last = $s
                }
                $uiStats = if ($ui) { Stop-AuditProbe $ui } else { $null }
                $keyStats = if ($key) { Stop-AuditProbe $key } else { $null }
                $cpuPercent = ($last.CpuSeconds - $first.CpuSeconds) / ([Math]::Max(1, ($last.Time - $first.Time).TotalSeconds)) * 100
                $growth = @(Test-AuditGrowth -Samples $series -WarmupShare 0.2)
                $body = [ordered]@{
                    minutes = $IdleMinutes; cpuPercentOfOneCore = [Math]::Round($cpuPercent, 3)
                    contextSwitchesPerSecMedian = Round1 (Median ([double[]]$switches.ToArray()))
                    privateMb = [Math]::Round($last.PrivateBytes / 1MB, 1); workingSetMb = [Math]::Round($last.WorkingSet / 1MB, 1)
                    heapMb = if ($last.ManagedHeap -ge 0) { [Math]::Round($last.ManagedHeap / 1MB, 2) } else { $null }
                    gdi = $last.Gdi; user = $last.User; handles = $last.Handles; threads = $last.Threads
                    probes = (-not $NoProbes)
                    ui = if ($uiStats) { Get-StatsObject $uiStats } else { $null }
                    key = if ($keyStats) { Get-StatsObject $keyStats } else { $null }
                    growth = @($growth)
                }
                Write-Host ("  idle {0} min: CPU {1:N3} % of one core, ~{2} context switches/s, private {3} MB (heap {4} MB), GDI {5}, USER {6}, handles {7}, threads {8}" -f `
                        $IdleMinutes, $body.cpuPercentOfOneCore, $body.contextSwitchesPerSecMedian, $body.privateMb, $body.heapMb, $body.gdi, $body.user, $body.handles, $body.threads)
                if ($uiStats) { Write-Host (Format-AuditStats $uiStats '  ui ping  ') }
                if ($keyStats) { Write-Host (Format-AuditStats $keyStats '  key probe') }
                $valid = (Test-ProbeValid $id $uiStats 'UI') -and (Test-ProbeValid $id $keyStats 'key')
                $body.valid = $valid
                if ($cpuPercent -gt $Budget.IdleCpuPercent) { Add-BudgetAdvisory 'B-6' "$id : idle CPU $([Math]::Round($cpuPercent, 2)) % of one core (proposed budget $($Budget.IdleCpuPercent) %)" }
                if ($body.privateMb -gt $Budget.PrivateMb) { Add-BudgetAdvisory 'B-6' "$id : idle private bytes $($body.privateMb) MB (budget $($Budget.PrivateMb) MB, CLAUDE.md)" }
                foreach ($g in $growth | Where-Object { $_.Verdict -eq 'Growth' }) {
                    Add-CheckFinding -Severity advisory -Name "growth-$($g.Property.ToLower())" -Reason "$id : $($g.Property) grows while idle: slope $($g.Slope) per 5 s (band $($g.BandLow)..$($g.BandHigh)), fitted total $($g.FittedTotal)"
                }
                Save-SceneResult $id $machine $body
            }
        }
    }
    catch {
        Add-CheckFinding -Severity fail -Name "$($id.ToLower())-aborted" -Reason ("{0} [{1}] (script line {2}; {3})" -f $_.Exception.Message, $_.Exception.GetType().Name, $_.InvocationInfo.ScriptLineNumber, ($_.ScriptStackTrace -replace '\r?\n', ' <- '))
    }
    finally {
        Stop-AuditLoad $load
        if ($clipboardBackup -and $null -ne $clipboardBackup.Text) { try { Set-Clipboard -Value $clipboardBackup.Text } catch { } }
        Exit-AuditProfile -Profile $profile
    }
}

Write-Host ''
Complete-Check
