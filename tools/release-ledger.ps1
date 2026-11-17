#requires -Version 7.0
<#
.SYNOPSIS
    CyrFlip's one door to the release ledger writer (RELEASE-LEDGER 0.1; the ledger is releases/).

.DESCRIPTION
    The writer is the canon's harness script releases/release-ledger, shipped in the sza plugin. CyrFlip has no
    tools/harness.ps1 forwarder, so this file is the forwarder: it finds the writer the way the canon's own
    forwarder does and hands the call over for this repository (-RepoRoot is added unless the caller names one).
    Everything after it is passed through untouched; the script declares no parameters on purpose, so a declared
    name never swallows a prefix of the writer's own parameters.

        pwsh -NoProfile -File tools\release-ledger.ps1 show
        pwsh -NoProfile -File tools\release-ledger.ps1 build -Package cyrflip -Kind test -Version 26.11.12.1652
        pwsh -NoProfile -File tools\release-ledger.ps1 publish -Key github -Version 26.11.12.1652 -State live
        pwsh -NoProfile -File tools\release-ledger.ps1 probe

    Who calls it: build.ps1 and msix\build-msix.ps1 (verb build, the last step of a successful build) and
    release.ps1 (verb publish, at each channel step). A caller that records on the way - a build, a release -
    reports a non-zero exit and goes on: the ledger never fails a build or a release.

    Where the writer is looked up, first hit wins:
      1. $env:SZA_HARNESS_ROOT (a harness directory, for a local canon checkout: <checkout>\tools\harness);
      2. $env:CLAUDE_PLUGIN_ROOT (tools\harness inside the running plugin);
      3. the installed sza plugin versions under the user's Claude plugin cache, newest first, compared as
         versions, the first one that has releases\release-ledger.ps1.

    The ledger folder is releases/ at the repository root (.sza-profile.json paths.releasesDir): docs/ is the
    published product site - Pages serves the whole folder - so the default docs/releases would publish the
    ledger to the web.

Exit codes: the writer's own (0 done, 1 the ledger could not be read or written, 2 invalid invocation);
2 when the writer cannot be found, with one line saying how to point at it.
#>

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($args.Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$args[0])) {
    Write-Host 'usage: tools\release-ledger.ps1 <verb> [writer arguments]   e.g. show | build | publish | confirm | probe | import | history | render'
    exit 2
}

function Get-WriterCandidates {
    if ($env:SZA_HARNESS_ROOT) { Join-Path $env:SZA_HARNESS_ROOT 'releases\release-ledger.ps1' }
    if ($env:CLAUDE_PLUGIN_ROOT) { Join-Path $env:CLAUDE_PLUGIN_ROOT 'tools\harness\releases\release-ledger.ps1' }
    $home_ = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
    if (-not $home_) { return }
    $cache = Join-Path $home_ '.claude\plugins\cache\sza-unified-rules\sza'
    if (-not (Test-Path -LiteralPath $cache)) { return }
    Get-ChildItem -LiteralPath $cache -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            $v = $null
            [pscustomobject]@{ Dir = $_.FullName; IsVersion = [version]::TryParse($_.Name, [ref]$v); Version = $v }
        } |
        Sort-Object -Property @{ Expression = 'IsVersion'; Descending = $true }, @{ Expression = 'Version'; Descending = $true } |
        ForEach-Object { Join-Path $_.Dir 'tools\harness\releases\release-ledger.ps1' }
}

$writer = $null
foreach ($candidate in @(Get-WriterCandidates)) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $writer = $candidate; break }
}
if (-not $writer) {
    Write-Host 'release-ledger: the writer (tools/harness/releases/release-ledger.ps1) is not in the installed sza plugin; update the plugin, or set SZA_HARNESS_ROOT to <canon checkout>\tools\harness. Nothing was recorded.'
    exit 2
}

$passThrough = @($args | ForEach-Object { [string]$_ })
if ($passThrough -cnotcontains '-RepoRoot') { $passThrough += @('-RepoRoot', $repoRoot) }
& pwsh -NoProfile -File $writer @passThrough
exit $LASTEXITCODE
