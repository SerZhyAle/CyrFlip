<#
    Render-ReleaseFacts.ps1 - the release facts the site states, from the version being released (S0024 F16).

    The 13 landings and the landing JSON-LD state the release - version, date, the versioned ZIP and its
    .sha256 - only from tools/site/release-facts.json, and that file holds nothing but the version: the date
    is the version's own (YY.M.D), the asset addresses follow release.yml's naming. Nothing is typed by hand
    and nothing is fetched. The /releases/latest buttons stay beside the block, and the landing's
    api.github.com call still refines the download button.

      .\tools\site\Render-ReleaseFacts.ps1 -Version 26.11.12.1400          # write the facts, regenerate the pages
      .\tools\site\Render-ReleaseFacts.ps1 -Version 26.11.12.1400 -Check   # read-only: do the pages state it?

    release.ps1 runs -Check in its preflight worktree of HEAD; when HEAD does not state the version being
    released it renders the facts into the working tree and fails, so the operator reviews and commits them
    and the tagged revision carries them. The pages go live with the push of main that carries the tag, so
    the versioned ZIP link answers once release.yml has published the asset, a few minutes later.

    -Root    Repository root to work in (default: this script's repository).

    Exit codes (CHECK-VERDICT): 0 the pages state the version (or were rendered), 1 drift (no facts, another
    version, or pages not regenerated), 2 could not verify (Python missing, unreadable facts file).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $Check,
    [string] $Root
)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

if ($Version -notmatch '^\d{2}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])\.\d{4}$') {
    Write-Error "Version '$Version' is not the YY.M.D.HHmm shape (e.g. 26.7.22.1712)." -ErrorAction Continue
    exit 1
}
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    Write-Error 'python not found on PATH: the release facts could not be rendered or checked.' -ErrorAction Continue
    exit 2
}
$generator = Join-Path $Root 'tools\site\sync-doc-quality.py'
$factsPath = Join-Path $Root 'tools\site\release-facts.json'

if ($Check) {
    if (-not (Test-Path $factsPath)) {
        Write-Host "release facts: FAIL - tools/site/release-facts.json is missing, so no page states v$Version." -ForegroundColor Red
        exit 1
    }
    try { $stated = (Get-Content $factsPath -Raw -Encoding UTF8 | ConvertFrom-Json).version }
    catch {
        Write-Error "tools/site/release-facts.json does not parse: $($_.Exception.Message)" -ErrorAction Continue
        exit 2
    }
    if ($stated -ne $Version) {
        Write-Host "release facts: FAIL - the pages state v$stated, the release is v$Version." -ForegroundColor Red
        exit 1
    }
    & $python.Source -I $generator --check
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'release facts: FAIL - the pages do not match tools/site/release-facts.json (run the generator with --write).' -ForegroundColor Red
        exit 1
    }
    Write-Host "release facts: PASS - the pages state v$Version."
    exit 0
}

# The file carries the version only; LF and no BOM like every generated site file.
[IO.File]::WriteAllText($factsPath, "{`n  `"version`": `"$Version`"`n}`n", [Text.UTF8Encoding]::new($false))
& $python.Source -I $generator --write
if ($LASTEXITCODE -ne 0) {
    Write-Host "release facts: FAIL - the generator did not finish (exit $LASTEXITCODE)." -ForegroundColor Red
    exit 1
}
Write-Host "release facts: rendered v$Version into tools/site/release-facts.json and the 13 landings."
exit 0
