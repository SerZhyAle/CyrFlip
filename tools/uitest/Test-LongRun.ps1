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

With -SettingsToggles N (ticket S0007 ST-5) it first runs a settings scene: "/launcher-settings" is sent
N times over the launcher pipe, and every one runs the settings window's whole Reload() - where the
image-list clones and fonts used to leak. After 20 warm-up refreshes GDI and USER counts are taken
before and after; growth past -ToggleGrowthLimit fails. It changes no setting. The scenario launcher
must be on (EnableScenarioLauncher=1): while it is off the pipe passes only /exit. It goes through the
pipe rather than UI Automation because the window's controls reach UIA only as Win32-proxy Panes whose
AutomationId is their HWND, and a control on an unselected page is not in the tree at all.

.EXAMPLE
pwsh -File tools\uitest\Test-LongRun.ps1 -DurationMinutes 60

.EXAMPLE
# A quick smoke test of the harness itself before leaving it running for an hour.
pwsh -File tools\uitest\Test-LongRun.ps1 -DurationMinutes 2 -SampleSeconds 10 -SkipHookCheck

.EXAMPLE
# The settings-refresh scene only.
pwsh -File tools\uitest\Test-LongRun.ps1 -SettingsToggles 500 -DurationMinutes 0 -SkipHookCheck
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
    [int]$HookCheckTimeoutSeconds = 30,
    # The settings-refresh scene (see .DESCRIPTION); 0 = skipped.
    [int]$SettingsToggles = 0,
    [int]$ToggleGrowthLimit = 30
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force

$app = Get-Process CyrFlip -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $app) {
    Set-CheckSubject 'uitest/Test-LongRun no-process'
    Write-Host 'CyrFlip is not running. Start it first - this script watches a live instance, it does not own one.' -ForegroundColor Yellow
    Add-CheckFinding -Severity notverified -Name 'app-not-running' -Reason 'CyrFlip is not running'
    Complete-Check
}

$banner = Get-CyrFlipSubjectBanner -ProcessId $app.Id
Set-CheckSubject "uitest/Test-LongRun $($banner.SubjectAxis)"
Write-Host "Subject: $($banner.BannerText)" -ForegroundColor Cyan

if (-not $CsvPath) {
    $CsvPath = Join-Path $PSScriptRoot ('longrun-' + (Get-Date -Format 'yyyyMMdd-HHmm') + '.csv')
}

Write-Host "CyrFlip long-run watch" -ForegroundColor Cyan
Write-Host "  process     : pid $($app.Id)"
Write-Host "  duration    : $DurationMinutes min, sampling every $SampleSeconds s"
Write-Host "  leak limits : GDI +$GdiGrowthLimit, USER +$UserGrowthLimit"
Write-Host "  csv         : $CsvPath"
Write-Host ''

$failures = New-Object System.Collections.Generic.List[string]

# --- the settings-refresh scene (S0007 ST-5) ------------------------------------------------------
if ($SettingsToggles -gt 0) {
    # Driven over the launcher pipe, not through UI Automation: the settings window's WinForms controls
    # reach UIA only through the Win32 proxy (every one a Pane whose AutomationId is its HWND, the
    # owner-drawn tabs no TabItems), so a control cannot be found by its Name, and one on a page that is
    # not selected is not in the tree at all. "/launcher-settings" lands in ShowSettings, which runs the
    # window's whole Reload() - the path that used to leak image-list clones and fonts.
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $pipeName = 'CyrFlip_Launcher_' + $sid + '_' + (Get-Process -Id $PID).SessionId
    $sendSettings = {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::Out)
        try {
            $client.Connect(2000)
            $writer = New-Object System.IO.StreamWriter($client, (New-Object System.Text.UTF8Encoding($false)))
            $writer.WriteLine('/launcher-settings')
            $writer.Flush()
            $writer.Dispose()
        }
        finally { $client.Dispose() }
    }
    try { & $sendSettings }
    catch { throw "The launcher pipe does not answer ($($_.Exception.Message)) - the scene needs the scenario launcher on (EnableScenarioLauncher=1), since the pipe passes nothing else while it is off." }

    # A warm-up first: the first refreshes fill caches (tab icons, launcher icons) that are meant to stay.
    Write-Host "Settings scene: $SettingsToggles refreshes of the settings window over the launcher pipe" -ForegroundColor Cyan
    for ($i = 0; $i -lt 20; $i++) { & $sendSettings; Start-Sleep -Milliseconds 60 }
    Start-Sleep -Seconds 2
    $before = Get-AppResourceUsage | Where-Object { $_.ProcessId -eq $app.Id } | Select-Object -First 1
    $sendFailures = 0
    for ($i = 0; $i -lt $SettingsToggles; $i++) {
        try { & $sendSettings } catch { $sendFailures++ }
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Seconds 2   # let the last Reload settle
    $after = Get-AppResourceUsage | Where-Object { $_.ProcessId -eq $app.Id } | Select-Object -First 1
    if ($sendFailures -gt 0) { $failures.Add("settings scene: $sendFailures of $SettingsToggles refresh commands could not be sent") }
    if (-not $after) { $failures.Add('the CyrFlip process disappeared during the settings scene') }
    else {
        $gdi = $after.GdiObjects - $before.GdiObjects
        $user = $after.UserObjects - $before.UserObjects
        Write-Host ("  GDI {0} -> {1} ({2:+#;-#;0})   USER {3} -> {4} ({5:+#;-#;0})" -f $before.GdiObjects, $after.GdiObjects, $gdi, $before.UserObjects, $after.UserObjects, $user)
        if ($gdi -gt $ToggleGrowthLimit) { $failures.Add("settings scene: GDI handles grew by $gdi over $SettingsToggles refreshes (limit $ToggleGrowthLimit)") }
        if ($user -gt $ToggleGrowthLimit) { $failures.Add("settings scene: USER handles grew by $user over $SettingsToggles refreshes (limit $ToggleGrowthLimit)") }
    }
    Write-Host ''
}
# A window of our own to switch layouts in. Without it the exercise would have to drive the tray,
# which would keep changing the layout of the user's real windows for the whole run.
$target = $null
if ($DurationMinutes -gt 0) {
    try { $target = Start-TargetWindow -Title 'CyrFlip long-run target' }
    catch { Write-Warning "No target window ($($_.Exception.Message)) - sampling only, without the layout exercise." }
}

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

if ($died) {
    $failures.Add('the CyrFlip process disappeared during the run')
}
elseif ($DurationMinutes -le 0) {
    Write-Host 'No long-run watch (-DurationMinutes 0).'
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
if ($SkipHookCheck) {
    Add-CheckFinding -Severity advisory -Name 'hook-check-skipped' -Reason 'hook liveness check skipped (-SkipHookCheck)'
}
elseif (-not $died) {
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

    if ($seen) {
        Write-Host '  the chord was seen - the keyboard hook is alive.' -ForegroundColor Green
    }
    else {
        Write-Host "  no window reacted to $chord - either the hook is dead, or the chord was never pressed." -ForegroundColor Yellow
        Add-CheckFinding -Severity notverified -Name 'chord-not-seen' -Reason "no window reacted to $chord - either the hook is dead, or the chord was never pressed"
    }
}

foreach ($f in $failures) {
    Add-CheckFinding -Severity fail -Name 'long-run-defect' -Reason $f
}

Write-Host ''
Write-Host "Samples: $CsvPath"
Complete-Check
