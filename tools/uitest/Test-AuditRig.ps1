<#
.SYNOPSIS
The audit rig's self-test (ticket S0061, acceptance 3): the rig must FAIL CLOSED - find a defect it was
shown, and stay quiet on a control run.

.DESCRIPTION
A check that cannot fail is a finding (S0027 B-slice), and a measurement kit that has never been shown a
stall or a leak has not been shown it can see one. So, against AuditVictim.ps1 - a process of its own
with a window, a WH_KEYBOARD_LL hook and a message loop, i.e. the shape of CyrFlip's UI thread - this
script injects

  * a 400 ms stall on the hook's thread: the UI ping must report it between 350 and 800 ms, and the key
    probe (what another process feels) must report at least 250 ms (Windows gives up on a hook after
    LowLevelHooksTimeout, ~300 ms, so ~300 is the honest ceiling there);
  * a leak of 2 kernel handles + 2 GDI objects + 1 USER object + 10 KB of native memory per iteration:
    the soak verdict must be Growth on all four of Handles, Gdi, User, PrivateBytes;
  * a control run of the same length that leaks nothing: no property may be called Growth.

It also reports the probes' own cost: the cadence error of the sampler thread (LoopJitterMaxMs) and the
baseline latency with nothing wrong, so a later gap can be told from a starved probe.

Unattended apart from one thing: for about twenty seconds a second low-level keyboard hook exists on this
desktop (the victim's), and the key probe injects an unassigned key (VK 0xE8) fifty times a second.
Typing is not disturbed except during the single 400 ms stall.

.EXAMPLE
pwsh -File tools\uitest\Test-AuditRig.ps1
#>
[CmdletBinding()]
param([int]$LeakIterations = 150)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.AuditRig.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Test-AuditRig'

$work = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-auditrig-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$cmd = Join-Path $work 'cmd.txt'
$ready = Join-Path $work 'ready.txt'
$hostExe = (Get-Process -Id $PID).Path
$victim = $null

function Send-VictimCommand([string]$Line, [int]$TimeoutMs = 5000) {
    Set-Content -Path $cmd -Value $Line -Encoding ASCII
    $waited = Wait-AuditCondition -Condition { -not (Test-Path $cmd) } -TimeoutMs $TimeoutMs -PollMs 5
    if ($waited -lt 0) { throw "The victim did not take '$Line' within $TimeoutMs ms." }
}

try {
    $victim = Start-Process -FilePath $hostExe -PassThru -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'AuditVictim.ps1'),
        '-CommandFile', $cmd, '-ReadyFile', $ready)
    if ((Wait-AuditCondition -Condition { Test-Path $ready } -TimeoutMs 20000 -PollMs 50) -lt 0) {
        Add-CheckFinding -Severity notverified -Name 'victim-not-ready' -Reason 'the stand-in process did not come up within 20 s'
        Complete-Check
    }
    Write-Host "victim pid $($victim.Id), probes sample every 10 ms" -ForegroundColor Cyan

    # ---- 1. baseline and the stall -----------------------------------------------------------------
    $ui = Start-AuditProbe -Kind Ui -ProcessId $victim.Id -IntervalMs 10
    $key = Start-AuditProbe -Kind Key -IntervalMs 10
    Start-Sleep -Seconds 3
    $quietUi = $ui.Samples(); $quietMark = $quietUi[0].Length
    Send-VictimCommand 'stall 400' 3000
    Start-Sleep -Seconds 2
    $uiStats = Stop-AuditProbe $ui
    $keyStats = Stop-AuditProbe $key

    Write-Host (Format-AuditStats $uiStats 'ui ping ')
    Write-Host (Format-AuditStats $keyStats 'key probe')

    if ($uiStats.Count -lt 100) { Add-CheckFinding -Severity fail -Name 'ui-probe-starved' -Reason "only $($uiStats.Count) UI pings in 5 s" }
    if ($uiStats.Max -lt 350 -or $uiStats.Max -gt 800) {
        Add-CheckFinding -Severity fail -Name 'stall-not-measured' -Reason ("an injected 400 ms UI-thread stall was reported as {0:F0} ms by the UI ping (expected 350..800)" -f $uiStats.Max)
    }
    if ($keyStats.Max -lt 250) {
        Add-CheckFinding -Severity fail -Name 'stall-not-felt-by-key-probe' -Reason ("an injected 400 ms stall on the hook's thread was seen as {0:F0} ms by the key probe (expected >= 250)" -f $keyStats.Max)
    }
    # the quiet 3 s before the stall: what "nothing wrong" looks like on this machine
    $quiet = @($quietUi[1][0..($quietMark - 1)]) | Sort-Object
    $quietP99 = $quiet[[int][Math]::Ceiling($quiet.Count * 0.99) - 1]
    $quietMax = $quiet[-1]
    Write-Host ("quiet phase  : n={0} p99={1:F1} ms max={2:F1} ms (the probe's own noise floor)" -f $quiet.Count, $quietP99, $quietMax)
    if ($quietMax -gt 150) { Add-CheckFinding -Severity advisory -Name 'noisy-baseline' -Reason ("the quiet phase already shows a {0:F0} ms gap - this machine is too busy for a clean self-test" -f $quietMax) }
    if ($uiStats.LoopJitterMaxMs -gt 60) { Add-CheckFinding -Severity advisory -Name 'probe-jitter' -Reason ("the probe's own cadence slipped by {0:F0} ms" -f $uiStats.LoopJitterMaxMs) }

    # ---- 2. the leak, and the control ---------------------------------------------------------------
    Write-Host "soak: $LeakIterations iterations of 'leak 2 2 1 10' (handles / GDI / USER / KB)" -ForegroundColor Cyan
    $leak = Invoke-AuditSoak -ProcessId $victim.Id -Iterations $LeakIterations -SampleEvery 3 -SettleMs 15 -Action { param($i) Send-VictimCommand 'leak 2 2 1 10' }
    $leak.Growth | Format-Table Property, First, Last, Slope, BandLow, BandHigh, FittedTotal, Verdict -AutoSize | Out-String | Write-Host
    foreach ($prop in 'Handles', 'Gdi', 'User', 'PrivateBytes') {
        $g = $leak.Growth | Where-Object Property -eq $prop
        if (-not $g -or $g.Verdict -ne 'Growth') {
            Add-CheckFinding -Severity fail -Name "leak-missed-$($prop.ToLower())" -Reason "an injected per-iteration leak of $prop was not called Growth (verdict: $(if ($g) { $g.Verdict } else { 'no data' }))"
        }
    }

    Write-Host "control: $LeakIterations iterations of 'noop'" -ForegroundColor Cyan
    $control = Invoke-AuditSoak -ProcessId $victim.Id -Iterations $LeakIterations -SampleEvery 3 -SettleMs 15 -Action { param($i) Send-VictimCommand 'noop' }
    $control.Growth | Format-Table Property, First, Last, Slope, BandLow, BandHigh, FittedTotal, Verdict -AutoSize | Out-String | Write-Host
    foreach ($g in $control.Growth) {
        if ($g.Verdict -eq 'Growth') {
            Add-CheckFinding -Severity fail -Name "false-leak-$($g.Property.ToLower())" -Reason "a run that leaks nothing was called Growth on $($g.Property) (slope $($g.Slope) per sample, fitted total $($g.FittedTotal))"
        }
    }
}
finally {
    try { if ($victim -and -not $victim.HasExited) { Set-Content -Path $cmd -Value 'exit' -Encoding ASCII; [void]$victim.WaitForExit(2000) } } catch { }
    try { if ($victim -and -not $victim.HasExited) { Stop-Process -Id $victim.Id -Force } } catch { }
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}

Complete-Check
