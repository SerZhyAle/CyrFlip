# Sign-out with CyrFlip running - the exit path xUnit cannot reach (spec S0003 LC-1 / LC-2).
#
# LifecycleTests proves the watcher answers WM_QUERYENDSESSION with "yes" and raises its callback
# once for WM_ENDSESSION(TRUE); FormCloseReasonTests proves no hidden window vetoes the session.
# Neither can prove what only a real sign-out shows: that Windows does not stop on "CyrFlip is
# preventing you from signing out", and that the cleanup actually reached the disk before the
# process was terminated. That takes a sign-out, so this script is two halves around one:
#
#   .\tools\uitest\Test-SessionEnd.ps1 -Before   # with CyrFlip running; then edit a note and sign out
#   .\tools\uitest\Test-SessionEnd.ps1 -After    # after signing back in, BEFORE starting CyrFlip
#
# Turn "Start with Windows" OFF for the run: with autostart on, CyrFlip rewrites layout.txt as soon
# as you sign in and the absence check below proves nothing (the script says so if it sees it).
#
# What it checks:
#   -Before  CyrFlip is running, layout.txt exists, and it records the quick-notes journal's size and
#            write time in %TEMP% for the second half;
#   -After   CyrFlip is not running, layout.txt and layout-klid.txt are gone (LAYOUT-SIGNAL rule 6),
#            and the journal was written after -Before ran - i.e. the pending note edit was flushed
#            by the session end rather than lost with the 750 ms debounce.
#
# It never writes into quick-notes.log: that file is the user's real notes.
[CmdletBinding(DefaultParameterSetName = 'Before')]
param(
    [Parameter(ParameterSetName = 'Before', Mandatory)][switch]$Before,
    [Parameter(ParameterSetName = 'After', Mandatory)][switch]$After
)

$ErrorActionPreference = 'Stop'

$dataDir = Join-Path $env:LOCALAPPDATA 'CyrFlip'
if (-not (Test-Path $dataDir) -and (Test-Path (Join-Path $env:ProgramData 'CyrFlip'))) {
    $dataDir = Join-Path $env:ProgramData 'CyrFlip'
}
$layout = Join-Path $dataDir 'layout.txt'
$layoutKlid = Join-Path $dataDir 'layout-klid.txt'
$journal = Join-Path $dataDir 'quick-notes.log'
$state = Join-Path $env:TEMP 'cyrflip-session-end-test.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$autostart = $null -ne (Get-ItemProperty -Path $runKey -Name 'CyrFlip' -ErrorAction SilentlyContinue)
$running = @(Get-Process -Name 'CyrFlip' -ErrorAction SilentlyContinue).Count -gt 0

"data folder : $dataDir"
"autostart   : $(if ($autostart) { 'ON - turn it off for a meaningful run' } else { 'off' })"
"CyrFlip     : $(if ($running) { 'running' } else { 'not running' })"

$failed = $false
function Fail([string]$message) { Write-Host "FAIL  $message" -ForegroundColor Red; $script:failed = $true }
function Pass([string]$message) { Write-Host "PASS  $message" -ForegroundColor Green }

if ($Before) {
    if (-not $running) { Fail 'CyrFlip is not running - start it first'; exit 1 }
    if (Test-Path $layout) { Pass 'layout.txt exists while CyrFlip runs' } else { Fail 'layout.txt is missing while CyrFlip runs' }
    if ($autostart) { Write-Warning 'Autostart is on: -After cannot prove the layout files were retracted.' }

    $record = [ordered]@{ taken = (Get-Date).ToUniversalTime().ToString('o'); journalBytes = -1; journalWrite = $null }
    if (Test-Path $journal) {
        $item = Get-Item $journal
        $record.journalBytes = $item.Length
        $record.journalWrite = $item.LastWriteTimeUtc.ToString('o')
    }
    $record | ConvertTo-Json | Set-Content -Path $state -Encoding UTF8
    ''
    'Now, by hand:'
    '  1. Open Settings once and close it (the window that used to veto the sign-out).'
    '  2. Open a quick note (its chord, Ctrl+Shift+Alt+N by default), type a line, and WITHOUT closing the window -'
    '  3. sign out at once (Start > account > Sign out), within a second of the last keystroke.'
    '  4. Watch the sign-out screen: "CyrFlip is preventing you from signing out" is a FAIL.'
    '  5. Sign back in and run this script with -After before starting CyrFlip.'
    exit ($(if ($failed) { 1 } else { 0 }))
}

if (-not (Test-Path $state)) { Fail 'no -Before record found - run with -Before first'; exit 1 }
$record = Get-Content $state -Raw | ConvertFrom-Json

if ($running) {
    Fail 'CyrFlip is already running - the absence check needs it not started (autostart off?)'
} else {
    if (Test-Path $layout) { Fail 'layout.txt survived the sign-out' } else { Pass 'layout.txt was retracted' }
    if (Test-Path $layoutKlid) { Fail 'layout-klid.txt survived the sign-out' } else { Pass 'layout-klid.txt was retracted' }
}

if (Test-Path $journal) {
    $item = Get-Item $journal
    $taken = [DateTime]::Parse($record.taken).ToUniversalTime()
    if ($item.LastWriteTimeUtc -gt $taken) {
        Pass "quick-notes.log was written after -Before ($($record.journalBytes) -> $($item.Length) bytes)"
    } else {
        Fail 'quick-notes.log was not written after -Before - the pending edit was lost'
    }
} else {
    Fail 'quick-notes.log does not exist - were the quick notes enabled?'
}

''
'By hand: start CyrFlip, open the quick notes - the line typed right before the sign-out must be there.'
'And: was there a "preventing you from signing out" screen? If yes, that is a FAIL too.'
Remove-Item $state -ErrorAction SilentlyContinue
exit ($(if ($failed) { 1 } else { 0 }))
