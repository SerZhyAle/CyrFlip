# Operator-invoked, off-screen demo renderer. Requires the built net48 test assembly.
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$Locale = '', [string]$Surface = '')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -Configuration $Configuration -Locale $Locale -Surface $Surface
    exit $LASTEXITCODE
}
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$built = Join-Path $repo "tests\CyrFlip.Tests\bin\$Configuration\net48"
$staging = Join-Path $repo ("artifacts/help-renderer/" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -Path (Join-Path $built "*") -Destination $staging -Recurse
$assembly = Join-Path $staging "CyrFlip.Tests.dll"
try {
    Add-Type -AssemblyName System.Windows.Forms
    $loaded = [Reflection.Assembly]::LoadFrom($assembly)
    $loaded.GetType('CyrFlip.Tests.HelpArtifacts').GetMethod('Generate').Invoke($null, [object[]]@([string]$repo, [string]$Locale, [string]$Surface))
} catch {
    Write-Error $_
    exit 1
}

