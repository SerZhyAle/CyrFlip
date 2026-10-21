# Operator-invoked, off-screen walk of the places that wear the exchange server's mark (EXCHANGE-MARK rung 3, S0079).
# Renders the settings page (header band + account entries), the sign-in dialog and the About credit in the light, dark and
# high-contrast themes, in English and in the right-to-left languages ar and ur, into one PNG per place - the file names carry
# the matrix. Needs the built net48 test assembly. Windows are shown inside a hidden host far off screen; nothing touches the
# desktop, the real settings or the clipboard. A per-monitor scale change (100 -> 200 percent) is NOT simulated here: it needs a
# live session, and the walk says so rather than fake it.
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$Out = '')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath -Configuration $Configuration -Out $Out
    exit $LASTEXITCODE
}
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Out) { $Out = Join-Path $repo ('artifacts/exchange-mark-walk/' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$built = Join-Path $repo "tests\CyrFlip.Tests\bin\$Configuration\net48"
$staging = Join-Path $repo ("artifacts/exchange-mark-walk-staging/" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -Path (Join-Path $built "*") -Destination $staging -Recurse
try {
    Add-Type -AssemblyName System.Windows.Forms
    $loaded = [Reflection.Assembly]::LoadFrom((Join-Path $staging 'CyrFlip.Tests.dll'))
    $loaded.GetType('CyrFlip.Tests.ExchangeMarkWalk').GetMethod('Run').Invoke($null, [object[]]@([string]$Out))
    "Captures: $Out"
} catch {
    Write-Error $_
    exit 1
} finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
