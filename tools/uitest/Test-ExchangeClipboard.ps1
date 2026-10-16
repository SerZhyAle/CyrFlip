<#
.SYNOPSIS
The clipboard on the exchange server, live (ticket S0074): enroll two throw-away devices on YOUR account,
send a text, a long text, a picture and a file from one and get them on the other, then revoke both.

.DESCRIPTION
What xUnit cannot prove is the thing between two real ends: a real TLS handshake and pin, a real server's
`enrolled`, `list`, `clipboard-put` and `clipboard-get`, and a sealed value that one device seals and
another opens. This script drives the built exe's own client and service (reflection, as
Test-ClipboardFlip.ps1 does) against the server you name. It is meant for the owner: it needs an
account, and it is the check of the acceptance bullet "a live check against your own server".

WHAT IT TOUCHES - read before you run it
  * It creates TWO devices on your account ("CyrFlip live check A" and "... B") and revokes both at the
    end (Remove account does the same). If the script dies in the middle, remove them in the portal's
    device list or from CyrFlip's own Settings -> Exchange server.
  * It REPLACES the value in the account's clipboard slot on the server (the slot holds one value). The
    last value it leaves there is a harmless test text; anything you had there is gone.
  * It writes DPAPI-protected secrets of the two scratch devices under
    HKCU\Software\CyrFlip\ExchangeSecrets\<random id> and deletes them at the end. It never touches
    your real account list (nothing is saved to AppConfig), your real clipboard or your real logs.
  * The password is typed at the prompt (a SecureString), is sent once to the server you confirm, and is
    written nowhere: not to a file, not to the console. A .NET string cannot be wiped; the process
    ends when the script does.

Run it from Windows PowerShell (the exe is .NET Framework), on a desktop you are not typing on:
  powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-ExchangeClipboard.ps1

.PARAMETER Server
The connection string of your server (FMSEXCH1:..), or host or host:port. Asked for when omitted.

.PARAMETER Login
The account login. Asked for when omitted.

.PARAMETER SkipFile
Skip the 3 MB file round trip (slow links).

.EXAMPLE
powershell -sta -NoProfile -ExecutionPolicy Bypass -File tools\uitest\Test-ExchangeClipboard.ps1 -Server FMSEXCH1:x=nas.example.org:22022
#>
[CmdletBinding()]
param(
    [string]$Server = '',
    [string]$Login = '',
    [switch]$SkipFile,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force

$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}{2}" -f $mark, $name, $(if ($detail) { " - $detail" } else { '' })) -ForegroundColor $colour
    if (-not $ok) { $script:failures += $name }
}

# ---- reflection into the built exe --------------------------------------------------------------
$exe = Get-CyrFlipExe -Configuration $Configuration
$asm = [Reflection.Assembly]::LoadFrom($exe)
$bf = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
function T([string]$name) { $asm.GetType("CyrFlip.$name", $true) }
function Call($type, [string]$method, [object[]]$arguments, $target = $null) {
    $m = $type.GetMethods($bf) | Where-Object { $_.Name -eq $method -and $_.GetParameters().Count -eq $arguments.Count } | Select-Object -First 1
    if (-not $m) { throw "No $($type.Name).$method/$($arguments.Count)" }
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].psobject.BaseObject }
    }
    , $m.Invoke($target, $arguments)
}
function Get-Member2($object, [string]$name) {
    $type = $object.GetType()
    $p = $type.GetProperty($name, $bf)
    if ($p) { return $p.GetValue($object) }
    $f = $type.GetField($name, $bf)
    if ($f) { return $f.GetValue($object) }
    throw "No member $($type.Name).$name"
}
function Set-Member2($object, [string]$name, $value) {
    $type = $object.GetType()
    $p = $type.GetProperty($name, $bf)
    if ($p) { $p.SetValue($object, $value); return }
    $f = $type.GetField($name, $bf)
    if ($f) { $f.SetValue($object, $value); return }
    throw "No member $($type.Name).$name"
}
function New-Instance($type, [object[]]$arguments = @()) { [Activator]::CreateInstance($type, $bf, $null, $arguments, $null) }
function Await($task) {
    try { [void]$task.Wait() } catch { throw $_.Exception.InnerException }
    $task.Result
}

$Client = T 'ExchangeClient'; $Pin = T 'ExchangePin'; $JoinRequest = T 'ExchangeJoinRequest'
$Enrollment = T 'ExchangeEnrollment'; $Service = T 'ExchangeClipboardService'; $Payload = T 'ExchangeClipboardPayload'
$ConnString = T 'ExchangeConnectionString'; $AppConfigType = T 'AppConfig'; $Entry = T 'ExchangeAccountEntry'

# This check must leave CyrFlip's real logs alone.
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("CyrFlip-S0074-" + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $scratch)
(T 'DiagnosticLog').GetProperty('OverrideFolder', $bf).SetValue($null, $scratch)

$cts = New-Object Threading.CancellationTokenSource
$token = $cts.Token

# ---- what the person gave us ---------------------------------------------------------------------
Write-Host ''
Write-Host 'CyrFlip S0074 - live check of the clipboard on the exchange server' -ForegroundColor Cyan
Write-Host 'Two throw-away devices will be made on your account and revoked at the end.' -ForegroundColor Yellow
Write-Host 'The value in the account''s clipboard slot on the server WILL BE REPLACED.' -ForegroundColor Yellow
if ($Server.Length -eq 0) { $Server = Read-Host 'Server (FMSEXCH1:.. string, host or host:port)' }
$parsed = Call $ConnString 'Parse' @($Server)
$p0 = $parsed[0]
$host_ = ''; $port = 22022; $fp = ''
if ($Server.Trim().StartsWith('FMSEXCH', [StringComparison]::Ordinal)) {
    if (-not (Get-Member2 $p0 'Ok')) { Write-Host ("The string was refused: " + (Get-Member2 $p0 'Reason')) -ForegroundColor Red; exit 2 }
    $host_ = Get-Member2 $p0 'Host'; $port = [int](Get-Member2 $p0 'Port'); $fp = Get-Member2 $p0 'Fingerprint'
} else {
    $t = $Server.Trim()
    $i = $t.LastIndexOf(':')
    if ($i -gt 0 -and $t.IndexOf(':') -eq $i) { $host_ = $t.Substring(0, $i); $port = [int]$t.Substring($i + 1) } else { $host_ = $t }
}
if ($host_.Length -eq 0) { Write-Host 'No host.' -ForegroundColor Red; exit 2 }
if ($Login.Length -eq 0) { $Login = Read-Host 'Login' }
$secure = Read-Host 'Password (not shown, not stored)' -AsSecureString

# ---- 1. trust before a password (BN, AD step 2) --------------------------------------------------
Write-Host ''
Write-Host '1. The server''s certificate' -ForegroundColor Cyan
$client = New-Instance $Client
$noPin = [Reflection.BindingFlags]'Public,NonPublic,Static' | ForEach-Object { $Pin.GetField('None', $_).GetValue($null) }
$probe = Await ((Call $Client 'ProbeAsync' @($host_, $port, $noPin, $token) $client)[0])
$probeCode = Get-Member2 $probe 'Code'
Check 'a probe reaches the server and sends no byte' ($probeCode.Length -eq 0) $(if ($probeCode.Length) { "code: $probeCode" } else { '' })
if ($probeCode.Length) { Write-Host 'Cannot go on.' -ForegroundColor Red; exit 1 }
$trust = Get-Member2 $probe 'Value'
$kind = [string](Get-Member2 $trust 'Kind')
$leaf = Get-Member2 $trust 'ObservedLeaf'; $key = Get-Member2 $trust 'ObservedKey'
Write-Host ("   verdict: {0}" -f $kind)
Write-Host ("   leaf   : {0}" -f $leaf)
Write-Host ("   key    : {0}" -f $key)
$pin = $noPin
if ($kind -eq 'NeedsAcceptance') {
    if ($fp.Length -gt 0) { Check 'the string''s fingerprint matches the certificate' ($fp -eq $leaf) '' }
    $answer = Read-Host 'Does this match what your server shows? Type yes to trust and pin it'
    if ($answer -ne 'yes') { Write-Host 'Stopped.' -ForegroundColor Yellow; exit 1 }
    $pin = New-Instance $Pin @($leaf, $key)
} elseif ($kind -ne 'Platform') {
    Write-Host 'Unexpected verdict, stopping.' -ForegroundColor Red; exit 1
}
$confirm = Read-Host ("The password will go to {0}:{1}. Type yes to continue" -f $host_, $port)
if ($confirm -ne 'yes') { Write-Host 'Stopped.' -ForegroundColor Yellow; exit 1 }

# ---- 2. two devices ------------------------------------------------------------------------------
Write-Host ''
Write-Host '2. Two throw-away devices' -ForegroundColor Cyan
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($secure)
try { $password = [Runtime.InteropServices.Marshal]::PtrToStringUni($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr) }

$devices = @{}
function Join-Device([string]$tag) {
    $req = New-Instance $JoinRequest
    Set-Member2 $req 'Host' $host_
    Set-Member2 $req 'Port' $port
    Set-Member2 $req 'Pin' $pin
    Set-Member2 $req 'Login' $Login
    Set-Member2 $req 'Password' $password
    Set-Member2 $req 'DeviceName' ("CyrFlip live check $tag")
    $outcome = Await ((Call $Enrollment 'JoinAsync' @($client, $req, $null, $token))[0])
    $code = Get-Member2 $outcome 'Code'
    Check "device $tag enrolls" ($code.Length -eq 0) $(if ($code.Length) { "code: $code" } else { '' })
    if ($code.Length) { return $null }
    $entry = Get-Member2 $outcome 'Value'
    $cfg = [Activator]::CreateInstance($AppConfigType, $true)
    $list = Get-Member2 $cfg 'ExchangeClipboardAccounts'
    [void]$list.GetType().GetMethod('Add').Invoke($list, @($entry))
    Set-Member2 $cfg 'ExchangeClipboardReceivedFolder' (Join-Path $scratch "received-$tag")
    $svc = New-Instance $Service @($cfg, $client, [Action]{ })
    [pscustomobject]@{ Tag = $tag; Entry = $entry; Config = $cfg; Service = $svc }
}
try {
    $A = Join-Device 'A'
    $B = $null
    if ($A) { $B = Join-Device 'B' }
} finally { $password = $null }
if (-not $A -or -not $B) {
    Write-Host 'Enrolling failed - revoking what was made.' -ForegroundColor Red
}

function New-TextPayload([string]$text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $p = New-Instance $Payload
    Set-Member2 $p 'Kind' 'text'; Set-Member2 $p 'ClearSize' ([long]$bytes.Length); Set-Member2 $p 'Bytes' $bytes
    $p
}
function Send-From($device, $payload, [string]$label) {
    $report = Await ((Call $Service 'SendAsync' @($payload, $token) $device.Service)[0])
    $code = Get-Member2 $report 'Code'
    $entries = Get-Member2 $report 'Entries'
    $entryCode = ''
    if ($entries.Count -gt 0) { $entryCode = Get-Member2 $entries[0] 'Code' }
    $ok = ($code.Length -eq 0 -and $entryCode.Length -eq 0)
    Check "$label - sent from $($device.Tag)" $ok $(if (-not $ok) { "code: $code$entryCode" } else { '' })
    $ok
}
function Get-On($device, [string]$label) {
    $report = Await ((Call $Service 'FetchAsync' @($token) $device.Service)[0])
    $code = Get-Member2 $report 'Code'
    $value = Get-Member2 $report 'Value'
    $ok = ($code.Length -eq 0 -and $null -ne $value)
    Check "$label - received on $($device.Tag)" $ok $(if (-not $ok) { "code: $code" } else { '' })
    if ($ok) { $value } else { $null }
}
function Sha([byte[]]$bytes) { $h = [Security.Cryptography.SHA256]::Create(); try { [BitConverter]::ToString($h.ComputeHash($bytes)).Replace('-', '') } finally { $h.Dispose() } }

try {
    if ($A -and $B) {
        Write-Host ''
        Write-Host '3. Values A -> B' -ForegroundColor Cyan

        # a short text: non-ASCII, a surrogate pair, LF ends (CLIPBOARD-GUARD item O keeps the text as it is)
        $text = "CyrFlip S0074 live check`nline two: Привет, мир ✓ " + [char]::ConvertFromUtf32(0x1F600) + "`n"
        if (Send-From $A (New-TextPayload $text) 'short text') {
            $v = Get-On $B 'short text'
            if ($v) {
                $got = [Text.Encoding]::UTF8.GetString((Get-Member2 $v 'Bytes'))
                Check 'short text arrives byte for byte' ($got -ceq $text) ''
                Check 'short text names its kind and source' ((Get-Member2 $v 'Kind') -eq 'text' -and (Get-Member2 $v 'SourceDeviceName') -like 'CyrFlip live check A*') ('source: ' + (Get-Member2 $v 'SourceDeviceName'))
            }
        }

        # a text longer than one 64 KiB segment of the sealed stream
        $rand = New-Object Random 74
        $sb = New-Object Text.StringBuilder
        while ($sb.Length -lt 200000) { [void]$sb.Append([char](0x20 + $rand.Next(0, 0x5E))); if ($rand.Next(0, 70) -eq 0) { [void]$sb.Append("`n") } }
        $long = $sb.ToString()
        if (Send-From $A (New-TextPayload $long) '200 KB text') {
            $v = Get-On $B '200 KB text'
            if ($v) {
                $got = [Text.Encoding]::UTF8.GetString((Get-Member2 $v 'Bytes'))
                Check '200 KB text arrives byte for byte' ($got -ceq $long) ''
            }
        }

        # a picture
        Add-Type -AssemblyName System.Drawing
        $bmp = New-Object Drawing.Bitmap 48, 48
        for ($x = 0; $x -lt 48; $x++) { for ($y = 0; $y -lt 48; $y++) { $bmp.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, ($x * 5) % 256, ($y * 5) % 256, 128)) } }
        $ms = New-Object IO.MemoryStream
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        $png = $ms.ToArray(); $ms.Dispose()
        $pp = New-Instance $Payload
        Set-Member2 $pp 'Kind' 'image'; Set-Member2 $pp 'ClearSize' ([long]$png.Length); Set-Member2 $pp 'Bytes' $png; Set-Member2 $pp 'MimeType' 'image/png'
        if (Send-From $A $pp 'picture') {
            $v = Get-On $B 'picture'
            if ($v) { Check 'the picture arrives byte for byte' ((Sha (Get-Member2 $v 'Bytes')) -eq (Sha $png)) '' }
        }

        # a file, streamed from disk and into the received-files folder
        if (-not $SkipFile) {
            $src = Join-Path $scratch 'live-check-S0074.bin'
            $blob = New-Object byte[] (3 * 1024 * 1024); (New-Object Random 75).NextBytes($blob)
            [IO.File]::WriteAllBytes($src, $blob)
            $fp2 = New-Instance $Payload
            Set-Member2 $fp2 'Kind' 'file'; Set-Member2 $fp2 'ClearSize' ([long]$blob.Length); Set-Member2 $fp2 'FilePath' $src
            Set-Member2 $fp2 'Name' 'live-check-S0074.bin'; Set-Member2 $fp2 'MimeType' 'application/octet-stream'
            Set-Member2 $fp2 'FileLastWriteUtcTicks' ([IO.File]::GetLastWriteTimeUtc($src).Ticks)
            if (Send-From $A $fp2 '3 MB file') {
                $v = Get-On $B '3 MB file'
                if ($v) {
                    $path = Get-Member2 $v 'FilePath'
                    Check 'the file lands in the received folder' (($path.Length -gt 0) -and (Test-Path $path)) $path
                    if ($path -and (Test-Path $path)) {
                        Check 'the file arrives byte for byte' ((Sha ([IO.File]::ReadAllBytes($path))) -eq (Sha $blob)) ''
                        Check 'the file keeps its name' ([IO.Path]::GetFileName($path) -like 'live-check-S0074*') ([IO.Path]::GetFileName($path))
                    }
                }
            }
        }

        # leave a harmless value in the slot
        [void](Send-From $A (New-TextPayload 'CyrFlip S0074 live check - safe to delete') 'final marker')
    }
} finally {
    # ---- 4. revoke both, remove their secrets ------------------------------------------------
    Write-Host ''
    Write-Host '4. Revoke and clean up' -ForegroundColor Cyan
    foreach ($d in @($A, $B)) {
        if (-not $d) { continue }
        $revoked = $false
        try { $revoked = [bool](Await ((Call $Client 'RevokeAsync' @($d.Entry, $token) $client)[0])) } catch { }
        Check "device $($d.Tag) is revoked on the server" $revoked 'if this fails, remove "CyrFlip live check" from the account''s devices by hand'
        try { [void]$d.Entry.GetType().GetMethod('DeleteSecret', $bf).Invoke($d.Entry, @()) } catch { }
    }
    try { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}

Write-Host ''
if ($failures.Count -eq 0) { Write-Host 'Unattended half: all checks passed.' -ForegroundColor Green }
else { Write-Host ("Unattended half: {0} failed - {1}" -f $failures.Count, ($failures -join '; ')) -ForegroundColor Red }

Write-Host @'

The part only a person can check (CyrFlip ignores injected chords and shows balloons to a human):

  [ ] Settings -> Exchange server: switch the module on, Add account.. - the dialog shows the server and its
      certificate BEFORE the password field is used, and "The password will go to <host>" names the right host.
  [ ] Copy a text; tray -> "Send clipboard to server": the balloon says the clipboard is on the server.
  [ ] On a second device of the account (Android, or CyrFlip on another PC): "Get clipboard from server";
      paste - the same text. Then copy a picture and a single file and repeat.
  [ ] Assign a chord to Send and one to Get on the page, press each in Notepad and in a browser: the same results
      as the tray items; with the module off the keystroke passes through.
  [ ] Put "Send" / "Get" on the desktop (portable build): a double click runs the action with the tray app
      already running, and with no CyrFlip running it says so instead of starting a tray.
  [ ] Taskbar Jump List of the pinned CyrFlip icon shows "Send clipboard to server" / "Get clipboard from server".
  [ ] Stop the server's network or point at a wrong port: the balloon reads "what happened + what to try" and
      shows no code; the code is only in "Details" of the dialog.
  [ ] Cancel the clipboard transfer (tray) during a large file: both clipboards stay as they were.
  [ ] Remove account: the device disappears from the account's device list on the server.
'@
if ($failures.Count -gt 0) { exit 1 }
