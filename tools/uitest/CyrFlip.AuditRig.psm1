# CyrFlip.AuditRig - the measurement kit of the runtime quality audit (ticket S0061, section 5.4).
#
# Import-Module .\tools\uitest\CyrFlip.AuditRig.psm1 -Force
#
# It measures a running process FROM OUTSIDE - nothing here instruments the product (S0061 D5):
#
#   * UiPingProbe   - SendMessageTimeout(WM_NULL) to a window of the target's UI thread, every few ms.
#                     "How long is the thread that owns both low-level hooks deaf to messages."
#   * KeyPingProbe   - the time a harmless injected key takes to leave SendInput. A WH_KEYBOARD_LL hook is
#                     called on its owner's thread and the injecting thread waits for it, so this is what
#                     ANOTHER process feels when CyrFlip's thread is busy - the user-visible form of the
#                     same stall, and the one that is also true when the stall is not in the ping's window.
#   * Resource sampler + growth verdict - GDI, USER, handles, threads, private bytes, managed heap (perf
#                     counter, as of the last GC), CPU time; slope after warm-up with a confidence band.
#   * Profile staging - a throwaway data folder (LOCALAPPDATA of the child only) and a throwaway
#                     HKCU\Software\CyrFlip, with the user's key, Run value and running instance put back
#                     in finally and a recovery manifest for a run that died.
#
# Load-bearing details - do not "simplify" them away:
#   * C# in this file is C# 5: the module also loads under Windows PowerShell 5.1.
#   * The probes sleep with timeBeginPeriod(1); without it Thread.Sleep(5) is 15.6 ms and the sampler's
#     own cadence would read as a gap.
#   * SMTO_ABORTIFHUNG is deliberately absent: a stalled thread is exactly what is measured.
#   * The key probe uses VK 0xE8 (unassigned). CyrFlip's own mask key is the same VK, but ITS injections
#     carry a tag in dwExtraInfo and the hook ignores every injected event anyway (LLKHF_INJECTED), so
#     the probe cannot trigger a chord - it can only measure the hook's round trip.
#   * Running the repo build re-points the user's HKCU\..\Run\CyrFlip value to that exe
#     (Autostart.SyncToThisExe). Enter-AuditProfile therefore saves and Exit-AuditProfile restores it.

$script:NativeReady = $false

function Initialize-AuditNative {
    if ($script:NativeReady) { return }
    if (-not ('CyrFlipAudit.UiPingProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CyrFlipAudit
{
    public sealed class Stats
    {
        public int Count;
        public double DurationMs;
        public double Min, Median, P95, P99, Max, Mean;
        public int Over50, Over100, Over150, Over250, Over300;
        public double FirstWindowAtMs = -1;   // ping probe: when a window of the target first answered
        public double NoWindowMs;             // ping probe: sampled time with no window to ping
        public double LoopJitterMaxMs;        // the probe's own cadence error, so a gap can be told from a starved probe
        public double[] WorstAtMs = new double[0];
        public double[] WorstMs = new double[0];
    }

    internal static class Native
    {
        public delegate bool EnumThreadProc(IntPtr h, IntPtr p);

        [DllImport("user32.dll")] public static extern bool EnumThreadWindows(uint tid, EnumThreadProc cb, IntPtr p);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }
    }

    public abstract class Probe : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<double> _at = new List<double>();
        private readonly List<double> _ms = new List<double>();
        private readonly Stopwatch _origin = new Stopwatch();
        private Thread _worker;
        private volatile bool _stop;
        private double _loopJitterMax;
        protected int IntervalMs;
        public DateTime OriginUtc { get; private set; }

        protected abstract void Loop();

        protected double NowMs { get { return _origin.Elapsed.TotalMilliseconds; } }
        protected bool Stopping { get { return _stop; } }

        protected void Record(double atMs, double ms)
        {
            lock (_gate) { _at.Add(atMs); _ms.Add(ms); }
        }

        protected void NoteLoopJitter(double ms)
        {
            if (ms > _loopJitterMax) _loopJitterMax = ms;
        }

        /// <summary>Milliseconds from this probe's start to a wall-clock instant, for lining up scene events.</summary>
        public double OffsetMs(DateTime utc) { return (utc - OriginUtc).TotalMilliseconds; }

        public void Start()
        {
            Native.timeBeginPeriod(1);
            OriginUtc = DateTime.UtcNow;
            _origin.Start();
            _stop = false;
            _worker = new Thread(Loop) { IsBackground = true, Name = GetType().Name };
            _worker.Priority = ThreadPriority.AboveNormal;
            _worker.Start();
        }

        public Stats Stop()
        {
            _stop = true;
            if (_worker != null) _worker.Join(40000);
            Native.timeEndPeriod(1);
            return Compute();
        }

        public double[][] Samples()
        {
            lock (_gate) { return new double[][] { _at.ToArray(), _ms.ToArray() }; }
        }

        protected virtual void Decorate(Stats s) { }

        private Stats Compute()
        {
            double[] at, ms;
            lock (_gate) { at = _at.ToArray(); ms = _ms.ToArray(); }
            var s = new Stats();
            s.Count = ms.Length;
            s.DurationMs = _origin.Elapsed.TotalMilliseconds;
            s.LoopJitterMaxMs = _loopJitterMax;
            if (ms.Length > 0)
            {
                double[] sorted = (double[])ms.Clone();
                Array.Sort(sorted);
                double sum = 0;
                for (int i = 0; i < ms.Length; i++)
                {
                    double v = ms[i];
                    sum += v;
                    if (v > 50) s.Over50++;
                    if (v > 100) s.Over100++;
                    if (v > 150) s.Over150++;
                    if (v > 250) s.Over250++;
                    if (v > 300) s.Over300++;
                }
                s.Min = sorted[0];
                s.Max = sorted[sorted.Length - 1];
                s.Mean = sum / ms.Length;
                s.Median = Pick(sorted, 0.50);
                s.P95 = Pick(sorted, 0.95);
                s.P99 = Pick(sorted, 0.99);
                int[] idx = new int[ms.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                Array.Sort(idx, (a, b) => ms[b].CompareTo(ms[a]));
                int n = Math.Min(10, idx.Length);
                s.WorstAtMs = new double[n];
                s.WorstMs = new double[n];
                for (int i = 0; i < n; i++) { s.WorstAtMs[i] = at[idx[i]]; s.WorstMs[i] = ms[idx[i]]; }
            }
            Decorate(s);
            return s;
        }

        private static double Pick(double[] sorted, double q)
        {
            int i = (int)Math.Ceiling(q * sorted.Length) - 1;
            if (i < 0) i = 0;
            if (i >= sorted.Length) i = sorted.Length - 1;
            return sorted[i];
        }

        public void Dispose() { if (!_stop) Stop(); }
    }

    /// <summary>WM_NULL round trips to the target's UI thread (the one the hooks share).</summary>
    public sealed class UiPingProbe : Probe
    {
        private readonly uint _pid;
        private IntPtr _hwnd = IntPtr.Zero;
        private double _firstWindowAt = -1;
        private double _noWindow;

        public UiPingProbe(uint pid, int intervalMs) { _pid = pid; IntervalMs = intervalMs < 1 ? 1 : intervalMs; }

        // The thread that owns the hooks is the process's main (earliest) thread in CyrFlip, but a
        // stand-in host need not follow that - so every thread is tried in start order and the first one
        // that owns a window is the UI thread.
        private IntPtr Resolve()
        {
            List<KeyValuePair<DateTime, uint>> threads = new List<KeyValuePair<DateTime, uint>>();
            try
            {
                Process proc = Process.GetProcessById((int)_pid);
                foreach (ProcessThread t in proc.Threads)
                {
                    try { threads.Add(new KeyValuePair<DateTime, uint>(t.StartTime, (uint)t.Id)); } catch { }
                }
            }
            catch { return IntPtr.Zero; }
            threads.Sort(delegate (KeyValuePair<DateTime, uint> a, KeyValuePair<DateTime, uint> b) { return a.Key.CompareTo(b.Key); });
            foreach (KeyValuePair<DateTime, uint> t in threads)
            {
                IntPtr found = IntPtr.Zero;
                Native.EnumThreadWindows(t.Value, delegate (IntPtr h, IntPtr p) { found = h; return false; }, IntPtr.Zero);
                if (found != IntPtr.Zero) return found;
            }
            return IntPtr.Zero;
        }

        protected override void Loop()
        {
            double plannedNext = NowMs;
            while (!Stopping)
            {
                if (_hwnd == IntPtr.Zero || !Native.IsWindow(_hwnd))
                {
                    double t = NowMs;
                    _hwnd = Resolve();
                    if (_hwnd == IntPtr.Zero)
                    {
                        Thread.Sleep(5);
                        _noWindow += NowMs - t;
                        continue;
                    }
                }
                double at = NowMs;
                IntPtr r;
                Native.SendMessageTimeout(_hwnd, 0, IntPtr.Zero, IntPtr.Zero, 0, 30000, out r);
                double ms = NowMs - at;
                if (_firstWindowAt < 0) _firstWindowAt = at;
                Record(at, ms);
                double wait = IntervalMs - ms;
                if (wait > 0) Thread.Sleep((int)wait);
                double expected = at + (ms > IntervalMs ? ms : IntervalMs);
                NoteLoopJitter(Math.Max(0, NowMs - expected));
            }
        }

        protected override void Decorate(Stats s)
        {
            s.FirstWindowAtMs = _firstWindowAt;
            s.NoWindowMs = _noWindow;
        }
    }

    /// <summary>How long an injected no-op key takes to leave SendInput, i.e. what the low-level hooks cost another process.</summary>
    public sealed class KeyPingProbe : Probe
    {
        public KeyPingProbe(int intervalMs) { IntervalMs = intervalMs < 1 ? 1 : intervalMs; }

        protected override void Loop()
        {
            Native.INPUT[] inputs = new Native.INPUT[2];
            for (int i = 0; i < 2; i++)
            {
                inputs[i].type = 1; // INPUT_KEYBOARD
                inputs[i].u.ki.wVk = 0xE8; // unassigned
                inputs[i].u.ki.dwFlags = (uint)(i == 0 ? 0 : 2); // KEYEVENTF_KEYUP
            }
            int size = Marshal.SizeOf(typeof(Native.INPUT));
            while (!Stopping)
            {
                double at = NowMs;
                Native.SendInput(2, inputs, size);
                double ms = NowMs - at;
                Record(at, ms);
                double wait = IntervalMs - ms;
                if (wait > 0) Thread.Sleep((int)wait);
                double expected = at + (ms > IntervalMs ? ms : IntervalMs);
                NoteLoopJitter(Math.Max(0, NowMs - expected));
            }
        }
    }

    /// <summary>
    /// The synthetic "weak machine" of S0061 D6: one spinning thread per logical core, at normal priority
    /// (the same as the app under test - a load at a lower priority would be politely ignored), busy for
    /// <c>busyPercent</c> of every 100 ms. CPU only: memory pressure on a machine somebody is working on
    /// would page their applications out, so the spec's "70 % of RAM" is deliberately not reproduced.
    /// </summary>
    public sealed class CpuLoad : IDisposable
    {
        private readonly List<Thread> _threads = new List<Thread>();
        private volatile bool _stop;

        public void Start(int threads, int busyPercent)
        {
            if (busyPercent < 1) return;
            if (busyPercent > 100) busyPercent = 100;
            for (int i = 0; i < threads; i++)
            {
                Thread t = new Thread(delegate ()
                {
                    Stopwatch sw = new Stopwatch();
                    while (!_stop)
                    {
                        sw.Restart();
                        while (sw.ElapsedMilliseconds < busyPercent && !_stop) { }
                        if (busyPercent < 100) Thread.Sleep(100 - busyPercent);
                    }
                });
                t.IsBackground = true;
                t.Name = "AuditCpuLoad";
                t.Start();
                _threads.Add(t);
            }
        }

        public void Dispose()
        {
            _stop = true;
            foreach (Thread t in _threads) t.Join(500);
            _threads.Clear();
        }
    }

    public static class Gui
    {
        /// <summary>"gdi user" of another process, or "" when it cannot be opened.</summary>
        public static string Counts(uint pid)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return "";
            try { return Native.GetGuiResources(h, 0) + " " + Native.GetGuiResources(h, 1); }
            finally { Native.CloseHandle(h); }
        }
    }
}
'@
    }
    $script:NativeReady = $true
}

# ----- probes -------------------------------------------------------------------------------------

function Start-AuditProbe {
    <#
    .SYNOPSIS
    Starts a latency probe: 'Ui' pings the UI thread of -ProcessId, 'Key' times an injected no-op key.
    The probe may start before the process exists (a startup scene) - Ui waits for the first window.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('Ui', 'Key')][string]$Kind,
        [int]$ProcessId = 0,
        [int]$IntervalMs = 20
    )
    Initialize-AuditNative
    $probe = if ($Kind -eq 'Ui') { New-Object CyrFlipAudit.UiPingProbe -ArgumentList ([uint32]$ProcessId), $IntervalMs }
             else { New-Object CyrFlipAudit.KeyPingProbe -ArgumentList $IntervalMs }
    $probe.Start()
    $probe
}

function Stop-AuditProbe {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Probe, [string]$CsvPath)
    $stats = $Probe.Stop()
    if ($CsvPath) {
        $s = $Probe.Samples()
        $dir = Split-Path -Parent $CsvPath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add('at_ms,latency_ms')
        for ($i = 0; $i -lt $s[0].Length; $i++) { $lines.Add(('{0:F1},{1:F2}' -f $s[0][$i], $s[1][$i])) }
        [IO.File]::WriteAllLines($CsvPath, $lines)
    }
    $stats
}

function Format-AuditStats {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Stats, [string]$Label = '')
    '{0}n={1} median={2:F1} p95={3:F1} p99={4:F1} max={5:F1} ms; >50:{6} >100:{7} >150:{8} >250:{9} >300:{10}; loop jitter max {11:F1} ms' -f `
        $(if ($Label) { "$Label : " } else { '' }), $Stats.Count, $Stats.Median, $Stats.P95, $Stats.P99, $Stats.Max,
        $Stats.Over50, $Stats.Over100, $Stats.Over150, $Stats.Over250, $Stats.Over300, $Stats.LoopJitterMaxMs
}

# ----- resources ----------------------------------------------------------------------------------

$script:HeapCounterInstance = @{}

function Get-ManagedHeapBytes {
    # '.NET CLR Memory\# Bytes in all Heaps' - as of the LAST GC, not after a forced one (a forced GC
    # would need a probe inside the process, which S0061 D5 declines). -1 = not a .NET process.
    param([int]$ProcessId)
    try {
        if (-not $script:HeapCounterInstance.ContainsKey($ProcessId)) {
            $cat = New-Object System.Diagnostics.PerformanceCounterCategory '.NET CLR Memory'
            $found = $null
            foreach ($inst in $cat.GetInstanceNames()) {
                $idc = New-Object System.Diagnostics.PerformanceCounter '.NET CLR Memory', 'Process ID', $inst, $true
                if ([int]$idc.NextValue() -eq $ProcessId) { $found = $inst; break }
            }
            $script:HeapCounterInstance[$ProcessId] = $found
        }
        $instance = $script:HeapCounterInstance[$ProcessId]
        if (-not $instance) { return -1 }
        $c = New-Object System.Diagnostics.PerformanceCounter '.NET CLR Memory', '# Bytes in all Heaps', $instance, $true
        [long]$c.NextValue()
    }
    catch { -1 }
}

function Get-AuditResourceSample {
    <#
    .SYNOPSIS
    One sample of everything that only grows: GDI, USER, all handles, threads, private bytes, working
    set, CPU seconds, managed heap. $null when the process is gone.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ProcessId, [switch]$NoHeap)
    Initialize-AuditNative
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { return $null }
    $counts = ([CyrFlipAudit.Gui]::Counts([uint32]$ProcessId)) -split ' '
    [pscustomobject]@{
        Time         = [DateTime]::UtcNow
        Gdi          = if ($counts.Count -eq 2) { [int]$counts[0] } else { -1 }
        User         = if ($counts.Count -eq 2) { [int]$counts[1] } else { -1 }
        Handles      = $p.HandleCount
        Threads      = $p.Threads.Count
        PrivateBytes = $p.PrivateMemorySize64
        WorkingSet   = $p.WorkingSet64
        CpuSeconds   = [Math]::Round($p.TotalProcessorTime.TotalSeconds, 3)
        ManagedHeap  = if ($NoHeap) { -1 } else { Get-ManagedHeapBytes -ProcessId $ProcessId }
    }
}

function Get-GrowthSlope {
    <#
    .SYNOPSIS
    Least-squares slope of a series after dropping a warm-up share, with a 95 % band (slope +- 2 SE).
    .DESCRIPTION
    The verdict is 'Growth' only when the whole band is above zero AND the fitted growth over the window
    reaches -MinTotal; anything else is 'Flat'. A series too short to say anything is 'Insufficient'.
    A single run cannot fail a band that is wide: 'Flat' with a wide band is reported in Band.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [double]$WarmupShare = 0.2,
        [double]$MinTotal = 1
    )
    $skip = [int][Math]::Floor($Values.Count * $WarmupShare)
    $y = @($Values | Select-Object -Skip $skip)
    $n = $y.Count
    if ($n -lt 8) { return [pscustomobject]@{ N = $n; Slope = 0.0; Low = 0.0; High = 0.0; Total = 0.0; Verdict = 'Insufficient' } }
    $mx = ($n - 1) / 2.0
    $my = ($y | Measure-Object -Average).Average
    $sxx = 0.0; $sxy = 0.0; $syy = 0.0
    for ($i = 0; $i -lt $n; $i++) {
        $dx = $i - $mx; $dy = $y[$i] - $my
        $sxx += $dx * $dx; $sxy += $dx * $dy; $syy += $dy * $dy
    }
    $slope = $sxy / $sxx
    $resid = [Math]::Max(0.0, ($syy - $slope * $sxy) / ($n - 2))
    $se = [Math]::Sqrt($resid / $sxx)
    $low = $slope - 2 * $se; $high = $slope + 2 * $se
    $total = $slope * ($n - 1)
    $verdict = if ($low -gt 0 -and $total -ge $MinTotal) { 'Growth' } else { 'Flat' }
    [pscustomobject]@{ N = $n; Slope = $slope; Low = $low; High = $high; Total = $total; Verdict = $verdict }
}

$script:GrowthFloors = @{ Gdi = 5; User = 5; Handles = 10; Threads = 2; PrivateBytes = 524288; ManagedHeap = 262144 }

function Test-AuditGrowth {
    <#
    .SYNOPSIS
    Verdict per property over a list of Get-AuditResourceSample results taken one per iteration batch.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Samples, [double]$WarmupShare = 0.2)
    foreach ($prop in 'Gdi', 'User', 'Handles', 'Threads', 'PrivateBytes', 'ManagedHeap') {
        $vals = @($Samples | ForEach-Object { [double]$_.$prop })
        if (($vals | Where-Object { $_ -lt 0 }).Count -eq $vals.Count) { continue }
        $g = Get-GrowthSlope -Values $vals -WarmupShare $WarmupShare -MinTotal $script:GrowthFloors[$prop]
        [pscustomobject]@{
            Property = $prop; First = $vals[0]; Last = $vals[-1]; Slope = [Math]::Round($g.Slope, 4)
            BandLow = [Math]::Round($g.Low, 4); BandHigh = [Math]::Round($g.High, 4)
            FittedTotal = [Math]::Round($g.Total, 1); N = $g.N; Verdict = $g.Verdict
        }
    }
}

function Invoke-AuditSoak {
    <#
    .SYNOPSIS
    Runs -Action -Iterations times against a live process and samples its resources every -SampleEvery
    iterations; returns the samples and the growth verdicts.
    .DESCRIPTION
    A sample is taken AFTER the action has settled (-SettleMs), at the same phase of every iteration, so
    growth is comparable. The first -WarmupShare of the samples is discarded by the fit.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [Parameter(Mandatory)][scriptblock]$Action,
        [int]$Iterations = 200,
        [int]$SampleEvery = 5,
        [int]$SettleMs = 50,
        [double]$WarmupShare = 0.2,
        [string]$CsvPath
    )
    $samples = New-Object System.Collections.Generic.List[object]
    for ($i = 1; $i -le $Iterations; $i++) {
        & $Action $i
        if ($i % $SampleEvery -eq 0) {
            Start-Sleep -Milliseconds $SettleMs
            $s = Get-AuditResourceSample -ProcessId $ProcessId
            if (-not $s) { throw "The process $ProcessId disappeared at iteration $i." }
            $s | Add-Member -NotePropertyName Iteration -NotePropertyValue $i
            $samples.Add($s)
        }
    }
    if ($CsvPath) {
        $dir = Split-Path -Parent $CsvPath
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $samples | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $CsvPath
    }
    [pscustomobject]@{ Samples = $samples; Growth = @(Test-AuditGrowth -Samples $samples -WarmupShare $WarmupShare) }
}

# ----- profile staging ----------------------------------------------------------------------------

$script:RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$script:AppKey = 'HKCU:\Software\CyrFlip'
$script:RecoveryPath = Join-Path ([IO.Path]::GetTempPath()) 'CyrFlip-audit-recovery.json'

function Get-AuditRepoRoot { Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

function Reset-AuditSystemCursors {
    if (-not ('CyrFlipAudit.Cursors' -as [type])) {
        Add-Type -Namespace CyrFlipAudit -Name Cursors -MemberDefinition '[DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint param, IntPtr pvParam, uint winIni);'
    }
    [void][CyrFlipAudit.Cursors]::SystemParametersInfo(0x57, 0, [IntPtr]::Zero, 0)   # SPI_SETCURSORS
}

function Restore-AuditProfile {
    <#
    .SYNOPSIS
    Puts the user's CyrFlip state back from the recovery manifest - the rescue for a run that died
    between Enter-AuditProfile and Exit-AuditProfile. Idempotent; does nothing without a manifest.
    #>
    [CmdletBinding()]
    param([switch]$KeepUserAppStopped)
    if (-not (Test-Path $script:RecoveryPath)) { return $false }
    $m = Get-Content -Raw -Path $script:RecoveryPath | ConvertFrom-Json

    # 1. nothing of ours may still run: ask politely over the pipe first (the app restores its own cursor
    #    and retracts its signal files), then force whatever is left.
    if (Get-Process CyrFlip -ErrorAction SilentlyContinue) {
        [void](Send-AuditPipeCommand -Line '/exit' -ConnectTimeoutMs 500)
        $deadline = [DateTime]::UtcNow.AddSeconds(4)
        while ((Get-Process CyrFlip -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    }
    $forced = $false
    Get-Process CyrFlip -ErrorAction SilentlyContinue | ForEach-Object {
        try { if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; $forced = $true } } catch { }
    }
    Start-Sleep -Milliseconds 300
    # A killed CyrFlip may have left the system I-beam replaced (SetSystemCursor is global); reloading
    # the scheme is the documented recovery - the same call the app itself makes on a clean exit.
    if ($forced) { Reset-AuditSystemCursors }

    # 2. the application key: the throwaway one goes, the user's comes back from its export.
    if (Test-Path $script:AppKey) { Remove-Item -Path $script:AppKey -Recurse -Force -ErrorAction SilentlyContinue }
    if ($m.KeyExisted -and $m.RegFile -and (Test-Path $m.RegFile)) {
        & reg.exe import $m.RegFile 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "reg import of $($m.RegFile) failed (exit $LASTEXITCODE); the manifest is kept at $script:RecoveryPath" }
    }

    # 3. the autostart value the repo build re-pointed.
    if ($m.RunExisted) {
        if (-not (Test-Path $script:RunKey)) { New-Item -Path $script:RunKey -Force | Out-Null }
        Set-ItemProperty -Path $script:RunKey -Name CyrFlip -Value $m.RunValue -Type String
    }
    else { Remove-ItemProperty -Path $script:RunKey -Name CyrFlip -ErrorAction SilentlyContinue }

    # 4. scratch folders, the manifest, and the user's own instance if it was running.
    if ($m.Root -and (Test-Path $m.Root)) { Remove-Item -Path $m.Root -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item -Path $script:RecoveryPath -Force -ErrorAction SilentlyContinue
    if ($m.UserWasRunning -and $m.UserExe -and (Test-Path $m.UserExe) -and -not $KeepUserAppStopped) {
        Start-Process -FilePath $m.UserExe | Out-Null
    }
    $true
}

function Enter-AuditProfile {
    <#
    .SYNOPSIS
    Stages a throwaway CyrFlip profile: the user's HKCU\Software\CyrFlip and Run value are exported first
    (the call REFUSES when that fails), the user's instance is stopped, a fresh key holding -Values is
    written, and a scratch root with its own LOCALAPPDATA and a copy of the build is created. Pair it with
    Exit-AuditProfile in a finally block; Restore-AuditProfile is the rescue for a run that died.
    .PARAMETER Values
    Registry values for the fresh key. WelcomeShown=1 is always set (an empty profile would open the
    first-run tour, a modal dialog) unless -Values names it.
    .PARAMETER Configuration
    Which build under src\CyrFlip\bin to copy. The copy runs from %TEMP%, which AppShortcuts leaves alone.
    #>
    [CmdletBinding()]
    param(
        [hashtable]$Values = @{},
        [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
        [string]$ExePath
    )
    Initialize-AuditNative
    if (Test-Path $script:RecoveryPath) {
        throw "A previous audit run did not finish (manifest at $script:RecoveryPath). Run Restore-AuditProfile first - the user's CyrFlip state is in its backup."
    }
    $repo = Get-AuditRepoRoot
    $source = if ($ExePath) { Split-Path -Parent (Resolve-Path $ExePath).Path }
              else { Join-Path $repo "src\CyrFlip\bin\$Configuration\net48" }
    if (-not (Test-Path (Join-Path $source 'CyrFlip.exe'))) { throw "Not built: $source\CyrFlip.exe" }

    $root = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlip-audit-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $root | Out-Null
    $regFile = Join-Path $root 'user-key-backup.reg'

    $keyExisted = Test-Path $script:AppKey
    if ($keyExisted) {
        & reg.exe export 'HKCU\Software\CyrFlip' $regFile /y 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $regFile) -or (Get-Item $regFile).Length -lt 40) {
            Remove-Item -Path $root -Recurse -Force -ErrorAction SilentlyContinue
            throw 'Could not back up HKCU\Software\CyrFlip - refusing to touch it.'
        }
    }
    $runValue = (Get-ItemProperty -Path $script:RunKey -Name CyrFlip -ErrorAction SilentlyContinue).CyrFlip
    $userProc = Get-Process CyrFlip -ErrorAction SilentlyContinue | Select-Object -First 1
    $manifest = [pscustomobject]@{
        CreatedUtc = [DateTime]::UtcNow.ToString('o'); Root = $root; RegFile = $regFile
        KeyExisted = $keyExisted; RunExisted = ($null -ne $runValue); RunValue = $runValue
        UserWasRunning = [bool]$userProc; UserExe = if ($userProc) { $userProc.Path } else { $null }
    }
    $manifest | ConvertTo-Json | Set-Content -Path $script:RecoveryPath -Encoding UTF8

    try {
        Get-Process CyrFlip -ErrorAction SilentlyContinue | ForEach-Object {
            [void]$_.CloseMainWindow(); Start-Sleep -Milliseconds 300
            if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force }
        }
        Start-Sleep -Milliseconds 300

        $appData = Join-Path $root 'LocalAppData'
        New-Item -ItemType Directory -Path $appData | Out-Null
        $bin = Join-Path $root 'bin'
        Copy-Item -Path $source -Destination $bin -Recurse -Force

        if (Test-Path $script:AppKey) { Remove-Item -Path $script:AppKey -Recurse -Force }
        New-Item -Path $script:AppKey -Force | Out-Null
        $all = @{ WelcomeShown = 1 }
        foreach ($k in $Values.Keys) { $all[$k] = $Values[$k] }
        foreach ($k in $all.Keys) {
            $v = $all[$k]
            if ($v -is [int] -or $v -is [bool]) { Set-ItemProperty -Path $script:AppKey -Name $k -Value ([int]$v) -Type DWord }
            else { Set-ItemProperty -Path $script:AppKey -Name $k -Value ([string]$v) -Type String }
        }
    }
    catch {
        Restore-AuditProfile | Out-Null
        throw
    }
    [pscustomobject]@{
        Root = $root; LocalAppData = $appData; Exe = (Join-Path $bin 'CyrFlip.exe'); DataFolder = (Join-Path $appData 'CyrFlip')
        UserWasRunning = $manifest.UserWasRunning
    }
}

function Start-AuditInstance {
    <#
    .SYNOPSIS
    Starts the staged copy with LOCALAPPDATA pointing at the scratch folder (so every log, journal and
    layout file lands there, never in the user's). Returns the Process and the launch instant (UTC).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Profile, [string[]]$Arguments = @())
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Profile.Exe
    $psi.Arguments = ($Arguments -join ' ')
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path -Parent $Profile.Exe
    $psi.EnvironmentVariables['LOCALAPPDATA'] = $Profile.LocalAppData
    $launched = [DateTime]::UtcNow
    $p = [System.Diagnostics.Process]::Start($psi)
    [pscustomobject]@{ Process = $p; LaunchedUtc = $launched; Id = $p.Id }
}

function Send-AuditPipeCommand {
    <#
    .SYNOPSIS
    One line to the launcher pipe of THIS session (CyrFlip_Launcher_{userSid}_{sessionId}) - the way the
    build scripts end the tray (/exit passes even with the launcher off) and the way Test-LongRun opens
    the settings window (/launcher-settings needs EnableScenarioLauncher=1). $true when it was written.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Line, [int]$ConnectTimeoutMs = 2000)
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $name = 'CyrFlip_Launcher_' + $sid + '_' + (Get-Process -Id $PID).SessionId
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, [System.IO.Pipes.PipeDirection]::Out)
    try {
        $client.Connect($ConnectTimeoutMs)
        $writer = New-Object System.IO.StreamWriter($client, (New-Object System.Text.UTF8Encoding($false)))
        $writer.WriteLine($Line)
        $writer.Flush()
        $writer.Dispose()
        $true
    }
    catch { $false }
    finally { $client.Dispose() }
}

function Stop-AuditInstance {
    <#
    .SYNOPSIS
    Ends the staged copy the way the build scripts do - /exit over the pipe, so the app runs its own
    shutdown (cursor restored, layout files retracted) - and only then, after -GraceMs, by force.
    Returns the milliseconds the graceful exit took, or -1 when it had to be killed.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Profile, [int]$GraceMs = 4000)
    $name = [IO.Path]::GetFileNameWithoutExtension($Profile.Exe)
    $took = 0
    $mine = @(Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Profile.Exe })
    foreach ($p in $mine) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        # The pipe is created by the app's constructor, so a /exit sent very early finds none: retry
        # until the process ends or the grace period is spent (one delivered line is enough).
        $exited = $false
        $sent = $false
        while (-not $exited -and $sw.ElapsedMilliseconds -lt $GraceMs) {
            if (-not $sent) { $sent = Send-AuditPipeCommand -Line '/exit' -ConnectTimeoutMs 300 }
            try { $exited = $p.WaitForExit(150) } catch { break }
        }
        if ($exited) { $took = [Math]::Max($took, [int]$sw.Elapsed.TotalMilliseconds) }
        else {
            try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
            $took = -1
        }
    }
    $took
}

function Get-AuditMachine {
    <#
    .SYNOPSIS
    The measured machine, named the way every report must name it (spec 3.2 Evidence).
    #>
    [CmdletBinding()]
    param([string]$ExePath)
    Add-Type -AssemblyName System.Windows.Forms
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $cs = Get-CimInstance Win32_ComputerSystem
    $build = $null
    if ($ExePath -and (Test-Path $ExePath)) {
        $v = (Get-Item $ExePath).VersionInfo
        $build = [pscustomobject]@{ FileVersion = $v.FileVersion; ProductVersion = $v.ProductVersion }
    }
    [pscustomobject]@{
        Cpu      = ($cpu.Name -replace '\s+', ' ').Trim()
        Cores    = $cpu.NumberOfCores
        Logical  = $cpu.NumberOfLogicalProcessors
        RamGb    = [Math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        Windows  = "$($os.Caption) $($os.Version)"
        Monitors = @([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { '{0}x{1}{2}' -f $_.Bounds.Width, $_.Bounds.Height, $(if ($_.Primary) { ' (primary)' } else { '' }) })
        Host     = "PowerShell $($PSVersionTable.PSVersion)"
        Build    = $build
        Utc      = [DateTime]::UtcNow.ToString('o')
    }
}

function Exit-AuditProfile {
    [CmdletBinding()]
    param($Profile, [switch]$KeepUserAppStopped)
    if ($Profile) { try { [void](Stop-AuditInstance -Profile $Profile) } catch { } }
    [void](Restore-AuditProfile -KeepUserAppStopped:$KeepUserAppStopped)
}

function Start-AuditLoad {
    <#
    .SYNOPSIS
    The weak-environment stand-in (S0061 D6): -Percent busy on every logical core until Stop-AuditLoad.
    #>
    [CmdletBinding()]
    param([int]$Percent = 90, [int]$Threads = 0)
    Initialize-AuditNative
    if ($Threads -le 0) { $Threads = [Environment]::ProcessorCount }
    $load = New-Object CyrFlipAudit.CpuLoad
    $load.Start($Threads, $Percent)
    $load
}

function Stop-AuditLoad {
    [CmdletBinding()]
    param($Load)
    if ($Load) { $Load.Dispose() }
}

function Wait-AuditCondition {
    [CmdletBinding()]
    param([Parameter(Mandatory)][scriptblock]$Condition, [int]$TimeoutMs = 15000, [int]$PollMs = 25)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if (& $Condition) { return [Math]::Round($sw.Elapsed.TotalMilliseconds, 0) }
        Start-Sleep -Milliseconds $PollMs
    }
    -1
}

Export-ModuleMember -Function Start-AuditProbe, Stop-AuditProbe, Format-AuditStats, Get-AuditResourceSample,
    Get-GrowthSlope, Test-AuditGrowth, Invoke-AuditSoak, Enter-AuditProfile, Exit-AuditProfile, Restore-AuditProfile,
    Start-AuditInstance, Stop-AuditInstance, Wait-AuditCondition, Get-ManagedHeapBytes, Send-AuditPipeCommand,
    Get-AuditMachine, Reset-AuditSystemCursors, Start-AuditLoad, Stop-AuditLoad
