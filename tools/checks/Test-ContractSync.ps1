<# Synthetic negative cases for S0077; no account, package or installed settings are changed. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('cyrflip-contract-sync-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$checks = 0
function Require([bool]$ok, [string]$reason) {
    if (-not $ok) { throw $reason }
    $script:checks++
}
try {
    $verdict = Join-Path $PSScriptRoot 'Test-CheckVerdict.ps1'
    foreach ($case in @(
        @{ outcome='pass'; code=0; word='PASS'; run=3; skipped=0 },
        @{ outcome='fail'; code=1; word='FAIL'; run=3; skipped=1 },
        @{ outcome='notverified'; code=2; word='COULD NOT VERIFY'; run=3; skipped=0 },
        @{ outcome='advisory'; code=3; word='PASS WITH ADVISORIES'; run=3; skipped=0 },
        @{ outcome='pass'; code=2; word='COULD NOT VERIFY'; run=0; skipped=0 },
        @{ outcome='pass'; code=2; word='COULD NOT VERIFY'; run=3; skipped=1 },
        @{ outcome='notapplicable'; code=0; word='NOT APPLICABLE'; run=0; skipped=0 }
    )) {
        $lines = @(& $pwsh -NoProfile -File $verdict -Outcome $case.outcome -RunCount $case.run -SkippedCount $case.skipped 2>&1)
        $code = $LASTEXITCODE
        Require ($code -eq $case.code) "verdict exit $code, expected $($case.code)"
        Require ($lines[-1] -match ('^Test-CheckVerdict: ' + [regex]::Escape($case.word) + '\b.*run=' + $case.run + ' skipped=' + $case.skipped)) "incorrect final verdict: $($lines[-1])"
    }

    $resultDir = Join-Path $scratch 'results'
    New-Item -ItemType Directory $resultDir | Out-Null
    $assertRun = Join-Path $repo 'tools/Assert-TestRun.ps1'
    foreach ($case in @(@{run=2; skip=0; fail=0; code=0}, @{run=2; skip=1; fail=0; code=2}, @{run=2; skip=1; fail=1; code=1}, @{run=0; skip=0; fail=0; code=2})) {
        $xml = '<TestRun><ResultSummary><Counters executed="' + $case.run + '" failed="' + $case.fail + '" notExecuted="' + $case.skip + '" /></ResultSummary></TestRun>'
        [IO.File]::WriteAllText((Join-Path $resultDir 'synthetic.trx'), $xml)
        $lines = @(& $pwsh -NoProfile -File $assertRun -ResultsDirectory $resultDir -Floor 1 2>&1)
        Require ($LASTEXITCODE -eq $case.code) "TRX case failed: $($lines -join ' ')"
        Require ($lines[-1] -match "run=$($case.run) skipped=$($case.skip)") 'TRX count grammar'
    }

    $msix = Join-Path $scratch 'msix'
    New-Item -ItemType Directory $msix | Out-Null
    foreach ($name in @('build-store-listing-csv.ps1', 'store-listing-export.csv', 'store-listing-export.sample.csv')) {
        Copy-Item -LiteralPath (Join-Path $repo "msix/$name") -Destination $msix
    }
    Copy-Item -LiteralPath (Join-Path $repo 'msix/listing') -Destination $msix -Recurse
    $builder = Join-Path $msix 'build-store-listing-csv.ps1'
    $sample = Join-Path $msix 'store-listing-export.sample.csv'
    $original = [IO.File]::ReadAllBytes($sample)
    $lines = @(& $pwsh -NoProfile -File $builder -FillNothing 2>&1)
    Require ($LASTEXITCODE -eq 0) "no-BOM round trip failed: $lines"
    [IO.File]::WriteAllBytes($sample, [byte[]](@(0xEF,0xBB,0xBF) + $original))
    $lines = @(& $pwsh -NoProfile -File $builder -FillNothing 2>&1)
    Require ($LASTEXITCODE -eq 0) "BOM round trip failed: $lines"

    $deck = Join-Path $msix 'listing/ar.txt'
    $text = [IO.File]::ReadAllText($deck)
    $overflow = (1..22 | ForEach-Object { "term$_" }) -join ' '
    $text = [regex]::Replace($text, '(?ms)(@@SearchTerm1\s*\r?\n).*?(?=\r?\n@@|\z)', ('$1' + $overflow))
    [IO.File]::WriteAllText($deck, $text)
    $destination = Join-Path $scratch 'must-not-write.csv'
    $lines = @(& $pwsh -NoProfile -File $builder -OutFile $destination 2>&1)
    Require ($LASTEXITCODE -eq 1) 'overflow must fail'
    Require (-not (Test-Path -LiteralPath $destination)) 'overflow wrote an import'
    Require (($lines -join ' ') -match 'ar search terms: \d+ words') 'overflow must name language and word count'
    $lines = @(& $pwsh -NoProfile -File $builder -SkipFields Description -OutFile $destination 2>&1)
    Require ($LASTEXITCODE -eq 1 -and -not (Test-Path -LiteralPath $destination)) 'blank-cell bisection must not write an import'

    Write-Host "contract-sync: PASS (run=$checks skipped=0)"
}
catch {
    Write-Host $_.Exception.Message
    Write-Host "contract-sync: FAIL (run=$checks skipped=0)"
    exit 1
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -like 'cyrflip-contract-sync-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
