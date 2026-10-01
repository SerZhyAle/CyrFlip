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
    [switch] $AllowDirty,
    # Skip remote origin reachable check without failing with NOT VERIFIED (exit 2).
    [switch] $Offline
)
$ErrorActionPreference = 'Stop'
$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'dotnet SDK not found on PATH.' -ForegroundColor Red
    exit 2
}

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
$preflightUnverified = [System.Collections.Generic.List[string]]::new()
$preflightFails = [System.Collections.Generic.List[string]]::new()
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
    if ($Offline) {
        Write-Host 'Could not reach origin - the remote tag/sync check was skipped (-Offline).' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Could not reach origin - the remote tag/sync check was skipped.' -ForegroundColor Yellow
        $preflightUnverified.Add('remote origin unreachable (tag/behind check skipped; pass -Offline to ignore)')
    }
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
        Step 'Check placement'
        & (Join-Path $preflightRoot 'tools\checks\Test-CheckPlacement.ps1')
        if ($LASTEXITCODE -ne 0) { $preflightFails.Add("placement check failed (exit $LASTEXITCODE)") }

        dotnet build CyrFlip.sln -c Release -p:Version=$Version --nologo
        if ($LASTEXITCODE -ne 0) {
            $preflightFails.Add("Build failed (exit $LASTEXITCODE)")
            Write-Host 'Build failed - skipping dependent gates.' -ForegroundColor Red
        }
        else {
            # The same FileVersion gate release.yml applies after the tag - run here, so a mismatch
            # fails before the tag exists. The SDK normalizes HHmm's leading zero in FileVersion.
            $exeInfo = (Get-Item (Join-Path $preflightRoot 'src\CyrFlip\bin\Release\net48\CyrFlip.exe')).VersionInfo
            if ([version]$exeInfo.FileVersion -ne [version]$Version) {
                $preflightFails.Add("exe FileVersion '$($exeInfo.FileVersion)' does not read back as '$Version'")
            }

            $testResults = Join-Path $preflightRoot 'TestResults-preflight'
            dotnet test CyrFlip.sln -c Release --no-build --nologo --logger trx --results-directory $testResults
            if ($LASTEXITCODE -ne 0) {
                $preflightFails.Add("Unit tests failed (exit $LASTEXITCODE)")
            }
            else {
                # A run that discovered nothing also exits 0 (S0029 RB-6).
                & (Join-Path $preflightRoot 'tools\Assert-TestRun.ps1') -ResultsDirectory $testResults
                if ($LASTEXITCODE -ne 0) { $preflightFails.Add("Test discovery count check failed") }
                else { Write-Host 'Detached-HEAD build + tests green.' -ForegroundColor Green }
            }

            # The VS Code extension is compiled by nothing else before it is published by hand, and its
            # source once shipped changes that out/extension.js never had (S0029 RB-7). Compile it here
            # whenever it changed since the last release tag.
            $lastTag = git describe --tags --abbrev=0 --match 'v*' HEAD 2>$null
            $extensionChanged = $true
            if ($LASTEXITCODE -eq 0 -and $lastTag) {
                $extensionChanged = [bool](git diff --name-only $lastTag HEAD -- vscode-extension)
            }
            if ($extensionChanged) {
                Step 'VS Code extension compile'
                Push-Location (Join-Path $preflightRoot 'vscode-extension')
                try {
                    npm ci --no-audit --no-fund
                    if ($LASTEXITCODE -ne 0) { $preflightFails.Add("npm ci failed in vscode-extension (exit $LASTEXITCODE)") }
                    else {
                        npm run compile
                        if ($LASTEXITCODE -ne 0) { $preflightFails.Add("The VS Code extension does not compile (exit $LASTEXITCODE)") }
                    }
                }
                finally { Pop-Location }
            }
        }

        # xUnit cannot see this one: store-listings.md and store/listing-*.txt repeat the listing copy
        # for the paste-by-hand path, and a mirror a release behind is exactly what gets pasted live.
        Step 'Store listing mirrors'
        & (Join-Path $preflightRoot 'msix\render-listing-mirrors.ps1') -Check
        if ($LASTEXITCODE -eq 1) {
            $preflightFails.Add('Store listing mirrors drifted from msix/store-listing-export.csv. Run msix\render-listing-mirrors.ps1, review the diff, commit.')
        }
        elseif ($LASTEXITCODE -eq 2) {
            $preflightUnverified.Add('Could not read msix/store-listing-export.csv or listing mirrors.')
        }
        elseif ($LASTEXITCODE -ne 0) {
            $preflightFails.Add("Store listing mirror check failed (exit $LASTEXITCODE).")
        }

        Step 'Store listing CSV import check'
        & (Join-Path $preflightRoot 'msix\build-store-listing-csv.ps1') -Check
        if ($LASTEXITCODE -eq 1) {
            $preflightFails.Add('Store listing import CSV drifted from export + language copy. Run msix\build-store-listing-csv.ps1, review the diff, commit.')
        }
        elseif ($LASTEXITCODE -eq 2) {
            $preflightUnverified.Add('Could not read msix/store-listing-export.csv or language files.')
        }
        elseif ($LASTEXITCODE -ne 0) {
            $preflightFails.Add("Store listing import CSV check failed (exit $LASTEXITCODE).")
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

if ($preflightFails.Count -gt 0 -or $preflightUnverified.Count -gt 0) {
    if ($Push) {
        Write-Host "Cannot push release tag $Tag because preflight checks did not pass." -ForegroundColor Red
    }
}

# --- Trigger the GitHub build (only with -Push and clean preflight) -----------
if ($Push -and $preflightFails.Count -eq 0 -and $preflightUnverified.Count -eq 0) {
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
elseif (-not $Push) {
    Step 'PREFLIGHT ONLY - nothing pushed'
    Write-Host "Re-run with -Push to create tag $Tag and start the GitHub release build." -ForegroundColor Yellow
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

[ ] 4. Microsoft Store (MSIX):  .\msix\build-msix.ps1 -ReleaseZip <downloaded-release-ZIP> -Version $Version
        (the Store identity SZA.CyrFlip / CN=F98ACEDB-... / SZA is the script's default; it checks the
        exe was built from this tag's commit.)
        Then Partner Center -> CyrFlip -> Create new submission -> replace .msix -> Store listings
        -> Import from msix/store-listing-export.csv (the source of truth, all 13 languages; use
        build-store-listing-csv.ps1 -ImportFolder when screenshots go with the copy) -> Submit.
        (Store ID 9NB4W41NGQJ4)

[ ] 5. VS Code extension (only if vscode-extension/ changed):
        bump version in vscode-extension/package.json, then in that folder:
        npm install ; npm run compile ; npx @vscode/vsce publish

[ ] 6. Smoke-test the published artefacts (winget install / Store install) once live.
"@ | Write-Host

# --- Machine-readable verdict line (last stdout line) -----------------------
$subjectName = if ($Push) { "release v$Version" } else { "release-preflight v$Version" }
if ($preflightFails.Count -gt 0) {
    Write-Host "`nPREFLIGHT FAILURES:" -ForegroundColor Red
    foreach ($f in $preflightFails) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host "$($subjectName): FAIL ($($preflightFails.Count))" -ForegroundColor Red
    exit 1
}
elseif ($preflightUnverified.Count -gt 0) {
    Write-Host "`nPREFLIGHT UNVERIFIED:" -ForegroundColor Yellow
    foreach ($u in $preflightUnverified) { Write-Host "  - $u" -ForegroundColor Yellow }
    Write-Host "$($subjectName): NOT VERIFIED ($($preflightUnverified.Count))" -ForegroundColor Yellow
    exit 2
}
else {
    Write-Host "$($subjectName): PASS" -ForegroundColor Green
    exit 0
}

