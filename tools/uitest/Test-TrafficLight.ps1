# The availability traffic light on a live app (ticket S0069 section 10, the owner-run half).
#
# What xUnit and Test-TrafficLightProbes.ps1 cannot reach: the module inside the real tray process - the
# registry, the hooks' thread, the journal on a real disk, the board in a real window. This script stages a
# known board of loopback and local targets, starts the app, lets it settle with the board HIDDEN (polling
# while hidden is D5), and then judges what is observable from outside:
#
#   * every staged target's first settled state, read from the change journal (one START line each);
#   * a target that did not ask for the journal leaves no line in it;
#   * the 50 MB budget with 20 targets, and GDI/USER handle counts while a 2-second target runs hidden;
#   * traffic-light.log gains no fault line;
#   * the board opens at launch when it was left visible (screenshot saved).
#
# It never presses the chord for you - CyrFlip ignores injected keystrokes by design - and it cannot pull a
# network cable; those and the rest of section 10 are the checklist printed at the end.
#
#   .\tools\uitest\Test-TrafficLight.ps1                 # stage, judge, screenshot, restore
#   .\tools\uitest\Test-TrafficLight.ps1 -Interactive    # keep the staged board until you press Enter
#
# It BORROWS THE REAL SETTINGS AND THE REAL JOURNAL, so it is careful in this order:
#   1. every value the module owns, its chord and switches are read from HKCU\Software\CyrFlip, and the
#      change journal file is copied, into a backup folder;
#   2. the script REFUSES TO START when that backup cannot be written and read back;
#   3. the way back (the restore routine) is built before the first scene;
#   4. everything is put back in `finally`, with the app stopped while the values are written (a running
#      app writes its own state on exit), and the app is started again if it was running before.
# A name or a file that did not exist before is deleted again, not left behind.
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$Interactive,
    [int]$SettleSeconds = 25,
    [int]$HandleWatchSeconds = 120,
    [int]$HandleSampleSeconds = 10,
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# Resolved here, not as the parameter's default: Windows PowerShell 5.1 leaves $PSScriptRoot empty there.
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts\uitest\traffic-light' }
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Test-TrafficLight'

$key = 'HKCU:\Software\CyrFlip'
# Everything the module reads or writes, the master switch, and the language the checklist is worded in.
$names = @(
    'EnableTrafficLight', 'TrafficLightHotkey', 'EnableTrafficLightHotkey', 'TrafficLightTargets',
    'TrafficLightVisible', 'TrafficLightPinned', 'TrafficLightTileSize', 'TrafficLightColumns',
    'TrafficLightX', 'TrafficLightY',
    'EnableHotkeys', 'UiLanguage', 'SettingsTab', 'SettingsPageId', 'SettingsGroupsState', 'WelcomeShown'
)

function Read-Settings {
    $snapshot = [ordered]@{}
    if (-not (Test-Path $key)) { return $snapshot }
    $item = Get-Item $key
    foreach ($name in $names) {
        if ($item.GetValueNames() -contains $name) {
            $snapshot[$name] = @{ Kind = [string]$item.GetValueKind($name); Value = $item.GetValue($name) }
        }
    }
    $snapshot
}

function Write-Settings($snapshot) {
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    foreach ($name in $names) {
        if ($snapshot.Contains($name)) {
            New-ItemProperty -Path $key -Name $name -Value $snapshot[$name].Value -PropertyType $snapshot[$name].Kind -Force | Out-Null
        }
        else { Remove-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue }
    }
}

$failures = @()
function Check([string]$name, [bool]$condition, [string]$detail = '') {
    if ($condition) { Write-Host "PASS $name - $detail" -ForegroundColor Green }
    else {
        Write-Host "FAIL $name - $detail" -ForegroundColor Red
        $script:failures += "${name}: $detail"
    }
}

if (-not (Test-Path (Get-CyrFlipExe -Configuration $Configuration))) {
    Add-CheckFinding -Severity notverified -Name 'exe-missing' -Reason "No $Configuration build of CyrFlip.exe - build it first."
    Complete-Check
}

# 1 + 2: the backup, written and read back before anything is touched.
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format 'yyMMdd_HHmmss'
$backupPath = Join-Path $OutDir "backup-$stamp.json"
$dataFolder = (Get-CyrFlipDataFolder).Path
$journalPath = Join-Path $dataFolder 'traffic-light-history.txt'
$faultLogPath = Join-Path $dataFolder 'traffic-light.log'
$journalBackup = Join-Path $OutDir "journal-$stamp.txt"
$before = Read-Settings
$journalExisted = Test-Path $journalPath
try {
    ($before | ConvertTo-Json -Depth 4) | Set-Content -Path $backupPath -Encoding UTF8
    $readBack = Get-Content -Path $backupPath -Raw | ConvertFrom-Json
    if (@($readBack.PSObject.Properties).Count -ne $before.Count) { throw 'the backup does not hold every value' }
    if ($journalExisted) {
        Copy-Item -LiteralPath $journalPath -Destination $journalBackup -Force
        if ((Get-Item $journalBackup).Length -ne (Get-Item $journalPath).Length) { throw 'the journal copy differs in length' }
    }
}
catch {
    Add-CheckFinding -Severity notverified -Name 'backup-failed' -Reason "Refusing to touch the settings: the backup could not be written and read back ($_)."
    Complete-Check
}
Write-Host "Backup of $($before.Count) value(s): $backupPath" -ForegroundColor Cyan
if ($journalExisted) { Write-Host "Journal copy: $journalBackup" -ForegroundColor Cyan }

# 3: the way back, resolved before the first scene.
$wasRunning = [bool](Get-Process CyrFlip -ErrorAction SilentlyContinue)
$restore = {
    Stop-CyrFlipApp
    Start-Sleep -Milliseconds 700
    Write-Settings $before
    if ($journalExisted) { Copy-Item -LiteralPath $journalBackup -Destination $journalPath -Force }
    elseif (Test-Path $journalPath) { Remove-Item -LiteralPath $journalPath -Force }
    if ($wasRunning) { Start-CyrFlipApp -Configuration $Configuration | Out-Null }
}

# Loopback servers the script owns, so no target ever leaves the machine.
function Start-StatusServer([int]$status) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $ps = [powershell]::Create()
    [void]$ps.AddScript({
        param($l, $code)
        while ($true) {
            try { $c = $l.AcceptTcpClient() } catch { break }
            try {
                $s = $c.GetStream()
                $buf = New-Object byte[] 4096
                [void]$s.Read($buf, 0, $buf.Length)
                $text = "HTTP/1.1 $code X`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
                $out = [Text.Encoding]::ASCII.GetBytes($text)
                $s.Write($out, 0, $out.Length)
            }
            catch { }
            finally { $c.Close() }
        }
    }).AddArgument($listener).AddArgument($status)
    [pscustomobject]@{
        Listener = $listener
        Shell    = $ps
        Async    = $ps.BeginInvoke()
        Port     = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
}

function Stop-StatusServer($server) {
    if (-not $server) { return }
    try { $server.Listener.Stop() } catch { }
    try { [void]$server.Shell.EndInvoke($server.Async) } catch { }
    try { $server.Shell.Dispose() } catch { }
}

function Get-ClosedPort {
    $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $l.Start(); $port = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
    $port
}

function New-Row([string]$label, [string]$kind, [string]$address, [hashtable]$set = @{}) {
    $row = [ordered]@{
        Id = [guid]::NewGuid().ToString('N'); Label = $label; Kind = $kind; Address = $address; Fingerprint = ''
        Group = ''; Ordinal = 0; Enabled = $true; IntervalSeconds = 60; TimeoutMs = 0; YellowThresholdMs = 0
        FailuresBeforeDown = 1; AcceptedStatusCodes = '200-399'; AllowUntrustedCertificate = $false
        Notify = $false; RecordHistory = $false
    }
    foreach ($k in $set.Keys) { $row[$k] = $set[$k] }
    $row
}

$ok200 = $null
$s401 = $null
try {
    Stop-CyrFlipApp
    Start-Sleep -Milliseconds 700

    $ok200 = Start-StatusServer 200
    $s401 = Start-StatusServer 401
    $closed = Get-ClosedPort
    $missing = Join-Path $OutDir 'folder-that-does-not-exist'
    if (Test-Path $missing) { Remove-Item -LiteralPath $missing -Recurse -Force }
    $existing = Join-Path $OutDir 'folder-that-exists'
    New-Item -ItemType Directory -Force -Path $existing | Out-Null

    # Scene 1: the board, hidden. Every meaningful row asks for the journal so its first settled state can be
    # read from outside; the "quiet" row does not and must leave no line.
    $rec = @{ RecordHistory = $true }
    $rows = @(
        (New-Row 'TL ping loopback' 'ping' '127.0.0.1' $rec),
        (New-Row 'TL tcp closed' 'tcp' "127.0.0.1:$closed" $rec),
        (New-Row 'TL http 200' 'http' "http://127.0.0.1:$($ok200.Port)/" $rec),
        (New-Row 'TL http 401 default' 'http' "http://127.0.0.1:$($s401.Port)/" $rec),
        (New-Row 'TL http 401 accepted' 'http' "http://127.0.0.1:$($s401.Port)/" @{ RecordHistory = $true; AcceptedStatusCodes = '200-399,401' }),
        (New-Row 'TL folder ok' 'folder' $existing $rec),
        (New-Row 'TL folder missing' 'folder' $missing $rec),
        (New-Row 'TL drive C' 'drive' 'C:' $rec),
        (New-Row 'TL fast 2s' 'ping' '127.0.0.1' @{ RecordHistory = $true; IntervalSeconds = 2 }),
        (New-Row 'TL hourly' 'ping' '127.0.0.1' @{ RecordHistory = $true; IntervalSeconds = 3600 }),
        (New-Row 'TL quiet down' 'tcp' "127.0.0.1:$closed" @{ RecordHistory = $false })
    )
    # Filler up to twenty targets: the memory budget is stated for 20.
    for ($n = 0; $rows.Count -lt 20; $n++) {
        $rows += , (New-Row ("TL filler {0:00}" -f $n) 'ping' ('127.0.0.{0}' -f (10 + $n)))
    }
    for ($i = 0; $i -lt $rows.Count; $i++) { $rows[$i].Ordinal = $i }

    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name 'EnableHotkeys' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'UiLanguage' -Value 'English' -Type String
    Set-ItemProperty -Path $key -Name 'WelcomeShown' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'EnableTrafficLight' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'EnableTrafficLightHotkey' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'TrafficLightVisible' -Value 0 -Type DWord
    Set-ItemProperty -Path $key -Name 'TrafficLightTargets' -Value (ConvertTo-Json -InputObject @($rows) -Compress) -Type String
    if (Test-Path $journalPath) { Remove-Item -LiteralPath $journalPath -Force }
    $faultLinesBefore = if (Test-Path $faultLogPath) { @(Get-Content -LiteralPath $faultLogPath).Count } else { 0 }

    $process = Start-CyrFlipApp -Configuration $Configuration -Fresh
    Write-Host "Scene 1: app started (pid $($process.Id)); settling for $SettleSeconds s with the board hidden..." -ForegroundColor Cyan
    Start-Sleep -Seconds $SettleSeconds

    Check 'board-hidden' (-not (Get-AppWindows | Where-Object { $_.Title -match 'traffic' })) 'the board did not open by itself while TrafficLightVisible=0.'
    Check 'journal-exists' (Test-Path $journalPath) "polling ran with the board hidden; the journal is $journalPath."
    $journal = if (Test-Path $journalPath) { [IO.File]::ReadAllText($journalPath) } else { '' }
    Check 'journal-start-marker' ($journal -match '-- CyrFlip started, monitoring resumed --') 'the journal opens with its start marker.'

    function Start-State([string]$label) {
        $m = [regex]::Match($journal, '(?m)^\S+ \S+ \S+\s+START\s+\[' + [regex]::Escape($label) + '\][^\r\n]*?state=(\w+)')
        if ($m.Success) { $m.Groups[1].Value } else { '(no START line)' }
    }
    $expect = [ordered]@{
        'TL ping loopback'      = 'OK'
        'TL tcp closed'         = 'DOWN'
        'TL http 200'           = 'OK'
        'TL http 401 default'   = 'DEGRADED'
        'TL http 401 accepted'  = 'OK'
        'TL folder ok'          = 'OK'
        'TL folder missing'     = 'DOWN'
        'TL drive C'            = 'OK'
        'TL fast 2s'            = 'OK'
        'TL hourly'             = 'OK'
    }
    foreach ($label in $expect.Keys) {
        $got = Start-State $label
        Check "first-state/$label" ($got -eq $expect[$label]) "expected $($expect[$label]), the journal says $got."
    }
    Check 'quiet-target-leaves-no-line' ($journal -notmatch 'TL quiet down') 'a target that did not ask for the journal is absent from it.'
    Check 'filler-targets-leave-no-line' ($journal -notmatch 'TL filler') 'filler targets did not ask for the journal.'
    $faultLinesAfter = if (Test-Path $faultLogPath) { @(Get-Content -LiteralPath $faultLogPath).Count } else { 0 }
    Check 'no-fault-lines' ($faultLinesAfter -eq $faultLinesBefore) "traffic-light.log: $faultLinesBefore -> $faultLinesAfter line(s)."

    # The budget and the handles: a 2 s target and nineteen others polled while hidden. A series, not two
    # points: something used for the first time (a lazily created object, a finalizer that has not run yet) is
    # a one-time step, not a leak, so growth is judged on the SECOND half of the window - a plateau leaves it
    # flat, a leak does not.
    $series = @()
    $sample = Get-AppResourceUsage | Select-Object -First 1
    if ($sample) { $series += $sample }
    $steps = [math]::Max(2, [int][math]::Ceiling($HandleWatchSeconds / $HandleSampleSeconds))
    for ($i = 0; $i -lt $steps; $i++) {
        Start-Sleep -Seconds $HandleSampleSeconds
        $sample = Get-AppResourceUsage | Select-Object -First 1
        if ($sample) { $series += $sample }
    }
    if ($series.Count -ge 3) {
        $first = $series[0]
        $last = $series[-1]
        $mid = $series[[int][math]::Floor(($series.Count - 1) / 2)]
        for ($i = 0; $i -lt $series.Count; $i++) {
            $s = $series[$i]
            Write-Host ("  +{0,4} s  GDI {1,3}  USER {2,3}  threads {3,3}  private {4,5:N1} MB" -f `
                    ($i * $HandleSampleSeconds), $s.GdiObjects, $s.UserObjects, $s.Threads, ($s.PrivateBytes / 1MB)) -ForegroundColor DarkCyan
        }
        $mb = [math]::Round($last.PrivateBytes / 1MB, 1)
        Write-Host ("Resources: private {0} MB, working set {1} MB, GDI {2} -> {3} -> {4}, USER {5} -> {6} -> {7}, threads {8} -> {9}" -f `
                $mb, [math]::Round($last.WorkingSet / 1MB, 1), $first.GdiObjects, $mid.GdiObjects, $last.GdiObjects,
                $first.UserObjects, $mid.UserObjects, $last.UserObjects, $first.Threads, $last.Threads) -ForegroundColor Cyan
        Check 'budget-50mb-with-20-targets' ($last.PrivateBytes -lt 50MB) "private bytes $mb MB (budget 50 MB)."
        Check 'gdi-handles-stable' (($last.GdiObjects - $mid.GdiObjects) -le 3) "GDI $($first.GdiObjects) -> $($mid.GdiObjects) -> $($last.GdiObjects); the second half of $HandleWatchSeconds s may grow by 3."
        Check 'user-handles-stable' (($last.UserObjects - $mid.UserObjects) -le 3) "USER $($first.UserObjects) -> $($mid.UserObjects) -> $($last.UserObjects); the second half of $HandleWatchSeconds s may grow by 3."
        Check 'threads-bounded' ($last.Threads -le ($first.Threads + 6)) "threads $($first.Threads) -> $($last.Threads) (caps: 16 async + 4 blocking)."
    }
    else { $failures += 'no CyrFlip process to sample' }

    # Scene 2: the same board left visible - it opens by itself at launch.
    Stop-CyrFlipApp
    Start-Sleep -Milliseconds 700
    Set-ItemProperty -Path $key -Name 'TrafficLightVisible' -Value 1 -Type DWord
    Start-CyrFlipApp -Configuration $Configuration | Out-Null
    $board = $null
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not $board) {
        $board = Get-AppWindows | Where-Object { $_.Title -match 'traffic' } | Select-Object -First 1
        if (-not $board) { Start-Sleep -Milliseconds 400 }
    }
    Check 'board-opens-at-launch' ([bool]$board) 'the board reopened because it was left visible.'
    if ($board) {
        Start-Sleep -Seconds 4
        $shot = Save-WindowShot -Handle $board.Handle -Path (Join-Path $OutDir 'traffic-light-board.png')
        Write-Host "Board screenshot: $($shot.Path) ($($shot.Width)x$($shot.Height))" -ForegroundColor Green

        # Hover tips, with a real pointer, on a board nobody has focused: the mouse never activates it
        # (MA_NOACTIVATE) and ToolTip.Show(.., window, ..) answers only for an active window, so a tip had
        # to be the control's own tool. The pointer is put back where it was.
        $rect = New-Object CyrFlipUi+RECT
        [void][CyrFlipUi]::GetWindowRect($board.Handle, [ref]$rect)
        $pointerBefore = [CyrFlipUi]::CursorAt()
        $tipAppears = {
            param([int[]]$xs, [int[]]$ys)
            foreach ($py in $ys) {
                foreach ($px in $xs) {
                    [void][CyrFlipUi]::SetCursorPos($px, $rect.Bottom + 80)     # from outside, so each is an entry
                    Start-Sleep -Milliseconds 250
                    [void][CyrFlipUi]::SetCursorPos($px, $py)
                    Start-Sleep -Milliseconds 80
                    [void][CyrFlipUi]::SetCursorPos($px + 1, $py)
                    Start-Sleep -Milliseconds 900
                    if (@(Get-AppWindows | Where-Object { $_.Class -match 'tooltip' }).Count -gt 0) { return $true }
                }
            }
            $false
        }
        $tileYs = @(); for ($py = $rect.Top + 38; $py -lt $rect.Bottom - 6; $py += 16) { $tileYs += $py }
        Check 'hover-tip-on-a-tile' (& $tipAppears @(($rect.Left + 20), ($rect.Left + 50)) $tileYs) 'a tip appears over a tile of a board that was never focused.'
        Check 'hover-tip-on-a-title-button' (& $tipAppears @(($rect.Right - 14)) @(($rect.Top + 10), ($rect.Top + 14))) 'a tip appears over the close button of the title bar.'
        $at = @($pointerBefore -split '[^0-9-]+' | Where-Object { $_ -match '^-?\d+$' })
        if ($at.Count -ge 2) { [void][CyrFlipUi]::SetCursorPos([int]$at[0], [int]$at[1]) }
    }

    Write-Host @'

Human checklist (the unattended half ends here - the staged board is on screen now; tick each item):
  [ ] Quick add by typing and Enter only, one at a time: D:\  //mycomp  \\mycomp\share  192,168,1,100  8.8.8.8
      www.google.com - each is recognized as the kind its row says, with that kind's defaults.
  [ ] A pasted list of ten lines adds ten rows; "Undo" removes exactly those. An unrecognized text opens the details.
  [ ] An FMSEXCH1: string and an sftp:// link: the row is added, and the saved list, the details and
      traffic-light.log show no password, user name or session id.
  [ ] \\host that answers ping -> ping; a Windows PC that drops ICMP -> tcp 445; neither -> a red ping row.
  [ ] A website that blocks ICMP is green (the HTTP fallback). A self-signed internal site: red/yellow with the
      certificate option off, green with it on.
  [ ] A folder on a sleeping network share is yellow on the slow first probe; a folder on a DEAD server leaves the
      board, the settings window and the other chords responsive while its probe hangs.
  [ ] A USB drive present, then unplugged: green, then red.
  [ ] The exchange server with the right fingerprint: green; with a wrong one: red "fingerprint changed".
  [ ] The 2-second row ("TL fast 2s") refreshes about every two seconds while "TL hourly" does not.
  [ ] "TL tcp closed" with "failures before down" 3: gray/yellow first, red only after the third failure.
  [ ] Pull the network cable: everything goes stale and gray with the "offline" note, and NO balloon storm;
      plug it back: states return and the journal has the offline/online markers.
  [ ] Sleep and wake: checks resume at once, no balloon for targets that are fine.
  [ ] A balloon appears on a down and on an up for a row with notifications on, named by label only.
  [ ] Open the journal in Notepad after a down/up cycle and again after a restart: one line per change, no line
      for an unchanged state, the file is plain text and readable in every UI language.
  [ ] The board in light, dark and high contrast: marks (check / triangle / cross / circle) readable without
      colour; keyboard: chord focuses the board, arrows move, Enter opens details, F5 checks, Esc closes.
  [ ] Drag the board, pin it, restart: position and pin are remembered.
  [ ] The board has its own title area: drag by it; the four icon buttons - add, edit (the pencil: the settings window
      on the traffic light page), pin (filled = on top, outlined = off) and close - each work and show a tooltip.
      "Add" asks for an address and a label only; the new tile appears with that label and the first check runs.
  [ ] Columns on the settings page: 3 columns and 4, 5, 6 targets are two rows at once; 1 column is one tall strip;
      a change refits the window at once and the window never leaves the screen; many targets scroll, none are lost.
  [ ] Tile size "small": bare squares with only the state glyph - no label, no status line, no title text; the name
      and the state appear on hover; the four buttons still fit. The medium step has the status line again.
  [ ] The board is blank or absent in a screenshot and in a screen share (excluded from capture, D9).
  [ ] A long language (de, hi), Arabic (right to left), 125 % / 200 % scaling: nothing clipped, tiles mirrored.
  [ ] Settings page: the module switch off stops every probe within a few seconds (traffic-light.log and the
      journal stop growing); the tray summary and the tray item disappear.
'@ -ForegroundColor Yellow

    if ($Interactive) { [void](Read-Host 'Press Enter when the checklist is done - the settings and the journal are put back after that') }
}
catch {
    $failures += "$_"
}
finally {
    Stop-StatusServer $ok200
    Stop-StatusServer $s401
    # 4: always put everything back, whatever happened above.
    try { & $restore; Write-Host 'Settings and journal restored from the backup.' -ForegroundColor Cyan }
    catch { $failures += "RESTORE FAILED - the backup is $backupPath ($_)" }
}

foreach ($f in $failures) { Add-CheckFinding -Severity fail -Name 'traffic-light-failure' -Reason $f }
Complete-Check
