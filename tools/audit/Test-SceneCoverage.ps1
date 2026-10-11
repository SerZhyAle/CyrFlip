<#
.SYNOPSIS
Self-test of Get-SceneCoverage.ps1 (S0061 acceptance 3 / 6): the coverage script must fail closed -
an open cell, a missing driver, a duplicate id, a measured cell without evidence and a CLAUDE.md feature
with no scene must each give the exit code the script promises, and a fully decided catalogue gives 0.
Same shape as Test-AuditCoverage.ps1.
#>
[CmdletBinding()]
param([string]$Manifest = (Join-Path $PSScriptRoot '..\..\PLAN\S0061_runtime-quality-audit\scenes.json'))

$ErrorActionPreference = 'Stop'
$coverage = Join-Path $PSScriptRoot 'Get-SceneCoverage.ps1'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-scenes-' + [guid]::NewGuid().ToString('N') + '.json')
$tmpClaude = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-claude-' + [guid]::NewGuid().ToString('N') + '.md')
$failed = $false

function New-Data { Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json }
function Assert-Exit {
    param($Data, [int]$Expected, [string]$Case, [string]$ClaudeMd)
    $Data | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding UTF8
    $arguments = @{ Manifest = $temporary; Quiet = $true }
    if ($ClaudeMd) { $arguments.ClaudeMd = $ClaudeMd }
    $null = & $coverage @arguments
    if ($LASTEXITCODE -ne $Expected) { Write-Output "FAIL $Case : expected exit $Expected, got $LASTEXITCODE"; $script:failed = $true }
    else { Write-Output "ok   $Case : exit $Expected" }
}
function Decide-All($Data) {
    foreach ($s in $Data.scenes) {
        foreach ($p in $Data.properties) {
            $s.cells.$p = [pscustomobject]@{ state = 'na'; reason = 'self-test' }
        }
    }
}

try {
    $d = New-Data; Decide-All $d
    Assert-Exit $d 0 'every cell decided'

    $d = New-Data; Decide-All $d
    $d.scenes[0].cells.RS = [pscustomobject]@{ state = 'open' }
    Assert-Exit $d 1 'one open cell'

    $d = New-Data; Decide-All $d
    $d.scenes[0].driver = ''
    Assert-Exit $d 2 'scene without a driver'

    $d = New-Data; Decide-All $d
    $d.scenes[0].driver = 'tools/uitest/Does-NotExist.ps1'
    Assert-Exit $d 2 'driver names a missing script'

    $d = New-Data; Decide-All $d
    $d.scenes[1].id = $d.scenes[0].id
    Assert-Exit $d 2 'duplicate scene id'

    $d = New-Data; Decide-All $d
    $d.scenes[0].cells.RS = [pscustomobject]@{ state = 'measured'; evidence = 'research/no-such-file.md' }
    Assert-Exit $d 2 'measured cell with no evidence file'

    $d = New-Data; Decide-All $d
    $d.scenes[0].cells.RS = [pscustomobject]@{ state = 'na'; reason = '' }
    Assert-Exit $d 2 'na cell with no reason'

    # A feature added to CLAUDE.md without a scene row must turn the check red on its own.
    $d = New-Data; Decide-All $d
    $text = [IO.File]::ReadAllText((Join-Path $repository 'CLAUDE.md'))
    $marker = "`n14. **The settings pages follow the features"
    if ($text.IndexOf($marker) -lt 0) { throw 'self-test could not find feature 14 in CLAUDE.md to extend the list after' }
    [IO.File]::WriteAllText($tmpClaude, $text.Replace($marker, "`n99. **A feature nobody wrote a scene for.**`n$marker"))
    Assert-Exit $d 2 'CLAUDE.md feature without a scene' $tmpClaude
}
finally {
    Remove-Item -LiteralPath $temporary, $tmpClaude -Force -ErrorAction SilentlyContinue
}
if ($failed) { exit 1 }
exit 0
