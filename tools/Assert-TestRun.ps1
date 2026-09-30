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

$files = @(Get-ChildItem -Path $ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) { throw "No TRX file under '$ResultsDirectory' - the test run produced no results." }

$executed = 0
$failed = 0
foreach ($file in $files) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if (-not $counters) { throw "TRX '$($file.Name)' has no ResultSummary counters." }
    $executed += [int]$counters.executed
    $failed += [int]$counters.failed
}

if ($executed -eq 0) { throw 'The test run executed 0 tests - discovery found nothing.' }
if ($executed -lt $Floor) { throw "The test run executed $executed tests, below the floor of $Floor - most of the suite did not run." }
if ($failed -gt 0) { throw "The TRX reports $failed failed test(s)." }

Write-Host "Tests executed: $executed (floor $Floor), failed: 0." -ForegroundColor Green
