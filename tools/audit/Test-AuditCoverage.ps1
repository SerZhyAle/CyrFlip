[CmdletBinding()]
param([string] $Manifest = (Join-Path $PSScriptRoot '../../PLAN/S0042_code-re-audit-before-release/slices.json'))

$ErrorActionPreference = 'Stop'
$data = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$coverage = Join-Path $PSScriptRoot 'Get-AuditCoverage.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-audit-' + [guid]::NewGuid().ToString('N') + '.json')

function Assert-Exit {
    param([int] $Expected, [string] $Case)
    $data | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding UTF8
    $null = & $coverage -Manifest $temporary -IncludeClassB
    if ($LASTEXITCODE -ne $Expected) { throw "$Case expected exit $Expected; got $LASTEXITCODE" }
    Write-Output "$Case : exit $Expected"
}

try {
    foreach ($slice in $data.slices) { $slice.state = 'closed' }
    Assert-Exit 0 'all closed'

    $data.slices[0].state = 'open'
    Assert-Exit 1 'one open'
    $data.slices[0].state = 'closed'

    $file = [string]$data.slices[0].files[0]
    $data.slices[0].files = @($data.slices[0].files | Where-Object { $_ -ne $file })
    Assert-Exit 2 'one missing'
    $data.slices[0].files = @($file) + @($data.slices[0].files)

    $data.slices[1].files = @($file) + @($data.slices[1].files)
    Assert-Exit 2 'one duplicate'
}
finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
exit 0
