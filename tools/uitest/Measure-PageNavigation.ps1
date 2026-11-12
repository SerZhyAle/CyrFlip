<#
.SYNOPSIS
Operator-typed measurement of the settings window's page changes (ticket S0076 M4, SETTINGS-NAVIGATION section 5 rule 4).
.DESCRIPTION
Runs PageNavigationTimingTests.MeasureFirstAndWarmVisits in -Runs fresh processes (default 22: the window opens on a different page in each, 15 pages, so every destination is a timed first visit in at least 20). Each process builds the settings
window once, visits every destination for the first time (the order rotates by run number, so each destination is
sometimes the first thing a cold process shows) and then visits every destination -Cycles more times (warm). A visit is
timed from the rail's mouse release to the moment Windows has painted every invalid region of the window and its children
(PageNavigationTrace) - not to the return of a handler. The raw rows of every process are kept; the budgets are judged
here, per destination, on ALL samples with the nearest-rank p95 and the maximum, no outlier dropped, and a destination
whose warm samples repeatedly exceed the budget fails even when its p95 passes.

It moves a real window onto the screen for the length of each run and uses the keyboard-less, mouse-less in-process
input path (the rail's own handlers), so it needs an interactive desktop but never touches the pointer.
.EXAMPLE
./Measure-PageNavigation.ps1 -Runs 20 -Cycles 20
.NOTES
The compositor's present of the finished frame (at most one refresh interval) is outside the measurement.
Names the configuration it ran on in the report; run it again on the minimum-supported machine to name that one.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 200)][int]$Runs = 22,
    [ValidateRange(1, 200)][int]$Cycles = 20,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/s0076/navigation'),
    [double]$FirstBudgetMs = 500,
    [double]$WarmBudgetMs = 150,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../checks/CheckVerdict.psm1') -Force
Write-CheckSubject 'Measure-PageNavigation'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $repo 'tests/CyrFlip.Tests/CyrFlip.Tests.csproj'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)   # the test host runs elsewhere: a relative path would land beside it
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$csv = Join-Path $OutputDirectory 'raw-samples.csv'
foreach ($f in @($csv, "$csv.meta")) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }

if (-not (Assert-Tool -Name 'dotnet')) { Complete-Check }

if (-not $NoBuild) {
    & dotnet build $project -c Release --no-restore -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { Add-CheckFinding -Severity fail -Name 'build' -Reason 'The test project did not build.'; Complete-Check }
}

$env:CYRFLIP_SWEEP = 'full'
$env:CYRFLIP_NAV_OUT = $csv
$env:CYRFLIP_NAV_CYCLES = "$Cycles"
$completed = 0
try {
    for ($run = 0; $run -lt $Runs; $run++) {
        $env:CYRFLIP_NAV_RUN = "$run"
        Write-Host ("run {0}/{1}" -f ($run + 1), $Runs)
        $out = & dotnet test $project -c Release --no-build --filter 'FullyQualifiedName~PageNavigationTimingTests.MeasureFirstAndWarmVisits' 2>&1
        if ($LASTEXITCODE -ne 0) {
            Add-CheckFinding -Severity notverified -Name "run-$run" -Reason ('The measuring process failed: ' + (($out | Select-Object -Last 6) -join ' | '))
            continue
        }
        $completed++
    }
} finally {
    Remove-Item Env:CYRFLIP_SWEEP, Env:CYRFLIP_NAV_OUT, Env:CYRFLIP_NAV_CYCLES, Env:CYRFLIP_NAV_RUN -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $csv)) { Add-CheckFinding -Severity notverified -Name 'samples' -Reason 'No samples were written.'; Complete-Check }

function Get-NearestRank([double[]]$values, [double]$percentile) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $rank = [int][Math]::Ceiling($percentile / 100.0 * $sorted.Count)
    return $sorted[[Math]::Min($sorted.Count, [Math]::Max(1, $rank)) - 1]
}

$rows = Import-Csv -LiteralPath $csv -Header run, destination, kind, ms
foreach ($row in $rows) { $row.ms = [double]::Parse($row.ms, [Globalization.CultureInfo]::InvariantCulture) }
$meta = if (Test-Path -LiteralPath "$csv.meta") { Get-Content -LiteralPath "$csv.meta" } else { @() }

$report = [System.Collections.Generic.List[string]]::new()
$report.Add('# Page navigation timing (S0076 M4)')
$report.Add('')
$report.Add('SETTINGS-NAVIGATION 2.0 section 5 rule 4. Activation = the rail''s mouse release; frame = every invalid region of the window and its children painted (RedrawWindow RDW_UPDATENOW|RDW_ALLCHILDREN). The compositor''s present of the frame is not included. Nearest-rank p95 over all samples; no outlier dropped.')
$report.Add('')
$report.Add('Configuration:')
foreach ($line in $meta) { $report.Add('- ' + $line) }
$report.Add("- processes: $completed of $Runs completed; warm cycles per process: $Cycles")
$report.Add("- budgets: first p95 < $FirstBudgetMs ms, warm p95 < $WarmBudgetMs ms")
$report.Add('')
$report.Add('| destination | first n | first p95 | first max | warm n | warm p95 | warm max | warm over budget | verdict |')
$report.Add('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |')

foreach ($group in ($rows | Group-Object destination | Sort-Object Name)) {
    $first = @($group.Group | Where-Object kind -eq 'first' | ForEach-Object { $_.ms })
    $warm = @($group.Group | Where-Object kind -eq 'warm' | ForEach-Object { $_.ms })
    $fp95 = Get-NearestRank $first 95; $wp95 = Get-NearestRank $warm 95
    $fmax = if ($first.Count) { ($first | Measure-Object -Maximum).Maximum } else { [double]::NaN }
    $wmax = if ($warm.Count) { ($warm | Measure-Object -Maximum).Maximum } else { [double]::NaN }
    $over = @($warm | Where-Object { $_ -ge $WarmBudgetMs }).Count
    $verdict = 'pass'
    if ($first.Count -lt 20 -or $warm.Count -lt 20) {
        $verdict = 'too few samples'
        Add-CheckFinding -Severity notverified -Name $group.Name -Reason "fewer than 20 first ($($first.Count)) or warm ($($warm.Count)) samples."
    } elseif ($fp95 -ge $FirstBudgetMs) {
        $verdict = 'FIRST over budget'
        Add-CheckFinding -Severity fail -Name $group.Name -Reason ("first-visit p95 {0:0.0} ms >= {1} ms" -f $fp95, $FirstBudgetMs)
    } elseif ($wp95 -ge $WarmBudgetMs) {
        $verdict = 'WARM over budget'
        Add-CheckFinding -Severity fail -Name $group.Name -Reason ("warm p95 {0:0.0} ms >= {1} ms" -f $wp95, $WarmBudgetMs)
    } elseif ($over -ge 3) {
        # An aggregate percentile can hide a reproducible delay: three or more warm samples over budget is repeated, not isolated.
        $verdict = 'repeated warm delays'
        Add-CheckFinding -Severity fail -Name $group.Name -Reason "$over warm samples reached the budget: a repeated delay."
    } elseif ($over -gt 0 -or $fmax -ge $FirstBudgetMs) {
        $verdict = 'pass, outlier to investigate'
        Add-CheckFinding -Severity advisory -Name $group.Name -Reason ("isolated outlier: first max {0:0.0} ms, {1} warm over {2} ms (max {3:0.0})" -f $fmax, $over, $WarmBudgetMs, $wmax)
    }
    $report.Add(('| {0} | {1} | {2:0.0} | {3:0.0} | {4} | {5:0.0} | {6:0.0} | {7} | {8} |' -f $group.Name, $first.Count, $fp95, $fmax, $warm.Count, $wp95, $wmax, $over, $verdict))
}

$reportPath = Join-Path $OutputDirectory 'report.md'
Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
Write-Host "Report: $reportPath"
Write-Host "Raw samples: $csv"
Complete-Check
