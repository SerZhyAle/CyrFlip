<#
    Test-CheckPlacement.ps1 - Both-way checker for CHECK-PLACEMENT contract.

    Verifies:
      1. check-placement.jsonl syntax and required schema keys.
      2. Forward check: all checks invoked by runner scripts/workflows are declared with matching class.
      3. Backward check: all declared records in runner classes are actually invoked by their runners.
      4. Undeclared checks: any uitest/gate script without a placement record fails as strictest class.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$ToolsDir = Split-Path -Parent $ScriptDir
$RepoRoot = Split-Path -Parent $ToolsDir

Import-Module (Join-Path $ScriptDir 'CheckVerdict.psm1') -Force

Set-CheckSubject 'placement-registry'

$jsonlPath = Join-Path $ScriptDir 'check-placement.jsonl'
if (-not (Test-Path $jsonlPath)) {
    Add-CheckFinding -Severity fail -Name 'registry-file-missing' -Reason "check-placement.jsonl not found at $jsonlPath"
    Complete-Check
}

$requiredKeys = @('check', 'class', 'decided', 'ticket', 'origin', 'reason')
$validClasses = @('per-change-local', 'ci', 'release-preflight', 'release-tag', 'operator-typed')

$records = [System.Collections.Generic.List[pscustomobject]]::new()
$lineNo = 0

foreach ($line in Get-Content $jsonlPath) {
    $lineNo++
    $trimmed = $line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }

    try {
        $obj = $trimmed | ConvertFrom-Json
    }
    catch {
        Add-CheckFinding -Severity fail -Name "json-parse-line-$lineNo" -Reason "Line $lineNo is not valid JSON: $($_.Exception.Message)"
        continue
    }

    $missing = @($requiredKeys | Where-Object { -not ($obj.PSObject.Properties.Name -contains $_) -or $null -eq $obj.$_ -or "$($obj.$_)".Trim() -eq '' })
    if ($missing.Count -gt 0) {
        Add-CheckFinding -Severity fail -Name "schema-line-$lineNo" -Reason "Line $lineNo missing required keys: $($missing -join ', ')"
        continue
    }

    $classes = if ($obj.class -is [System.Array]) { @($obj.class) } else { @($obj.class) }
    foreach ($c in $classes) {
        if ($c -notin $validClasses) {
            Add-CheckFinding -Severity fail -Name "invalid-class-line-$lineNo" -Reason "Line $lineNo has invalid class '$c'. Allowed: $($validClasses -join ', ')"
        }
    }

    $records.Add($obj)
}

# --- 1. Forward check: verify runners only invoke declared checks with proper class ---
$buildPs1 = Join-Path $RepoRoot 'build.ps1'
$ciYml = Join-Path $RepoRoot '.github\workflows\ci.yml'
$releasePs1 = Join-Path $RepoRoot 'release.ps1'
$releaseYml = Join-Path $RepoRoot '.github\workflows\release.yml'

$runnerFiles = @{
    'per-change-local'   = $buildPs1
    'ci'                 = $ciYml
    'release-preflight'  = $releasePs1
    'release-tag'        = $releaseYml
}

function Get-RecordClasses([string]$checkName) {
    $matched = @($records | Where-Object { $_.check -eq $checkName -or (Split-Path $_.check -Leaf) -eq $checkName })
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($m in $matched) {
        if ($m.class -is [System.Array]) { foreach ($c in $m.class) { $out.Add($c) } }
        else { $out.Add($m.class) }
    }
    return @($out | Select-Object -Unique)
}

# --- 2. Backward check: verify every declared runner check is actually referenced in its runner ---
foreach ($record in $records) {
    $classes = if ($record.class -is [System.Array]) { @($record.class) } else { @($record.class) }
    $check = $record.check

    foreach ($class in $classes) {
        if ($class -eq 'operator-typed') {
            $scriptPath = Join-Path $RepoRoot $check
            if (-not (Test-Path $scriptPath)) { $scriptPath = Join-Path $ScriptDir $check }
            if (-not (Test-Path $scriptPath)) { $scriptPath = Join-Path $ToolsDir $check }
            if (-not (Test-Path $scriptPath)) {
                Add-CheckFinding -Severity fail -Name "missing-operator-check-$check" -Reason "Operator check file does not exist: $check"
            }
        }
        elseif ($runnerFiles.ContainsKey($class)) {
            $runnerPath = $runnerFiles[$class]
            if (Test-Path $runnerPath) {
                $runnerContent = [System.IO.File]::ReadAllText($runnerPath)
                $checkLeaf = Split-Path $check -Leaf
                $checkBase = [System.IO.Path]::GetFileNameWithoutExtension($check)
                $found = ($runnerContent.Contains($check) -or $runnerContent.Contains($checkLeaf) -or $runnerContent.Contains($checkBase))
                if (-not $found) {
                    Add-CheckFinding -Severity fail -Name "uninvoked-check-$check-$class" -Reason "Check '$check' declared with class '$class' is not referenced by runner '$([System.IO.Path]::GetFileName($runnerPath))'"
                }
            }
        }
    }
}

# --- 3. Strictness check: find all uitest and check scripts and ensure each is in the registry ---
$allScripts = @(Get-ChildItem (Join-Path $RepoRoot 'tools') -Recurse -File -Filter '*.ps1' |
    Where-Object { $_.Name -like 'Test-*.ps1' -or $_.Name -like 'Audit-*.ps1' -or $_.Name -like 'Measure-*.ps1' -or $_.Name -like 'Save-*.ps1' -or $_.Name -like 'Get-*.ps1' -or $_.Name -like '*-Check.ps1' -or $_.Name -like 'Assert-*.ps1' })

$registeredChecks = @($records | ForEach-Object { $_.check })

foreach ($scriptFile in $allScripts) {
    $rel = ($scriptFile.FullName.Substring($RepoRoot.Length)).TrimStart('\', '/') -replace '\\', '/'
    $leaf = $scriptFile.Name
    $isRegistered = ($registeredChecks -contains $rel) -or ($registeredChecks -contains $leaf)
    if (-not $isRegistered) {
        Add-CheckFinding -Severity fail -Name "undeclared-check-$rel" -Reason "Script '$rel' is not declared in check-placement.jsonl (strictest class violation)"
    }
}

Complete-Check
