<# Read-only catalog audit for S0076/S0077. Never writes adoption stamps or consumer versions. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CatalogRoot,
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'
$ids = @('APP-ACTIVATION','APP-STYLE','APP-SETTINGS','BOOLEAN-OPTION','BUTTONS','CAPTURE-OUTPUT',
    'CHECK-VERDICT','COLLAPSIBLE-GROUP','CONTEXT-HELP','DEVICE-EXCHANGE','DIAGNOSTIC-REPORT',
    'DOC-EXTERNAL-QUALITY','DOC-INTERNAL-QUALITY','HARNESS-PROFILE','ICON-RENDER','ICON-SET',
    'INPUT-CHORD','INPUT-PARITY','PAGE-CONTENT','PAGE-STYLE','SETTINGS-NAVIGATION','SITE-EXPERIENCE',
    'SITE-FAMILY-MAP','SITE-STRUCTURE','UI-SIZING','UPDATE-MANIFEST','WINDOWS-STORE','WINDOWS-UI','SCENARIO-FILE')
$root = (Resolve-Path -LiteralPath $CatalogRoot).Path
$records = @{}
foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.md' -Recurse) {
    if ($file.Name -like 'PROPOSAL-*' -or $file.FullName -match '[\\/]history[\\/]') { continue }
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($block in [regex]::Matches($text, '(?ms)^```contract\s*\r?\n(.*?)^```')) {
        $id = [regex]::Match($block.Groups[1].Value, '(?m)^id:\s*(\S+)').Groups[1].Value
        if ($ids -notcontains $id) { continue }
        $version = [regex]::Match($block.Groups[1].Value, '(?m)^version:\s*(\S+)').Groups[1].Value
        $contractHome = $file.FullName.Substring($root.Length + 1).Replace('\','/')
        if ($records.ContainsKey($id)) {
            # Domain indexes repeat metadata; the dedicated contract page is authoritative.
            if ($file.Name -ne "$id.md") { continue }
        }
        $records[$id] = [ordered]@{ id=$id; read_version=$version; home=$contractHome;
            sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();
            consolidation=($text -match 'Amendment 2026-10-17|2026-10-17.*ownership confirmed');
            implements='unchanged; see scoped audit and measured evidence' }
    }
}
$missing = @($ids | Where-Object { -not $records.ContainsKey($_) })
if ($missing.Count) { Write-Host "Missing contract metadata: $($missing -join ', ')"; Write-Host 'contract-targets: COULD NOT VERIFY'; exit 2 }
$registry = Join-Path $root '_meta/REGISTRY.md'
$ownRows = @([IO.File]::ReadAllLines($registry) | Where-Object { $_ -match '^\|.*\| CyrFlip(?: \([^|]*\))? \|' })
$pointerRoot = Join-Path $PSScriptRoot '../../docs/contracts'
$pointers = @(Get-ChildItem -LiteralPath $pointerRoot -Filter '*.md' | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    [ordered]@{ file=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();
        version_line=([regex]::Match($text, '(?m)^\| \*\*Version\*\* \| (.*)').Groups[1].Value);
        partial=($text -match '(?i)partial|unverified|not held|exception|owed|pending|not bound') }
})
$audit = [ordered]@{ date='2026-10-18'; contracts=@($ids | ForEach-Object { $records[$_] });
    pointer_inventory=$pointers; own_registry_row_count=$ownRows.Count;
    own_registry_rows=$ownRows; adoption_rows_advanced=$false }
if ($OutFile) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutFile), ($audit | ConvertTo-Json -Depth 8)) }
foreach ($id in $ids) { Write-Host "$id $($records[$id].read_version)" }
Write-Host "contract-targets: PASS (run=$($ids.Count) skipped=0)"
