<#
.SYNOPSIS
Operator-typed measurement of how long every CyrFlip window takes to open, and how long the settings window takes to answer a change (ticket S0135, phase 0).
.DESCRIPTION
Runs WindowOpenTimingTests.MeasureOpen in -Runs fresh processes for each language and theme asked for. Each process builds every window the product opens
(ThemeCoverageTests.Windows) twice - the first construction in the process (cold) and a second one (warm) - with the theme and the size profile initialised
as the app does, shows it off-screen, and lets OpenTrace record the span from the start of its construction to the first frame Windows has painted after Show
(RedrawWindow RDW_UPDATENOW|RDW_ALLCHILDREN, as Measure-PageNavigation.ps1 does), plus the theme and size-profile attach spans, the settings window's page,
construction and reload spans, and the time from toggling a check box in the settings window to the painted frame. The raw rows of every process are kept; the budgets are
judged here, per window and language, on ALL samples with the nearest-rank p95 and the maximum (SETTINGS-NAVIGATION 2.0 section 5 rule 4: at least 20 samples, no outlier dropped).

Budgets (S0135 quiz decisions of 2026-11-18): the settings window opens (cold) in p95 under 500 ms - 1000 ms in hi, bn, ar, ur and zh -, a change in it paints in p95 under
100 ms, every other window opens warm in p95 under 500 ms (the dialog budget of WINDOWS-UI); a window over 100 ms warm is reported as an advisory.

It shows real windows far off-screen for the length of each run, so it needs an interactive desktop, but it never touches the pointer and never takes the focus. Run it on a quiet
machine: another job running at the same time makes every number noisier (the first S0135 pass was measured that way and says so).
Build the test project first, or let the script do it (-NoBuild skips that). One language and theme takes about -Runs x 25 seconds.
.EXAMPLE
./Measure-WindowOpen.ps1 -Runs 22 -Languages en,ru,hi,ar -Themes light,dark
.NOTES
The compositor's present of the finished frame (at most one refresh interval) is outside the measurement. The report names the configuration it ran on; run it again on the
minimum-supported machine to name that one. The result is written as baseline-<date>.md in -OutputDirectory; copy it under PLAN/S0135_ui-window-speed-optimization/ to commit it.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 200)][int]$Runs = 22,
    [string[]]$Languages = @('en', 'ru'),
    [string[]]$Themes = @('light'),
    [ValidateRange(1, 200)][int]$Changes = 20,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/s0135/window-open'),
    [double]$OpenBudgetMs = 500,
    [double]$ComplexScriptOpenBudgetMs = 1000,
    [double]$ChangeBudgetMs = 100,
    [double]$WindowBudgetMs = 500,
    [double]$AdvisoryMs = 100,
    [string]$Project = '',   # a test project to run instead of tests/CyrFlip.Tests (a side build, when that one does not compile)
    [ValidateRange(0, 10000)][int]$FirstRun = 0,   # the run index of this call's first process, so one measurement can be taken in several calls
    [switch]$KeepSamples,    # append to the raw samples already in -OutputDirectory instead of starting them afresh
    [switch]$ReportOnly,     # start no process: judge the raw samples already in -OutputDirectory (after several -KeepSamples calls)
    [string]$ReportName = 'baseline',   # the report is written as <ReportName>-<date>.md
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../checks/CheckVerdict.psm1') -Force
Write-CheckSubject 'Measure-WindowOpen'

$Languages = @($Languages | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })   # -File passes "en,ru" as one string
$Themes = @($Themes | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($theme in $Themes) { if ($theme -notin 'light', 'dark') { throw "Unknown theme '$theme'; use light or dark." } }
$complexScripts = @('hi', 'bn', 'ar', 'ur', 'zh')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = if ($Project) { [IO.Path]::GetFullPath($Project) } else { Join-Path $repo 'tests/CyrFlip.Tests/CyrFlip.Tests.csproj' }
if (-not (Test-Path -LiteralPath $project)) { throw "Test project not found: $project" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)   # the test host runs elsewhere: a relative path would land beside it
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

if (-not (Assert-Tool -Name 'dotnet')) { Complete-Check }
if (-not $NoBuild -and -not $ReportOnly) {
    & dotnet build $project -c Release --no-restore -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { Add-CheckFinding -Severity fail -Name 'build' -Reason 'The test project did not build.'; Complete-Check }
}

function Get-NearestRank([double[]]$values, [double]$percentile) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $rank = [int][Math]::Ceiling($percentile / 100.0 * $sorted.Count)
    return $sorted[[Math]::Min($sorted.Count, [Math]::Max(1, $rank)) - 1]
}

function Get-Max([double[]]$values) {
    if (@($values).Count -eq 0) { return [double]::NaN }
    return ($values | Measure-Object -Maximum).Maximum
}

$report = [System.Collections.Generic.List[string]]::new()
$report.Add('# Window open and change timing (S0135)')
$report.Add('')
$report.Add('Open = from the start of the window''s construction to the first frame Windows has painted after Show (every invalid region of the window and its children, RedrawWindow RDW_UPDATENOW|RDW_ALLCHILDREN). Cold = the first construction of the type in the process, warm = the second. Change = from toggling a check box in the settings window to the painted frame. The compositor''s present of the frame is not included. Nearest-rank p95 over all samples; no outlier dropped; verdicts need at least 20 samples.')
$report.Add('')
$completedTotal = 0
$metaWritten = $false
$env:CYRFLIP_SWEEP = 'full'
try {
    foreach ($language in $Languages) {
        foreach ($theme in $Themes) {
            $csv = Join-Path $OutputDirectory "trace-$language-$theme.csv"
            if (-not $KeepSamples -and -not $ReportOnly) {
                foreach ($f in @($csv, "$csv.stats", "$csv.meta")) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
            }
            $env:CYRFLIP_OPEN_OUT = $csv
            $env:CYRFLIP_OPEN_LANGUAGE = $language
            $env:CYRFLIP_OPEN_THEME = $theme
            $env:CYRFLIP_OPEN_CHANGES = "$Changes"
            $completed = 0
            $lastRun = if ($ReportOnly) { $FirstRun } else { $FirstRun + $Runs }
            for ($run = $FirstRun; $run -lt $lastRun; $run++) {
                $env:CYRFLIP_OPEN_RUN = "$run"
                Write-Host ("{0}/{1}: run {2}/{3}" -f $language, $theme, ($run + 1), $lastRun)
                $out = & dotnet test $project -c Release --no-build --filter 'FullyQualifiedName~WindowOpenTimingTests.MeasureOpen' 2>&1
                if ($LASTEXITCODE -ne 0) {
                    Add-CheckFinding -Severity notverified -Name "$language-$theme-run-$run" -Reason ('The measuring process failed: ' + (($out | Select-Object -Last 6) -join ' | '))
                    continue
                }
                $completed++
            }
            if (-not (Test-Path -LiteralPath $csv)) { Add-CheckFinding -Severity notverified -Name "$language-$theme" -Reason 'No samples were written.'; continue }

            $trace = @(Import-Csv -LiteralPath $csv -Header utc, pid, window, span, ms)
            $processes = @($trace | Select-Object -ExpandProperty pid -Unique).Count   # what the samples hold, whichever calls wrote them
            if ($ReportOnly -or $KeepSamples) { $completed = $processes }
            $completedTotal += $completed
            foreach ($row in $trace) { $row.ms = [double]::Parse($row.ms, [Globalization.CultureInfo]::InvariantCulture) }
            $stats = if (Test-Path -LiteralPath "$csv.stats") { @(Import-Csv -LiteralPath "$csv.stats" -Header run, language, theme, window, phase, controls, handles) } else { @() }

            # The first "open" of a window in a process is its cold open, the second its warm one; a third (the settings window built again for the change timing) is not a sample of either.
            $cold = @{}; $warm = @{}; $seen = @{}
            foreach ($row in ($trace | Where-Object { $_.span -eq 'open' })) {
                $key = "$($row.pid)|$($row.window)"
                $index = if ($seen.ContainsKey($key)) { $seen[$key] } else { 0 }
                $seen[$key] = $index + 1
                $bucket = if ($index -eq 0) { $cold } elseif ($index -eq 1) { $warm } else { $null }
                if ($null -eq $bucket) { continue }
                if (-not $bucket.ContainsKey($row.window)) { $bucket[$row.window] = [System.Collections.Generic.List[double]]::new() }
                $bucket[$row.window].Add($row.ms)
            }

            $openBudget = if ($complexScripts -contains $language) { $ComplexScriptOpenBudgetMs } else { $OpenBudgetMs }
            $report.Add("## $language, $theme theme")
            $report.Add('')
            if (-not $metaWritten -and (Test-Path -LiteralPath "$csv.meta")) {
                $report.Add('Configuration:')
                foreach ($line in (Get-Content -LiteralPath "$csv.meta")) { $report.Add('- ' + $line) }
                $asked = if ($ReportOnly -or $KeepSamples) { $processes } else { $Runs }
                $report.Add("- processes: $completed of $asked completed for this language and theme; settings changes timed per process: $Changes")
                $report.Add('')
                $metaWritten = $true
            }
            $report.Add("Budgets: settings cold open p95 < $openBudget ms, settings change p95 < $ChangeBudgetMs ms, every other window warm open p95 < $WindowBudgetMs ms (advisory above $AdvisoryMs ms).")
            $report.Add('')
            $report.Add('| window | cold n | cold p95 | cold max | warm n | warm p95 | warm max | controls | handles | verdict |')
            $report.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |')
            foreach ($window in (@($cold.Keys) + @($warm.Keys) | Sort-Object -Unique)) {
                $c = if ($cold.ContainsKey($window)) { [double[]]$cold[$window].ToArray() } else { [double[]]@() }
                $w = if ($warm.ContainsKey($window)) { [double[]]$warm[$window].ToArray() } else { [double[]]@() }
                $cp95 = Get-NearestRank $c 95; $wp95 = Get-NearestRank $w 95
                $own = @($stats | Where-Object { $_.window -eq $window -and $_.phase -eq 'warm' })
                $controls = if ($own.Count) { $own[0].controls } else { '-' }
                $handles = if ($own.Count) { $own[0].handles } else { '-' }
                $verdict = 'pass'
                $name = "$language-$theme-$window"
                if ($c.Count -lt 20 -or $w.Count -lt 20) {
                    $verdict = 'too few samples'
                    Add-CheckFinding -Severity notverified -Name $name -Reason "fewer than 20 cold ($($c.Count)) or warm ($($w.Count)) samples."
                } elseif ($window -eq 'SettingsForm' -and $cp95 -ge $openBudget) {
                    $verdict = 'OPEN over budget'
                    Add-CheckFinding -Severity fail -Name $name -Reason ('settings cold open p95 {0:0.0} ms >= {1} ms' -f $cp95, $openBudget)
                } elseif ($window -ne 'SettingsForm' -and $wp95 -ge $WindowBudgetMs) {
                    $verdict = 'WARM over budget'
                    Add-CheckFinding -Severity fail -Name $name -Reason ('warm open p95 {0:0.0} ms >= {1} ms' -f $wp95, $WindowBudgetMs)
                } elseif ($window -ne 'SettingsForm' -and $wp95 -ge $AdvisoryMs) {
                    $verdict = 'pass, over 100 ms'
                    Add-CheckFinding -Severity advisory -Name $name -Reason ('warm open p95 {0:0.0} ms is over the {1} ms advisory line' -f $wp95, $AdvisoryMs)
                }
                $report.Add(('| {0} | {1} | {2:0.0} | {3:0.0} | {4} | {5:0.0} | {6:0.0} | {7} | {8} | {9} |' -f $window, $c.Count, $cp95, (Get-Max $c), $w.Count, $wp95, (Get-Max $w), $controls, $handles, $verdict))
            }
            $report.Add('')

            # What happens inside the settings window and inside every window's attach path.
            $report.Add('Spans (all windows: theme and profile attach; settings window: construction, pages, reloads, change):')
            $report.Add('')
            $report.Add('| window | span | n | median | p95 | max |')
            $report.Add('| --- | --- | ---: | ---: | ---: | ---: |')
            foreach ($group in ($trace | Where-Object { $_.span -ne 'open' } | Group-Object { "$($_.window)|$($_.span)" } | Sort-Object Name)) {
                $values = [double[]]@($group.Group | ForEach-Object { $_.ms })
                $parts = $group.Name -split '\|', 2
                if ($parts[0] -ne 'SettingsForm' -and $parts[1] -notin 'theme-attach', 'profile-attach') { continue }
                if ($parts[0] -ne 'SettingsForm' -and $parts[0] -notin 'ConfirmDialog', 'HotkeyDialog', 'QuickNotesWindow', 'WelcomeDialog', 'SettingsTransferDialog') { continue }   # the floor is read off a few, not 26 rows
                $report.Add(('| {0} | {1} | {2} | {3:0.0} | {4:0.0} | {5:0.0} |' -f $parts[0], $parts[1], $values.Count, (Get-NearestRank $values 50), (Get-NearestRank $values 95), (Get-Max $values)))
            }
            $report.Add('')

            $changeValues = [double[]]@($trace | Where-Object { $_.window -eq 'SettingsForm' -and $_.span -eq 'change' } | ForEach-Object { $_.ms })
            if ($changeValues.Count -lt 20) {
                Add-CheckFinding -Severity notverified -Name "$language-$theme-change" -Reason "fewer than 20 settings change samples ($($changeValues.Count))."
            } else {
                $chp95 = Get-NearestRank $changeValues 95
                $report.Add(('Settings change (toggle to painted frame): n {0}, p95 {1:0.0} ms, max {2:0.0} ms.' -f $changeValues.Count, $chp95, (Get-Max $changeValues)))
                $report.Add('')
                if ($chp95 -ge $ChangeBudgetMs) {
                    Add-CheckFinding -Severity fail -Name "$language-$theme-change" -Reason ('settings change p95 {0:0.0} ms >= {1} ms' -f $chp95, $ChangeBudgetMs)
                }
            }
        }
    }
} finally {
    Remove-Item Env:CYRFLIP_SWEEP, Env:CYRFLIP_OPEN_OUT, Env:CYRFLIP_OPEN_LANGUAGE, Env:CYRFLIP_OPEN_THEME, Env:CYRFLIP_OPEN_CHANGES, Env:CYRFLIP_OPEN_RUN -ErrorAction SilentlyContinue
}

$reportPath = Join-Path $OutputDirectory ('{0}-{1}.md' -f $ReportName, (Get-Date -Format 'yyyy-MM-dd'))
Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
Write-Host "Report: $reportPath"
Write-Host "Raw samples: $OutputDirectory (trace-<language>-<theme>.csv and .stats)"
Complete-Check
