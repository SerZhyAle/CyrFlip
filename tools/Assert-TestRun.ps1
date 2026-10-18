<#
.SYNOPSIS
    Fails when a `dotnet test` run executed no tests, or fewer than a floor (ticket S0029 RB-6).

.DESCRIPTION
    `dotnet test` exits 0 when discovery finds nothing - a broken test adapter, a filter that matches
    nothing, an assembly that failed to load - and a green run that ran zero tests is exactly what a
    preflight must not accept. Every caller runs `dotnet test` with a TRX logger into a fresh folder and
    then this script, which sums the `executed` counters of the TRX files found there.

    The floor sits well below the real count (about 1200 in September 2026): it exists to catch "almost
    nothing ran", not to be bumped with every new test.

.EXAMPLE
    dotnet test CyrFlip.sln -c Release --no-build --logger trx --results-directory $dir
    & tools\Assert-TestRun.ps1 -ResultsDirectory $dir
#>
param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [int]$Floor = 1000
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'checks/CheckVerdict.psm1') -Force
Set-CheckSubject 'test-run'

try {
$files = @(Get-ChildItem -Path $ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) { throw 'The test run produced no TRX results.' }

$executed = 0
$failed = 0
$skipped = 0
foreach ($file in $files) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if (-not $counters) { throw "TRX '$($file.Name)' has no ResultSummary counters." }
    $executed += [int]$counters.executed
    $failed += [int]$counters.failed
    $skipped += [int]$counters.notExecuted
}

if ($executed -gt 0 -and $executed -lt $Floor) { Add-CheckFinding -Severity fail -Name 'test-floor' -Reason "Executed $executed tests, below the floor of $Floor." }
if ($failed -gt 0) { Add-CheckFinding -Severity fail -Name 'failed-tests' -Reason "The TRX reports $failed failed test(s)." }

Complete-Check -RunCount $executed -SkippedCount $skipped
}
catch {
    Add-CheckFinding -Severity notverified -Name 'trx-results' -Reason $_.Exception.Message
    Complete-Check
}
