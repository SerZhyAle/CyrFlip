<#
    Builds an MSIX package for CyrFlip (Microsoft Store / sideload).

    What it does:
      1. Builds Release (unless -NoBuild) and locates CyrFlip.exe.
      2. Derives a Store-legal 4-part version (revision forced to 0) from the exe's YY.M.D.HHmm stamp.
      3. Stages the exe + config.json, generates the required logo PNGs from assets/icon-256.png.
      4. Fills the AppxManifest.xml placeholders (Identity Name / Publisher / version).
      5. Packs it into msix/dist/CyrFlip-<version>-x64.msix with makeappx.

    For the STORE you submit the UNSIGNED .msix — Microsoft re-signs it during certification, so
    you don't need a paid code-signing certificate. Set -Publisher / -IdentityName / -PublisherDisplayName
    to the exact values reserved for you in Partner Center (Product ▸ Product identity).

    For LOCAL testing, add -SelfSign: it creates a self-signed cert whose subject equals -Publisher,
    signs the package, and prints how to trust + install it. (Self-signing requires the manifest
    Publisher to match the cert subject — keep them equal.)

    Examples:
      # Store-ready package (fill these from Partner Center):
      .\build-msix.ps1 -IdentityName "1234SerZhyAle.CyrFlip" -Publisher "CN=ABCD1234-..." -PublisherDisplayName "SerZhyAle"

      # Local sideload test (self-signed):
      .\build-msix.ps1 -SelfSign
#>
[CmdletBinding()]
param(
    [string] $IdentityName        = 'SerZhyAle.CyrFlip',
    [string] $Publisher           = 'CN=SerZhyAle',
    [string] $PublisherDisplayName= 'SerZhyAle',
    [string] $Configuration       = 'Release',
    [switch] $NoBuild,
    [switch] $SelfSign
)
$ErrorActionPreference = 'Stop'

$MsixDir   = $PSScriptRoot
$RepoRoot  = Split-Path $MsixDir -Parent
$Csproj    = Join-Path $RepoRoot 'src\CyrFlip\CyrFlip.csproj'
$OutDir    = Join-Path $RepoRoot "src\CyrFlip\bin\$Configuration\net48"
$IconPng   = Join-Path $RepoRoot 'assets\icon-256.png'
$Stage     = Join-Path $MsixDir 'stage'
$Dist      = Join-Path $MsixDir 'dist'
$Manifest  = Join-Path $MsixDir 'AppxManifest.xml'

function Find-SdkTool([string] $name) {
    $tool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows 10/11 SDK (winget install Microsoft.WindowsSDK)." }
    return $tool.FullName
}

# --- 1. Build ---------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host 'Building Release...' -ForegroundColor Cyan
    dotnet build $Csproj -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}
$Exe = Join-Path $OutDir 'CyrFlip.exe'
if (-not (Test-Path $Exe)) { throw "CyrFlip.exe not found at $Exe (build first, or drop -NoBuild)." }

# --- 2. Store-legal version (revision must be 0) ----------------------------
# Exe is stamped YY.M.D.HHmm. Map to Major.Minor.Build.0 within the 0..65535 per-part limit:
#   Major = YY, Minor = M*100+D, Build = HHmm, Revision = 0  (monotonic over time, unique per minute).
$fileVer = (Get-Item $Exe).VersionInfo.FileVersion
$p = $fileVer.Split('.')
if ($p.Count -lt 4) { throw "Unexpected exe version '$fileVer' (want YY.M.D.HHmm)." }
$yy = [int]$p[0]; $m = [int]$p[1]; $d = [int]$p[2]; $hhmm = [int]$p[3]
$MsixVersion = "$yy.$($m*100+$d).$hhmm.0"
Write-Host "Exe version $fileVer  ->  MSIX version $MsixVersion" -ForegroundColor Green

# --- 3. Stage payload -------------------------------------------------------
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Stage 'Assets') -Force | Out-Null
Copy-Item $Exe $Stage -Force
$cfg = Join-Path $OutDir 'config.json'
if (Test-Path $cfg) { Copy-Item $cfg $Stage -Force }

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
    Write-Host 'Self-signing for local testing...' -ForegroundColor Cyan
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
