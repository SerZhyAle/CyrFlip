<#
    Test-StoreChannelPreflight.ps1 - fast, offline checks of the release's Store channel preflight (ticket S0100).

    Runs msix\Invoke-StoreChannelPreflight.ps1 in-process, with no network, no credential and no real msstore:
      A. against a stand-in for the shared canon script: the arguments it receives, the exit-code mapping
         (0 ready, 1 refused, 2 manual - also for a crash, an odd exit code and a missing script), one line
         for a manual outcome, one evidence line per run holding only values the caller itself sets, the
         locator (CLAUDE_PLUGIN_ROOT first), the MSIX version remap;
      B. against the REAL shared script (found the way a release finds it) with a function named msstore as
         the Store: published and free, pending, version not newer, price not Free, unconfigured CLI, no CLI,
         an expired recorded credential - and every call the stub saw is `submission get <Store ID>`, nothing else;
      C. static: the remap formula is the same line in build-msix.ps1, release.ps1 calls the preflight BEFORE
         the tag and only a refusal fails it, and the caller has no way to run an msstore verb of its own.

    Exit codes (CHECK-VERDICT): 0 pass; 1 a case failed; 2 could not verify (the shared script is not installed,
    so part B did not run). Target: well under 5 s.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
Import-Module (Join-Path $ScriptDir 'CheckVerdict.psm1') -Force
Set-CheckSubject 'store-channel-preflight'

$caller = Join-Path $RepoRoot 'msix\Invoke-StoreChannelPreflight.ps1'
$repoIdentity = Join-Path $RepoRoot 'msix\identity.json'
$clock = [Diagnostics.Stopwatch]::StartNew()
$script:Cases = 0

function Assert-Case([string] $Name, [bool] $Condition, [string] $Detail = '') {
    $script:Cases++
    if (-not $Condition) { Add-CheckFinding -Severity fail -Name $Name -Reason $Detail }
}

if (-not (Test-Path -LiteralPath $caller)) {
    Add-CheckFinding -Severity fail -Name 'caller-missing' -Reason 'msix\Invoke-StoreChannelPreflight.ps1 does not exist.'
    Complete-Check
}

# The caller's pure functions, taken without running its main block (dot-sourcing in a child scope keeps the
# caller's parameters out of this script).
$findShared = & { . $caller; ${function:Find-SharedPreflight} }
$msixOf = & { . $caller; ${function:Get-MsixVersion} }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-storepf-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$savedEnv = @{
    PluginRoot = $env:CLAUDE_PLUGIN_ROOT
    Expiry     = $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY
    Path       = $env:PATH
}

function Invoke-Caller([hashtable] $Params) {
    $global:LASTEXITCODE = 0
    $out = @(& $caller @Params 6>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{ Code = [int]$global:LASTEXITCODE; Lines = @($out | Where-Object { $_.Trim() }) }
}

function New-Fixture([string] $Name, [string] $Body) {
    $dir = Join-Path $tmp $Name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $header = @'
[CmdletBinding()]
param([string]$StoreId, [Parameter(Mandatory)][string]$ProductName, [string]$PackageVersion, [string]$Package,
      [string]$IdentityFile, [string[]]$ChannelSubmissionIds = @(), [string]$CredentialExpiry, [string]$ListingPath, [switch]$Json)
$seen = @{}; foreach ($k in $PSBoundParameters.Keys) { $seen[$k] = $PSBoundParameters[$k] }
$seen | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'args.json')
'@
    $path = Join-Path $dir 'Store-Preflight.ps1'
    Set-Content -LiteralPath $path -Value ($header + "`n" + $Body) -Encoding UTF8
    return $path
}

function Read-Evidence([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}

try {
    $fixtureIdentity = Join-Path $tmp 'identity.json'
    Set-Content -LiteralPath $fixtureIdentity -Value '{"StoreId":"ABCDEFGHIJKL"}' -Encoding UTF8
    $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY = $null
    $evidenceKeys = 'checkedAt,exit,mode,msixVersion,product,storeId'

    # ---- A. the caller against a stand-in for the shared script -------------------------------------------
    $ok = New-Fixture 'ok' 'Write-Output "CyrFlip: fixture passed."; exit 0'
    $evidence = Join-Path $tmp 'evidence.jsonl'
    $r = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $ok; EvidenceFile = $evidence }
    Assert-Case 'A1-exit-ready' ($r.Code -eq 0) "expected exit 0, got $($r.Code)"
    $seen = Get-Content -LiteralPath (Join-Path (Split-Path $ok) 'args.json') -Raw | ConvertFrom-Json
    Assert-Case 'A1-msix-version' ($seen.PackageVersion -eq '26.1112.1000.0') "PackageVersion was '$($seen.PackageVersion)'"
    Assert-Case 'A1-identity-passed' ($seen.IdentityFile -eq $fixtureIdentity) 'the identity file was not handed to the shared script'
    Assert-Case 'A1-name-passed' ($seen.ProductName -eq 'CyrFlip') 'the product name was not handed over'
    Assert-Case 'A1-no-store-id-arg' (-not ($seen.PSObject.Properties.Name -contains 'StoreId')) 'the Store ID must come from the identity file only'
    Assert-Case 'A1-no-json-no-package' (-not ($seen.PSObject.Properties.Name -contains 'Json') -and -not ($seen.PSObject.Properties.Name -contains 'Package')) 'unexpected switches reached the shared script'
    $rec = @(Read-Evidence $evidence)
    Assert-Case 'A1-one-evidence-line' ($rec.Count -eq 1) "evidence lines: $($rec.Count)"
    if ($rec.Count -eq 1) {
        Assert-Case 'A1-evidence-keys' ((($rec[0].PSObject.Properties.Name | Sort-Object) -join ',') -eq $evidenceKeys) "keys: $(($rec[0].PSObject.Properties.Name | Sort-Object) -join ',')"
        Assert-Case 'A1-evidence-values' ($rec[0].mode -eq 'ready' -and $rec[0].exit -eq 0 -and $rec[0].storeId -eq 'ABCDEFGHIJKL' -and $rec[0].msixVersion -eq '26.1112.1000.0' -and $rec[0].product -eq 'CyrFlip') 'evidence values differ from the run'
        Assert-Case 'A1-evidence-no-cli-text' (-not ((Get-Content -LiteralPath $evidence -Raw) -match 'fixture')) 'text from the shared script reached the record'
    }

    $refused = New-Fixture 'refused' 'Write-Output "CyrFlip: pending or non-published submission; stop."; exit 1'
    $r = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $refused; EvidenceFile = $evidence }
    Assert-Case 'A2-exit-refused' ($r.Code -eq 1) "expected exit 1, got $($r.Code)"
    Assert-Case 'A2-reason-printed' (@($r.Lines | Where-Object { $_ -match 'pending' }).Count -eq 1) 'the refusal reason was not printed'

    $manual = New-Fixture 'manual' 'Write-Output "CyrFlip: Store step is manual (channel unavailable)."; exit 2'
    $r = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $manual; EvidenceFile = $evidence }
    Assert-Case 'A3-exit-manual' ($r.Code -eq 2) "expected exit 2, got $($r.Code)"
    Assert-Case 'A3-one-line' ($r.Lines.Count -eq 1 -and $r.Lines[0] -match 'Store step is manual') "lines: $($r.Lines -join ' | ')"

    $crash = New-Fixture 'crash' "throw 'boom'"
    $r = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $crash; EvidenceFile = $evidence }
    Assert-Case 'A4-crash-is-manual' ($r.Code -eq 2 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -match 'Store step is manual') "code $($r.Code), lines: $($r.Lines -join ' | ')"
    Assert-Case 'A4-crash-text-not-printed' (-not (($r.Lines -join ' ') -match 'boom')) 'an exception text reached the console line'

    $odd = New-Fixture 'odd' 'exit 7'
    $r = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $odd; EvidenceFile = $evidence }
    Assert-Case 'A5-odd-exit-is-manual' ($r.Code -eq 2 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -match 'Store step is manual') "code $($r.Code), lines: $($r.Lines -join ' | ')"

    $r = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = (Join-Path $tmp 'nowhere\Store-Preflight.ps1'); EvidenceFile = $evidence }
    Assert-Case 'A6-missing-script-is-manual' ($r.Code -eq 2 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -match 'Store step is manual') "code $($r.Code), lines: $($r.Lines -join ' | ')"

    $modes = @(Read-Evidence $evidence | ForEach-Object { "$($_.mode):$($_.exit)" }) -join ','
    Assert-Case 'A7-one-line-per-run' ($modes -eq 'ready:0,refused:1,manual:2,manual:2,manual:2,manual:2') "evidence modes: $modes"

    $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY = '2031-02-03'
    $null = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $ok }
    $seen = Get-Content -LiteralPath (Join-Path (Split-Path $ok) 'args.json') -Raw | ConvertFrom-Json
    Assert-Case 'A8-expiry-from-env' ($seen.CredentialExpiry -eq '2031-02-03') "CredentialExpiry was '$($seen.CredentialExpiry)'"
    $null = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $ok; CredentialExpiry = '2032-04-05' }
    $seen = Get-Content -LiteralPath (Join-Path (Split-Path $ok) 'args.json') -Raw | ConvertFrom-Json
    Assert-Case 'A8-expiry-parameter-wins' ($seen.CredentialExpiry -eq '2032-04-05') "CredentialExpiry was '$($seen.CredentialExpiry)'"
    $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY = $null
    $null = Invoke-Caller @{ Version = '26.11.12.1000'; IdentityFile = $fixtureIdentity; PreflightScript = $ok }
    $seen = Get-Content -LiteralPath (Join-Path (Split-Path $ok) 'args.json') -Raw | ConvertFrom-Json
    Assert-Case 'A8-no-expiry-no-argument' (-not ($seen.PSObject.Properties.Name -contains 'CredentialExpiry')) 'an expiry was passed although none is recorded'

    $r = Invoke-Caller @{ Version = '26.13.40.9999'; IdentityFile = $fixtureIdentity; PreflightScript = $ok; EvidenceFile = (Join-Path $tmp 'never.jsonl') }
    Assert-Case 'A9-bad-version' ($r.Code -eq 1 -and -not (Test-Path -LiteralPath (Join-Path $tmp 'never.jsonl'))) "code $($r.Code); a malformed version must stop before anything is asked or recorded"

    Assert-Case 'A10-remap' ((& $msixOf '26.10.21.1750') -eq '26.1021.1750.0' -and (& $msixOf '26.1.5.0905') -eq '26.105.905.0' -and (& $msixOf '26.12.31.2359') -eq '26.1231.2359.0') 'the MSIX version remap changed'

    $root = Join-Path $tmp 'plugin-root'
    New-Item -ItemType Directory -Path (Join-Path $root 'tools') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'tools\Store-Preflight.ps1') -Value 'exit 0' -Encoding UTF8
    $env:CLAUDE_PLUGIN_ROOT = $root
    Assert-Case 'A11-locator-plugin-root-first' ((& $findShared) -eq (Join-Path $root 'tools\Store-Preflight.ps1')) 'CLAUDE_PLUGIN_ROOT was not preferred'
    $env:CLAUDE_PLUGIN_ROOT = (Join-Path $tmp 'no-such-root')
    Assert-Case 'A11-locator-falls-through' ((& $findShared) -ne (Join-Path $tmp 'no-such-root\tools\Store-Preflight.ps1')) 'a plugin root without the script was returned'
    $env:CLAUDE_PLUGIN_ROOT = $null

    # ---- B. the real shared script, a function named msstore as the Store ---------------------------------
    $shared = & $findShared
    $unverifiedB = $false
    if (-not $shared) {
        $unverifiedB = $true
        Add-CheckFinding -Severity notverified -Name 'B-shared-script-not-found' -Reason 'The sza plugin (tools\Store-Preflight.ps1) is not installed here, so the cases against the real shared script did not run.'
    }
    else {
        $global:StubMode = ''
        $global:StubJson = ''
        $global:StubCalls = New-Object System.Collections.ArrayList
        function global:msstore {
            [void]$global:StubCalls.Add(($args -join ' '))
            $global:LASTEXITCODE = 0
            switch ($global:StubMode) {
                'published' { Write-Output 'Could not find a pending submission, found the last published submission.'; Write-Output $global:StubJson }
                'pending'   { Write-Output $global:StubJson }
                'unconfigured' { Write-Output 'Error: SellerId is not set. Run reconfigure.'; $global:LASTEXITCODE = 1 }
                default     { Write-Output 'stub: unexpected mode'; $global:LASTEXITCODE = 1 }
            }
        }
        function New-StubJson([string] $Status, [string] $Price, [string] $LiveVersion) {
            $o = [ordered]@{
                Id = '1152921504000000000'; FriendlyName = 'Submission 15'; Status = $Status
                Pricing = [ordered]@{ PriceId = $Price }
                ApplicationPackages = @([ordered]@{ FileName = 'CyrFlip.msix'; Version = $LiveVersion; Architecture = 'X64'; MandatoryUpdate = $false })
            }
            # The CLI wraps its JSON at console width, inside a value too; the shared script must remove it.
            return (ConvertTo-Json $o -Depth 5 -Compress).Replace('CyrFlip.msix', "CyrFl`r`nip.msix")
        }
        if ((Get-Command msstore).CommandType -ne 'Function') { throw 'The msstore stub is not what a call would reach.' }
        $evidenceB = Join-Path $tmp 'evidence-b.jsonl'
        $run = { param($Tag) Invoke-Caller @{ Version = $Tag; IdentityFile = $repoIdentity; EvidenceFile = $evidenceB } }

        $global:StubMode = 'published'; $global:StubJson = New-StubJson 'Published' 'Free' '26.1021.1750.0'
        $global:StubCalls.Clear()
        $r = & $run '26.11.12.1000'
        Assert-Case 'B1-ready' ($r.Code -eq 0) "expected exit 0, got $($r.Code): $($r.Lines -join ' | ')"
        Assert-Case 'B1-one-store-call' ($global:StubCalls.Count -eq 1 -and $global:StubCalls[0] -eq 'submission get 9NB4W41NGQJ4') "calls: $($global:StubCalls -join ' | ')"

        $global:StubMode = 'pending'; $global:StubJson = New-StubJson 'PendingCommit' 'Free' '26.1021.1750.0'
        $r = & $run '26.11.12.1000'
        Assert-Case 'B2-pending-refused' ($r.Code -eq 1 -and @($r.Lines | Where-Object { $_ -match 'pending' }).Count -ge 1) "code $($r.Code): $($r.Lines -join ' | ')"

        $global:StubMode = 'published'; $global:StubJson = New-StubJson 'Published' 'Free' '26.1021.1750.0'
        $r = & $run '26.10.21.1750'
        Assert-Case 'B3-version-not-newer' ($r.Code -eq 1 -and @($r.Lines | Where-Object { $_ -match 'not greater' }).Count -ge 1) "code $($r.Code): $($r.Lines -join ' | ')"

        $global:StubJson = New-StubJson 'Published' 'Base' '26.1021.1750.0'
        $r = & $run '26.11.12.1000'
        Assert-Case 'B4-price-not-free' ($r.Code -eq 1 -and @($r.Lines | Where-Object { $_ -match 'price' }).Count -ge 1) "code $($r.Code): $($r.Lines -join ' | ')"

        $global:StubMode = 'unconfigured'
        $r = & $run '26.11.12.1000'
        Assert-Case 'B5-unconfigured-is-manual' ($r.Code -eq 2 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -match 'Store step is manual') "code $($r.Code): $($r.Lines -join ' | ')"

        $global:StubMode = 'published'; $global:StubJson = New-StubJson 'Published' 'Free' '26.1021.1750.0'
        $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY = '2020-01-01'
        $callsBefore = $global:StubCalls.Count
        $r = & $run '26.11.12.1000'
        $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY = $null
        Assert-Case 'B6-expired-credential-is-manual' ($r.Code -eq 2 -and $r.Lines.Count -eq 1 -and $global:StubCalls.Count -eq $callsBefore) "code $($r.Code), store calls $($global:StubCalls.Count - $callsBefore): $($r.Lines -join ' | ')"

        $onlyReads = @($global:StubCalls | Where-Object { $_ -ne 'submission get 9NB4W41NGQJ4' }).Count -eq 0
        Assert-Case 'B7-only-submission-get' $onlyReads "the Store stub saw: $(@($global:StubCalls | Select-Object -Unique) -join ' | ')"

        Remove-Item -LiteralPath Function:\msstore
        $emptyBin = Join-Path $tmp 'empty-bin'
        New-Item -ItemType Directory -Path $emptyBin -Force | Out-Null
        $env:PATH = $emptyBin
        try { $r = & $run '26.11.12.1000' } finally { $env:PATH = $savedEnv.Path }
        Assert-Case 'B8-no-cli-is-manual' ($r.Code -eq 2 -and $r.Lines.Count -eq 1 -and $r.Lines[0] -match 'Store step is manual') "code $($r.Code): $($r.Lines -join ' | ')"

        $rows = @(Read-Evidence $evidenceB)
        Assert-Case 'B9-evidence-per-run' ($rows.Count -eq 7) "evidence lines: $($rows.Count)"
        $badKeys = @($rows | Where-Object { (($_.PSObject.Properties.Name | Sort-Object) -join ',') -ne $evidenceKeys }).Count
        Assert-Case 'B9-evidence-keys' ($badKeys -eq 0) 'an evidence line carries a key beyond the six the caller sets'
        Assert-Case 'B9-evidence-modes' ((($rows | ForEach-Object { $_.mode }) -join ',') -eq 'ready,refused,refused,refused,manual,manual,manual') "modes: $((($rows | ForEach-Object { $_.mode }) -join ','))"
        $recordText = Get-Content -LiteralPath $evidenceB -Raw
        Assert-Case 'B9-evidence-no-submission-data' (-not ($recordText -match '1152921504000000000|Submission 15|CyrFlip\.msix|SellerId')) 'a Store response field reached the record'
    }

    # ---- C. static -----------------------------------------------------------------------------------------
    $build = Get-Content -LiteralPath (Join-Path $RepoRoot 'msix\build-msix.ps1') -Raw
    $callerText = Get-Content -LiteralPath $caller -Raw
    $formula = '"$yy.$($m*100+$d).$hhmm.0"'
    Assert-Case 'C1-remap-same-formula' ($build.Contains($formula) -and $callerText.Contains($formula)) 'build-msix.ps1 and Invoke-StoreChannelPreflight.ps1 no longer share the MSIX version formula'

    $release = Get-Content -LiteralPath (Join-Path $RepoRoot 'release.ps1') -Raw
    $call = $release.IndexOf('msix\Invoke-StoreChannelPreflight.ps1')
    $tag = $release.IndexOf('Tag + push')
    Assert-Case 'C2-before-the-tag' ($call -gt 0 -and $tag -gt $call) 'release.ps1 does not call the Store channel preflight before the tag step'
    if ($call -gt 0) {
        $end = $release.IndexOf('finally { Pop-Location }', $call)
        $segment = $release.Substring($call, $end - $call)
        Assert-Case 'C2-only-refusal-fails' ($segment -match '\$LASTEXITCODE -eq 1\)\s*\{\s*\$preflightFails\.Add' -and $segment -notmatch 'preflightUnverified') 'release.ps1 must fail the preflight on exit 1 only; an unavailable channel changes nothing'
    }
    Assert-Case 'C2-skip-switch' ($release -match '\[switch\] \$SkipStoreChannel') 'release.ps1 lost -SkipStoreChannel'

    $runsMsstore = $callerText -match '&\s*[''"]?msstore|msstore\.exe|Invoke-Expression|\biex\b|Start-Process'
    Assert-Case 'C3-no-own-msstore-call' (-not $runsMsstore) 'the caller must reach the Store only through the shared script'
}
finally {
    $env:CLAUDE_PLUGIN_ROOT = $savedEnv.PluginRoot
    $env:CYRFLIP_STORE_CREDENTIAL_EXPIRY = $savedEnv.Expiry
    $env:PATH = $savedEnv.Path
    if (Test-Path Function:\msstore) { Remove-Item -LiteralPath Function:\msstore }
    Remove-Variable -Name StubMode, StubJson, StubCalls -Scope Global -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$clock.Stop()
Write-Host ('{0} cases in {1:N1} s{2}' -f $script:Cases, $clock.Elapsed.TotalSeconds, $(if ($unverifiedB) { ' (part B did not run)' } else { '' }))
Complete-Check
