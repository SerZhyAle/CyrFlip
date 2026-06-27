<#
    CyrFlip РЕЛИЗ orchestrator.

    A РЕЛИЗ (release) is the paid, outward-facing path (vs a local "сборка" = build.ps1):
      docs/site update -> GitHub build (release.yml) -> winget -> Microsoft Store -> VS Code Marketplace.

    This script drives the automatable core safely and prints the manual checklist for the rest.

      .\release.ps1                 # PREFLIGHT only: clean-tree + on-main checks, local build+test,
                                    #   compute version, print the full checklist. Makes NO git changes,
                                    #   spends NO GitHub minutes. Run this first, every time.

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

$dirty = (git status --porcelain)
if ($dirty -and -not $AllowDirty) {
    Write-Host $dirty
    throw "Working tree is not clean. Commit your сборки first (build.ps1 -Commit), or pass -AllowDirty."
}

# --- Version ----------------------------------------------------------------
if (-not $Version) { $Version = (Get-Date).ToString('yy.M.d.HHmm') }
$Tag = "v$Version"
if (git tag --list $Tag) { throw "Tag $Tag already exists. Pick another -Version." }
Write-Host "Release version: $Version  (tag $Tag)" -ForegroundColor Green

# --- Local build + test (fail BEFORE spending GitHub minutes) ---------------
Step 'Local build + test'
dotnet build CyrFlip.sln -c Release -p:Version=$Version --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
dotnet test CyrFlip.sln -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
Write-Host 'Local build + tests green.' -ForegroundColor Green

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
    if ($LASTEXITCODE -ne 0) { throw "git tag failed (exit $LASTEXITCODE)." }
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

[ ] 3. winget (SerZhyAle.CyrFlip):
        wingetcreate update SerZhyAle.CyrFlip --version $Version --urls <ZIP_URL> --submit

[ ] 4. Microsoft Store (MSIX):  .\msix\build-msix.ps1 ``
          -IdentityName "SZA.CyrFlip" ``
          -Publisher "CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD" ``
          -PublisherDisplayName "SZA"
        Then Partner Center -> CyrFlip -> Create new submission -> replace .msix -> refresh
        EN/RU/UK listings from msix/store-listings.md -> Submit. (Store ID 9NB4W41NGQJ4)

[ ] 5. VS Code extension (only if vscode-extension/ changed):
        bump version in vscode-extension/package.json, then in that folder:
        npm install ; npm run compile ; npx @vscode/vsce publish

[ ] 6. Smoke-test the published artefacts (winget install / Store install) once live.
"@ | Write-Host
