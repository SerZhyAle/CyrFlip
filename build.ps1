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

Write-Host 'Building Release...' -ForegroundColor Cyan
dotnet build $Solution -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

Write-Host 'Running tests...' -ForegroundColor Cyan
dotnet test $Solution -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }

$ExePath = Join-Path $OutDir $ExeName
if (-not (Test-Path $ExePath)) { throw "Output not found: $ExePath" }

$version = (Get-Item $ExePath).VersionInfo.FileVersion
Write-Host "Built CyrFlip $version" -ForegroundColor Green

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
