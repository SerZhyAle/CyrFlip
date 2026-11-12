<#
    Invoke-StoreChannelPreflight.ps1 - the read-only Store check a release makes BEFORE the tag.

    This is NOT a wrapper around msstore. The check is one shared implementation, the canon's
    tools\Store-Preflight.ps1 (the sza plugin; WINDOWS-STORE 0.4 section D). This script only
      1. finds that script (CLAUDE_PLUGIN_ROOT, else the newest copy in the plugin cache),
      2. hands it the product name, this repository's msix\identity.json (the Store ID is read from there,
         rule 19) and the MSIX version the release is about to build (the same remap build-msix.ps1 makes),
      3. maps its answer to the release's own exit codes, and
      4. appends one evidence line per run to -EvidenceFile.
    The shared script can issue exactly one Store operation, `msstore submission get <Store ID>`, and refuses
    every other verb itself. Nothing here writes to the Store, runs another msstore verb, touches a submission
    made in the console, tags, releases or uploads (hard limits of ticket S0100).

    Exit codes:
      0  the checks passed: nothing pending, price Free, the MSIX version is newer than every live package.
      1  the channel refuses this release (a submission is pending, the version is not newer, the price is not
         Free, a missing or unusable identity file, ..): the release stops BEFORE the tag, with the reason.
      2  the channel is unavailable (CLI missing or unconfigured, token refused, recorded credential expired,
         the shared script not installed or broken): one line says the Store step is manual and the release
         goes on exactly as without the channel (rule 21). The channel never fails a release after the tag.

    The recorded credential expiry date (rule 21, a date, never a secret) comes from -CredentialExpiry, else from
    the environment variable CYRFLIP_STORE_CREDENTIAL_EXPIRY; none = the expiry is not checked here.

    The evidence line holds only values this script itself sets - time, product, Store ID, MSIX version, mode
    (ready / refused / manual) and the exit code. Nothing the CLI or the shared script printed is copied into
    it, so no account identifier, credential or upload address can reach the record (rule 26).
#>
[CmdletBinding()]
param(
    # The release version, YY.M.D.HHmm (the tag without the v).
    [string] $Version,
    [string] $IdentityFile,
    [string] $ProductName = 'CyrFlip',
    # Test seam: use this copy of the shared script instead of discovering one.
    [string] $PreflightScript,
    # One JSON line is appended here per run; empty = no record.
    [string] $EvidenceFile,
    [string] $CredentialExpiry
)

# The remap of build-msix.ps1 section 2: YY.M.D.HHmm -> YY.(M*100+D).HHmm.0. The two must agree; the
# placement gate (tools\checks\Test-StoreChannelPreflight.ps1) pins the formula line in both files.
function Get-MsixVersion([string] $TagVersion) {
    $p = $TagVersion.Split('.')
    $yy = [int]$p[0]; $m = [int]$p[1]; $d = [int]$p[2]; $hhmm = [int]$p[3]
    return "$yy.$($m*100+$d).$hhmm.0"
}

# Where the shared script is: the plugin's own root when this runs inside it, else the newest cached copy.
function Find-SharedPreflight {
    if ($env:CLAUDE_PLUGIN_ROOT) {
        $own = Join-Path (Join-Path $env:CLAUDE_PLUGIN_ROOT 'tools') 'Store-Preflight.ps1'
        if (Test-Path -LiteralPath $own) { return $own }
    }
    $cache = Join-Path (Join-Path (Join-Path (Join-Path (Join-Path $HOME '.claude') 'plugins') 'cache') 'sza-unified-rules') 'sza'
    if (-not (Test-Path -LiteralPath $cache)) { return $null }
    $newest = @(Get-ChildItem -LiteralPath $cache -Directory -ErrorAction SilentlyContinue | Sort-Object -Descending -Property @{
        Expression = { $v = $null; if ([version]::TryParse($_.Name, [ref]$v)) { $v } else { [version]'0.0' } }
    }, Name)
    foreach ($dir in $newest) {
        $candidate = Join-Path (Join-Path $dir.FullName 'tools') 'Store-Preflight.ps1'
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Invoke-StoreChannelPreflight {
    param([string] $TagVersion, [string] $Identity, [string] $Product, [string] $Shared, [string] $Evidence, [string] $Expiry)

    if ($TagVersion -notmatch '^\d{2}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])\.\d{4}$') {
        Write-Host "Store channel preflight: '$TagVersion' is not the YY.M.D.HHmm shape."
        return 1
    }
    if (-not $Identity) { $Identity = Join-Path $PSScriptRoot 'identity.json' }
    $msixVersion = Get-MsixVersion $TagVersion
    if (-not $Expiry) { $Expiry = $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY }

    # The Store ID is public and is the one product fact rule 26 names; it is read here for the record only.
    $storeId = $null
    try { $storeId = [string](Get-Content -LiteralPath $Identity -Raw | ConvertFrom-Json).StoreId } catch { }

    $manualLine = $null
    $code = 2
    $lines = @()
    if (-not $Shared) { $Shared = Find-SharedPreflight }
    if (-not $Shared -or -not (Test-Path -LiteralPath $Shared)) {
        $manualLine = "${Product}: Store step is manual (the shared Store preflight is not installed)."
    }
    else {
        $sharedArgs = @{ ProductName = $Product; IdentityFile = $Identity; PackageVersion = $msixVersion }
        if ($Expiry) { $sharedArgs.CredentialExpiry = $Expiry }
        # The shared script sets no preference of its own: under 'Stop' a line msstore writes to stderr would
        # turn into a terminating error and read as "channel unavailable". It runs as it does standalone.
        $savedPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $global:LASTEXITCODE = 0
            $lines = @(& $Shared @sharedArgs 2>&1 | ForEach-Object { "$_" })
            $code = [int]$global:LASTEXITCODE
        }
        catch {
            $manualLine = "${Product}: Store step is manual (the shared Store preflight failed to run)."
            $code = 2
        }
        finally { $ErrorActionPreference = $savedPreference }
        if (-not $manualLine -and $code -ne 0 -and $code -ne 1 -and $code -ne 2) {
            $manualLine = "${Product}: Store step is manual (the shared Store preflight ended with an unexpected exit)."
            $code = 2
        }
        if (-not $manualLine -and $code -eq 2 -and @($lines | Where-Object { $_ -match 'Store step is manual' }).Count -eq 0) {
            $manualLine = "${Product}: Store step is manual (channel unavailable)."
        }
    }

    # What the shared script said is the reason; for a manual outcome it is already the one line.
    if ($manualLine) { Write-Host $manualLine }
    else { foreach ($line in $lines) { if ($line.Trim()) { Write-Host $line } } }

    if ($Evidence) {
        $mode = if ($code -eq 0) { 'ready' } elseif ($code -eq 1) { 'refused' } else { 'manual' }
        $record = [ordered]@{
            checkedAt   = (Get-Date).ToUniversalTime().ToString('o')
            product     = $Product
            storeId     = $storeId
            msixVersion = $msixVersion
            mode        = $mode
            exit        = $code
        }
        try {
            $folder = Split-Path -Parent $Evidence
            if ($folder -and -not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
            [IO.File]::AppendAllText($Evidence, ((ConvertTo-Json $record -Compress) + "`n"), (New-Object Text.UTF8Encoding($false)))
        }
        catch { Write-Host "Store channel preflight: the evidence line could not be written ($($_.Exception.GetType().Name))." }
    }
    return $code
}

if ($MyInvocation.InvocationName -ne '.') {
    exit (Invoke-StoreChannelPreflight -TagVersion $Version -Identity $IdentityFile -Product $ProductName `
            -Shared $PreflightScript -Evidence $EvidenceFile -Expiry $CredentialExpiry)
}
