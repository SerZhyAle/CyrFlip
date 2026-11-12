<#
.SYNOPSIS
The fault stager's self-test (ticket S0061, section 5.4): every helper of CyrFlip.FaultStager.psm1 must
really stage its fault - and really undo it - before an ER-3 / NB row is measured with it.

.DESCRIPTION
A fault that was never shown to bite is a fault the table cannot rely on: a "clipboard holder" that
quietly failed to open the clipboard would turn every row measured under it into a pass. So each helper
is checked from the other side, against something harmless the script owns:

  * clipboard holder   - while held, this process cannot open the clipboard; after the stop, it can.
                         Nothing on the clipboard is read or changed (open + close only), for ~1 s.
  * suspend / resume   - an idle helper process: every thread Suspended, then none.
  * kill on a signal   - an idle helper process ends within 2 s of Fire().
  * slow HTTP server   - 5 body bytes at 150 ms: the answer takes >= 600 ms and arrives whole.
  * silent server      - the connect succeeds, a 300 ms read gets nothing.
  * blackhole address  - a 400 ms connect to 10.255.255.1 has not completed.
  * registry deny      - a throwaway HKCU\Software\CyrFlip-audit-* key: a write fails while denied,
                         succeeds after the restore, the DACL is byte-for-byte the original; key deleted.
  * folder deny        - the same for a folder inside %TEMP%.
  * small volume       - elevated sessions only: a 32 MB VHD mounted, filled to < 64 KB free, removed.
                         Not elevated: NOT VERIFIED, unless -SkipVolume says the row is not wanted.
  * exchange corpus    - every file written, the ids are the hashes of their texts, oversize.txt is
                         exactly 25 MB + 1. -WithReader also reads every file through the built exe's
                         own CyrFlipExchangeReader (Windows PowerShell 5.1, reflection) and compares the
                         outcome with the expectation the corpus records.

Exit codes (CHECK-VERDICT): 0 pass, 1 a helper did not stage or undo its fault, 2 not verified (a row
could not run here), 3 pass with advisories. The last stdout line is the verdict line.

.EXAMPLE
pwsh -File tools\uitest\Test-FaultStager.ps1 -SkipVolume
.EXAMPLE
pwsh -File tools\uitest\Test-FaultStager.ps1 -WithReader          # from an elevated session: every row
#>
[CmdletBinding()]
param(
    [switch]$SkipVolume,
    [switch]$WithReader,
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.FaultStager.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'uitest/Test-FaultStager'

$work = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-faultstager-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$total = [Diagnostics.Stopwatch]::StartNew()

$script:Failures = 0

function Step([string]$Name, [scriptblock]$Body) {
    $before = $script:Failures
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Body
        $verdict = if ($script:Failures -gt $before) { "FAIL" } else { "ok  " }
        Write-Host ("{0,-18} {1} {2,6:F0} ms" -f $Name, $verdict, $sw.Elapsed.TotalMilliseconds)
    }
    catch {
        $script:Failures++
        Add-CheckFinding -Severity fail -Name "$Name-error" -Reason "$Name threw: $($_.Exception.Message)"
        Write-Host ("{0,-18} FAIL {1}" -f $Name, $_.Exception.Message) -ForegroundColor Red
    }
}

function Fail([string]$Name, [string]$Reason) {
    $script:Failures++
    Add-CheckFinding -Severity fail -Name $Name -Reason $Reason
    Write-Host "  FAIL $Reason" -ForegroundColor Red
}

try {
    Write-Host "helper exe: $(Get-FaultHelperExe)" -ForegroundColor Cyan

    Step 'clipboard-holder' {
        if (-not (Test-AuditClipboardOpenable)) {
            Add-CheckFinding -Severity notverified -Name 'clipboard-busy' -Reason 'the clipboard was already held by another program before the test'
            return
        }
        $holder = Start-AuditClipboardHolder -HoldMs 5000
        try {
            if (Test-AuditClipboardOpenable) { Fail 'clipboard-not-held' 'this process opened the clipboard while the holder held it' }
            Start-Sleep -Milliseconds 300
            if (Test-AuditClipboardOpenable) { Fail 'clipboard-not-held-later' 'the hold did not last 300 ms' }
        }
        finally { $code = Stop-AuditClipboardHolder -Holder $holder }
        if ($code -ne 0) { Fail 'clipboard-holder-exit' "the holder process ended with $code (0 expected)" }
        $reopened = $false
        for ($i = 0; $i -lt 20 -and -not ($reopened = Test-AuditClipboardOpenable); $i++) { Start-Sleep -Milliseconds 25 }
        if (-not $reopened) { Fail 'clipboard-not-released' 'the clipboard stayed unopenable after the holder stopped' }
    }

    Step 'suspend-resume' {
        $idle = Start-AuditIdleProcess -MaxMs 20000
        try {
            if (Test-AuditProcessSuspended -Id $idle.Id) { Fail 'suspended-before' 'a fresh process already read as suspended' }
            $held = Invoke-AuditSuspended -Id $idle.Id -Milliseconds 200 -During {
                if (-not (Test-AuditProcessSuspended -Id $idle.Id)) { Fail 'not-suspended' 'NtSuspendProcess left a thread running' }
            }
            if ($held -lt 190) { Fail 'suspend-short' "suspended for $held ms (200 asked)" }
            if (Test-AuditProcessSuspended -Id $idle.Id) { Fail 'not-resumed' 'the process stayed suspended after the resume' }
        }
        finally { [void](Stop-AuditIdleProcess -Helper $idle) }
    }

    Step 'kill-on-signal' {
        $idle = Start-AuditIdleProcess -MaxMs 20000
        $kill = Start-AuditKillOnSignal -Id $idle.Id
        try {
            Start-Sleep -Milliseconds 100
            if ($idle.Process.HasExited) { Fail 'killed-early' 'the target died before the signal' }
            $kill.Fire()
            if (-not $kill.WaitDone(3000)) { Fail 'kill-timeout' 'the kill did not complete within 3 s' }
            elseif (-not $kill.Killed -or -not $idle.Process.WaitForExit(2000)) { Fail 'not-killed' 'the target survived the signal' }
            else { Write-Host ("  signal to exit {0:F0} ms" -f $kill.FiredToKilledMs) }
        }
        finally { $kill.Dispose(); [void](Stop-AuditIdleProcess -Helper $idle) }
    }

    Step 'slow-http' {
        $server = Start-AuditSlowHttpServer -ByteIntervalMs 150 -Body 'hello' -ContentType 'text/plain'
        try {
            $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $server.Port)
            try {
                $s = $c.GetStream()
                $req = [Text.Encoding]::ASCII.GetBytes("GET /api/tags HTTP/1.1`r`nHost: 127.0.0.1`r`n`r`n")
                $sw = [Diagnostics.Stopwatch]::StartNew()
                $s.Write($req, 0, $req.Length)
                $s.ReadTimeout = 5000
                $reader = New-Object System.IO.StreamReader($s, [Text.Encoding]::ASCII)
                $answer = $reader.ReadToEnd()
                $ms = $sw.Elapsed.TotalMilliseconds
            }
            finally { $c.Close() }
            if (-not $answer.StartsWith('HTTP/1.1 200')) { Fail 'slow-http-status' "unexpected answer: $($answer.Split("`n")[0])" }
            if (-not $answer.EndsWith("`r`n`r`nhello")) { Fail 'slow-http-body' 'the body did not arrive whole' }
            if ($ms -lt 600) { Fail 'slow-http-fast' ("5 bytes at 150 ms came in {0:F0} ms (>= 600 expected)" -f $ms) }
            if ($server.Requests -ne 1) { Fail 'slow-http-count' "the server counted $($server.Requests) requests (1 sent)" }
            Write-Host ("  5 bytes in {0:F0} ms" -f $ms)
        }
        finally { Stop-AuditServer -Server $server }
    }

    Step 'silent-server' {
        $server = Start-AuditSilentServer
        try {
            $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $server.Port)
            try {
                $s = $c.GetStream()
                $s.ReadTimeout = 300
                $got = $null
                try { $got = $s.ReadByte() } catch [System.IO.IOException] { $got = 'timeout' }
                if ($got -ne 'timeout') { Fail 'silent-server-spoke' "a read returned $got instead of timing out" }
            }
            finally { $c.Close() }
            if ($server.Accepted -lt 1) { Fail 'silent-server-accept' 'the connection was never accepted' }
        }
        finally { Stop-AuditServer -Server $server }
    }

    Step 'blackhole' {
        $ep = Get-AuditBlackholeEndpoint -Form tcp
        $hostName, $port = $ep.Split(':')
        $c = New-Object System.Net.Sockets.TcpClient
        try {
            $task = $c.ConnectAsync($hostName, [int]$port)
            $done = $task.Wait(400)
            if ($done -and -not $task.IsFaulted) { Fail 'blackhole-answered' "$ep accepted a connection - it is no blackhole on this network" }
            elseif ($done) {
                Add-CheckFinding -Severity advisory -Name 'blackhole-refused' -Reason "$ep was refused at once ($($task.Exception.InnerException.Message)) - on this network the address fails fast instead of hanging"
            }
        }
        finally { $c.Close() }
    }

    Step 'registry-deny' {
        $key = 'HKCU:\Software\CyrFlip-audit-selftest-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
        New-Item -Path $key -Force | Out-Null
        try {
            New-Item -Path "$key\ExchangeSecrets" -Force | Out-Null
            $before = (Get-Acl -Path $key).GetSecurityDescriptorSddlForm('Access')
            $token = Set-AuditRegistryDeny -Path $key
            try {
                $denied = $false
                try { Set-ItemProperty -Path $key -Name Probe -Value 1 -Type DWord -ErrorAction Stop } catch { $denied = $true }
                if (-not $denied) { Fail 'registry-not-denied' 'a value was written under the deny ACE' }
                $denied = $false
                try { Set-ItemProperty -Path "$key\ExchangeSecrets" -Name Probe -Value 1 -Type DWord -ErrorAction Stop } catch { $denied = $true }
                if (-not $denied) { Fail 'registry-subkey-not-denied' 'the deny ACE did not reach an existing subkey' }
            }
            finally { Restore-AuditRegistryAcl -Token $token }
            Set-ItemProperty -Path $key -Name Probe -Value 1 -Type DWord -ErrorAction Stop
            $after = (Get-Acl -Path $key).GetSecurityDescriptorSddlForm('Access')
            # SetNamedSecurityInfo marks the DACL auto-inherited (the "AI" flag after "D:"); the ACEs are what
            # must come back unchanged, and a protected DACL must stay protected.
            $aces = { param($s) ($s -replace '^D:([A-Z]*)', { if ($_.Groups[1].Value -match 'P') { 'D:P' } else { 'D:' } }) }
            if ((& $aces $after) -ne (& $aces $before)) { Fail 'registry-acl-not-restored' "DACL after restore differs: $after vs $before" }
            $refused = $false
            try { [void](Set-AuditRegistryDeny -Path 'HKCU:\Software\SomethingElse') } catch { $refused = $true }
            if (-not $refused) { Fail 'registry-guard' 'a key outside the audit namespace was accepted' }
            if (-not (Test-Path (Join-Path ([IO.Path]::GetTempPath()) 'CyrFlip-audit-recovery.json'))) {
                $refused = $false
                try { [void](Set-AuditRegistryDeny -Path 'HKCU:\Software\CyrFlip') } catch { $refused = $true }
                if (-not $refused) { Fail 'registry-guard-app-key' 'the real CyrFlip key was accepted with no audit profile staged' }
            }
        }
        finally {
            Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $key) { Fail 'registry-key-left' "the throwaway key could not be deleted: $key" }
        }
    }

    Step 'folder-deny' {
        $folder = Join-Path $work 'data'
        New-Item -ItemType Directory -Path $folder | Out-Null
        Set-Content -Path (Join-Path $folder 'existing.log') -Value 'x'
        $before = (Get-Acl -LiteralPath $folder).GetSecurityDescriptorSddlForm('Access')
        $token = Set-AuditFolderDeny -Path $folder
        try {
            $denied = $false
            try { [IO.File]::WriteAllText((Join-Path $folder 'new.log'), 'x') } catch { $denied = $true }
            if (-not $denied) { Fail 'folder-create-not-denied' 'a file was created under the deny ACE' }
            $denied = $false
            try { [IO.File]::AppendAllText((Join-Path $folder 'existing.log'), 'y') } catch { $denied = $true }
            if (-not $denied) { Fail 'folder-append-not-denied' 'an existing file was appended to under the deny ACE' }
        }
        finally { Restore-AuditFolderAcl -Token $token }
        [IO.File]::WriteAllText((Join-Path $folder 'new.log'), 'x')
        $after = (Get-Acl -LiteralPath $folder).GetSecurityDescriptorSddlForm('Access')
        if ($after -ne $before) { Fail 'folder-acl-not-restored' "DACL after restore differs: $after vs $before" }
        $refused = $false
        try { [void](Set-AuditFolderDeny -Path $env:USERPROFILE) } catch { $refused = $true }
        if (-not $refused) { Fail 'folder-guard' 'a folder outside %TEMP% was accepted' }
    }

    Step 'small-volume' {
        if ($SkipVolume) { Write-Host '  skipped (-SkipVolume)'; return }
        if (-not (Test-AuditElevated)) {
            Add-CheckFinding -Severity notverified -Name 'volume-needs-elevation' -Reason 'the 32 MB VHD row needs an elevated session (run elevated, or pass -SkipVolume to leave the row out)'
            Write-Host '  not verified: needs an elevated session'
            return
        }
        $mount = Join-Path $work 'mnt'
        $vol = New-AuditSmallVolume -MountPath $mount -SizeMB 32
        try {
            $free = Write-AuditVolumeFiller -Path $mount
            if ($free -gt 64KB) { Fail 'volume-not-full' "the filled volume still has $free bytes free" }
            $denied = $false
            try { [IO.File]::WriteAllBytes((Join-Path $mount 'more.bin'), (New-Object byte[] (128KB))) } catch { $denied = $true }
            if (-not $denied) { Fail 'volume-accepts-writes' 'a 128 KB write succeeded on the full volume' }
            Write-Host "  full: $free bytes free"
        }
        finally { Remove-AuditSmallVolume -Token $vol }
        if (Test-Path -LiteralPath $vol.VhdPath) { Fail 'volume-not-removed' "the VHD is still there: $($vol.VhdPath)" }
    }

    $script:corpus = $null
    Step 'exchange-corpus' {
        $script:corpus = New-AuditExchangeCorpus -Folder (Join-Path $work 'exchange')
        $over = $script:corpus | Where-Object File -eq 'oversize.txt'
        if ($over.Bytes -ne 25MB + 1) { Fail 'oversize-size' "oversize.txt is $($over.Bytes) bytes ($(25MB + 1) expected)" }
        $valid = Get-Content -Raw -LiteralPath ($script:corpus | Where-Object File -eq 'valid.txt').Path
        if (-not $valid.StartsWith("# CyrFlip Exchange Text v1`n")) { Fail 'valid-marker' 'valid.txt does not start with the v1 marker and an LF' }
        if ($valid -notmatch '(?m)^Id: (sha256:[0-9a-f]{64})\n') { Fail 'valid-id' 'valid.txt has no history id' }
        elseif ($Matches[1] -ne (Get-AuditExchangeId 'audit clipboard text')) { Fail 'valid-hash' 'the history id is not the hash of its text' }
        Write-Host ("  {0} files, {1:N0} bytes" -f $script:corpus.Count, ($script:corpus | Measure-Object Bytes -Sum).Sum)
    }

    if ($WithReader -and $script:corpus) {
        Step 'exchange-reader' {
            $exe = if ($ExePath) { (Resolve-Path $ExePath).Path } else { Join-Path $repo 'src\CyrFlip\bin\Release\net48\CyrFlip.exe' }
            if (-not (Test-Path $exe)) {
                Add-CheckFinding -Severity notverified -Name 'reader-not-built' -Reason "no build at $exe"
                return
            }
            $v = (Get-Item $exe).VersionInfo
            Write-Host "  reader: $exe $($v.FileVersion)"
            $list = Join-Path $work 'corpus-paths.txt'
            Set-Content -Path $list -Value ($script:corpus | ForEach-Object Path) -Encoding UTF8
            $probe = @'
param($Exe, $List)
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$reader = $asm.GetType('CyrFlip.CyrFlipExchangeReader', $true)
$read = $reader.GetMethod('ReadFile', [Reflection.BindingFlags]'Static,Public,NonPublic')
foreach ($p in Get-Content -Path $List -Encoding UTF8) {
    $r = $read.Invoke($null, @([string]$p))
    $kinds = @($r.Problems | ForEach-Object { $_.Kind.ToString() } | Sort-Object -Unique) -join ','
    '{0}|{1}|{2}|{3}' -f [IO.Path]::GetFileName($p), $(if ($null -ne $r.Fatal) { $r.Fatal.ToString() } else { '' }), $kinds, $r.ValidBlocks
}
'@
            $probeFile = Join-Path $work 'reader-probe.ps1'
            Set-Content -Path $probeFile -Value $probe -Encoding UTF8
            $lines = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $probeFile -Exe $exe -List $list 2>&1
            if ($LASTEXITCODE -ne 0) { Fail 'reader-run' "the reader probe failed: $($lines -join ' | ')"; return }
            foreach ($l in $lines) {
                $file, $fatal, $kinds, $valid = "$l".Split('|')
                $row = $script:corpus | Where-Object File -eq $file
                if (-not $row) { continue }
                $actual = if ($fatal) { "Fatal=$fatal" } elseif ($kinds) { "Problem=$kinds" } else { 'Accepted' }
                $note = "expected: $($row.Expected) | actual: $actual (valid blocks $valid)"
                if ($row.Expected -eq 'Observe') { Write-Host ("  {0,-22} observed {1}" -f $file, $note) }
                elseif ($row.Expected -ne $actual) { Fail "reader-$file" "$file - $note" }
                else { Write-Host ("  {0,-22} {1}" -f $file, $note) }
            }
        }
    }
}
finally {
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("total {0:F1} s" -f $total.Elapsed.TotalSeconds)
}

Complete-Check
