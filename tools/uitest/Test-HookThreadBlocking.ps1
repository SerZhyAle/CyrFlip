# The hooks' thread never waits on a share (ticket S0030) - the part xUnit cannot reach.
#
# Both low-level hooks run on CyrFlip's UI thread, and Windows silently drops a hook whose callback
# waits past LowLevelHooksTimeout (~300 ms). HookThreadBlockingTests proves the seams: a remote
# scenario path is never probed, icons and launches leave the calling thread. This script proves the
# outcome on the live app, with a scenario on an address that never answers (10.255.255.1 is not
# routed, so an SMB connect to it waits out its full timeout, some twenty seconds):
#
#   unattended  - CyrFlip is started with the launcher on and one staged scenario, then the settings
#                 window is opened (/launcher-settings); all the while the UI thread is pinged with
#                 SendMessageTimeout(WM_NULL) every 50 ms. That runs twice: once with the scenario on
#                 a local path (the baseline - starting up and building the settings window cost the
#                 UI thread something on their own) and once on the unreachable share. PASS = the
#                 share adds no more than -MaxExtraMs to the worst ping of either phase. The code
#                 before S0030 stalled the thread for the whole SMB timeout, at startup and again
#                 when settings built the scenario table.
#   by hand     - a settings checkbox click, a launch from the tray and one from the text context
#                 menu, each with typing in Notepad at the same time, and a chord pressed afterwards
#                 (the one real proof the hook was not dropped: CyrFlip ignores injected keys, so a
#                 synthesized chord would prove nothing - see Test-LongRun.ps1).
#
#   .\tools\uitest\Test-HookThreadBlocking.ps1 -Unattended   # the ping half only; exit code 0 = pass
#   .\tools\uitest\Test-HookThreadBlocking.ps1               # then the scenes by hand
#   .\tools\uitest\Test-HookThreadBlocking.ps1 -Unattended -ExePath C:\older\CyrFlip.exe
#                                                            # the same against a build before S0030
#
# The staged scenario file is deleted and the two registry values it needed are put back as they
# were found, whatever happens. CyrFlip is restarted for the run and afterwards left running only if
# it was running before.
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$ExePath,
    [switch]$Unattended,
    [int]$MaxExtraMs = 1000,
    [int]$WatchSeconds = 12
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi   # also loads the module's native helpers ([CyrFlipUi]::ProcessWindows)

Add-Type -Namespace CyrFlipUiTest -Name Ping -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
    uint flags, uint timeout, out IntPtr result);
'@

$failed = $false
function Fail([string]$message) { Write-Host "FAIL  $message" -ForegroundColor Red; $script:failed = $true }
function Pass([string]$message) { Write-Host "PASS  $message" -ForegroundColor Green }

$exe = if ($ExePath) { (Resolve-Path $ExePath).Path } else { Get-CyrFlipExe -Configuration $Configuration }
$regPath = 'HKCU:\Software\CyrFlip'
$scenarios = Join-Path $env:APPDATA 'CyrFlip\Scenarios'
$id = [guid]::NewGuid()
$staged = Join-Path $scenarios ("$id.xml")
$unreachable = '\\10.255.255.1\x\a.exe'
"exe               : $exe"

function Get-Saved([string]$Name) { (Get-ItemProperty $regPath -Name $Name -ErrorAction SilentlyContinue).$Name }
$wasRunning = [bool](Get-Process CyrFlip -ErrorAction SilentlyContinue)
$saved = @{ EnableScenarioLauncher = Get-Saved 'EnableScenarioLauncher'; LauncherFirstEnableDone = Get-Saved 'LauncherFirstEnableDone' }

function Set-StagedScenario([string]$Path) {
    New-Item -ItemType Directory -Force -Path $scenarios | Out-Null
    @"
<?xml version="1.0" encoding="utf-8"?>
<AppItem xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Id>$id</Id>
  <Name>S0030 staged scenario</Name>
  <Path>$Path</Path>
  <Arguments />
  <WorkingDirectory />
  <RunAsAdmin>false</RunAsAdmin>
  <Order>9999</Order>
</AppItem>
"@ | Set-Content -Path $staged -Encoding UTF8
}

function Get-UiWindow {
    # Any top-level window of the process belongs to its UI thread - the one the hooks share.
    $p = Get-Process CyrFlip -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { return [IntPtr]::Zero }
    $all = @([CyrFlipUi]::ProcessWindows([uint32]$p.Id, $false))
    if ($all.Count -eq 0) { return [IntPtr]::Zero }
    $all[0]
}

function Watch-UiThread([int]$Seconds) {
    # SMTO_ABORTIFHUNG is deliberately absent: a stalled thread is exactly what is being measured.
    $worst = 0; $pings = 0; $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $h = Get-UiWindow
        if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100; continue }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $r = [IntPtr]::Zero
        [void][CyrFlipUiTest.Ping]::SendMessageTimeout($h, 0, [IntPtr]::Zero, [IntPtr]::Zero, 0, 60000, [ref]$r)
        $ms = $clock.ElapsedMilliseconds
        $pings++
        if ($ms -gt $worst) { $worst = $ms }
        Start-Sleep -Milliseconds 50
    }
    [pscustomobject]@{ Pings = $pings; Worst = $worst }
}

function Measure-Run([string]$ScenarioPath) {
    Set-StagedScenario $ScenarioPath
    Stop-CyrFlipApp
    Start-Process $exe | Out-Null
    $startup = Watch-UiThread $WatchSeconds
    Start-Process $exe -ArgumentList '/launcher-settings' | Out-Null   # forwarded over the pipe
    $settings = Watch-UiThread $WatchSeconds
    [pscustomobject]@{ Startup = $startup; Settings = $settings }
}

function Compare-Phase([string]$Phase, $Baseline, $Share) {
    $line = "{0}: worst ping {1} ms with a local path, {2} ms with the unreachable share ({3}/{4} pings)" -f `
        $Phase, $Baseline.Worst, $Share.Worst, $Baseline.Pings, $Share.Pings
    if ($Baseline.Pings -eq 0 -or $Share.Pings -eq 0) { Fail "$Phase - no window of CyrFlip to ping" }
    elseif ($Share.Worst -gt $Baseline.Worst + $MaxExtraMs) { Fail $line }
    else { Pass $line }
}

try {
    New-Item -Path $regPath -Force | Out-Null
    Set-ItemProperty $regPath -Name EnableScenarioLauncher -Value 1 -Type DWord
    Set-ItemProperty $regPath -Name LauncherFirstEnableDone -Value 1 -Type DWord

    "baseline          : $env:WINDIR\System32\calc.exe"
    $baseline = Measure-Run "$env:WINDIR\System32\calc.exe"
    "share             : $unreachable"
    $share = Measure-Run $unreachable

    Compare-Phase 'startup (Jump List + tray submenu built)' $baseline.Startup $share.Startup
    Compare-Phase 'settings opened (scenario table built)' $baseline.Settings $share.Settings

    if (-not $Unattended) {
        $scenes = @(
            'HT-1  With Notepad focused and typing, click any checkbox on any settings page (and back). Typing never freezes.',
            'HT-1  Type in the launcher page search box. The table filters at once; the share row shows the CyrFlip icon.',
            'HT-1  Tray > Быстрый запуск > "S0030 staged scenario", then type in Notepad at once. Typing stays fluid; a failure balloon comes later.',
            'HT-5  Put \\10.255.255.1\x\a.exe in Notepad, select it, open the text context menu, "Запустить", confirm; type at once. Typing stays fluid.',
            'HT-3  Open the text context menu 20 times fast (Ctrl + right click). Every time it opens; context-menu.log shows no error.',
            'ALL   Now press a CyrFlip chord (e.g. Ctrl+Shift+F12 on a selected word). It works - the hook was not dropped.'
        )
        foreach ($scene in $scenes) {
            Write-Host ''
            Write-Host $scene -ForegroundColor Cyan
            $answer = Read-Host 'Result? [y = as described / n = not / s = skip]'
            switch ($answer.Trim().ToLowerInvariant()) {
                'y' { Pass $scene.Substring(0, 5) }
                'n' { Fail $scene.Substring(0, 5) }
                default { Write-Host 'SKIP' -ForegroundColor Yellow }
            }
        }
    }
}
finally {
    Remove-Item $staged -ErrorAction SilentlyContinue
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-ItemProperty $regPath -Name $name -ErrorAction SilentlyContinue }
        else { Set-ItemProperty $regPath -Name $name -Value $saved[$name] -Type DWord }
    }
    # Restarted so the running instance forgets the staged scenario and the launcher switch - and
    # only when one was running before the test; otherwise it is left stopped, as it was found.
    Stop-CyrFlipApp
    if ($wasRunning) { Start-Process $exe | Out-Null }
}

if ($failed) { exit 1 }
exit 0
