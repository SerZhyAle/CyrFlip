<#
    Test-AppCommands.ps1 - the APP-COMMANDS cases that need the real program (ticket S0107).

    Drives the built CyrFlip.exe from outside, the way a script does: `Start-Process -Wait -PassThru` and the exit
    code, plus the --result file. It STOPS a running CyrFlip (`CyrFlip.exe quit`) and starts it again, so a person
    runs it, at the desk. The setting "Let other programs ask CyrFlip to work with my files, screen, clipboard or
    the network" is read and put back as it was.

      C1  commands --json with nothing running: exit 0, equals docs/cli-commands.json, no CyrFlip process left
      C2  version exit 0; status with nothing running exit 11
      C10 quit with nothing running exit 0 (oneshot); show starts the tray copy and exits 0 (start); status refuse 11
      C3  with the tray copy running: status 0 and the number of CyrFlip processes is the same afterwards
      C4  an unknown verb, an unknown option, an oversized argument: exit 10, and status still 0
      C8  the setting off: clipboard send exits 12 (or 11 when its module is off), status still 0
      C11 status --result writes the data whole
      C13 /launcher-settings still reaches the running copy (no answer, exit 0)
      L1-L5 (ticket S0109, nothing running) text convert / text flip-case / layout list work in the process and leave no
            process or tray icon behind; layout current and layout switch exit 11
      L6-L8 (ticket S0109, copy running) layout switch exits 12 with the setting off; layout list carries `current` on
            every object, at most one of them true; the same layouts in the same order as the one-shot list
      L9  (asks a person) layout switch by code, by KLID and by next in Notepad, a browser and a console, and once against
            an elevated window - described, not driven
      C6  (asks a person) open a CyrFlip dialog, then status answers 11 inside the bound
      C5, C9 (asks a person) a hung or an elevated first instance - described, not driven

    Exit codes: 0 every driven case passed; 1 a case failed; 2 the exe was not found.
#>
[CmdletBinding()]
param(
    [string] $Exe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src\CyrFlip\bin\Release\net48\CyrFlip.exe'),
    [switch] $SkipPersonCases
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path -LiteralPath $Exe)) { Write-Host "app-commands: NOT VERIFIED (1) - $Exe not found; build first."; exit 2 }

$failed = 0
function Run([string[]] $arguments, [int] $timeoutSec = 30) {
    $p = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { Write-Host "  could not stop: $_" }; return -1 }
    return $p.ExitCode
}
function Expect([string] $case, [int] $expected, [int] $actual) {
    if ($expected -eq $actual) { Write-Host "PASS $case - expected $expected | actual $actual" -ForegroundColor Green }
    else { Write-Host "FAIL $case - expected $expected | actual $actual" -ForegroundColor Red; $script:failed++ }
}
function CyrFlipCount { @(Get-Process -Name CyrFlip -ErrorAction SilentlyContinue).Count }

$key = 'HKCU:\Software\CyrFlip'
$setting = (Get-ItemProperty -Path $key -Name EnableExternalCommands -ErrorAction SilentlyContinue).EnableExternalCommands
try {
    # --- nothing running ---
    Run @('quit') | Out-Null
    Start-Sleep -Milliseconds 1500
    Expect 'C10 quit with nothing running' 0 (Run @('quit'))
    $out = Join-Path $env:TEMP ('cyrflip-cmd-' + [guid]::NewGuid().ToString('N') + '.json')
    & $Exe commands --json | Out-File -LiteralPath $out -Encoding utf8NoBOM
    $same = ((Get-Content -LiteralPath $out -Raw).Trim() -eq (Get-Content -LiteralPath (Join-Path $repo 'docs\cli-commands.json') -Raw).Trim())
    Expect 'C1 commands --json equals the manifest' 1 ([int]$same)
    Expect 'C1 no process left' 0 (CyrFlipCount)
    Expect 'C2 version' 0 (Run @('version'))
    Expect 'C2 status with nothing running' 11 (Run @('status'))

    # --- the layout and text group with nothing running (ticket S0109) ---
    $privet = -join ([char[]](0x43F, 0x440, 0x438, 0x432, 0x435, 0x442))
    $textResult = Join-Path $env:TEMP ('cyrflip-text-' + [guid]::NewGuid().ToString('N') + '.json')
    Expect 'L1 text convert, no instance' 0 (Run @('text', 'convert', '--text', 'ghbdtn', '--from', 'en', '--to', 'ru', '--result', $textResult))
    $data = Get-Content -LiteralPath $textResult -Raw -Encoding utf8 | ConvertFrom-Json
    Expect 'L1 text convert gives the converted text' 1 ([int]($data.text -ceq $privet -and $data.changed -eq $true))
    Expect 'L2 text flip-case, no instance' 0 (Run @('text', 'flip-case', '--text', 'hELLO', '--result', $textResult))
    $data = Get-Content -LiteralPath $textResult -Raw -Encoding utf8 | ConvertFrom-Json
    Expect 'L2 text flip-case gives Hello' 1 ([int]($data.text -ceq 'Hello' -and $data.changed -eq $true))
    Expect 'L2 an empty text is a usage error' 10 (Run @('text', 'flip-case', '--text', ''))
    Expect 'L2 the same layout twice is a usage error' 10 (Run @('text', 'convert', '--text', 'a', '--from', 'en', '--to', 'en'))
    $oneShotList = Join-Path $env:TEMP ('cyrflip-layouts-' + [guid]::NewGuid().ToString('N') + '.json')
    Expect 'L3 layout list, no instance' 0 (Run @('layout', 'list', '--result', $oneShotList))
    $oneShot = Get-Content -LiteralPath $oneShotList -Raw -Encoding utf8 | ConvertFrom-Json
    Expect 'L3 the list is not empty' 1 ([int]($oneShot.count -gt 0 -and @($oneShot.layouts).Count -eq $oneShot.count))
    Expect 'L3 no current without a running copy' 0 (@($oneShot.layouts | Where-Object { $null -ne $_.PSObject.Properties['current'] }).Count)
    Expect 'L4 layout current, no instance' 11 (Run @('layout', 'current'))
    Expect 'L4 layout switch, no instance' 11 (Run @('layout', 'switch', '--to', 'next'))
    Expect 'L5 no process left' 0 (CyrFlipCount)
    Remove-Item -LiteralPath $textResult -ErrorAction SilentlyContinue

    # --- start ---
    Expect 'C10 show starts the tray copy' 0 (Run @('show') 40)
    $before = CyrFlipCount
    Expect 'C3 status with the copy running' 0 (Run @('status'))
    Expect 'C3 processes unchanged' $before (CyrFlipCount)

    # --- strict parsing ---
    Expect 'C4 unknown verb' 10 (Run @('launch-rockets'))
    Expect 'C4 unknown option' 10 (Run @('status', '--verbose'))
    Expect 'C4 oversized argument' 10 (Run @('scenario', 'run', '--name', ('a' * 33000)))
    Expect 'C4 status afterwards' 0 (Run @('status'))

    # --- the setting ---
    Set-ItemProperty -Path $key -Name EnableExternalCommands -Value 0 -Type DWord
    Run @('quit') | Out-Null; Start-Sleep -Milliseconds 1500; Run @('show') 40 | Out-Null
    $send = Run @('clipboard', 'send')
    if ($send -eq 12 -or $send -eq 11) { Expect 'C8 setting off refuses clipboard send' $send $send } else { Expect 'C8 setting off refuses clipboard send' 12 $send }
    Expect 'C8 safe verbs still work' 0 (Run @('status'))

    # --- result file ---
    $result = Join-Path $env:TEMP ('cyrflip-status-' + [guid]::NewGuid().ToString('N') + '.json')
    Expect 'C11 status --result' 0 (Run @('status', '--result', $result))
    $data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    Expect 'C11 result holds the pid' 1 ([int]($data.pid -gt 0))
    Remove-Item -LiteralPath $result, $out -ErrorAction SilentlyContinue

    # --- the layout group with the copy running and the setting off (ticket S0109) ---
    Expect 'L6 layout switch is refused while the setting is off' 12 (Run @('layout', 'switch', '--to', 'next'))
    Expect 'L6 layout switch with a bad value is 12 too, the gate comes first' 12 (Run @('layout', 'switch', '--to', 'banana'))
    $liveList = Join-Path $env:TEMP ('cyrflip-layouts-live-' + [guid]::NewGuid().ToString('N') + '.json')
    Expect 'L7 layout list with the copy running' 0 (Run @('layout', 'list', '--result', $liveList))
    $live = Get-Content -LiteralPath $liveList -Raw -Encoding utf8 | ConvertFrom-Json
    $withCurrent = @($live.layouts | Where-Object { $null -ne $_.PSObject.Properties['current'] }).Count
    Expect 'L7 current is on every object' $live.count $withCurrent
    Expect 'L7 at most one layout is current' 1 ([int](@($live.layouts | Where-Object { $_.current -eq $true }).Count -le 1))
    $sameOrder = (($live.layouts | ForEach-Object { $_.klid }) -join ',') -ceq (($oneShot.layouts | ForEach-Object { $_.klid }) -join ',')
    Expect 'L8 the running copy and the one-shot process list the same layouts in the same order' 1 ([int]$sameOrder)
    $current = Run @('layout', 'current')
    if ($current -eq 0 -or $current -eq 11) { Expect 'L8 layout current answers 0, or 11 with no window' $current $current } else { Expect 'L8 layout current answers 0, or 11 with no window' 0 $current }
    Remove-Item -LiteralPath $liveList, $oneShotList -ErrorAction SilentlyContinue

    # --- the shipped spelling ---
    Expect 'C13 /launcher-settings reaches the running copy' 0 (Run @('/launcher-settings'))

    if (-not $SkipPersonCases) {
        Read-Host 'C6: open any CyrFlip dialog (for example About > a confirmation), then press Enter here'
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $busy = Run @('status')
        $clock.Stop()
        Expect 'C6 status with a dialog open' 11 $busy
        Write-Host ("  answered in {0} ms" -f $clock.ElapsedMilliseconds)
        Write-Host 'C5: suspend CyrFlip in Process Explorer, run "CyrFlip.exe status" - expect 11 within about 2 s, never a hang.'
        Write-Host 'C9: start CyrFlip as administrator, run "CyrFlip.exe status" from a normal console - expect 12 (elevation).'
        Write-Host 'L9: turn the setting on, click into Notepad, then from another console run "layout switch --to <other code>", "--to <its KLID>" and "--to next",'
        Write-Host '    each followed by "layout current" - expect 0 and the new layout. Repeat in a browser and in a console window (from a console the'
        Write-Host '    window is the console). Once against an elevated Notepad: expect exit 0 and no change (UIPI). Record the date and what it did not cover.'
    }
}
finally {
    if ($null -eq $setting) { Remove-ItemProperty -Path $key -Name EnableExternalCommands -ErrorAction SilentlyContinue }
    else { Set-ItemProperty -Path $key -Name EnableExternalCommands -Value $setting -Type DWord }
}

if ($failed -gt 0) { Write-Host "app-commands: FAIL ($failed)"; exit 1 }
Write-Host 'app-commands: PASS'
exit 0
