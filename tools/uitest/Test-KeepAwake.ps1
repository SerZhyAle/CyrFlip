# Keep-awake, end to end - the part xUnit cannot reach.
#
# KeepAwakeTests proves the flag mask; only Windows can say whether the mask actually became a
# power request. `powercfg /requests` is that answer, and it is the only one: SetThreadExecutionState
# returns the previous state, never a confirmation, so a silently ignored request looks identical to
# a working one from inside the process.
#
# The script drives the persisted state (HKCU\Software\CyrFlip, written by the two toggles) and
# restarts CyrFlip, so it covers the restore-at-startup path as well as the P/Invoke:
#
#   both saved on  -> CyrFlip.exe listed under DISPLAY and under SYSTEM;
#   both saved off -> CyrFlip.exe listed under neither.
#
#   .\tools\uitest\Test-KeepAwake.ps1
#
# `powercfg /requests` needs administrator rights; the script elevates just that call (a UAC prompt
# per reading, three in a full run) and reads its output back from a temp file. Other processes in
# the lists are none of our business - PowerToys Awake and driver-level "Legacy Kernel Caller"
# entries are common - so every assertion looks for our exe path and nothing else.
#
# The registry values are put back the way they were found, and CyrFlip is left running on them.
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force

$regPath = 'HKCU:\Software\CyrFlip'
$exe = Get-CyrFlipExe -Configuration $Configuration
"exe               : $exe"

function Get-SavedSwitch {
    param([string]$Name)
    $value = (Get-ItemProperty $regPath -Name $Name -ErrorAction SilentlyContinue).$Name
    if ($null -eq $value) { 0 } else { [int]$value }
}

function Set-SavedSwitches {
    param([int]$SystemAwake, [int]$ScreenOn)
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty $regPath -Name KeepSystemAwake -Value $SystemAwake -Type DWord
    Set-ItemProperty $regPath -Name KeepScreenOn -Value $ScreenOn -Type DWord
}

function Get-PowerRequests {
    <#
    .SYNOPSIS
    `powercfg /requests` split into a hashtable of section name -> array of lines.
    #>
    $out = Join-Path ([System.IO.Path]::GetTempPath()) 'cyrflip-powercfg-requests.txt'
    Remove-Item $out -ErrorAction SilentlyContinue
    Start-Process powershell -Verb RunAs -Wait -ArgumentList `
        '-NoProfile', '-Command', "powercfg /requests | Out-File -Encoding utf8 '$out'"
    if (-not (Test-Path $out)) { throw "powercfg produced no output - was the UAC prompt declined?" }

    $sections = @{}
    $current = $null
    foreach ($line in Get-Content $out) {
        if ($line -match '^([A-Z]+):\s*$') { $current = $Matches[1]; $sections[$current] = @() }
        elseif ($current -and $line.Trim()) { $sections[$current] += $line.Trim() }
    }
    Remove-Item $out -ErrorAction SilentlyContinue
    $sections
}

function Test-CyrFlipRequest {
    <#
    .SYNOPSIS
    Whether the named section holds a request from our exe. Matched on the file name plus the
    configuration folder: powercfg prints a device path (\Device\HarddiskVolumeN\..), not a drive
    letter, so the full path from Get-CyrFlipExe never matches literally.
    #>
    param([hashtable]$Sections, [string]$Section)
    $needle = "net48\CyrFlip.exe"
    @($Sections[$Section]) -join "`n" -match [regex]::Escape($needle)
}

function Assert-Requests {
    param([string]$Stage, [bool]$Expected)
    $sections = Get-PowerRequests
    foreach ($section in 'DISPLAY', 'SYSTEM') {
        $actual = [bool](Test-CyrFlipRequest -Sections $sections -Section $section)
        $verdict = if ($actual -eq $Expected) { 'OK  ' } else { 'FAIL' }
        "$verdict $Stage : $section holds a CyrFlip request = $actual (expected $Expected)"
        if ($actual -ne $Expected) {
            throw "$Stage : expected CyrFlip $(if ($Expected) { 'in' } else { 'absent from' }) the $section list."
        }
    }
}

$wasSystemAwake = Get-SavedSwitch -Name KeepSystemAwake
$wasScreenOn = Get-SavedSwitch -Name KeepScreenOn
"saved state       : KeepSystemAwake=$wasSystemAwake KeepScreenOn=$wasScreenOn (restored at the end)"

try {
    Set-SavedSwitches -SystemAwake 1 -ScreenOn 1
    Start-CyrFlipApp -Configuration $Configuration -Fresh | Out-Null
    Assert-Requests -Stage 'both saved on ' -Expected $true

    Set-SavedSwitches -SystemAwake 0 -ScreenOn 0
    Start-CyrFlipApp -Configuration $Configuration -Fresh | Out-Null
    Assert-Requests -Stage 'both saved off' -Expected $false
}
finally {
    Set-SavedSwitches -SystemAwake $wasSystemAwake -ScreenOn $wasScreenOn
    Start-CyrFlipApp -Configuration $Configuration -Fresh | Out-Null
    "restored        : KeepSystemAwake=$wasSystemAwake KeepScreenOn=$wasScreenOn, CyrFlip restarted on it"
}

""
"PASS - the saved keep-awake state reaches Windows as a real power request."
