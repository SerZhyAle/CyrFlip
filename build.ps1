<#
    CyrFlip local build + deploy.

    Builds Release, runs tests, and stages the single self-contained exe (net48 needs no
    bundled runtime). Then copies it to the local sync folders. Mirrors the convention used
    across SerZhyAle's Windows apps.
#>
$ErrorActionPreference = 'Stop'

$SolutionDir = $PSScriptRoot
$Solution    = Join-Path $SolutionDir 'CyrFlip.sln'
$OutDir      = Join-Path $SolutionDir 'src\CyrFlip\bin\Release\net48'
$ExeName     = 'CyrFlip.exe'
$SingleDir   = Join-Path $SolutionDir 'bin\SingleFile'
$Destinations = @(
    'C:\GD\i\',
    'C:\GD\tc\SZA\_APP\'
)

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
