<#
.SYNOPSIS
Watches a running CyrFlip for hours and fails on a handle leak - the check behind CLAUDE.md's
long-standing "memory stability over 1+ hour" item.

.DESCRIPTION
A tray app that renders its own icon, its own mouse cursor and a caret overlay lives or dies by its
GDI and USER handle discipline: a leak there is invisible in the memory column and ends with the app
- or the whole desktop session - out of handles. This samples both counts (plus private bytes and
thread count) while driving the very code path that allocates them, and writes every sample to CSV.

What it exercises each cycle:
  * a layout switch of its own throwaway window, which makes CursorIndicator re-render the tray icon,
    the I-beam cursor and the caret marker (the GDI path). Posted to that window, so the layout of
    whatever you are really typing in is never touched;
  * a one-pixel mouse move, which is what makes Windows repaint the replaced I-beam.

What it deliberately does NOT do: synthesize the hotkey chords. CyrFlip ignores injected keystrokes
by design (KeyboardHook checks LLKHF_INJECTED, so its own SendInput cannot re-enter the hook) - a
synthetic Ctrl+Shift+F10 would prove nothing at all. Whether the keyboard hook is still alive after
hours is therefore asked of a human at the end: the script waits for you to press the clipboard
history chord and watches for the window. That is the one check that catches Windows silently
dropping a low-level hook - the failure the hook watchdog exists to prevent.

Verdict:
  * FAIL when GDI or USER handles grew past the threshold (a real leak);
  * FAIL when the process died mid-run;
  * FAIL when the hook check was run and the window never appeared;
  * private bytes are reported, never judged - the clipboard history is unbounded by decision
    (2026-07-26), so growth there is the feature working.

Deliberately outside dotnet test / CI, like every script here: it moves the real mouse and needs the
desktop to itself for as long as it runs.

.EXAMPLE
pwsh -File tools\uitest\Test-LongRun.ps1 -DurationMinutes 60

.EXAMPLE
# A quick smoke test of the harness itself before leaving it running for an hour.
pwsh -File tools\uitest\Test-LongRun.ps1 -DurationMinutes 2 -SampleSeconds 10 -SkipHookCheck
#>
[CmdletBinding()]
param(
    [int]$DurationMinutes = 60,
    [int]$SampleSeconds = 30,
    [string]$CsvPath,
    # Handle growth (over the first settled sample) that counts as a leak rather than as noise.
    [int]$GdiGrowthLimit = 200,
    [int]$UserGrowthLimit = 200,
    # Skip the manual "press the chord" step - for an unattended run.
    [switch]$SkipHookCheck,
    [int]$HookCheckTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force

$app = Get-Process CyrFlip -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $app) { throw 'CyrFlip is not running. Start it first - this script watches a live instance, it does not own one.' }

if (-not $CsvPath) {
    $CsvPath = Join-Path $PSScriptRoot ('longrun-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.csv')
}

Write-Host "CyrFlip long-run watch" -ForegroundColor Cyan
Write-Host "  process     : pid $($app.Id)"
Write-Host "  duration    : $DurationMinutes min, sampling every $SampleSeconds s"
Write-Host "  leak limits : GDI +$GdiGrowthLimit, USER +$UserGrowthLimit"
Write-Host "  csv         : $CsvPath"
Write-Host ''

# A window of our own to switch layouts in. Without it the exercise would have to drive the tray,
# which would keep changing the layout of the user's real windows for the whole run.
$target = $null
try { $target = Start-TargetWindow -Title 'CyrFlip long-run target' }
catch { Write-Warning "No target window ($($_.Exception.Message)) - sampling only, without the layout exercise." }

$samples = New-Object System.Collections.Generic.List[object]
$deadline = (Get-Date).AddMinutes($DurationMinutes)
$died = $false

try {
    while ((Get-Date) -lt $deadline) {
        if ($target) {
            # Exercise first, sample after, so each sample reflects work that has already happened.
            [void](Switch-WindowLayout -Handle $target.Handle)
            $cursor = New-Object CyrFlipUi+POINT
            [void][CyrFlipUi]::GetCursorPos([ref]$cursor)
            [void][CyrFlipUi]::SetCursorPos($cursor.X + 1, $cursor.Y)
            Start-Sleep -Milliseconds 300
            [void][CyrFlipUi]::SetCursorPos($cursor.X, $cursor.Y)
        }

        $sample = Get-AppResourceUsage | Where-Object { $_.ProcessId -eq $app.Id } | Select-Object -First 1
        if (-not $sample) { $died = $true; break }

        $samples.Add($sample)
        $sample | Export-Csv -Path $CsvPath -NoTypeInformation -Append -Encoding UTF8

        $elapsed = [int]((Get-Date) - $samples[0].Time).TotalMinutes
        Write-Host ("  [{0,3} min] private {1,7:N0} KB   GDI {2,5}   USER {3,5}   threads {4,3}" -f `
                $elapsed, ($sample.PrivateBytes / 1KB), $sample.GdiObjects, $sample.UserObjects, $sample.Threads)

        Start-Sleep -Seconds $SampleSeconds
    }
}
finally {
    if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host ''
$failures = New-Object System.Collections.Generic.List[string]

if ($died) {
    $failures.Add('the CyrFlip process disappeared during the run')
}
elseif ($samples.Count -lt 2) {
    $failures.Add("only $($samples.Count) sample(s) collected - the run was too short to say anything")
}
else {
    # Compared against the second sample, not the first: the first is taken while the app is still
    # settling (JIT, first paint of every surface) and would flatter any later growth.
    $baseline = $samples[1]
    $last = $samples[$samples.Count - 1]
    $gdiGrowth = $last.GdiObjects - $baseline.GdiObjects
    $userGrowth = $last.UserObjects - $baseline.UserObjects
    $peakGdi = ($samples | Measure-Object GdiObjects -Maximum).Maximum
    $peakUser = ($samples | Measure-Object UserObjects -Maximum).Maximum

    Write-Host 'Result' -ForegroundColor Cyan
    Write-Host ("  samples      : {0} over {1:N0} min" -f $samples.Count, ($last.Time - $samples[0].Time).TotalMinutes)
    Write-Host ("  GDI objects  : {0} -> {1} (peak {2}, growth {3:+#;-#;0})" -f $baseline.GdiObjects, $last.GdiObjects, $peakGdi, $gdiGrowth)
    Write-Host ("  USER objects : {0} -> {1} (peak {2}, growth {3:+#;-#;0})" -f $baseline.UserObjects, $last.UserObjects, $peakUser, $userGrowth)
    Write-Host ("  private bytes: {0:N0} KB -> {1:N0} KB (reported, not judged)" -f ($baseline.PrivateBytes / 1KB), ($last.PrivateBytes / 1KB))
    Write-Host ("  threads      : {0} -> {1}" -f $baseline.Threads, $last.Threads)

    if ($gdiGrowth -gt $GdiGrowthLimit) { $failures.Add("GDI handles grew by $gdiGrowth (limit $GdiGrowthLimit)") }
    if ($userGrowth -gt $UserGrowthLimit) { $failures.Add("USER handles grew by $userGrowth (limit $UserGrowthLimit)") }
}

# --- the hook liveness check, which only a human can trigger (see the .DESCRIPTION) --------------
if (-not $SkipHookCheck -and -not $died) {
    $chord = (Get-ItemProperty -Path 'HKCU:\Software\CyrFlip' -Name ClipboardHistoryHotkey -ErrorAction SilentlyContinue).ClipboardHistoryHotkey
    if (-not $chord) { $chord = 'Ctrl+Shift+F10' }

    Write-Host ''
    Write-Host "Hook check: press $chord now (you have $HookCheckTimeoutSeconds s)." -ForegroundColor Yellow
    Write-Host '  A synthesized chord would prove nothing - CyrFlip ignores injected keystrokes by design.'

    $before = @(Get-AppWindows | Where-Object { $_.Width -ge 100 -and $_.Height -ge 100 }).Count
    $seen = $false
    $until = (Get-Date).AddSeconds($HookCheckTimeoutSeconds)
    while ((Get-Date) -lt $until) {
        $now = @(Get-AppWindows | Where-Object { $_.Width -ge 100 -and $_.Height -ge 100 }).Count
        if ($now -ne $before) { $seen = $true; break }
        Start-Sleep -Milliseconds 400
    }

    if ($seen) { Write-Host '  the chord was seen - the keyboard hook is alive.' -ForegroundColor Green }
    else { $failures.Add("no window reacted to $chord - either the hook is dead, or the chord was never pressed") }
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'PASS - no handle leak detected.' -ForegroundColor Green
    Write-Host "Samples: $CsvPath"
    exit 0
}

Write-Host 'FAIL' -ForegroundColor Red
foreach ($failure in $failures) { Write-Host "  - $failure" -ForegroundColor Red }
Write-Host "Samples: $CsvPath"
exit 1
