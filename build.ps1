<#
    CyrFlip local build + deploy  ==  a "СБОРКА" (build) in this project's vocabulary.

    A СБОРКА is fully LOCAL and costs no GitHub Actions minutes:
      build Release -> run tests -> stage the single self-contained exe -> deploy to sync folders.
    net48 needs no bundled runtime. Mirrors the convention across SerZhyAle's Windows apps.

    Optionally it also commits the result (the "если удачно - коммитим" half of a сборка):
      -Commit            git add -A + commit AFTER a green build/test
      -Message "<text>"  commit message (required with -Commit)
      -Push              also push the branch to origin

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
# fast test loop, so stop the single-instance tray app first and start the fresh output afterwards.
# CyrFlip deliberately has a unique process name, therefore this also covers a copy launched from
# one of the local sync folders.
$running = @(Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host 'Stopping running CyrFlip..' -ForegroundColor Cyan
    $running | Stop-Process -Force
    foreach ($process in $running) {
        try { $process.WaitForExit(5000) | Out-Null } catch { }
    }
}

Write-Host 'Building Release..' -ForegroundColor Cyan
dotnet build $Solution -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

Write-Host 'Running tests..' -ForegroundColor Cyan
dotnet test $Solution -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }

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
    if (-not $signtool) { throw 'signtool.exe not found (install the Windows 10/11 SDK).' }
    Write-Host 'Signing CyrFlip.exe..' -ForegroundColor Cyan
    & $signtool.FullName sign /f $env:CYRFLIP_SIGN_PFX /p $env:CYRFLIP_SIGN_PASSWORD `
        /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
        /d 'CyrFlip' /du 'https://github.com/SerZhyAle/CyrFlip' $ExePath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }
    & $signtool.FullName verify /pa $ExePath | Out-Null
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
    Write-Host "Starting fresh build: $ExePath" -ForegroundColor Cyan
    Start-Process -FilePath $ExePath -WorkingDirectory $OutDir
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
    if ($LASTEXITCODE -ne 0) { throw "git commit failed (exit $LASTEXITCODE) - nothing to commit?" }
    Write-Host "Committed: $msg" -ForegroundColor Green
    if ($Push) {
        Write-Host "Pushing.." -ForegroundColor Cyan
        git push
        if ($LASTEXITCODE -ne 0) { throw "git push failed (exit $LASTEXITCODE)." }
        Write-Host "Pushed (CI skipped via [skip ci] - no GitHub minutes spent)." -ForegroundColor Green
    }
}
