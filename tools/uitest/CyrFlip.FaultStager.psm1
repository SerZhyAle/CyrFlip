# CyrFlip.FaultStager - the fault stager of the runtime quality audit (ticket S0061, section 5.4).
#
# Import-Module .\tools\uitest\CyrFlip.FaultStager.psm1 -Force
#
# The faults the ER-3 (wrapped errors) and NB (no system-wide blocking) tables are measured under. Each
# helper stages ONE fault and hands back what undoes it; a caller undoes it in finally. Nothing here
# touches the product's code - CyrFlip is shown a hostile world from outside:
#
#   * Start-AuditClipboardHolder   - another PROCESS holds the clipboard open for N ms (a message-only
#                                    window of its own), so the 12 x 15 ms retry budget meets a hostile holder.
#   * Suspend-/Resume-AuditProcess - NtSuspendProcess / NtResumeProcess: the "stuck" state of the NB table.
#     Invoke-AuditSuspended          (CyrFlip itself, or a target app while a delay-rendered paste is pending).
#   * Start-AuditKillOnSignal      - TerminateProcess the moment a named event is set: the "killed" state,
#                                    timed to a point of a scene (mid-cursor-replacement, mid-paste).
#   * Start-AuditSlowHttpServer    - a loopback HTTP server that answers one byte per interval (the
#                                    translator's first-token budget and 30 s idle timer).
#   * Start-AuditSilentServer      - accepts TCP connections and never says a word (an endpoint that hangs
#                                    after the connect, which a blackholed address does not reproduce).
#     Get-AuditBlackholeEndpoint     - 10.255.255.1, the address S0030 used: the connect itself never ends.
#   * Set-AuditRegistryDeny        - a deny-write ACE on HKCU\Software\CyrFlip (or one of its subkeys) for
#                                    the current user; Restore-AuditRegistryAcl puts the saved DACL back.
#   * Set-AuditFolderDeny          - the same for a data folder: a read-only data folder.
#   * New-AuditSmallVolume         - a 32 MB VHD mounted on an empty folder, Write-AuditVolumeFiller fills it:
#                                    a FULL data folder. Needs elevation (diskpart); refuses without it.
#   * New-AuditExchangeCorpus      - "CyrFlip Exchange Text v1" files: valid, hostile (one per problem kind of
#                                    the reader) and 25 MB + 1, with the outcome each one should produce.
#
# Safety rules - do not "simplify" them away:
#   * The registry deny refuses HKCU\Software\CyrFlip unless an audit profile is staged (the recovery
#     manifest of CyrFlip.AuditRig exists, so the user's key is backed up and will be put back); any other
#     key must be a throwaway HKCU\Software\CyrFlip-audit-* key.
#   * The folder deny and the VHD mount refuse any path outside %TEMP% - the staged profile lives there.
#   * The clipboard holder never reads, empties or writes the clipboard: it only keeps it open. Its process
#     ends by itself after -HoldMs even when the caller dies, and the open is released with its thread.
#   * The helper process is compiled once with the .NET Framework's own csc.exe (C# 5) into
#     %TEMP%\CyrFlip-fault-helper\<hash>\ - pwsh 7's Add-Type cannot emit an executable.
#   * C# in this file is C# 5: the module also loads under Windows PowerShell 5.1.

$script:NativeReady = $false

function Initialize-FaultStagerNative {
    if ($script:NativeReady) { return }
    if (-not ('CyrFlipFault.Freezer' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CyrFlipFault
{
    public static class Freezer
    {
        [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr process);
        [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        private static int Call(int pid, bool suspend)
        {
            IntPtr h = OpenProcess(PROCESS_SUSPEND_RESUME, false, (uint)pid);
            if (h == IntPtr.Zero) throw new InvalidOperationException("OpenProcess(" + pid + ") failed, Win32 error " + Marshal.GetLastWin32Error());
            try { return suspend ? NtSuspendProcess(h) : NtResumeProcess(h); }
            finally { CloseHandle(h); }
        }

        /// <summary>Suspends every thread of the process; returns the NTSTATUS (0 = success).</summary>
        public static int Suspend(int pid) { return Call(pid, true); }
        public static int Resume(int pid) { return Call(pid, false); }

        /// <summary>True when every thread of the process is waiting with the reason Suspended.</summary>
        public static bool IsSuspended(int pid)
        {
            using (Process p = Process.GetProcessById(pid))
            {
                p.Refresh();
                if (p.Threads.Count == 0) return false;
                foreach (ProcessThread t in p.Threads)
                {
                    if (t.ThreadState != System.Diagnostics.ThreadState.Wait) return false;
                    if (t.WaitReason != ThreadWaitReason.Suspended) return false;
                }
                return true;
            }
        }
    }

    public static class Acl
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SetNamedSecurityInfo(string objectName, int objectType, uint securityInfo,
            IntPtr owner, IntPtr group, byte[] dacl, IntPtr sacl);
        private const int SE_REGISTRY_KEY = 4;
        private const uint DACL_SECURITY_INFORMATION = 0x4;
        private const uint PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000;
        private const uint UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000;

        /// <summary>
        /// Puts the DACL of an SDDL string on a registry key ("CURRENT_USER\Software\..") - WRITE_DAC is the
        /// only right it needs, and the system carries inheritable ACEs to the existing subkeys and takes
        /// them off again. Returns the Win32 error (0 = success).
        /// </summary>
        public static uint SetRegistryDacl(string objectName, string sddl)
        {
            System.Security.AccessControl.RawSecurityDescriptor sd = new System.Security.AccessControl.RawSecurityDescriptor(sddl);
            byte[] dacl = new byte[sd.DiscretionaryAcl.BinaryLength];
            sd.DiscretionaryAcl.GetBinaryForm(dacl, 0);
            bool isProtected = (sd.ControlFlags & System.Security.AccessControl.ControlFlags.DiscretionaryAclProtected) != 0;
            uint info = DACL_SECURITY_INFORMATION | (isProtected ? PROTECTED_DACL_SECURITY_INFORMATION : UNPROTECTED_DACL_SECURITY_INFORMATION);
            return SetNamedSecurityInfo(objectName, SE_REGISTRY_KEY, info, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
        }
    }

    public static class ClipboardProbe
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();

        /// <summary>Opens and at once closes the clipboard - reads, empties and writes nothing.</summary>
        public static bool CanOpen()
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            CloseClipboard();
            return true;
        }
    }

    public static class Disk
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeToCaller, out ulong total, out ulong totalFree);

        /// <summary>Free bytes for this user on the volume holding the folder - a mount point included.</summary>
        public static long FreeBytes(string folder)
        {
            ulong free, total, totalFree;
            string path = folder.EndsWith("\\") ? folder : folder + "\\";
            if (!GetDiskFreeSpaceEx(path, out free, out total, out totalFree))
                throw new InvalidOperationException("GetDiskFreeSpaceEx(" + path + ") failed, Win32 error " + Marshal.GetLastWin32Error());
            return (long)free;
        }
    }

    public sealed class KillOnSignal : IDisposable
    {
        private readonly EventWaitHandle _signal;
        private readonly ManualResetEvent _cancel = new ManualResetEvent(false);
        private readonly ManualResetEvent _done = new ManualResetEvent(false);
        private readonly int _pid;
        public readonly string EventName;
        public volatile bool Killed;
        public double FiredToKilledMs = -1;

        public KillOnSignal(int pid, string eventName)
        {
            _pid = pid;
            EventName = eventName;
            _signal = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Name = "CyrFlipFault.KillOnSignal";
            t.Start();
        }

        private void Run()
        {
            try
            {
                int which = WaitHandle.WaitAny(new WaitHandle[] { _signal, _cancel });
                if (which != 0) return;
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    using (Process p = Process.GetProcessById(_pid))
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                        Killed = p.HasExited;
                    }
                }
                catch (ArgumentException) { Killed = true; }   // already gone
                catch (InvalidOperationException) { Killed = true; }
                FiredToKilledMs = sw.Elapsed.TotalMilliseconds;
            }
            finally { _done.Set(); }
        }

        /// <summary>The signal a scene raises at the instant it wants the target dead.</summary>
        public void Fire() { _signal.Set(); }

        public bool WaitDone(int timeoutMs) { return _done.WaitOne(timeoutMs); }

        public void Dispose()
        {
            _cancel.Set();
            _done.WaitOne(6000);
            _signal.Dispose();
        }
    }

    /// <summary>
    /// A loopback HTTP/1.1 server that answers slowly: the status line and headers after
    /// FirstByteDelayMs (or one byte per interval with TrickleHeaders), then the body one byte per
    /// ByteIntervalMs. Every request gets the same answer; Connection: close.
    /// </summary>
    public sealed class SlowHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private volatile bool _stopped;
        private int _requests;
        public readonly int FirstByteDelayMs;
        public readonly int ByteIntervalMs;
        public readonly bool TrickleHeaders;
        public readonly byte[] Body;
        public readonly string ContentType;
        public int Port;
        public int Requests { get { return Interlocked.CompareExchange(ref _requests, 0, 0); } }

        public SlowHttpServer(int firstByteDelayMs, int byteIntervalMs, string body, string contentType, bool trickleHeaders)
        {
            FirstByteDelayMs = firstByteDelayMs;
            ByteIntervalMs = byteIntervalMs;
            TrickleHeaders = trickleHeaders;
            Body = Encoding.UTF8.GetBytes(body ?? "");
            ContentType = contentType;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Thread t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Name = "CyrFlipFault.SlowHttpServer";
            t.Start();
        }

        private void AcceptLoop()
        {
            while (!_stopped)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                lock (_clients) _clients.Add(c);
                Thread t = new Thread(Serve);
                t.IsBackground = true;
                t.Start(c);
            }
        }

        private bool Pause(int ms)
        {
            int left = ms;
            while (left > 0 && !_stopped) { int step = Math.Min(left, 25); Thread.Sleep(step); left -= step; }
            return !_stopped;
        }

        private void Serve(object state)
        {
            TcpClient c = (TcpClient)state;
            try
            {
                NetworkStream s = c.GetStream();
                c.ReceiveTimeout = 10000;
                byte[] buf = new byte[4096];
                StringBuilder head = new StringBuilder();
                while (head.Length < 65536 && head.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) < 0)
                {
                    int n = s.Read(buf, 0, buf.Length);
                    if (n <= 0) return;
                    head.Append(Encoding.ASCII.GetString(buf, 0, n));
                }
                Interlocked.Increment(ref _requests);
                byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: " + ContentType
                    + "\r\nContent-Length: " + Body.Length + "\r\nConnection: close\r\n\r\n");
                if (!Pause(FirstByteDelayMs)) return;
                if (TrickleHeaders)
                {
                    for (int i = 0; i < headers.Length; i++)
                    {
                        if (i > 0 && !Pause(ByteIntervalMs)) return;
                        s.WriteByte(headers[i]); s.Flush();
                    }
                }
                else { s.Write(headers, 0, headers.Length); s.Flush(); }
                for (int i = 0; i < Body.Length; i++)
                {
                    if (!Pause(ByteIntervalMs)) return;
                    s.WriteByte(Body[i]); s.Flush();
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                lock (_clients) _clients.Remove(c);
                c.Close();
            }
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
            lock (_clients) { foreach (TcpClient c in _clients) c.Close(); _clients.Clear(); }
        }
    }

    /// <summary>One "## Note" or "## Clipboard" block of an exchange file, faults included.</summary>
    public sealed class ExchangeBlock
    {
        public string Id { get; set; }          // a note: the GUID line (null = no Id line); a clip: null = the hash of Text
        public string Title { get; set; }
        public string Body { get; set; }
        public string Text { get; set; }
        public string CreatedAt { get; set; }
        public bool NoBody { get; set; }
        public bool Unclosed { get; set; }
    }

    /// <summary>
    /// Writes "CyrFlip Exchange Text v1" the way CyrFlipExchangeWriter does (LF framing, a three-backtick
    /// fence, sha256: ids) - in C#, because the corpus holds 25 MB and ten thousand blocks.
    /// </summary>
    public static class ExchangeText
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string Id(string text)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Utf8.GetBytes(text));
                StringBuilder hex = new StringBuilder("sha256:", 71);
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        private static void Line(StringBuilder sb, string s) { sb.Append(s).Append('\n'); }

        public static string Build(string marker, ExchangeBlock[] notes, ExchangeBlock[] clips)
        {
            StringBuilder sb = new StringBuilder();
            Line(sb, marker);
            Line(sb, "Exported-At: 2026-10-29T12:00:00Z");
            Line(sb, "Application: CyrFlip");
            Line(sb, "Includes: quick-notes, clipboard-history");
            Line(sb, "");
            Line(sb, "# Quick notes");
            foreach (ExchangeBlock n in notes ?? new ExchangeBlock[0])
            {
                Line(sb, "");
                Line(sb, "## Note");
                if (!string.IsNullOrEmpty(n.Id)) Line(sb, "Id: " + n.Id);
                if (n.Title != null) Line(sb, "Title: " + n.Title);
                Line(sb, "Kind: text");
                Line(sb, "Created-At: " + (n.CreatedAt ?? "2026-10-01T08:00:00Z"));
                Line(sb, "Updated-At: 2026-10-02T08:00:00Z");
                Line(sb, "");
                if (n.NoBody) continue;
                Line(sb, "```text");
                sb.Append(n.Body ?? "").Append('\n');
                if (!n.Unclosed) Line(sb, "```");
            }
            Line(sb, "");
            Line(sb, "# Clipboard history");
            foreach (ExchangeBlock c in clips ?? new ExchangeBlock[0])
            {
                string text = c.Text ?? "";
                Line(sb, "");
                Line(sb, "## Clipboard");
                Line(sb, "Id: " + (c.Id ?? Id(text)));
                Line(sb, "Copied-At: 2026-10-03T08:00:00Z");
                Line(sb, "Pinned: false");
                Line(sb, "");
                Line(sb, "```text");
                sb.Append(text).Append('\n');
                Line(sb, "```");
            }
            return sb.ToString();
        }

        /// <summary>count history entries "entry 0" .. "entry n-1".</summary>
        public static ExchangeBlock[] Numbered(int count)
        {
            ExchangeBlock[] list = new ExchangeBlock[count];
            for (int i = 0; i < count; i++) { ExchangeBlock b = new ExchangeBlock(); b.Text = "entry " + i; list[i] = b; }
            return list;
        }

        /// <summary>
        /// A valid file of exactly targetBytes: whole history entries of 60 000 characters, the last one
        /// sized to land on the byte - between 60 000 and 120 000 characters, so under the 128 KB history
        /// cap and the size is the file's only fault.
        /// </summary>
        public static string Oversize(string marker, long targetBytes)
        {
            const int chunk = 60000;
            long empty = Utf8.GetByteCount(Build(marker, null, null));
            ExchangeBlock probe = new ExchangeBlock(); probe.Text = new string('p', chunk);
            long one = Utf8.GetByteCount(Build(marker, null, new[] { probe })) - empty;
            int count = (int)((targetBytes - empty) / one);
            List<ExchangeBlock> list = new List<ExchangeBlock>(count);
            for (int i = 0; i < count - 1; i++)
            {
                ExchangeBlock b = new ExchangeBlock();
                b.Text = new string('p', chunk - 6) + i.ToString("D6");
                list.Add(b);
            }
            long used = empty + (long)(count - 1) * one;
            ExchangeBlock last = new ExchangeBlock();
            last.Text = new string('q', (int)(chunk + targetBytes - used - one));
            list.Add(last);
            string text = Build(marker, null, list.ToArray());
            if (Utf8.GetByteCount(text) != targetBytes) throw new InvalidOperationException("oversize: " + Utf8.GetByteCount(text) + " bytes, " + targetBytes + " asked");
            return text;
        }
    }

    /// <summary>Accepts every connection and never reads or writes a byte, until disposed.</summary>
    public sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private volatile bool _stopped;
        public int Port;
        public int Accepted { get { lock (_clients) return _accepted; } }
        private int _accepted;

        public SilentServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Thread t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Name = "CyrFlipFault.SilentServer";
            t.Start();
        }

        private void AcceptLoop()
        {
            while (!_stopped)
            {
                try
                {
                    TcpClient c = _listener.AcceptTcpClient();
                    lock (_clients) { _clients.Add(c); _accepted++; }
                }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
            lock (_clients) { foreach (TcpClient c in _clients) c.Close(); _clients.Clear(); }
        }
    }
}
'@
    }
    $script:NativeReady = $true
}

# ----- the helper process ---------------------------------------------------------------------------

$script:HelperSource = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

// CyrFlipFaultHelper <mode> <maxMs> <readyEvent> [stopEvent]
//   clip-hold - open the clipboard with a message-only window of its own (nothing read or written),
//               signal ready, hold it until the stop event or maxMs, close it. OpenClipboard(NULL) is
//               NOT used: measured on Windows 11 26200, a clipboard opened with no window does not stop
//               another process from opening it - the "holder" would hold nothing.
//   idle      - signal ready and sleep until the stop event or maxMs (a process to suspend or kill).
// Exit codes: 0 done, 3 the clipboard could not be opened within 2 s, 4 no window, 64 bad arguments.
static class Program
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    static int Main(string[] args)
    {
        if (args.Length < 3) return 64;
        int maxMs;
        if (!int.TryParse(args[1], out maxMs) || maxMs < 0) return 64;
        EventWaitHandle ready = EventWaitHandle.OpenExisting(args[2]);
        EventWaitHandle stop = args.Length > 3 ? EventWaitHandle.OpenExisting(args[3]) : new ManualResetEvent(false);
        if (args[0] == "clip-hold")
        {
            IntPtr owner = CreateWindowEx(0, "STATIC", "CyrFlipFaultHolder", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (owner == IntPtr.Zero) return 4;
            bool opened = false;
            for (int i = 0; i < 200 && !(opened = OpenClipboard(owner)); i++) Thread.Sleep(10);
            if (!opened) { DestroyWindow(owner); return 3; }
            try { ready.Set(); stop.WaitOne(maxMs); }
            finally { CloseClipboard(); DestroyWindow(owner); }
            return 0;
        }
        if (args[0] == "idle")
        {
            ready.Set();
            stop.WaitOne(maxMs);
            return 0;
        }
        return 64;
    }
}
'@

function Get-FaultHelperExe {
    <#
    .SYNOPSIS
    The helper process's exe, compiled once per source revision with the .NET Framework's csc.exe.
    #>
    [CmdletBinding()]
    param()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($script:HelperSource))[0..5] | ForEach-Object { $_.ToString('x2') }) }
    finally { $sha.Dispose() }
    $dir = Join-Path ([IO.Path]::GetTempPath()) "CyrFlip-fault-helper\$hash"
    $exe = Join-Path $dir 'CyrFlipFaultHelper.exe'
    if (Test-Path $exe) { return $exe }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (-not (Test-Path $csc)) { throw 'csc.exe of the .NET Framework 4.x was not found - the helper process cannot be built.' }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $src = Join-Path $dir 'CyrFlipFaultHelper.cs'
    Set-Content -Path $src -Value $script:HelperSource -Encoding UTF8
    $out = & $csc /nologo /target:exe /optimize+ "/out:$exe" $src 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "csc.exe failed (exit $LASTEXITCODE): $out" }
    $exe
}

function Start-FaultHelper {
    param([Parameter(Mandatory)][ValidateSet('clip-hold', 'idle')][string]$Mode, [int]$MaxMs, [int]$ReadyTimeoutMs = 5000)
    $exe = Get-FaultHelperExe
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 12)
    $readyName = "Local\CyrFlipFault-ready-$tag"
    $stopName = "Local\CyrFlipFault-stop-$tag"
    $ready = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, $readyName)
    $stop = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, $stopName)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = "$Mode $MaxMs $readyName $stopName"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $ready.WaitOne($ReadyTimeoutMs)) {
        $code = if ($p.HasExited) { $p.ExitCode } else { 'still running' }
        try { if (-not $p.HasExited) { $p.Kill() } } catch { }
        $ready.Dispose(); $stop.Dispose()
        throw "The fault helper ($Mode) did not get ready within $ReadyTimeoutMs ms (exit: $code)."
    }
    $ready.Dispose()
    [pscustomobject]@{ Mode = $Mode; Process = $p; Id = $p.Id; StopEvent = $stop; StartedUtc = [DateTime]::UtcNow }
}

function Stop-FaultHelper {
    param([Parameter(Mandatory)]$Helper, [int]$GraceMs = 2000)
    try { $Helper.StopEvent.Set() | Out-Null } catch { }
    $p = $Helper.Process
    try { if (-not $p.WaitForExit($GraceMs)) { $p.Kill(); [void]$p.WaitForExit(2000) } } catch { }
    try { $Helper.StopEvent.Dispose() } catch { }
    if ($p.HasExited) { $p.ExitCode } else { -1 }
}

# ----- clipboard ------------------------------------------------------------------------------------

function Start-AuditClipboardHolder {
    <#
    .SYNOPSIS
    Another process opens the clipboard and keeps it open for -HoldMs (or until Stop-AuditClipboardHolder).
    Nothing on the clipboard is read or changed. Returns the holder; pair with Stop-AuditClipboardHolder.
    #>
    [CmdletBinding()]
    param([ValidateRange(50, 120000)][int]$HoldMs = 5000)
    Start-FaultHelper -Mode clip-hold -MaxMs $HoldMs
}

function Stop-AuditClipboardHolder {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Holder)
    Stop-FaultHelper -Helper $Holder
}

function Test-AuditClipboardOpenable {
    <# .SYNOPSIS True when this process can open the clipboard right now (it closes it at once). #>
    Initialize-FaultStagerNative
    [CyrFlipFault.ClipboardProbe]::CanOpen()
}

# ----- stuck and killed -----------------------------------------------------------------------------

function Start-AuditIdleProcess {
    <# .SYNOPSIS A harmless process to suspend or kill (the helper in idle mode). Stop with Stop-AuditIdleProcess. #>
    [CmdletBinding()]
    param([int]$MaxMs = 60000)
    Start-FaultHelper -Mode idle -MaxMs $MaxMs
}

function Stop-AuditIdleProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Helper)
    Stop-FaultHelper -Helper $Helper
}

function Suspend-AuditProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Id)
    Initialize-FaultStagerNative
    $status = [CyrFlipFault.Freezer]::Suspend($Id)
    if ($status -ne 0) { throw ('NtSuspendProcess({0}) returned 0x{1:X8}' -f $Id, $status) }
}

function Resume-AuditProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Id)
    Initialize-FaultStagerNative
    $status = [CyrFlipFault.Freezer]::Resume($Id)
    if ($status -ne 0) { throw ('NtResumeProcess({0}) returned 0x{1:X8}' -f $Id, $status) }
}

function Test-AuditProcessSuspended {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Id)
    Initialize-FaultStagerNative
    [CyrFlipFault.Freezer]::IsSuspended($Id)
}

function Invoke-AuditSuspended {
    <#
    .SYNOPSIS
    Suspends the process for -Milliseconds and resumes it in finally, running -During (if given) while it
    is suspended. Returns the milliseconds it was actually suspended.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Id, [Parameter(Mandatory)][int]$Milliseconds, [scriptblock]$During)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Suspend-AuditProcess -Id $Id
    try {
        if ($During) { & $During }
        $left = $Milliseconds - [int]$sw.ElapsedMilliseconds
        if ($left -gt 0) { Start-Sleep -Milliseconds $left }
    }
    finally { Resume-AuditProcess -Id $Id }
    [Math]::Round($sw.Elapsed.TotalMilliseconds, 0)
}

function Start-AuditKillOnSignal {
    <#
    .SYNOPSIS
    Arms a hard kill (TerminateProcess) of -Id that fires the moment the returned object's Fire() is
    called, or the named event it reports is set by another process. Dispose() disarms it.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Id)
    Initialize-FaultStagerNative
    $name = 'Local\CyrFlipFault-kill-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
    New-Object CyrFlipFault.KillOnSignal($Id, $name)
}

# ----- network --------------------------------------------------------------------------------------

function Get-AuditBlackholeEndpoint {
    <#
    .SYNOPSIS
    An address whose TCP connect never completes (S0030's dead share). -Port picks the service:
    11434 is Ollama's, 445 SMB.
    #>
    [CmdletBinding()]
    param([int]$Port = 11434, [ValidateSet('http', 'tcp', 'unc')][string]$Form = 'http')
    switch ($Form) {
        'http' { "http://10.255.255.1:$Port" }
        'tcp' { "10.255.255.1:$Port" }
        'unc' { '\\10.255.255.1\x' }
    }
}

function Start-AuditSlowHttpServer {
    <#
    .SYNOPSIS
    A loopback HTTP server that answers every request with -Body, the headers after -FirstByteDelayMs and
    then one byte every -ByteIntervalMs (-TrickleHeaders: the headers too). The default body is an Ollama
    /api/tags answer with one model, so the translator's probe gets far enough to wait on it.
    Returns the server (.Port, .Requests); Stop-AuditServer ends it.
    #>
    [CmdletBinding()]
    param(
        [int]$FirstByteDelayMs = 0,
        [int]$ByteIntervalMs = 1000,
        [string]$Body = '{"models":[{"name":"aya-expanse:8b","model":"aya-expanse:8b","size":1}]}',
        [string]$ContentType = 'application/json',
        [switch]$TrickleHeaders
    )
    Initialize-FaultStagerNative
    $server = New-Object CyrFlipFault.SlowHttpServer($FirstByteDelayMs, $ByteIntervalMs, $Body, $ContentType, [bool]$TrickleHeaders)
    $server | Add-Member -NotePropertyName Url -NotePropertyValue ("http://127.0.0.1:{0}" -f $server.Port) -PassThru
}

function Start-AuditSilentServer {
    <# .SYNOPSIS A loopback TCP listener that accepts and never answers. Returns (.Port, .Accepted); Stop-AuditServer ends it. #>
    [CmdletBinding()]
    param()
    Initialize-FaultStagerNative
    $server = New-Object CyrFlipFault.SilentServer
    $server | Add-Member -NotePropertyName Url -NotePropertyValue ("http://127.0.0.1:{0}" -f $server.Port) -PassThru
}

function Stop-AuditServer {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Server)
    $Server.Dispose()
}

# ----- denied writes --------------------------------------------------------------------------------

$script:AuditRecoveryManifest = Join-Path ([IO.Path]::GetTempPath()) 'CyrFlip-audit-recovery.json'

function Assert-AuditRegistryTarget([string]$Path) {
    $norm = $Path -replace '^Registry::HKEY_CURRENT_USER', 'HKCU:' -replace '^HKCU:(?!\\)', 'HKCU:\'
    if ($norm -match '^HKCU:\\Software\\CyrFlip(\\|$)') {
        if (-not (Test-Path $script:AuditRecoveryManifest)) {
            throw "Refusing to deny writes on ${norm}: no audit profile is staged (Enter-AuditProfile of CyrFlip.AuditRig backs the user's key up first)."
        }
    }
    elseif ($norm -notmatch '^HKCU:\\Software\\CyrFlip-audit-[^\\]+(\\|$)') {
        throw "Refusing to deny writes on ${norm}: only HKCU\Software\CyrFlip (inside a staged audit profile) or a throwaway HKCU\Software\CyrFlip-audit-* key."
    }
    if (-not (Test-Path $norm)) { throw "No such key: $norm" }
    $norm
}

function Assert-AuditTempTarget([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if (-not $full.StartsWith($temp + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing ${full}: only a folder inside %TEMP% (where the staged audit profile lives)."
    }
    $full
}

function Set-AuditRegistryDeny {
    <#
    .SYNOPSIS
    Adds a deny ACE (SetValue, CreateSubKey) for the current user to a key and every subkey; READ, DELETE
    and the permission rights stay granted, so the restore and the profile teardown still work.
    Registry ACLs are per key - "deny one value" is staged on the subkey that holds it (ExchangeSecrets).
    Returns the token for Restore-AuditRegistryAcl.
    #>
    [CmdletBinding()]
    param([string]$Path = 'HKCU:\Software\CyrFlip')
    $key = Assert-AuditRegistryTarget $Path
    $acl = Get-Acl -Path $key
    $original = $acl.GetSecurityDescriptorSddlForm([System.Security.AccessControl.AccessControlSections]::Access)
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $rights = [System.Security.AccessControl.RegistryRights]'SetValue, CreateSubKey'
    $rule = New-Object System.Security.AccessControl.RegistryAccessRule($sid, $rights,
        [System.Security.AccessControl.InheritanceFlags]::ContainerInherit,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Deny)
    $acl.AddAccessRule($rule)
    Set-AuditRegistryAccess -Path $key -Security $acl
    [pscustomobject]@{ Kind = 'registry'; Path = $key; Sddl = $original }
}

function Set-AuditRegistryAccess([string]$Path, $Security) {
    # Neither Set-Acl nor RegistryKey.SetAccessControl: both insist on a key opened for writing, which the
    # deny ACE itself refuses, so the restore would fail. SetNamedSecurityInfo needs WRITE_DAC alone, and
    # it carries the inheritable ACEs down to the existing subkeys and takes them off again.
    Initialize-FaultStagerNative
    $name = 'CURRENT_USER\' + ($Path -replace '^HKCU:\\', '')
    $sddl = $Security.GetSecurityDescriptorSddlForm([System.Security.AccessControl.AccessControlSections]::Access)
    $err = [CyrFlipFault.Acl]::SetRegistryDacl($name, $sddl)
    if ($err -ne 0) { throw "SetNamedSecurityInfo($name) failed, Win32 error $err" }
}

function Restore-AuditRegistryAcl {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Token)
    $acl = Get-Acl -Path $Token.Path
    $acl.SetSecurityDescriptorSddlForm($Token.Sddl, [System.Security.AccessControl.AccessControlSections]::Access)
    Set-AuditRegistryAccess -Path $Token.Path -Security $acl
}

function Set-AuditFolderDeny {
    <#
    .SYNOPSIS
    A read-only data folder: a deny ACE (write, append, delete of children) for the current user on the
    folder and everything inside it. Reading and the permission rights stay. Only inside %TEMP%.
    Returns the token for Restore-AuditFolderAcl.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $full = Assert-AuditTempTarget $Path
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw "No such folder: $full" }
    $acl = Get-Acl -LiteralPath $full
    $original = $acl.GetSecurityDescriptorSddlForm([System.Security.AccessControl.AccessControlSections]::Access)
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $rights = [System.Security.AccessControl.FileSystemRights]'WriteData, AppendData, WriteExtendedAttributes, WriteAttributes, DeleteSubdirectoriesAndFiles'
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, $rights,
        [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit',
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Deny)
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $full -AclObject $acl
    [pscustomobject]@{ Kind = 'folder'; Path = $full; Sddl = $original }
}

function Restore-AuditFolderAcl {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Token)
    $acl = Get-Acl -LiteralPath $Token.Path
    $acl.SetSecurityDescriptorSddlForm($Token.Sddl, [System.Security.AccessControl.AccessControlSections]::Access)
    Set-Acl -LiteralPath $Token.Path -AclObject $acl
}

# ----- a full volume --------------------------------------------------------------------------------

function Test-AuditElevated {
    $p = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
    $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-AuditDiskpart([string[]]$Lines) {
    $script = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-diskpart-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
    Set-Content -Path $script -Value $Lines -Encoding ASCII
    try {
        $out = & diskpart.exe /s $script 2>&1
        if ($LASTEXITCODE -ne 0) { throw "diskpart failed (exit $LASTEXITCODE): $($out -join ' | ')" }
        $out
    }
    finally { Remove-Item -Path $script -Force -ErrorAction SilentlyContinue }
}

function New-AuditSmallVolume {
    <#
    .SYNOPSIS
    Creates a -SizeMB NTFS volume in a VHD and mounts it on -MountPath (an empty folder inside %TEMP%,
    created when missing) - e.g. the staged profile's LocalAppData\CyrFlip before the instance starts, so
    every journal and log lands on it. Needs an elevated session (diskpart). Pair with Remove-AuditSmallVolume.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$MountPath, [ValidateRange(8, 1024)][int]$SizeMB = 32)
    if (-not (Test-AuditElevated)) { throw 'New-AuditSmallVolume needs an elevated session (diskpart attaches the VHD).' }
    $mount = Assert-AuditTempTarget $MountPath
    if (Test-Path -LiteralPath $mount) {
        if (@(Get-ChildItem -LiteralPath $mount -Force).Count -gt 0) { throw "The mount folder is not empty: $mount" }
    }
    else { New-Item -ItemType Directory -Path $mount | Out-Null }
    $vhd = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-audit-volume-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.vhdx')
    $token = [pscustomobject]@{ Kind = 'volume'; VhdPath = $vhd; MountPath = $mount; SizeMB = $SizeMB }
    try {
        [void](Invoke-AuditDiskpart @(
            "create vdisk file=""$vhd"" maximum=$SizeMB type=expandable",
            "select vdisk file=""$vhd""",
            'attach vdisk',
            'create partition primary',
            'format fs=ntfs quick label=CYRFAULT',
            "assign mount=""$mount"""))
    }
    catch {
        try { Remove-AuditSmallVolume -Token $token } catch { }
        throw
    }
    $token
}

function Remove-AuditSmallVolume {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Token)
    if (Test-Path -LiteralPath $Token.VhdPath) {
        try { [void](Invoke-AuditDiskpart @("select vdisk file=""$($Token.VhdPath)""", 'detach vdisk')) } catch { }
        Remove-Item -LiteralPath $Token.VhdPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $Token.MountPath) {
        if (@(Get-ChildItem -LiteralPath $Token.MountPath -Force -ErrorAction SilentlyContinue).Count -eq 0) {
            Remove-Item -LiteralPath $Token.MountPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-AuditVolumeFiller {
    <#
    .SYNOPSIS
    Fills the volume holding -Path until less than -LeaveFreeKB is free (one filler file, written in 1 MB
    then 4 KB steps). Returns the free bytes left. Only inside %TEMP% (i.e. a mounted audit volume).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path, [int]$LeaveFreeKB = 0)
    Initialize-FaultStagerNative
    $folder = Assert-AuditTempTarget $Path
    $filler = Join-Path $folder ('filler-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.bin')
    $big = New-Object byte[] (1MB)
    $small = New-Object byte[] 4096
    # bufferSize 1: no write may sit in a buffer and fail again at Dispose
    $fs = New-Object System.IO.FileStream($filler, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 1)
    try {
        foreach ($chunk in @($big, $small)) {
            while ($true) {
                try { $fs.Write($chunk, 0, $chunk.Length); $fs.Flush($true) }
                catch [System.IO.IOException] { break }
            }
        }
    }
    finally { try { $fs.Dispose() } catch { } }
    if ($LeaveFreeKB -gt 0) {
        $f = Get-Item -LiteralPath $filler
        $fs = [IO.File]::Open($filler, [IO.FileMode]::Open, [IO.FileAccess]::Write)
        try { $fs.SetLength([Math]::Max(0, $f.Length - $LeaveFreeKB * 1KB)) } finally { $fs.Dispose() }
    }
    [CyrFlipFault.Disk]::FreeBytes($folder)
}

# ----- exchange files -------------------------------------------------------------------------------

$script:ExchangeMarker = '# CyrFlip Exchange Text v1'
$script:ExchangeMaxFileBytes = 25L * 1024 * 1024
$script:ExchangeMaxClipboardBytes = 128 * 1024
$script:ExchangeMaxNoteBytes = 512 * 1024
$script:ExchangeMaxMetadataChars = 4096
$script:ExchangeMaxObjects = 10000

function Get-AuditExchangeId([string]$Text) {
    # The history's id of a text, as the file writes it: sha256: and the lower-case hex of its UTF-8 bytes.
    Initialize-FaultStagerNative
    [CyrFlipFault.ExchangeText]::Id($Text)
}

function New-ExchangeText {
    param([object[]]$Notes = @(), [object[]]$Clipboard = @(), [string]$Marker = $script:ExchangeMarker)
    Initialize-FaultStagerNative
    $n = [CyrFlipFault.ExchangeBlock[]]@(foreach ($x in $Notes) { [CyrFlipFault.ExchangeBlock]$x })
    $c = [CyrFlipFault.ExchangeBlock[]]@(foreach ($x in $Clipboard) { [CyrFlipFault.ExchangeBlock]$x })
    [CyrFlipFault.ExchangeText]::Build($Marker, $n, $c)
}

function Write-ExchangeFile([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function New-AuditExchangeCorpus {
    <#
    .SYNOPSIS
    Writes the exchange-file corpus of the ER-3 table into -Folder and returns one row per file: its name,
    what it stages, and the outcome the reader is expected to report - Fatal=<kind> (the file refused
    whole), Problem=<kind> (one block refused, the rest read), Accepted (no problem), or Observe (no
    expectation recorded: the run decides and the table says what happened).
    The kinds are the reader's ExchangeProblemKind names.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Folder)
    Initialize-FaultStagerNative
    New-Item -ItemType Directory -Path $Folder -Force | Out-Null
    $rows = New-Object System.Collections.Generic.List[object]
    $add = {
        param($Name, $Stages, $Expected, $Text)
        $p = Join-Path $Folder $Name
        Write-ExchangeFile $p $Text
        $rows.Add([pscustomobject]@{ File = $Name; Path = $p; Stages = $Stages; Expected = $Expected; Bytes = (Get-Item -LiteralPath $p).Length })
    }
    $note = @{ Id = '6f1c2a3b-0000-4000-8000-000000000001'; Title = 'Audit note'; Body = "line one`r`nline two`n`tindented" }
    $clip = @{ Text = 'audit clipboard text' }

    & $add 'valid.txt' 'one note (mixed CRLF/LF, a tab) and one history entry' 'Accepted' (New-ExchangeText -Notes @($note) -Clipboard @($clip))
    & $add 'valid-crlf.txt' 'the valid file converted to CRLF as a whole (an editor, a mail client)' 'Accepted' ((New-ExchangeText -Notes @($note) -Clipboard @($clip)) -replace "`n", "`r`n")
    & $add 'note-without-id.txt' 'a hand-written note with no Id line' 'Accepted' (New-ExchangeText -Notes @(@{ Title = 'hand-written'; Body = 'no id here' }))
    & $add 'bidi-nul.txt' 'a title with U+202E and a body with NUL and a bidi override' 'Accepted' (New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000002'; Title = "invoice $([char]0x202E)fdp.exe"; Body = "a$([char]0)b $([char]0x202E)txt.exe" }))
    & $add 'no-marker.txt' 'the first line is not the marker' 'Fatal=NoMarker' ((New-ExchangeText -Notes @($note)) -replace [regex]::Escape($script:ExchangeMarker), '# Some other file')
    & $add 'unknown-version.txt' 'a v2 marker' 'Fatal=UnknownVersion' (New-ExchangeText -Notes @($note) -Marker '# CyrFlip Exchange Text v2')
    & $add 'unclosed-fence.txt' 'a note whose fence is never closed (swallows the rest)' 'Problem=UnclosedFence' (New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000003'; Body = 'open'; Unclosed = $true }))
    & $add 'no-body.txt' 'a note heading with no fence before the next heading' 'Problem=NoBody' (New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000004'; NoBody = $true }, $note))
    & $add 'hash-mismatch.txt' 'a history entry whose text does not hash to its id' 'Problem=HashMismatch' (New-ExchangeText -Clipboard @(@{ Text = 'edited text'; Id = (Get-AuditExchangeId 'original text') }))
    & $add 'bad-field.txt' 'a note whose Created-At does not parse' 'Problem=BadField' (New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000005'; Body = 'x'; CreatedAt = 'yesterday' }))
    & $add 'metadata-too-long.txt' "a Title line over $script:ExchangeMaxMetadataChars characters" 'Problem=MetadataTooLong' (New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000006'; Title = ('t' * $script:ExchangeMaxMetadataChars); Body = 'x' }))
    & $add 'history-too-large.txt' "a history text of $script:ExchangeMaxClipboardBytes bytes + 1 (hash correct)" 'Problem=TooLarge' (New-ExchangeText -Clipboard @(@{ Text = ('h' * ($script:ExchangeMaxClipboardBytes + 1)) }))
    & $add 'note-too-large.txt' "a note body of $script:ExchangeMaxNoteBytes bytes + 1" 'Problem=TooLarge' (New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000007'; Body = ('n' * ($script:ExchangeMaxNoteBytes + 1)) }))
    & $add 'too-many-objects.txt' "$($script:ExchangeMaxObjects + 1) history entries" 'Problem=TooManyObjects' ([CyrFlipFault.ExchangeText]::Build($script:ExchangeMarker, @(), [CyrFlipFault.ExchangeText]::Numbered($script:ExchangeMaxObjects + 1)))

    # Invalid UTF-8 cannot go through a string: the bytes are spliced into a valid file.
    $bad = Join-Path $Folder 'invalid-utf8.txt'
    $text = New-ExchangeText -Notes @(@{ Id = '6f1c2a3b-0000-4000-8000-000000000008'; Body = 'BEFORE@@AFTER' })
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($text)
    $at = [Text.Encoding]::ASCII.GetString($bytes).IndexOf('@@')
    $bytes[$at] = 0xC3; $bytes[$at + 1] = 0x28   # a lead byte followed by a non-continuation byte
    [IO.File]::WriteAllBytes($bad, $bytes)
    $rows.Add([pscustomobject]@{ File = 'invalid-utf8.txt'; Path = $bad; Stages = 'a note body holding an invalid UTF-8 sequence'; Expected = 'Observe'; Bytes = $bytes.Length })

    $target = $script:ExchangeMaxFileBytes + 1
    & $add 'oversize.txt' "exactly $target bytes ($script:ExchangeMaxFileBytes + 1), otherwise valid" 'Fatal=FileTooLarge' ([CyrFlipFault.ExchangeText]::Oversize($script:ExchangeMarker, $target))

    $rows.ToArray()
}

Export-ModuleMember -Function Get-FaultHelperExe, Start-AuditClipboardHolder, Stop-AuditClipboardHolder, Test-AuditClipboardOpenable,
    Start-AuditIdleProcess, Stop-AuditIdleProcess, Suspend-AuditProcess, Resume-AuditProcess, Test-AuditProcessSuspended,
    Invoke-AuditSuspended, Start-AuditKillOnSignal, Get-AuditBlackholeEndpoint, Start-AuditSlowHttpServer,
    Start-AuditSilentServer, Stop-AuditServer, Set-AuditRegistryDeny, Restore-AuditRegistryAcl, Set-AuditFolderDeny,
    Restore-AuditFolderAcl, Test-AuditElevated, New-AuditSmallVolume, Remove-AuditSmallVolume, Write-AuditVolumeFiller,
    Get-AuditExchangeId, New-AuditExchangeCorpus
