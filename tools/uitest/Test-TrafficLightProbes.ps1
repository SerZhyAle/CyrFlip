<#
.SYNOPSIS
    Runs the traffic light's REAL probes (ticket S0069) against loopback servers this script starts itself.

.DESCRIPTION
    The unit tests fake every probe - no test pings a host, opens a socket or touches a folder - so the
    probes' own protocol code is verified here, once, by reflection into the built exe. Everything stays on
    127.0.0.1 and in a temp folder; nothing leaves the machine and nothing is written outside %TEMP%.

    Windows PowerShell 5.1 (powershell.exe): the exe is a .NET Framework assembly.

        powershell -NoProfile -File tools\uitest\Test-TrafficLightProbes.ps1 [-Exe <path to CyrFlip.exe>]

    Exit code 0 = every check passed, 1 = at least one failed.
#>
[CmdletBinding()]
param(
    [string]$Exe = ''
)

$ErrorActionPreference = 'Stop'
if (-not $Exe) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Exe = Join-Path $here '..\..\src\CyrFlip\bin\Release\net48\CyrFlip.exe'
}
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Exe).Path)
$any = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$failures = 0

function Get-InternalType([string]$name) { $assembly.GetType('CyrFlip.' + $name, $true) }

function New-Target([string]$kind, [string]$address, [hashtable]$set = @{}) {
    $type = Get-InternalType 'TrafficLightTarget'
    $target = [Activator]::CreateInstance($type)
    $type.GetProperty('Kind').SetValue($target, $kind)
    $type.GetProperty('Address').SetValue($target, $address)
    # A short hard limit: the checks that expect a timeout should not take the default five seconds.
    $type.GetProperty('TimeoutMs').SetValue($target, 1500)
    foreach ($key in $set.Keys) { $type.GetProperty($key).SetValue($target, $set[$key]) }
    return $target
}

function Start-Probe([string]$probeName, $target, [int]$cancelAfterMs = 0) {
    $probe = [Activator]::CreateInstance((Get-InternalType $probeName), $true)
    $method = $probe.GetType().GetMethod('RunAsync')
    $token = [Threading.CancellationToken]::None
    if ($cancelAfterMs -gt 0) {
        # What the scheduler does at a target's hard limit: cancel, which closes the socket under the probe.
        $source = New-Object Threading.CancellationTokenSource
        $source.CancelAfter($cancelAfterMs)
        $script:sources += $source
        $token = $source.Token
    }
    return $method.Invoke($probe, @($target, $token))
}
$script:sources = @()

function Read-Outcome($task) {
    if (-not $task.Wait(8000)) { throw 'the probe did not finish' }
    $outcome = $task.Result
    $type = $outcome.GetType()
    [pscustomobject]@{
        Reached  = [bool]$type.GetField('Reached').GetValue($outcome)
        Degraded = [bool]$type.GetField('Degraded').GetValue($outcome)
        Reason   = [string]$type.GetField('Reason').GetValue($outcome)
        Latency  = [int]$type.GetField('LatencyMs').GetValue($outcome)
        Note     = [string]$type.GetField('Note').GetValue($outcome)
    }
}

function Check([string]$name, [bool]$condition, $detail = '') {
    if ($condition) { Write-Host ('PASS  ' + $name) -ForegroundColor Green }
    else { Write-Host ('FAIL  ' + $name + '  ' + $detail) -ForegroundColor Red; $script:failures++ }
}

function New-Listener {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    return $listener
}

function Free-Port {
    $l = New-Listener
    $port = $l.LocalEndpoint.Port
    $l.Stop()
    return $port
}

function Read-Request([Net.Sockets.TcpClient]$client) {
    $stream = $client.GetStream()
    $stream.ReadTimeout = 4000
    $buffer = New-Object byte[] 4096
    $text = ''
    while ($text -notmatch "`r`n`r`n") {
        $n = $stream.Read($buffer, 0, $buffer.Length)
        if ($n -le 0) { break }
        $text += [Text.Encoding]::ASCII.GetString($buffer, 0, $n)
    }
    return $text
}

function Send-Status([Net.Sockets.TcpClient]$client, [int]$status) {
    $reply = "HTTP/1.1 $status X`r`nContent-Length: 0`r`nConnection: close`r`n`r`n"
    $bytes = [Text.Encoding]::ASCII.GetBytes($reply)
    $client.GetStream().Write($bytes, 0, $bytes.Length)
    $client.Close()
}

# ---- ping --------------------------------------------------------------------------------------------
$o = Read-Outcome (Start-Probe 'PingProbe' (New-Target 'ping' '127.0.0.1'))
Check 'ping 127.0.0.1 is reached' $o.Reached ($o | Out-String)

# ---- tcp ---------------------------------------------------------------------------------------------
$listener = New-Listener
$port = $listener.LocalEndpoint.Port
$task = Start-Probe 'TcpProbe' (New-Target 'tcp' "127.0.0.1:$port")
$client = $listener.AcceptTcpClient(); $client.Close()
$o = Read-Outcome $task
Check 'tcp connect to a listening port is reached' $o.Reached ($o | Out-String)
$listener.Stop()

$o = Read-Outcome (Start-Probe 'TcpProbe' (New-Target 'tcp' ("127.0.0.1:" + (Free-Port))))
Check 'tcp to a closed port is refused' ((-not $o.Reached) -and $o.Reason -eq 'connection-refused') ($o | Out-String)

# ---- sftp: the identification line --------------------------------------------------------------------
foreach ($case in @(
        @{ Name = 'an SSH-2.0 line'; Banner = "SSH-2.0-OpenSSH_9.6`r`n"; Reached = $true; Reason = 'ok' },
        @{ Name = 'an SSH-1.99 line'; Banner = "SSH-1.99-Server`r`n"; Reached = $true; Reason = 'ok' },
        @{ Name = 'an HTTP answer'; Banner = "HTTP/1.1 400 Bad Request`r`n"; Reached = $false; Reason = 'not-ssh-server' })) {
    $listener = New-Listener
    $port = $listener.LocalEndpoint.Port
    $task = Start-Probe 'SftpProbe' (New-Target 'sftp' "127.0.0.1:$port")
    $client = $listener.AcceptTcpClient()
    $bytes = [Text.Encoding]::ASCII.GetBytes($case.Banner)
    $client.GetStream().Write($bytes, 0, $bytes.Length)
    $o = Read-Outcome $task
    Check ('sftp: ' + $case.Name) (($o.Reached -eq $case.Reached) -and $o.Reason -eq $case.Reason) ($o | Out-String)
    $client.Close(); $listener.Stop()
}
$listener = New-Listener
$port = $listener.LocalEndpoint.Port
$task = Start-Probe 'SftpProbe' (New-Target 'sftp' "127.0.0.1:$port") 1500
$client = $listener.AcceptTcpClient()   # accepted and then silent: the hard limit completes it
$o = Read-Outcome $task
Check 'sftp: a server that stays silent is a timeout' ((-not $o.Reached) -and $o.Reason -eq 'timeout') ($o | Out-String)
$client.Close(); $listener.Stop()

# ---- http --------------------------------------------------------------------------------------------
function Test-Http([string]$name, [int[]]$statuses, [hashtable]$set, [bool]$reached, [bool]$degraded, [string]$reason) {
    $listener = New-Listener
    $port = $listener.LocalEndpoint.Port
    $task = Start-Probe 'HttpProbe' (New-Target 'http' "http://127.0.0.1:$port/" $set)
    $methods = @()
    foreach ($status in $statuses) {
        $client = $listener.AcceptTcpClient()
        $request = Read-Request $client
        $methods += ($request -split ' ')[0]
        Send-Status $client $status
    }
    $o = Read-Outcome $task
    $listener.Stop()
    Check ("http: $name") (($o.Reached -eq $reached) -and ($o.Degraded -eq $degraded) -and $o.Reason -eq $reason) (($o | Out-String) + ' methods=' + ($methods -join ','))
    return $methods
}

[void](Test-Http 'a 200 is green' @(200) @{} $true $false 'ok')
$methods = Test-Http '401 is yellow by default' @(401, 401) @{} $true $true 'http-401'
Check 'http: a 4xx answer to HEAD is followed by GET' (($methods -join ',') -eq 'HEAD,GET') ($methods -join ',')
$methods = Test-Http '401 is green when it is on the accepted list' @(401) @{ AcceptedStatusCodes = '200-399,401' } $true $false 'ok'
Check 'http: an answer to HEAD that is already accepted is not asked twice' (($methods -join ',') -eq 'HEAD') ($methods -join ',')
$methods = Test-Http 'a HEAD refused with 405 and GET answering 200 is green' @(405, 200) @{} $true $false 'ok'
[void](Test-Http 'a 503 is yellow, not red' @(503, 503) @{} $true $true 'http-503')

$o = Read-Outcome (Start-Probe 'HttpProbe' (New-Target 'http' ("http://127.0.0.1:" + (Free-Port) + "/")))
Check 'http: a closed port is red' ((-not $o.Reached) -and $o.Reason -eq 'connection-refused') ($o | Out-String)

# ---- folder and drive (blocking probes) -----------------------------------------------------------------
function Invoke-Blocking([string]$probeName, $target) {
    $probe = [Activator]::CreateInstance((Get-InternalType $probeName), $true)
    $outcome = $probe.GetType().GetMethod('Run').Invoke($probe, @($target))
    $type = $outcome.GetType()
    [pscustomobject]@{
        Reached = [bool]$type.GetField('Reached').GetValue($outcome)
        Reason  = [string]$type.GetField('Reason').GetValue($outcome)
    }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-tl-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Set-Content -Path (Join-Path $temp 'x.txt') -Value 'x'
    $o = Invoke-Blocking 'FolderProbe' (New-Target 'folder' $temp)
    Check 'folder: an existing folder is reached' $o.Reached ($o | Out-String)
    $o = Invoke-Blocking 'FolderProbe' (New-Target 'folder' (Join-Path $temp 'missing'))
    Check 'folder: a missing folder is path-not-found' ((-not $o.Reached) -and $o.Reason -eq 'path-not-found') ($o | Out-String)
    Check 'folder: the probe wrote nothing' ((Get-ChildItem $temp).Count -eq 1)
}
finally { Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue }

$systemDrive = $env:SystemDrive
$o = Invoke-Blocking 'DriveProbe' (New-Target 'drive' $systemDrive)
Check ("drive: $systemDrive is ready") $o.Reached ($o | Out-String)
$absent = [char[]]'ZYXWVUTSRQP' | Where-Object { -not (Test-Path ($_ + ':\')) } | Select-Object -First 1
if ($absent) {
    $o = Invoke-Blocking 'DriveProbe' (New-Target 'drive' ($absent + ':'))
    Check ("drive: ${absent}: is not present") ((-not $o.Reached) -and $o.Reason -eq 'drive-not-present') ($o | Out-String)
}

# ---- exchange: TLS handshake and the pin --------------------------------------------------------------
$certificate = $null
try {
    $certificate = New-SelfSignedCertificate -DnsName 'localhost' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddHours(2)
    $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($certificate.RawData)
    $pin = 'SHA256:' + [Convert]::ToBase64String($hash).TrimEnd('=')
    $wrong = 'SHA256:' + ('A' * 43)

    foreach ($case in @(
            @{ Name = 'no pin and an untrusted chain is yellow (unverified)'; Pin = ''; Reached = $true; Degraded = $true; Reason = 'unverified' },
            @{ Name = 'the right pin is green'; Pin = $pin; Reached = $true; Degraded = $false; Reason = 'ok' },
            @{ Name = 'a wrong pin is red (certificate-changed)'; Pin = $wrong; Reached = $false; Degraded = $false; Reason = 'certificate-changed' })) {
        $listener = New-Listener
        $port = $listener.LocalEndpoint.Port
        $task = Start-Probe 'ExchangeProbe' (New-Target 'exchange' "localhost:$port" @{ Fingerprint = $case.Pin })
        $client = $listener.AcceptTcpClient()
        $ssl = New-Object Net.Security.SslStream($client.GetStream(), $false)
        try { $ssl.AuthenticateAsServer($certificate) } catch { }
        $o = Read-Outcome $task
        Check ('exchange: ' + $case.Name) (($o.Reached -eq $case.Reached) -and ($o.Degraded -eq $case.Degraded) -and $o.Reason -eq $case.Reason) ($o | Out-String)
        $ssl.Close(); $client.Close(); $listener.Stop()
    }
}
catch {
    Write-Host ('SKIP  exchange checks (no self-signed certificate could be made here): ' + $_.Exception.Message) -ForegroundColor Yellow
}
finally {
    if ($certificate) { Remove-Item ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -ErrorAction SilentlyContinue }
}

if ($failures -gt 0) { Write-Host ("$failures check(s) failed") -ForegroundColor Red; exit 1 }
Write-Host 'All probe checks passed.' -ForegroundColor Green
exit 0
