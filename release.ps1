<#
    CyrFlip РЕЛИЗ orchestrator.

    A РЕЛИЗ (release) is the paid, outward-facing path (vs a local "сборка" = build.ps1):
      docs/site update -> GitHub build (release.yml) -> winget -> Microsoft Store -> VS Code Marketplace.

    This script drives the automatable core safely and prints the manual checklist for the rest.

      .\release.ps1                 # PREFLIGHT only: on-main check, local build+test, compute version,
                                    #   print the full checklist. Makes NO git changes, spends NO GitHub
                                    #   minutes. Run this first, every time.

    A dirty working tree is NOT a blocker: this is a single-developer project, and stopping a
    preflight over uncommitted files was pure friction. The preflight says how many files are
    uncommitted and carries on. Pass -RequireClean to get the old refuse-if-dirty behaviour.

      .\release.ps1 -Push           # After a green preflight: create the "release: vX" commit + tag and
                                    #   push them. The tag triggers release.yml (the PAID GitHub build that
                                    #   produces the signed ZIP + GitHub Release). Then follow the checklist.

      .\release.ps1 -Version 26.6.27.1600 -Push   # pin an explicit version instead of "now"

    Why a dedicated empty "release:" commit: the tag must point at a commit WITHOUT [skip ci]
    (local сборки carry [skip ci]); the "release:" prefix makes ci.yml skip the branch push so we
    are not billed twice (CI on the branch + release.yml on the tag).
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Push,
    # Refuse to release from a dirty tree. Off by default (see the header); the switch is here for
    # the day this repo has more than one pair of hands.
    [switch] $RequireClean,
    # Kept so older invocations and the checklists that mention it still run: dirty is the default
    # now, so this is a no-op.
    [switch] $AllowDirty
)
$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

function Step($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }

# --- Preflight: branch + clean tree ----------------------------------------
Step 'Preflight'
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "Release must run from 'main' (you are on '$branch')." }

$dirty = @(git status --porcelain)
if ($dirty.Count -gt 0) {
    if ($RequireClean) {
        Write-Host ($dirty -join "`n")
        throw "Working tree is not clean ($($dirty.Count) path(s)) and -RequireClean was passed. Commit first: build.ps1 -Commit -Message '<text>'."
    }
    # Not a blocker, but the release ships what is COMMITTED: the tag points at the anchor commit,
    # and release.yml builds from that. Anything uncommitted is simply not in the release.
    Write-Host "Working tree has $($dirty.Count) uncommitted path(s) - they will NOT be in the release." -ForegroundColor Yellow
    Write-Host "  Commit them first if they should ship: build.ps1 -Commit -Message '<text>'" -ForegroundColor DarkGray
}

# --- Version ----------------------------------------------------------------
if (-not $Version) { $Version = (Get-Date).ToString('yy.M.d.HHmm') }
# Guard: a mistyped -Version must fail BEFORE tagging/pushing. The tag shape is
# YY.M.D.HHmm (dotted, M/D not zero-padded); reject anything else and reject a
# value that is not a real date (e.g. 26.13.40.9999).
if ($Version -notmatch '^\d{2}\.\d{1,2}\.\d{1,2}\.\d{4}$') {
    throw "Version '$Version' is not the YY.M.D.HHmm shape (e.g. 26.7.22.1712)."
}
try { [datetime]::ParseExact($Version, 'yy.M.d.HHmm', [Globalization.CultureInfo]::InvariantCulture) | Out-Null }
catch { throw "Version '$Version' does not parse as a real YY.M.D.HHmm date/time." }
$Tag = "v$Version"
if (git tag --list $Tag) { throw "Tag $Tag already exists locally. Pick another -Version." }

# The local tag list is not the whole story: a tag that exists only on the remote would fail at
# push time, after the anchor commit and the local tag were already made. Ask origin first - and
# while we are asking, make sure main is not behind, for the same reason.
$remoteReachable = $true
try {
    git fetch --quiet --tags origin 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { $remoteReachable = $false }
}
catch { $remoteReachable = $false }

if ($remoteReachable) {
    if (git ls-remote --tags origin "refs/tags/$Tag") { throw "Tag $Tag already exists on origin. Pick another -Version." }
    $behind = (git rev-list --count "HEAD..origin/$branch")
    if ($LASTEXITCODE -eq 0 -and [int]$behind -gt 0) {
        throw "Local $branch is $behind commit(s) behind origin/$branch. Pull first, then re-run."
    }
}
else {
    Write-Host 'Could not reach origin - the remote tag/sync check was skipped.' -ForegroundColor Yellow
}

Write-Host "Release version: $Version  (tag $Tag)" -ForegroundColor Green

# --- Local build + test (fail BEFORE spending GitHub minutes) ---------------
Step 'Local build + test'
# A running tray app keeps bin\Release\net48\CyrFlip.exe locked, and the build then dies with
# MSB3027 after ten retries - i.e. the preflight failed for a reason that has nothing to do with
# the release. Stop it first, exactly as build.ps1 does; the process name is unique to CyrFlip, so
# this also catches a copy started from one of the local sync folders.
$running = @(Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host 'Stopping running CyrFlip (it locks the build output)..' -ForegroundColor Cyan
    $running | Stop-Process -Force
    foreach ($process in $running) {
        try { $process.WaitForExit(5000) | Out-Null } catch { }
    }
}

dotnet build CyrFlip.sln -c Release -p:Version=$Version --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
dotnet test CyrFlip.sln -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
Write-Host 'Local build + tests green.' -ForegroundColor Green

# Put back what we stopped: a preflight is run repeatedly, and it should not leave the user's tray
# app closed behind it. The fresh build is what starts, which is also a free smoke test.
if ($running.Count -gt 0) {
    $exe = Join-Path $RepoRoot 'src\CyrFlip\bin\Release\net48\CyrFlip.exe'
    if (Test-Path $exe) {
        Write-Host 'Restarting CyrFlip..' -ForegroundColor Cyan
        Start-Process $exe | Out-Null
    }
}

# --- Trigger the GitHub build (only with -Push) -----------------------------
if (-not $Push) {
    Step 'PREFLIGHT ONLY - nothing pushed'
    Write-Host "Re-run with -Push to create tag $Tag and start the GitHub release build." -ForegroundColor Yellow
}
else {
    Step "Tag + push $Tag (triggers paid GitHub release build)"
    # Empty, non-[skip ci] anchor commit; the "release:" prefix makes ci.yml skip the branch push.
    git commit --allow-empty -m "release: $Tag"
    if ($LASTEXITCODE -ne 0) { throw "release commit failed (exit $LASTEXITCODE)." }
    git tag $Tag
    # The anchor commit already exists at this point; say so, or the next run trips over it.
    if ($LASTEXITCODE -ne 0) { throw "git tag failed (exit $LASTEXITCODE). The empty 'release: $Tag' commit was made - drop it with 'git reset --hard HEAD~1' before retrying." }
    git push origin $branch
    if ($LASTEXITCODE -ne 0) { throw "git push (branch) failed (exit $LASTEXITCODE)." }
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { throw "git push (tag) failed (exit $LASTEXITCODE)." }
    Write-Host "Pushed $Tag - release.yml is now building the signed ZIP + GitHub Release." -ForegroundColor Green
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        Write-Host 'Watch:  gh run watch   (or: gh run list --workflow=Release)' -ForegroundColor DarkGray
    }
}

# --- The rest is manual / external: print the checklist ---------------------
Step "РЕЛИЗ checklist for $Tag  (see RELEASE.md for detail)"
@"
[ ] 1. GitHub build green: release.yml produced CyrFlip-$Version-windows-x64.zip + .sha256
        and a GitHub Release. Copy the ZIP asset URL and the SHA256 from the run log.

[ ] 2. Site/docs (auto-deploys from /docs on the push above) - verify GitHub Pages updated:
        bump any version/changelog text in docs/ if the release changes user-facing behaviour.
        The "what's new" copy lives in four places and they must agree:
        msix/store-listing-export.csv (ReleaseNotes row) -> store/listing-*.txt,
        msix/store-listings.md, winget/*.locale.*.yaml (ReleaseNotes).

[ ] 3. winget (SerZhyAle.CyrFlip):
        wingetcreate update SerZhyAle.CyrFlip --version $Version --urls <ZIP_URL> --submit

[ ] 4. Microsoft Store (MSIX):  .\msix\build-msix.ps1 ``
          -IdentityName "SZA.CyrFlip" ``
          -Publisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" ``
          -PublisherDisplayName "SZA"
        Then Partner Center -> CyrFlip -> Create new submission -> replace .msix -> Store listings
        -> Import from msix/store-listing-export.csv (the source of truth; store-listings.md and
        store/listing-*.txt render from it, and the CSV carries only en-us + ru, so the Ukrainian
        listing is still pasted by hand from msix/store-listings.md) -> Submit. (Store ID 9NB4W41NGQJ4)

[ ] 5. VS Code extension (only if vscode-extension/ changed):
        bump version in vscode-extension/package.json, then in that folder:
        npm install ; npm run compile ; npx @vscode/vsce publish

[ ] 6. Smoke-test the published artefacts (winget install / Store install) once live.
"@ | Write-Host
