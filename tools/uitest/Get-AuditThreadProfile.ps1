<#
.SYNOPSIS
Which thread of an idle CyrFlip is awake, and how often (S0061 NB "idle citizenship").

.DESCRIPTION
The scene runner reports one number for the whole process (CPU % and context switches per second).
This stages a throwaway profile, lets the app settle, then for -Seconds reads per thread: CPU time
consumed and the `Thread\Context Switches/sec` counter, and prints the threads that did anything, busiest
first. Thread ids map to nothing readable from outside (managed names are not exposed), so the answer is
"thread N woke X times per second and used Y ms of CPU"; combined with -Set bisecting (a module switched
off, the thread count and the rate change) that names the owner.

Changes nothing: the staged profile is thrown away, the user's key / Run value / instance restored.

.EXAMPLE
pwsh -File tools\uitest\Get-AuditThreadProfile.ps1 -Seconds 20
pwsh -File tools\uitest\Get-AuditThreadProfile.ps1 -Set EnableCaretOverlay=0
#>
[CmdletBinding()]
param(
    [int]$WarmSeconds = 10,
    [int]$Seconds = 20,
    [string[]]$Set = @(),
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.AuditRig.psm1') -Force

$values = @{}
foreach ($pair in $Set) {
    $name, $value = $pair -split '=', 2
    $values[$name.Trim()] = if ($value -match '^-?\d+$') { [int]$value } else { $value }
}

$profile = Enter-AuditProfile -Values $values -Configuration $Configuration
try {
    $inst = Start-AuditInstance -Profile $profile
    $layout = Join-Path $profile.DataFolder 'layout.txt'
    if ((Wait-AuditCondition -TimeoutMs 20000 -Condition { Test-Path $layout }) -lt 0) { throw 'the app never published a layout' }
    Start-Sleep -Seconds $WarmSeconds

    $proc = Get-Process -Id $inst.Id
    $cpuBefore = @{}
    foreach ($t in $proc.Threads) { try { $cpuBefore[$t.Id] = $t.TotalProcessorTime.TotalMilliseconds } catch { } }

    $category = New-Object System.Diagnostics.PerformanceCounterCategory 'Thread'
    $counters = @()
    foreach ($instance in $category.GetInstanceNames() | Where-Object { $_ -like 'CyrFlip/*' -or $_ -like 'CyrFlip#*/*' }) {
        $ctx = New-Object System.Diagnostics.PerformanceCounter 'Thread', 'Context Switches/sec', $instance, $true
        $tid = New-Object System.Diagnostics.PerformanceCounter 'Thread', 'ID Thread', $instance, $true
        [void]$ctx.NextValue()
        $counters += [pscustomobject]@{ Ctx = $ctx; Tid = $tid; Sum = 0.0; Samples = 0; Id = 0 }
    }
    for ($s = 0; $s -lt $Seconds; $s++) {
        Start-Sleep -Seconds 1
        foreach ($c in $counters) {
            try { $c.Sum += $c.Ctx.NextValue(); $c.Samples++; $c.Id = [int]$c.Tid.NextValue() } catch { }
        }
    }

    $proc.Refresh()
    $rows = foreach ($t in $proc.Threads) {
        $cpu = 0.0
        try { $cpu = $t.TotalProcessorTime.TotalMilliseconds - $(if ($cpuBefore.ContainsKey($t.Id)) { $cpuBefore[$t.Id] } else { 0 }) } catch { }
        $c = $counters | Where-Object { $_.Id -eq $t.Id } | Select-Object -First 1
        [pscustomobject]@{
            Thread = $t.Id; CpuMs = [Math]::Round($cpu, 1)
            CtxPerSec = if ($c -and $c.Samples) { [Math]::Round($c.Sum / $c.Samples, 1) } else { 0 }
            State = $t.ThreadState; Wait = try { $t.WaitReason } catch { '' }
            Start = '0x{0:X}' -f $t.StartAddress.ToInt64()
        }
    }
    "idle thread profile over $Seconds s after $WarmSeconds s of settling (registry overrides: $($Set -join ', '))"
    "threads: $(@($rows).Count); total CPU {0:N0} ms ({1:N2} % of one core); total ~{2:N0} context switches/s" -f `
        ($rows | Measure-Object CpuMs -Sum).Sum, (($rows | Measure-Object CpuMs -Sum).Sum / ($Seconds * 10)), ($rows | Measure-Object CtxPerSec -Sum).Sum
    $rows | Where-Object { $_.CpuMs -gt 0 -or $_.CtxPerSec -ge 1 } | Sort-Object CtxPerSec -Descending | Format-Table -AutoSize
}
finally { Exit-AuditProfile -Profile $profile }
