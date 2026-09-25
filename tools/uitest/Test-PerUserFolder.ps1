# Ticket S0016 - the Store build's per-user data folder, end to end. The part xUnit cannot reach.
#
# DataFolderTests prove the path string; only a real MSIX package can prove what Windows does with it.
# This is the gate of the spec's section 7.3, to be run on Windows 10 22H2 and on Windows 11 with the
# Store (or a sideloaded) CyrFlip package installed:
#
#   1. a write from inside the package to the EXPLICIT LocalCache path lands at exactly that path
#      (no second redirection);
#   2. a write from inside the package to the plain %LOCALAPPDATA%\CyrFlip lands in that same folder
#      (so the explicit path really is where the package's own writes go);
#   3. a file an unpackaged process (this script - standing in for the VS Code extension) writes there
#      is visible to the package at the same path;
#   4. when the packaged CyrFlip is running: layout.txt / layout-klid.txt in the per-user folder, the
#      mirror copy in %ProgramData%\CyrFlip, and no logs or journal left in %ProgramData%\CyrFlip that
#      belong to the current user.
#
#   .\tools\uitest\Test-PerUserFolder.ps1
#   .\tools\uitest\Test-PerUserFolder.ps1 -PackageFamilyName <family of a self-signed test build>
#
# Steps 1-3 run commands inside the package with Invoke-CommandInDesktopPackage (Windows 10 1809+), which
# needs no elevation but may need Developer Mode for a sideloaded package. Every probe file is removed again.
# The two-account check (each user's extension shows their own layout; each user's notes survive a
# restart; neither can read the other's logs) needs two people or two sessions and is printed at the end.
[CmdletBinding()]
param(
    [string]$PackageFamilyName = 'SZA.CyrFlip_fdk7e19xt9z9j',
    [string]$AppId = 'CyrFlip'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force

$package = Get-AppxPackage | Where-Object PackageFamilyName -eq $PackageFamilyName | Select-Object -First 1
if (-not $package) { throw "Package $PackageFamilyName is not installed for this user - install the Store or a sideloaded build first." }
Write-Host "Package: $($package.PackageFullName)"

$realLocal = $env:LOCALAPPDATA
$folder = Join-Path $realLocal "Packages\$PackageFamilyName\LocalCache\Local\CyrFlip"
$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
$failures = New-Object System.Collections.Generic.List[string]

function Invoke-InPackage([string]$commandLine) {
    Invoke-CommandInDesktopPackage -PackageFamilyName $PackageFamilyName -AppId $AppId `
        -Command "$env:SystemRoot\System32\cmd.exe" -Args "/c $commandLine" -PreventBreakaway
    Start-Sleep -Milliseconds 1500 # the command runs detached; give it time to finish
}

function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS  $name" -ForegroundColor Green }
    else { Write-Host "FAIL  $name - $detail" -ForegroundColor Red; $failures.Add($name) }
}

$explicit = Join-Path $folder "gate-explicit-$tag.txt"
$virtual = "gate-virtual-$tag.txt"
$outside = Join-Path $folder "gate-outside-$tag.txt"
$seen = Join-Path $folder "gate-seen-$tag.txt"
try {
    # 1. explicit path from inside
    Invoke-InPackage "mkdir `"$folder`" 2>nul & echo explicit>`"$explicit`""
    Check 'explicit LocalCache path is not redirected again' (Test-Path $explicit) "not found at $explicit"

    # 2. virtualized %LOCALAPPDATA% from inside lands in the same folder
    Invoke-InPackage "mkdir `"%LOCALAPPDATA%\CyrFlip`" 2>nul & echo virtual>`"%LOCALAPPDATA%\CyrFlip\$virtual`""
    $landed = Join-Path $folder $virtual
    $plain = Join-Path $realLocal "CyrFlip\$virtual"
    Check 'the package''s own %LOCALAPPDATA% writes land in that folder' (Test-Path $landed) `
        ("not at $landed" + $(if (Test-Path $plain) { "; found at the unvirtualized $plain instead" } else { '' }))

    # 3. unpackaged write, packaged read
    New-Item -ItemType Directory -Force $folder | Out-Null
    Set-Content -LiteralPath $outside -Value 'from-outside' -Encoding ascii
    Invoke-InPackage "type `"$outside`">`"$seen`""
    Check 'a file an unpackaged process writes there is visible inside the package' `
        ((Test-Path $seen) -and ((Get-Content -LiteralPath $seen -Raw) -match 'from-outside')) "the package could not read $outside"
}
finally {
    foreach ($f in @($explicit, (Join-Path $folder $virtual), (Join-Path $realLocal "CyrFlip\$virtual"), $outside, $seen)) {
        Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
    }
}

# 4. the live app
$running = Get-Process CyrFlip -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*WindowsApps*$($package.Name)*" }
if ($running) {
    Check 'layout.txt in the per-user folder' (Test-Path (Join-Path $folder 'layout.txt')) "missing in $folder"
    Check 'layout-klid.txt in the per-user folder' (Test-Path (Join-Path $folder 'layout-klid.txt')) "missing in $folder"
    $legacy = Join-Path $env:ProgramData 'CyrFlip'
    Check 'mirror layout.txt in %ProgramData%\CyrFlip (LAYOUT-SIGNAL 1.1)' (Test-Path (Join-Path $legacy 'layout.txt')) `
        'missing - expected unless another account owns the mirror'
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $leftovers = @('quick-notes.log', 'launcher.log', 'translate.log', 'context-menu.log', 'caret-diagnostics.txt',
        'quick-notes-diagnostics.log', 'clipboard-history-diagnostics.log', 'clipboard-flip.log') |
        ForEach-Object { Join-Path $legacy $_ } | Where-Object { Test-Path $_ } |
        Where-Object { (Get-Acl $_).GetOwner([System.Security.Principal.SecurityIdentifier]) -eq $me }
    Check 'no file of this user left in %ProgramData%\CyrFlip after the migration' (-not $leftovers) ($leftovers -join ', ')
}
else {
    Write-Host 'SKIP  live-app checks - start the Store CyrFlip and rerun to include them' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Two-account checklist (by hand, Store build signed in as both A and B on this PC):'
Write-Host '  [ ] A and B each switch layout: each one''s VS Code extension shows their OWN layout.'
Write-Host '  [ ] B with the quick notes on: a note survives a CyrFlip restart; no "records could not be read" balloon.'
Write-Host '  [ ] B cannot open A''s ...\Packages\<family>\LocalCache\Local\CyrFlip\launcher.log (access denied).'
Write-Host '  [ ] A pre-S0016 journal of A in %ProgramData%\CyrFlip moved to A''s folder on A''s first start; B''s untouched.'

if ($failures.Count) { throw "$($failures.Count) check(s) failed: $($failures -join '; ')" }
Write-Host 'All automated checks passed.' -ForegroundColor Green
