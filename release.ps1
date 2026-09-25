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

      .\release.ps1 -Push           # After a green preflight: create the "release: vX" anchor + tag and
                                    #   push them. The tag triggers release.yml (the PAID GitHub build that
                                    #   produces the ZIP + GitHub Release). Then follow the checklist.

      .\release.ps1 -Version 26.6.27.1600 -Push   # pin an explicit version instead of "now"

    Why a dedicated "release:" anchor: the tag must point at a commit WITHOUT [skip ci]
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
# value that is not a real date (e.g. 26.13.40.9999). A zero-padded month/day (26.09.05.1200)
# is refused too: the SDK normalizes it, so the exe's FileVersion would never read back as the tag.
if ($Version -notmatch '^\d{2}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])\.\d{4}$') {
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

# Store and package feeds only move forward. The local tag set includes the tags fetched above,
# so refuse a hand-typed version (or the DST fall-back hour) that would not be an upgrade.
$highestVersion = $null
foreach ($existingTag in @(git tag --list 'v*')) {
    if ($existingTag -notmatch '^v(\d{2}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])\.\d{4})$') { continue }
    try {
        $candidate = [version]$Matches[1]
        if ($highestVersion -eq $null -or $candidate -gt $highestVersion) { $highestVersion = $candidate }
    }
    catch { }
}
if ($highestVersion -ne $null -and [version]$Version -le $highestVersion) {
    throw "Version '$Version' is not newer than the highest release tag version '$highestVersion'."
}

Write-Host "Release version: $Version  (tag $Tag)" -ForegroundColor Green

# --- Local build + test (fail BEFORE spending GitHub minutes) ---------------
Step 'Local build + test'
# Build and test a detached worktree of HEAD. A dirty index or working tree is expressly allowed
# above, but neither can be evidence for the tree that the tag and GitHub will actually ship.
$running = @(Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue)
$restartPath = $null
$preflightRoot = Join-Path ([IO.Path]::GetTempPath()) ("CyrFlip-release-" + [Guid]::NewGuid().ToString('N'))
$worktreeAdded = $false
try {
    if ($running.Count -gt 0) {
        Write-Host 'Requesting that running CyrFlip exits..' -ForegroundColor Cyan
        foreach ($process in $running) {
            try {
                if (-not $restartPath -and $process.Path) { $restartPath = $process.Path }
                if ($process.Path) { & $process.Path /exit | Out-Null }
            }
            catch { }
        }
    }
    foreach ($process in $running) {
        try { $process.WaitForExit(5000) | Out-Null } catch { }
        if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            Write-Host "CyrFlip pid $($process.Id) did not exit in 5 seconds; forcing it." -ForegroundColor Yellow
            try { Stop-Process -Id $process.Id -Force -ErrorAction Stop } catch { }
        }
    }

    git worktree add --detach $preflightRoot HEAD
    if ($LASTEXITCODE -ne 0) { throw "Could not create detached preflight worktree (exit $LASTEXITCODE)." }
    $worktreeAdded = $true
    Push-Location $preflightRoot
    try {
        dotnet build CyrFlip.sln -c Release -p:Version=$Version --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
        # The same FileVersion gate release.yml applies after the tag - run here, so a mismatch
        # fails before the tag exists. The SDK normalizes HHmm's leading zero in FileVersion.
        $exeInfo = (Get-Item (Join-Path $preflightRoot 'src\CyrFlip\bin\Release\net48\CyrFlip.exe')).VersionInfo
        if ([version]$exeInfo.FileVersion -ne [version]$Version) {
            throw "exe FileVersion '$($exeInfo.FileVersion)' does not read back as '$Version'."
        }
        dotnet test CyrFlip.sln -c Release --no-build --nologo
        if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
        Write-Host 'Detached-HEAD build + tests green.' -ForegroundColor Green

        # xUnit cannot see this one: store-listings.md and store/listing-*.txt repeat the listing copy
        # for the paste-by-hand path, and a mirror a release behind is exactly what gets pasted live.
        Step 'Store listing mirrors'
        & (Join-Path $preflightRoot 'msix\render-listing-mirrors.ps1') -Check
        if ($LASTEXITCODE -ne 0) {
            throw 'Store listing mirrors drifted from msix/store-listing-export.csv. Run msix\render-listing-mirrors.ps1, review the diff, commit.'
        }
    }
    finally { Pop-Location }
}
finally {
    if ($worktreeAdded) {
        git worktree remove --force $preflightRoot
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove temporary preflight worktree: $preflightRoot" }
    }

    # A preflight should not leave the user's tray app closed. Restart the deployed copy that was
    # actually running, never bin\Release (which would overwrite the autostart target and lock builds).
    if ($restartPath -and (Test-Path $restartPath)) {
        Write-Host "Restarting CyrFlip: $restartPath" -ForegroundColor Cyan
        Start-Process $restartPath | Out-Null
    }
}

# --- Trigger the GitHub build (only with -Push) -----------------------------
if (-not $Push) {
    Step 'PREFLIGHT ONLY - nothing pushed'
    Write-Host "Re-run with -Push to create tag $Tag and start the GitHub release build." -ForegroundColor Yellow
}
else {
    Step "Tag + push $Tag (triggers paid GitHub release build)"
    # The anchor gets precisely HEAD's tree, regardless of what the user staged. `git commit` would
    # consume the index; commit-tree does not. Move the current branch only after the object exists.
    $previousHead = (git rev-parse HEAD).Trim()
    $anchor = (git commit-tree 'HEAD^{tree}' -p HEAD -m "release: $Tag").Trim()
    if ($LASTEXITCODE -ne 0 -or -not $anchor) { throw "release anchor creation failed (exit $LASTEXITCODE)." }
    git update-ref HEAD $anchor $previousHead
    if ($LASTEXITCODE -ne 0) { throw "Could not advance the branch to the release anchor (exit $LASTEXITCODE)." }
    git tag $Tag
    if ($LASTEXITCODE -ne 0) {
        $tagExit = $LASTEXITCODE
        git update-ref HEAD $previousHead $anchor
        throw "git tag failed (exit $tagExit). The release anchor was removed; your working tree and index were left untouched."
    }
    git push origin $branch
    if ($LASTEXITCODE -ne 0) { throw "git push (branch) failed (exit $LASTEXITCODE)." }
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { throw "git push (tag) failed (exit $LASTEXITCODE)." }
    Write-Host "Pushed $Tag - release.yml is now building the ZIP + GitHub Release." -ForegroundColor Green
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
        The "what's new" copy lives in two sources that must agree - msix/store-listing-export.csv
        (ReleaseNotes row, all 13 languages) and winget/*.locale.*.yaml (ReleaseNotes) - plus the
        GitHub Release body. store/listing-*.txt and msix/store-listings.md are GENERATED from the
        CSV: edit the CSV, then run msix\render-listing-mirrors.ps1 (the preflight checks it).

[ ] 3. winget (SerZhyAle.CyrFlip) - NOT "wingetcreate update": it rebuilds from the manifest already
        published in winget-pkgs and only bumps version/URL, so this repo's Description /
        ShortDescription / Tags / ReleaseNotes never reach the store. Build from the templates:
          copy winget\*.yaml to a scratch dir, replace __VERSION__ / __URL__ / __SHA256__,
          point ReleaseNotesUrl at /releases/tag/$Tag, then
          winget validate --manifest <dir>   # yaml-only copy: winget\ itself trips over README.md
          winget install  --manifest <dir>   # required by the PR checklist
          wingetcreate submit --prtitle "SerZhyAle.CyrFlip version $Version" --no-open ``
            --token (gh auth token) <dir>
        Then FILL IN THE PR BODY by hand (gh pr edit <n> --repo microsoft/winget-pkgs --body-file):
        wingetcreate submits Microsoft's template untouched - empty description, every box unticked.

[ ] 4. Microsoft Store (MSIX):  .\msix\build-msix.ps1 -ReleaseZip <downloaded-release-ZIP> -Version $Version ``
          -IdentityName "SZA.CyrFlip" ``
          -Publisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" ``
          -PublisherDisplayName "SZA"
        Then Partner Center -> CyrFlip -> Create new submission -> replace .msix -> Store listings
        -> Import from msix/store-listing-export.csv (the source of truth, all 13 languages; use
        build-store-listing-csv.ps1 -ImportFolder when screenshots go with the copy) -> Submit.
        (Store ID 9NB4W41NGQJ4)

[ ] 5. VS Code extension (only if vscode-extension/ changed):
        bump version in vscode-extension/package.json, then in that folder:
        npm install ; npm run compile ; npx @vscode/vsce publish

[ ] 6. Smoke-test the published artefacts (winget install / Store install) once live.
"@ | Write-Host
