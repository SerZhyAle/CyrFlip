[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [switch] $IncludeClassB
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$data = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

function Get-RepositoryFiles {
    param([string[]] $Pathspec)
    $output = & git -C $repository ls-files --cached --others --exclude-standard -- @Pathspec
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    @($output | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
}

$classA = @(Get-RepositoryFiles @('src/CyrFlip/*.cs', 'vscode-extension/src/*'))
$classB = @()
if ($IncludeClassB) {
    $classB = @(Get-RepositoryFiles @(
        'tests/CyrFlip.Tests', 'tools', 'build.ps1', 'release.ps1',
        '.github/workflows', 'msix', 'winget',
        'src/CyrFlip/CyrFlip.csproj', 'src/CyrFlip/app.manifest', 'src/CyrFlip/config.json'
    ) | Where-Object {
        $_ -notmatch '(^|/)(bin|obj)/' -and
        $_ -match '\.(cs|csproj|ps1|psm1|yml|yaml|xml|json|jsonl)$|/app\.manifest$'
    })
}
$expected = @($classA + $classB | Sort-Object -Unique)
$slices = @($data.slices | Where-Object { $_.class -eq 'A' -or ($IncludeClassB -and $_.class -eq 'B') })
$owners = @{}
foreach ($slice in $slices) {
    foreach ($file in @($slice.files)) {
        $path = [string]$file
        if (-not $owners.ContainsKey($path)) { $owners[$path] = @() }
        $owners[$path] += [string]$slice.id
    }
}
$missing = @($expected | Where-Object { -not $owners.ContainsKey($_) })
$duplicate = @($owners.Keys | Where-Object { $owners[$_].Count -gt 1 } | Sort-Object)
$stale = @($owners.Keys | Where-Object { $_ -notin $expected } | Sort-Object)
$open = @($slices | Where-Object { $_.state -ne 'closed' })

Write-Output "Class A files: $($classA.Count); class B files: $($classB.Count); slices: $($slices.Count)"
Write-Output "Uncovered: $($missing.Count); duplicate: $($duplicate.Count); stale manifest entries: $($stale.Count); open slices: $($open.Count)"
foreach ($file in $missing) { Write-Output "UNCOVERED $file" }
foreach ($file in $duplicate) { Write-Output "DUPLICATE $file ($($owners[$file] -join ', '))" }
foreach ($file in $stale) { Write-Output "STALE $file" }
foreach ($slice in $open) { Write-Output "OPEN $($slice.id) $($slice.title)" }
foreach ($severity in @('P0', 'P1', 'P2', 'P3')) {
    $count = 0
    foreach ($slice in $slices) { $count += [int]$slice.findings.$severity }
    Write-Output "$severity findings: $count"
}
if ($missing.Count -or $duplicate.Count -or $stale.Count) { exit 2 }
if ($open.Count) { exit 1 }
exit 0
