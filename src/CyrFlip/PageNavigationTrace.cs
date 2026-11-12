using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Activation-to-frame timing of the settings window's page changes (SETTINGS-NAVIGATION section 5 rule 4,
    /// ticket S0076 M3/M4): from the user's input - the mouse release or key press on the page rail - to the moment
    /// every control of the window has painted the new page, <b>not</b> to the return of the handler.
    ///
    /// <para><b>Completion is a forced paint, not a guess.</b> The measurement posts a callback to the window
    /// (so it runs after the layout and the viewport restore that the change queued - the queue is FIFO) and
    /// then asks Windows to paint every invalid region of the window and all its children now
    /// (<c>RedrawWindow</c> with <c>RDW_UPDATENOW | RDW_ALLCHILDREN</c>); the stamp is taken when that returns.
    /// What this does not include is the compositor's own present of the finished frame (at most one refresh
    /// interval); the report says so.</para>
    ///
    /// <para><b>A newer navigation supersedes an older one.</b> Each <see cref="Begin"/> takes the next
    /// generation; a callback of an older generation does nothing to the window, and the superseded navigation
    /// is recorded as such and never as a latency sample (rapid switching must not report the time of a
    /// destination the user already left, and an old result must not touch the current one).</para>
    ///
    /// <para>The trace records nothing unless it is switched on - by a test, or by the environment variable
    /// <c>CYRFLIP_NAV_TRACE=&lt;file&gt;</c>, which appends one CSV row per navigation to that local file. It is a
    /// diagnostic of the person running it: nothing is sent anywhere.</para>
    /// </summary>
    internal sealed class PageNavigationTrace
    {
        internal enum Kind { First, Warm, Superseded }

        internal sealed class Sample
        {
            public string Destination = "";
            public Kind Kind;
            public double Milliseconds;
        }

        private const int RDW_UPDATENOW = 0x0100;
        private const int RDW_ALLCHILDREN = 0x0080;

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, int flags);

        private readonly HashSet<string> _visited = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Sample> _samples = new List<Sample>();
        private bool _recording;
        private string? _path;
        private int _generation;
        private bool _pending;
        private int _pendingIndex = -1;
        private string _pendingKey = "";
        private long _pendingStart;
        private Kind _pendingKind;

        /// <summary>Environment variable that switches the CSV trace on for ordinary use.</summary>
        internal const string EnvironmentVariable = "CYRFLIP_NAV_TRACE";

        internal bool Recording => _recording;
        internal bool Pending => _pending;
        internal IReadOnlyList<Sample> Samples => _samples;

        /// <summary>Start recording in memory; with a path, also append each sample as a CSV row to that file.</summary>
        internal void Record(string? csvPath = null)
        {
            _recording = true;
            _path = string.IsNullOrWhiteSpace(csvPath) ? null : csvPath;
        }

        internal void RecordFromEnvironment()
        {
            string? path = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(path)) Record(path);
        }

        /// <summary>A destination that is on screen when the window first appears counts as visited: its first paint is the window's.</summary>
        internal void MarkVisited(string key) => _visited.Add(key);

        /// <summary>
        /// A page change was requested. <paramref name="startStamp"/> is the <see cref="Stopwatch"/> stamp of the
        /// input event (0 = now). The same selection reported twice - by the rail and by the page host - is one navigation.
        /// </summary>
        internal void Begin(Control host, int index, string key, long startStamp)
        {
            if (!_recording || !host.IsHandleCreated || host.IsDisposed || !host.Visible) return;
            if (_pending && _pendingIndex == index) return;
            if (_pending) Add(new Sample { Destination = _pendingKey, Kind = Kind.Superseded, Milliseconds = Ms(Stopwatch.GetTimestamp() - _pendingStart) });
            _pending = true;
            _pendingIndex = index;
            _pendingKey = key;
            _pendingStart = startStamp > 0 ? startStamp : Stopwatch.GetTimestamp();
            _pendingKind = _visited.Add(key) ? Kind.First : Kind.Warm;
            int token = ++_generation;
            host.BeginInvoke(new Action(() => Complete(host, token)));
        }

        private void Complete(Control host, int token)
        {
            if (token != _generation || !_pending) return;   // a newer navigation owns the window now
            if (host.IsHandleCreated && !host.IsDisposed) RedrawWindow(host.Handle, IntPtr.Zero, IntPtr.Zero, RDW_UPDATENOW | RDW_ALLCHILDREN);
            long end = Stopwatch.GetTimestamp();
            _pending = false;
            Add(new Sample { Destination = _pendingKey, Kind = _pendingKind, Milliseconds = Ms(end - _pendingStart) });
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private void Add(Sample sample)
        {
            _samples.Add(sample);
            if (_path == null) return;
            try
            {
                File.AppendAllText(_path, string.Join(",", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), sample.Destination,
                    sample.Kind.ToString().ToLowerInvariant(), sample.Milliseconds.ToString("0.000", CultureInfo.InvariantCulture)) + "\r\n");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { _path = null; }   // a diagnostic that cannot write turns itself off rather than failing every page change
        }

        /// <summary>Nearest-rank percentile of <paramref name="values"/> (the method SETTINGS-NAVIGATION section 5 rule 4 names).</summary>
        internal static double NearestRank(IEnumerable<double> values, double percentile)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return double.NaN;
            int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
            return sorted[Math.Min(sorted.Length, Math.Max(1, rank)) - 1];
        }
    }
}
