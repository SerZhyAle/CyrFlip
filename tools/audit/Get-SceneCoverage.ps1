<#
.SYNOPSIS
The coverage matrix of the runtime quality audit (ticket S0061, section 5.1 pillar 3): scenes x properties,
each cell measured / not applicable (with a reason) / open.

.DESCRIPTION
Reads scenes.json and answers three questions with an exit code, in the discipline of Get-AuditCoverage.ps1:

  2  the catalogue itself is defective - a scene with no driver, a driver that names a script or test
     class that does not exist, a cell `measured` with no evidence file, a cell `na` with no reason, an
     unknown state, a duplicate scene id, or a feature of the numbered list in CLAUDE.md that no scene
     covers (a new feature adds a scene row, not a new audit - spec 5.9);
  1  the catalogue is sound but cells are still `open`;
  0  every cell is decided.

The feature list is read from the live CLAUDE.md ("What CyrFlip is", lines `N. **...`), so a feature
added there without a scene turns this red by itself.

.PARAMETER Manifest
Path to scenes.json (default: the S0061 folder).
#>
[CmdletBinding()]
param(
    [string]$Manifest,
    [string]$ClaudeMd,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Manifest) { $Manifest = Join-Path $repository 'PLAN\S0061_runtime-quality-audit\scenes.json' }
if (-not $ClaudeMd) { $ClaudeMd = Join-Path $repository 'CLAUDE.md' }
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$evidenceRoot = Join-Path $repository 'PLAN\S0061_runtime-quality-audit'
$data = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$problems = New-Object System.Collections.Generic.List[string]
function Out([string]$s) { if (-not $Quiet) { Write-Output $s } }

# --- the features CLAUDE.md lists ---------------------------------------------------------------
$claudeFeatures = @()
$inSection = $false
foreach ($line in [IO.File]::ReadLines($ClaudeMd)) {
    if ($line -match '^## What CyrFlip is') { $inSection = $true; continue }
    if ($inSection -and $line -match '^## ') { break }
    if ($inSection -and $line -match '^(\d+)\.\s+\*\*') { $claudeFeatures += [int]$Matches[1] }
}
if ($claudeFeatures.Count -eq 0) { $problems.Add('no numbered feature found in CLAUDE.md "What CyrFlip is" - the section moved or was renamed') }

# --- the scenes ---------------------------------------------------------------------------------
$properties = @($data.properties)
$ids = @{}
$covered = New-Object System.Collections.Generic.HashSet[int]
$counts = @{}
foreach ($p in $properties) { $counts[$p] = @{ measured = 0; na = 0; open = 0 } }
$openCells = New-Object System.Collections.Generic.List[string]

function Test-Driver([string]$Driver, [string]$SceneId) {
    if (-not $Driver -or -not $Driver.Trim()) { $problems.Add("$SceneId has no driver"); return }
    if ($Driver -eq 'by-hand') { return }
    if ($Driver -match '^xunit:(\w+)$') {
        $class = $Matches[1]
        $hit = Get-ChildItem -Path (Join-Path $repository 'tests\CyrFlip.Tests') -Filter '*.cs' -Recurse |
            Select-String -Pattern "\bclass\s+$class\b" -List | Select-Object -First 1
        if (-not $hit) { $problems.Add("$SceneId driver names test class '$class', which does not exist") }
        return
    }
    $path = ($Driver -split '\s+-')[0].Trim()
    if (-not (Test-Path -LiteralPath (Join-Path $repository $path))) { $problems.Add("$SceneId driver names '$path', which does not exist") }
}

foreach ($scene in @($data.scenes)) {
    $id = [string]$scene.id
    if ($ids.ContainsKey($id)) { $problems.Add("duplicate scene id $id") }
    $ids[$id] = $true
    Test-Driver ([string]$scene.driver) $id
    foreach ($f in @($scene.features)) { [void]$covered.Add([int]$f) }
    foreach ($p in $properties) {
        $cell = $scene.cells.$p
        if ($null -eq $cell) { $problems.Add("$id has no cell for $p"); continue }
        switch ([string]$cell.state) {
            'measured' {
                $counts[$p].measured++
                $ev = [string]$cell.evidence
                if (-not $ev) { $problems.Add("$id/$p is measured with no evidence") }
                elseif (-not (Test-Path -LiteralPath (Join-Path $evidenceRoot ($ev -split '#')[0]))) { $problems.Add("$id/$p evidence '$ev' does not exist") }
            }
            'na' {
                $counts[$p].na++
                if (-not ([string]$cell.reason).Trim()) { $problems.Add("$id/$p is na with no reason") }
            }
            'open' { $counts[$p].open++; $openCells.Add("$id/$p") }
            default { $problems.Add("$id/$p has unknown state '$($cell.state)'") }
        }
    }
}

foreach ($n in $claudeFeatures) {
    if (-not $covered.Contains($n)) { $problems.Add("CLAUDE.md feature $n has no scene") }
}

# --- report -------------------------------------------------------------------------------------
$sceneCount = @($data.scenes).Count
$cellTotal = $sceneCount * $properties.Count
Out "Scenes: $sceneCount; cells: $cellTotal; CLAUDE.md features: $($claudeFeatures.Count) ($($claudeFeatures -join ','))"
foreach ($p in $properties) { Out ("  {0}: measured {1}, na {2}, open {3}" -f $p, $counts[$p].measured, $counts[$p].na, $counts[$p].open) }
Out "Open cells: $($openCells.Count)"
foreach ($c in $problems) { Out "DEFECT $c" }
if ($problems.Count) { exit 2 }
if ($openCells.Count) { exit 1 }
exit 0
