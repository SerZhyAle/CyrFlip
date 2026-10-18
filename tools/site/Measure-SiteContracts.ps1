<#
.SYNOPSIS
    Measures the rendered docs/ site against the rules of SITE-STRUCTURE and SITE-EXPERIENCE that a machine can
    read off a page (ticket S0049), and says which rules deviate and by how much.
.DESCRIPTION
    Serves docs/ on a loopback port and loads every page in headless Chrome (an iframe at 360, 1920 and 2560 px,
    both themes): landmarks, headings, lang/dir, skip link, inline styles, contrast, touch targets, full width,
    the origins a page contacts and what the privacy page names, language-control targets, and - with the
    preference forced both ways - motion. A source pass reads the link graph, the sitemap, the forwarders and the
    page scripts. tools/site/measure/ holds the pieces; nothing in docs/ is written.

    What a browser cannot give is not measured here and stays manual (SITE-CHECKLIST): a real Tab walk (the
    automation key does not move focus), the accessible names a screen reader announces, the published host's
    answers, and every box that reads text for meaning (SITE-REPRESENTATION).

    Operator-typed (tools/checks/check-placement.jsonl): it needs a browser, so no runner of the build or CI calls
    it. Verdict per CHECK-VERDICT: a rule that deviates is an advisory (exit 3) while the deviation is a dated
    exception in the contracts registry, and a failure (exit 1) with -Strict; a run that could not measure is
    NOT VERIFIED (exit 2).
.PARAMETER KitSha256
    The SHA-256 of the catalog's current kit file; without it the kit is not compared (SITE-EXPERIENCE rule 1).
.PARAMETER DeclaredOrigins
    The third-party hosts the product declares (SITE-EXPERIENCE rule 14).
.PARAMETER Strict
    A deviating rule fails instead of advising.
#>
[CmdletBinding()]
param(
    [string]$KitSha256 = '-',
    [string[]]$DeclaredOrigins = @('api.github.com'),
    [string]$SiteBase = 'https://serzhyale.github.io/CyrFlip/',
    [string]$Chrome,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cyrflip-site-measure'),
    [int]$TimeoutSeconds = 420,
    [string[]]$Pages,
    [switch]$Strict
)

Import-Module "$PSScriptRoot/../checks/CheckVerdict.psm1" -Force
Set-CheckSubject 'site-measurement'
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$docs = Join-Path $repo 'docs'
$measure = Join-Path $PSScriptRoot 'measure'
$python = (Get-Command python, py -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $Chrome) {
    $Chrome = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $python) { Add-CheckFinding -Severity notverified -Name 'python' -Reason 'python not found on PATH' }
if (-not $Chrome) { Add-CheckFinding -Severity notverified -Name 'chrome' -Reason 'no Chrome or Edge found; pass -Chrome' }
if (-not $python -or -not $Chrome) { Complete-Check }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
foreach ($stale in 'static', 'full', 'motion', 'motion-baseline', 'findings') { Remove-Item (Join-Path $OutDir "$stale.json") -ErrorAction SilentlyContinue }

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$base = "http://127.0.0.1:$port"

$allPagePaths = @(Get-ChildItem $docs -Recurse -Filter *.html | ForEach-Object { $_.FullName.Substring($docs.Length + 1).Replace('\', '/') } | Sort-Object)
if ($Pages.Count -gt 0) {
    $missing = @($Pages | Where-Object { $allPagePaths -notcontains $_ })
    if ($missing.Count -gt 0) {
        Add-CheckFinding -Severity notverified -Name 'requested-pages' -Reason "Unknown pages: $($missing -join ', ')"
        Complete-Check
    }
    $selectedPagePaths = @($Pages | Select-Object -Unique)
    Write-Host "Requested browser sample: $($selectedPagePaths -join ', '); this is not full-corpus evidence."
}
else { $selectedPagePaths = $allPagePaths }
Set-Content (Join-Path $OutDir 'pages.json') (ConvertTo-Json -InputObject @($selectedPagePaths)) -Encoding utf8

$server = $null
$browsers = @()
try {
    & $python (Join-Path $measure 'measure-static.py') $docs (Join-Path $OutDir 'static.json') $SiteBase
    if ($LASTEXITCODE -ne 0) { throw 'the source pass failed' }

    $server = Start-Process -FilePath $python -ArgumentList @((Join-Path $measure 'measure-server.py'), $port, $docs, $OutDir, $SiteBase) -WindowStyle Hidden -PassThru
    $up = $false
    for ($i = 0; $i -lt 40 -and -not $up; $i++) {
        try { $up = (Invoke-WebRequest "$base/harness/measure-run.html" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $up) { throw 'the local server did not answer' }

    $passes = @(
        @{ name = 'full'; mode = 'full'; flag = $null },
        @{ name = 'motion'; mode = 'motion'; flag = '--force-prefers-reduced-motion' },
        @{ name = 'motion-baseline'; mode = 'motion'; flag = '--force-prefers-no-reduced-motion' })
    foreach ($pass in $passes) {
        $profileDir = Join-Path $OutDir "chrome-$($pass.name)"
        $argList = @('--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$profileDir", '--window-size=2600,1500')
        if ($pass.flag) { $argList += $pass.flag }
        $argList += "$base/harness/measure-run.html?mode=$($pass.mode)&name=$($pass.name)"
        $proc = Start-Process -FilePath $Chrome -ArgumentList $argList -WindowStyle Hidden -PassThru
        $browsers += $proc
        $result = Join-Path $OutDir "$($pass.name).json"
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while (-not (Test-Path $result) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
        & taskkill /T /F /PID $proc.Id 2>&1 | Out-Null
        if (-not (Test-Path $result)) { Add-CheckFinding -Severity notverified -Name "pass-$($pass.name)" -Reason "headless browser produced no result within $TimeoutSeconds s" }
    }

    if (Test-Path (Join-Path $OutDir 'full.json')) {
        & $python (Join-Path $measure 'measure-report.py') $OutDir (Join-Path $docs 'assets/sza-kit.css') $KitSha256 ($DeclaredOrigins -join ',') ([Uri]$SiteBase).Host
        if ($LASTEXITCODE -ne 0) { throw 'the report pass failed' }
        $severity = if ($Strict) { 'fail' } else { 'advisory' }
        foreach ($f in (Get-Content (Join-Path $OutDir 'findings.json') -Raw | ConvertFrom-Json)) {
            if ($null -eq $f.ok) { Add-CheckFinding -Severity notverified -Name "$($f.rule) $($f.group)" -Reason $f.text }
            elseif (-not $f.ok) { Add-CheckFinding -Severity $severity -Name "$($f.rule) $($f.group)" -Reason $f.text }
        }
    }
}
catch {
    Add-CheckFinding -Severity notverified -Name 'measurement' -Reason $_.Exception.Message
}
finally {
    if ($server) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
    foreach ($b in $browsers) { & taskkill /T /F /PID $b.Id 2>&1 | Out-Null }
    foreach ($pass in 'full', 'motion', 'motion-baseline') { Remove-Item (Join-Path $OutDir "chrome-$pass") -Recurse -Force -ErrorAction SilentlyContinue }
}
Complete-Check
