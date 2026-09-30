<#
    Builds an MSIX package for CyrFlip (Microsoft Store / sideload).

    What it does:
      1. Takes CyrFlip.exe from the released ZIP, or from an explicitly pre-built output (-NoBuild).
      2. Derives a Store-legal 4-part version (revision forced to 0) from the exe's YY.M.D.HHmm stamp.
      3. Stages the exe + config.json + THIRD-PARTY-NOTICES.md, generates the required logo PNGs from assets/icon-256.png.
      4. Fills the AppxManifest.xml placeholders (Identity Name / Publisher / version).
      5. Packs it into msix/dist/CyrFlip-<version>-x64.msix with makeappx.

    For the STORE you submit the UNSIGNED .msix - Microsoft re-signs it during certification, so
    you don't need a paid code-signing certificate. The identity defaults ARE the frozen Store anchors
    reserved in Partner Center (Product > Product identity): Name SZA.CyrFlip, Publisher
    CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD, display name SZA (S0038 RP-2). They are never changed; the
    script used to default to a test identity Partner Center rejects.

    For LOCAL testing, add -SelfSign: it creates a self-signed cert whose subject equals the Publisher,
    signs the package, and prints how to trust + install it. Add -TestIdentity as well to sideload beside
    an installed Store build (a separate package family, with its own data folder) - with the Store
    identity a self-signed package cannot install over the Store-signed one.

    Examples:
      # Store-ready package, from the ZIP the tag's release run published:
      .\build-msix.ps1 -ReleaseZip ..\CyrFlip-26.6.11.1700-windows-x64.zip -Version 26.6.11.1700

      # Local sideload test of the tag's own build, beside the Store install (self-signed):
      .\build-msix.ps1 -NoBuild -Version 26.6.11.1700 -SelfSign -TestIdentity
#>
[CmdletBinding()]
param(
    # Frozen anchors (hard invariant 1): the Store identity, never anything else by default.
    [string] $IdentityName        = 'SZA.CyrFlip',
    [string] $Publisher           = 'CN=F98ACEDB-1E22-4C39-AF63-F9FCFE807DCD',
    [string] $PublisherDisplayName= 'SZA',
    [string] $Configuration       = 'Release',
    [string] $ReleaseZip,
    [string] $Version,
    [switch] $NoBuild,
    [switch] $SelfSign,
    # A local test identity, for a sideload beside the Store build - never for Partner Center.
    [switch] $TestIdentity
)
$ErrorActionPreference = 'Stop'

if ($TestIdentity) {
    if (-not $SelfSign) { throw '-TestIdentity is for a self-signed local sideload only - add -SelfSign.' }
    $IdentityName = 'SerZhyAle.CyrFlip.Test'
    $Publisher = 'CN=SerZhyAle'
    $PublisherDisplayName = 'SerZhyAle (test)'
}

$MsixDir   = $PSScriptRoot
$RepoRoot  = Split-Path $MsixDir -Parent
$Csproj    = Join-Path $RepoRoot 'src\CyrFlip\CyrFlip.csproj'
$OutDir    = Join-Path $RepoRoot "src\CyrFlip\bin\$Configuration\net48"
$IconPng   = Join-Path $RepoRoot 'assets\icon-256.png'
$Stage     = Join-Path $MsixDir 'stage'
$Dist      = Join-Path $MsixDir 'dist'
$Manifest  = Join-Path $MsixDir 'AppxManifest.xml'
$ExtractedRelease = $null

function Find-SdkTool([string] $name) {
    $tool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK)." }
    return $tool.FullName
}

# --- 1. Select the released payload ----------------------------------------
if (-not $Version -or $Version -notmatch '^\d{2}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])\.\d{4}$') {
    throw 'Pass -Version YY.M.D.HHmm from the release tag.'
}
if ($ReleaseZip -and $NoBuild) { throw 'Use either -ReleaseZip or -NoBuild, not both.' }
if ($ReleaseZip) {
    if (-not (Test-Path $ReleaseZip)) { throw "Release ZIP not found: $ReleaseZip" }
    $ExtractedRelease = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-msix-' + [Guid]::NewGuid().ToString('N'))
    Expand-Archive -Path $ReleaseZip -DestinationPath $ExtractedRelease -Force
    $Exe = @(Get-ChildItem $ExtractedRelease -Recurse -File -Filter 'CyrFlip.exe')
    if ($Exe.Count -ne 1) { throw "Release ZIP must contain exactly one CyrFlip.exe (found $($Exe.Count))." }
    $Exe = $Exe[0].FullName
    $PayloadFolder = Split-Path $Exe -Parent
}
elseif ($NoBuild) {
    $Exe = Join-Path $OutDir 'CyrFlip.exe'
    if (-not (Test-Path $Exe)) {
        throw "CyrFlip.exe not found at $Exe. Check out tag v$Version into a clean tree and run " +
            "'dotnet build CyrFlip.sln -c $Configuration -p:Version=$Version' first - the preflight's own build " +
            "lives in a temporary worktree that is gone once release.ps1 finishes."
    }
    $PayloadFolder = $OutDir
}
else {
    throw 'Refusing to build an unverified working tree for the Store. Pass -ReleaseZip, or -NoBuild with a build of the tag''s own commit.'
}

# --- 2. Store-legal version (revision must be 0) ----------------------------
# Exe is stamped YY.M.D.HHmm. Map to Major.Minor.Build.0 within the 0..65535 per-part limit:
#   Major = YY, Minor = M*100+D, Build = HHmm, Revision = 0  (monotonic over time, unique per minute).
$versionInfo = (Get-Item $Exe).VersionInfo
$fileVer = $versionInfo.FileVersion
if ([version]$fileVer -ne [version]$Version) {
    throw "Payload FileVersion '$fileVer' does not match release tag version '$Version'."
}
# Which commit the exe was built from, not only which version it says (S0038 RP-3; release.yml checks
# the same): a Release build of any tree passes the FileVersion gate, and would go to the Store as code
# no tag names.
$tagCommit = (git -C $RepoRoot rev-parse --verify --quiet "v$Version^{commit}")
if ($LASTEXITCODE -ne 0 -or -not $tagCommit) { throw "Tag v$Version is not known here - run 'git fetch --tags' first." }
$wantProduct = "$Version+$($tagCommit.Trim())"
if ($versionInfo.ProductVersion -ne $wantProduct) {
    throw "Payload ProductVersion '$($versionInfo.ProductVersion)' is not '$wantProduct' - it was not built from tag v$Version."
}
$p = $fileVer.Split('.')
if ($p.Count -lt 4) { throw "Unexpected exe version '$fileVer' (want YY.M.D.HHmm)." }
$yy = [int]$p[0]; $m = [int]$p[1]; $d = [int]$p[2]; $hhmm = [int]$p[3]
$MsixVersion = "$yy.$($m*100+$d).$hhmm.0"
Write-Host "Exe version $fileVer  ->  MSIX version $MsixVersion" -ForegroundColor Green

# --- 3. Stage payload -------------------------------------------------------
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Stage 'Assets') -Force | Out-Null
Copy-Item $Exe $Stage -Force
$cfg = Join-Path $PayloadFolder 'config.json'
if (Test-Path $cfg) { Copy-Item $cfg $Stage -Force }
# Apache-2.0 notice for the vendored Material Icons glyphs (ICON-EXTERNAL rule 5): the copy inside the
# release ZIP when building from one, else the repository's own. Missing = a failed build.
$notices = Join-Path $PayloadFolder 'THIRD-PARTY-NOTICES.md'
if (-not (Test-Path $notices)) { $notices = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md' }
if (-not (Test-Path $notices)) { throw 'THIRD-PARTY-NOTICES.md not found in the release payload or the repository root.' }
Copy-Item $notices $Stage -Force
if ($ExtractedRelease) {
    try { Remove-Item $ExtractedRelease -Recurse -Force -ErrorAction Stop } catch { Write-Warning "Could not remove temporary release extraction: $ExtractedRelease" }
    $ExtractedRelease = $null
}

# Generate the logo PNGs from the 256px master.
if (-not (Test-Path $IconPng)) { throw "Icon master not found: $IconPng" }
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($IconPng)
try {
    $logos = @{
        'Square44x44Logo.png'   = 44
        'StoreLogo.png'         = 50
        'Square71x71Logo.png'   = 71
        'Square150x150Logo.png' = 150
    }
    foreach ($kv in $logos.GetEnumerator()) {
        $size = $kv.Value
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($src, 0, 0, $size, $size)
        $bmp.Save((Join-Path $Stage "Assets\$($kv.Key)"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    }
}
finally { $src.Dispose() }
Write-Host 'Generated logo assets.'

# --- 4. Fill the manifest placeholders --------------------------------------
$xml = Get-Content $Manifest -Raw
$xml = $xml.Replace('__IDENTITY_NAME__',     $IdentityName)
$xml = $xml.Replace('__PUBLISHER__',         $Publisher)
$xml = $xml.Replace('__PUBLISHER_DISPLAY__', $PublisherDisplayName)
$xml = $xml.Replace('__VERSION__',           $MsixVersion)
# AppxManifest.xml must be at the package root.
Set-Content -Path (Join-Path $Stage 'AppxManifest.xml') -Value $xml -Encoding UTF8

# --- 5. Pack ----------------------------------------------------------------
New-Item -ItemType Directory -Path $Dist -Force | Out-Null
$MsixPath = Join-Path $Dist "CyrFlip-$MsixVersion-x64.msix"
$makeappx = Find-SdkTool 'makeappx.exe'
& $makeappx pack /d $Stage /p $MsixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit $LASTEXITCODE)." }
Write-Host "Packed: $MsixPath" -ForegroundColor Green

# --- 6. Optional self-sign for local sideload testing -----------------------
if ($SelfSign) {
    Write-Host 'Self-signing for local testing..' -ForegroundColor Cyan
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
            -KeyUsage DigitalSignature -FriendlyName 'CyrFlip MSIX test cert' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host "Created test cert: $($cert.Thumbprint)"
    }
    $signtool = Find-SdkTool 'signtool.exe'
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $MsixPath
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }

    $cer = Join-Path $Dist 'CyrFlip-test-cert.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Write-Host ''
    Write-Host 'Signed. To install locally, trust the cert once (RUN AS ADMIN):' -ForegroundColor Yellow
    Write-Host "  Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host 'Then install the package:'
    Write-Host "  Add-AppxPackage `"$MsixPath`""
}
else {
    Write-Host ''
    Write-Host 'Unsigned package ready for the Store (Microsoft re-signs on certification).' -ForegroundColor Yellow
    Write-Host 'For a LOCAL test build instead, re-run with -SelfSign.'
}
