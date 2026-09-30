<#
    CyrFlip local build + deploy  ==  a "СБОРКА" (build) in this project's vocabulary.

    A СБОРКА is fully LOCAL and costs no GitHub Actions minutes:
      build Release -> run tests -> stage the single self-contained exe -> deploy to sync folders.
    net48 needs no bundled runtime. Mirrors the convention across SerZhyAle's Windows apps.

    Optionally it also commits the result (the "если удачно - коммитим" half of a сборка):
      -Commit            git add -A + commit AFTER a green build/test
      -Message "<text>"  commit message (required with -Commit)
      -Push              also push the branch to origin

    A clean tree is never a blocker here: with nothing to commit the script says so and finishes
    normally (the build and tests already passed, which is the point of a сборка).

    The commit message gets "[skip ci]" appended automatically, so pushing a сборка to main
    does NOT trigger the paid GitHub CI build - the build was already validated here.
    (A РЕЛИЗ is the separate, paid path: see release.ps1 / RELEASE.md.)
#>
[CmdletBinding()]
param(
    [switch] $Commit,
    [string] $Message,
    [switch] $Push,
    [switch] $NoRun
)
$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'dotnet SDK not found on PATH.' -ForegroundColor Red
    exit 2
}

if ($Commit -and -not $Message) { throw 'Pass -Message "<text>" when using -Commit.' }
if ($Push   -and -not $Commit)  { throw '-Push requires -Commit (nothing to push otherwise).' }

$SolutionDir = $PSScriptRoot
$Solution    = Join-Path $SolutionDir 'CyrFlip.sln'
$OutDir      = Join-Path $SolutionDir 'src\CyrFlip\bin\Release\net48'
$ExeName     = 'CyrFlip.exe'
$SingleDir   = Join-Path $SolutionDir 'bin\SingleFile'
$Destinations = @(
    'C:\GD\i\',
    'C:\GD\tc\SZA\_APP\'
)

# A running .NET Framework exe may keep the previous build output locked. Local builds are the
# fast test loop, so ask the single-instance tray app to exit first and start the deployed copy afterwards.
# CyrFlip deliberately has a unique process name, therefore this also covers a copy launched from
# one of the local sync folders.
$running = @(Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue)
# Where the stopped copy ran from: a build or a test that fails leaves the user without it otherwise -
# indicator, hotkeys and keep-awake all off until the next green build (S0038 RP-5).
$stoppedPaths = @($running | ForEach-Object { try { $_.Path } catch { $null } } | Where-Object { $_ } | Select-Object -Unique)
$restartStopped = $false
if ($running.Count -gt 0) {
    Write-Host 'Stopping running CyrFlip..' -ForegroundColor Cyan
    foreach ($process in $running) {
        try {
            if ($process.Path) { & $process.Path /exit | Out-Null }
        }
        catch { }
    }
    foreach ($process in $running) {
        try { $process.WaitForExit(5000) | Out-Null } catch { }
        if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            Write-Host "CyrFlip pid $($process.Id) did not exit in 5 seconds; forcing it." -ForegroundColor Yellow
            try { Stop-Process -Id $process.Id -Force -ErrorAction Stop } catch { }
        }
    }
}

$restartStopped = $stoppedPaths.Count -gt 0
try {
Write-Host 'Building Release..' -ForegroundColor Cyan
dotnet build $Solution -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

Write-Host 'Running tests..' -ForegroundColor Cyan
$testResults = Join-Path ([IO.Path]::GetTempPath()) ("CyrFlip-tests-" + [Guid]::NewGuid().ToString('N'))
try {
    dotnet test $Solution -c Release --no-build --nologo --logger trx --results-directory $testResults
    if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
    # A run that discovered nothing also exits 0 (S0029 RB-6).
    & (Join-Path $PSScriptRoot 'tools\Assert-TestRun.ps1') -ResultsDirectory $testResults

    Write-Host 'Checking check placement..' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'tools\checks\Test-CheckPlacement.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Placement checks failed (exit $LASTEXITCODE)." }
}
finally { Remove-Item -LiteralPath $testResults -Recurse -Force -ErrorAction SilentlyContinue }

$ExePath = Join-Path $OutDir $ExeName
if (-not (Test-Path $ExePath)) { throw "Output not found: $ExePath" }

$version = (Get-Item $ExePath).VersionInfo.FileVersion
Write-Host "Built CyrFlip $version" -ForegroundColor Green

# Optional Authenticode signing. Set CYRFLIP_SIGN_PFX (path to .pfx) and CYRFLIP_SIGN_PASSWORD
# to sign locally; reduces antivirus heuristic false positives (IDP.Generic & friends).
# Without the env vars this is a no-op, so unsigned dev builds still work.
if ($env:CYRFLIP_SIGN_PFX -and (Test-Path $env:CYRFLIP_SIGN_PFX)) {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) {
        Write-Host 'signtool.exe not found (install the Windows 10/11 SDK).' -ForegroundColor Red
        exit 2
    }
    Write-Host 'Signing CyrFlip.exe..' -ForegroundColor Cyan
    & $signtool.FullName sign /f $env:CYRFLIP_SIGN_PFX /p $env:CYRFLIP_SIGN_PASSWORD `
        /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
        /d 'CyrFlip' /du 'https://github.com/SerZhyAle/CyrFlip' $ExePath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }
    & $signtool.FullName verify /pa $ExePath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool verify failed (exit $LASTEXITCODE)." }
    Write-Host 'Signed.' -ForegroundColor Green
} else {
    Write-Host 'Skipping code signing (set CYRFLIP_SIGN_PFX + CYRFLIP_SIGN_PASSWORD to enable).' -ForegroundColor DarkGray
}

# Stage the distributable (exe + default config.json).
New-Item -ItemType Directory -Path $SingleDir -Force | Out-Null
Copy-Item $ExePath (Join-Path $SingleDir $ExeName) -Force
$cfg = Join-Path $OutDir 'config.json'
if (Test-Path $cfg) { Copy-Item $cfg $SingleDir -Force }
Write-Host "Staged single-file build at: $SingleDir"

# Deploy to local sync folders.
foreach ($Destination in $Destinations) {
    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }
    Copy-Item (Join-Path $SingleDir $ExeName) (Join-Path $Destination $ExeName) -Force
    Write-Host "Deployed -> $Destination$ExeName"
}

if (-not $NoRun) {
    $DeployedExe = Join-Path $Destinations[0] $ExeName
    Write-Host "Starting deployed CyrFlip: $DeployedExe" -ForegroundColor Cyan
    Start-Process -FilePath $DeployedExe -WorkingDirectory (Split-Path $DeployedExe -Parent)
}
# Built, tested and deployed: from here the new copy is what runs (or, with -NoRun, deliberately nothing).
$restartStopped = $false
}
finally {
    if ($restartStopped) {
        # The previous copy, from where it ran - the deployed one is not overwritten before the tests pass.
        $previous = $stoppedPaths[0]
        if (Test-Path $previous) {
            Write-Host "Build did not finish - restarting the previous CyrFlip: $previous" -ForegroundColor Yellow
            try { Start-Process -FilePath $previous -WorkingDirectory (Split-Path $previous -Parent) }
            catch { Write-Host "Could not restart it: $($_.Exception.Message)" -ForegroundColor Yellow }
        }
    }
}

# Optional commit of the сборка. Always carries [skip ci] so the push to main does not
# spend GitHub minutes re-building what we just built+tested locally.
if ($Commit) {
    $msg = $Message
    if ($msg -notmatch '\[skip ci\]') { $msg = "$msg [skip ci]" }
    Write-Host "Committing build.." -ForegroundColor Cyan
    git add -A
    if ($LASTEXITCODE -ne 0) { throw "git add failed (exit $LASTEXITCODE)." }
    git commit -m $msg
    $committed = $LASTEXITCODE -eq 0
    if (-not $committed) {
        # "Nothing to commit" is not a failure of the сборка - the build and tests already passed.
        # Say so and carry on; anything else is a real git problem and still stops the script.
        if (@(git status --porcelain).Count -eq 0) {
            Write-Host 'Nothing to commit - the tree was already clean.' -ForegroundColor DarkGray
        }
        else { throw "git commit failed (exit $LASTEXITCODE)." }
    }
    else { Write-Host "Committed: $msg" -ForegroundColor Green }
    if ($Push -and $committed) {
        Write-Host "Pushing.." -ForegroundColor Cyan
        git push
        if ($LASTEXITCODE -ne 0) { throw "git push failed (exit $LASTEXITCODE)." }
        Write-Host "Pushed (CI skipped via [skip ci] - no GitHub minutes spent)." -ForegroundColor Green
    }
}

Write-Host "build ${version}: PASS" -ForegroundColor Green

