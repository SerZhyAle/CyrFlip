# The quick notes, end to end - the part xUnit cannot reach.
#
# QuickNoteTests, QuickNotesOrderTests, QuickNotesStoreTests and QuickNotesServiceTests cover the
# model, the order, the journal and the CRUD, all against a fake cipher in a temp folder. Three
# things stay outside their reach, and this script is about those:
#
#   1. real DPAPI. The unit tests deliberately fake the cipher (it is a per-user machine secret and
#      a CI runner has a different account), so nothing in `dotnet test` ever proves that the real
#      journal on this machine encrypts and decrypts;
#   2. the journal surviving an actual restart of the actual app;
#   3. the chord, the window and the context-menu capture - which need a human at the keyboard.
#
#   .\tools\uitest\Test-QuickNotes.ps1               # the mechanical half, then the human half
#   .\tools\uitest\Test-QuickNotes.ps1 -NoUi         # the mechanical half only
#
# It never writes to the journal itself. The file is the user's real notes; a script that dropped a
# canary into it would corrupt what the app reads back, exactly as Test-SupportBundle refuses to
# write into clipboard-history.log.
[CmdletBinding()]
param(
    [switch]$NoUi,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $repo "src\CyrFlip\bin\$Configuration\net48\CyrFlip.exe"

Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force

$banner = Get-CyrFlipSubjectBanner -ExePath $exe
Set-CheckSubject "uitest/Test-QuickNotes $($banner.SubjectAxis)"
Write-Host "Subject: $($banner.BannerText)" -ForegroundColor Cyan

$dataFolder = Get-CyrFlipDataFolder   # portable, Store (S0016) or a pre-S0016 Store build
$logDir = $dataFolder.Path
$packaged = $dataFolder.Packaged

$journal = Join-Path $logDir 'quick-notes.log'
$backup = "$journal.bak"
$diag = Join-Path $logDir 'quick-notes-diagnostics.log'

"exe               : $exe"
"log folder        : $logDir$(if ($packaged) { '  (MSIX layout)' })"
"journal           : $journal"

# ---- Settings ----

$reg = 'HKCU:\Software\CyrFlip'
$enabled = 0
$chord = '(unset)'
if (Test-Path $reg) {
    $values = Get-ItemProperty $reg
    if ($null -ne $values.EnableQuickNotes) { $enabled = [int]$values.EnableQuickNotes }
    if ($values.QuickNotesHotkey) { $chord = $values.QuickNotesHotkey }
}
"feature enabled   : $enabled"
"chord             : $chord"
if ($enabled -ne 1) {
    Write-Warning 'The quick notes are off. Settings > Быстрые заметки > enable, then run this again.'
    Write-Warning 'While off there is deliberately no journal at all, so nothing below can be checked.'
    Add-CheckFinding -Severity notverified -Name 'feature-disabled' -Reason 'Quick notes feature is disabled in settings'
    Complete-Check
}

# ---- The journal on disk ----

if (-not (Test-Path $journal)) {
    Write-Warning 'No journal yet: create one note first, then run this again.'
    Add-CheckFinding -Severity notverified -Name 'journal-absent' -Reason 'No journal file found (create a note first)'
    Complete-Check
}

$lines = @(Get-Content $journal)
"journal lines     : $($lines.Count)"
"journal bytes     : $((Get-Item $journal).Length)"
"backup present    : $(Test-Path $backup)"

# Nothing readable beside the encrypted record. Every line must be base64 and nothing else: a record
# written in the clear by a future change would show up here and nowhere else.
$plain = @($lines | Where-Object { $_ -and $_ -notmatch '^[A-Za-z0-9+/]+={0,2}$' })
if ($plain.Count -gt 0) {
    Write-Host "FAIL: $($plain.Count) journal line(s) are not base64 - something is writing in the clear." -ForegroundColor Red
    Add-CheckFinding -Severity fail -Name 'cleartext-journal' -Reason "$($plain.Count) journal line(s) are not base64"
} else {
    'journal shape     : OK - every line is base64, nothing readable beside it'
}

# Real DPAPI, on this machine, against the real file. This is the check the unit tests cannot make.
Add-Type -AssemblyName System.Security
$decoded = 0
$failed = 0
foreach ($line in $lines) {
    if (-not $line) { continue }
    try {
        $bytes = [Convert]::FromBase64String($line)
        $clear = [Security.Cryptography.ProtectedData]::Unprotect($bytes, $null, 'CurrentUser')
        $json = [Text.Encoding]::UTF8.GetString($clear)
        if ($json -notmatch '"Action"') { throw 'decrypted, but not a record' }
        $decoded++
    } catch {
        $failed++
    }
}
"DPAPI decrypt     : $decoded ok, $failed unreadable"
if ($decoded -eq 0) {
    Write-Host 'FAIL: not one record decrypted under this Windows account.' -ForegroundColor Red
    Add-CheckFinding -Severity fail -Name 'dpapi-decrypt-zero' -Reason 'not one record decrypted under this Windows account'
} elseif ($failed -gt 0) {
    Write-Warning "$failed record(s) did not decrypt. The app skips those and keeps the rest;"
    Write-Warning "quick-notes-diagnostics.log should say the same number."
    Add-CheckFinding -Severity advisory -Name 'dpapi-partial-unreadable' -Reason "$failed record(s) did not decrypt"
}

if (Test-Path $diag) {
    'diagnostics tail  :'
    Get-Content $diag -Tail 5 | ForEach-Object { "  $_" }
}

# The diagnostics file must hold no note text. It is in the support bundle's whitelist, so anything
# that leaked into it would be mailed to the author.
if (Test-Path $diag) {
    $diagText = Get-Content $diag -Raw
    if ($diagText -match 'note text|Title|RawText') {
        Write-Host 'FAIL: the diagnostics log looks like it is carrying note content.' -ForegroundColor Red
        Add-CheckFinding -Severity fail -Name 'diag-log-leak' -Reason 'diagnostics log appears to carry note content'
    } else {
        'diagnostics shape : OK - counts and errors only, no note content'
    }
}

if ($NoUi) { Complete-Check }

# ---- The human half ----

@'

The rest needs you at the keyboard. In order:

  1. press the quick-notes chord (above) in any app
     -> the window opens with a new note and the caret already in the body;
  2. paste a fragment of code with tabs into it and close the window;
  3. select some text in an editor, Ctrl + right click, "Сохранить выделение в быстрые заметки"
     -> the window opens holding that text, and the clipboard manager did NOT record the copy
        (open the history and check the fragment is not the newest entry);
  4. type part of the fragment into the search box
     -> the note is found by its body, even though it has no name;
  5. exit CyrFlip from the tray and start it again, open the notes
     -> the fragment is there, character for character, tabs included.
  6. (S0035 QN2-6) turn the clipboard history on, copy three short texts, edit a note and at once
     sign out of Windows; sign back in
     -> the note edit and all three copies are there, and quick-notes-diagnostics.log shows no
        second "journal compacted" line for that sign-out.

Step 3 is the one worth being fussy about: the copy behind it is scaffolding, and the whole point
of the feature is that the history stays the record of what YOU copied.
'@

if (-not (Test-Path $exe)) {
    Write-Warning "Built exe not found at $exe - build first if you want to run it from here."
}

Complete-Check
