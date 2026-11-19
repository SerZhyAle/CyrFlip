using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Where the time of opening a window, and of changing a setting in it, goes (ticket S0135, phase 0). One CSV row per
    /// span - <c>UTC time, process id, window, span, milliseconds</c> - appended to the local file named by
    /// <c>CYRFLIP_OPEN_TRACE=&lt;file&gt;</c>, in the shape of <see cref="PageNavigationTrace"/>: nothing is recorded unless
    /// the variable (or a test, through <see cref="Record"/>) switches it on, and while it is off every entry point is one
    /// static flag test and a return, so the trace costs an ordinary run nothing.
    ///
    /// <para><b>A span ends at a painted frame, not at a handler's return</b> where it says so
    /// (<see cref="FrameAfter"/>): the same forced paint of the window and all its children that
    /// <see cref="PageNavigationTrace"/> uses (<c>RedrawWindow</c> with <c>RDW_UPDATENOW | RDW_ALLCHILDREN</c>), posted behind
    /// whatever the change queued. The compositor's own present of the finished frame is outside the measurement.</para>
    ///
    /// <para><b>Nothing a person typed or owns is ever written.</b> A window and a span are fixed names chosen in code
    /// (<c>SettingsForm</c>, <c>page:Hotkeys</c>, <c>change</c>); anything outside letters, digits and <c>:._-</c> is replaced,
    /// so a row cannot be split or carry a caption. A file that cannot be written turns the trace off rather than ever
    /// failing the window.</para>
    /// </summary>
    internal static class OpenTrace
    {
        /// <summary>Environment variable that switches the CSV trace on for ordinary use.</summary>
        internal const string EnvironmentVariable = "CYRFLIP_OPEN_TRACE";

        private const int RDW_UPDATENOW = 0x0100;
        private const int RDW_ALLCHILDREN = 0x0080;

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, int flags);

        private static readonly object Gate = new object();
        private static string? _path = ReadEnvironment();
        private static bool _recording = _path != null;
        private static int _processId;

        private static string? ReadEnvironment()
        {
            string? value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>True while rows are being written.</summary>
        internal static bool Enabled => _recording;

        /// <summary>Starts appending rows to <paramref name="csvPath"/> (a test's way in; the environment variable is the user's).</summary>
        internal static void Record(string csvPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath)) return;
            lock (Gate) { _path = csvPath; _recording = true; }
        }

        /// <summary>Stops recording. A test seam: the trace is a process-wide switch.</summary>
        internal static void Stop()
        {
            lock (Gate) { _recording = false; _path = null; }
        }

        /// <summary>The start stamp of a span; 0 while the trace is off (and <see cref="End"/> then does nothing).</summary>
        internal static long Begin() => _recording ? Stopwatch.GetTimestamp() : 0;

        /// <summary>Ends a span at the moment of the call.</summary>
        internal static void End(string window, string span, long start)
        {
            if (!_recording || start == 0) return;
            Write(window, span, Milliseconds(Stopwatch.GetTimestamp() - start));
        }

        /// <summary>
        /// Ends a span when Windows has painted the frame the work asked for: posts to <paramref name="host"/> (so it runs
        /// after the layout the work queued - the queue is FIFO), forces the paint of the window and its children, then ends.
        /// </summary>
        internal static void FrameAfter(Control host, string window, string span, long start)
        {
            if (!_recording || start == 0 || host.IsDisposed || !host.IsHandleCreated) return;
            host.BeginInvoke(new Action(() =>
            {
                if (host.IsDisposed) return;
                if (host.IsHandleCreated) RedrawWindow(host.Handle, IntPtr.Zero, IntPtr.Zero, RDW_UPDATENOW | RDW_ALLCHILDREN);
                End(window, span, start);
            }));
        }

        private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        /// <summary>Letters, digits and <c>:._-</c> only: a name is data to a CSV reader, never a way to add a column.</summary>
        internal static string Clean(string name)
        {
            var clean = new StringBuilder(name.Length);
            foreach (char c in name)
                clean.Append(char.IsLetterOrDigit(c) || c == ':' || c == '.' || c == '_' || c == '-' ? c : '_');
            return clean.ToString();
        }

        private static void Write(string window, string span, double milliseconds)
        {
            lock (Gate)
            {
                string? path = _path;
                if (!_recording || path == null) return;
                if (_processId == 0) _processId = Process.GetCurrentProcess().Id;
                string row = string.Join(",", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    _processId.ToString(CultureInfo.InvariantCulture), Clean(window), Clean(span),
                    milliseconds.ToString("0.000", CultureInfo.InvariantCulture));
                try { File.AppendAllText(path, row + "\r\n"); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _recording = false;   // a diagnostic that cannot write turns itself off rather than ever failing a window
                    _path = null;
                }
            }
        }
    }
}
