using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CyrFlip
{
    /// <summary>
    /// The shared body of CyrFlip's six append-only diagnostic logs (<see cref="TranslateLog"/>,
    /// <see cref="TextMenuLog"/>, <see cref="LauncherLog"/>, <see cref="QuickNotesLog"/>,
    /// <see cref="ClipboardHistoryLog"/>, <see cref="ClipboardFlipLog"/>): the MSIX-aware folder,
    /// <b>rotation</b>, and - since ticket S0030 HT-4 - <b>a caller that never waits on the disk</b>.
    ///
    /// <para><see cref="Append"/> only queues the line. One background writer drains the queue in
    /// order, opening each file once per batch. Most callers are on the UI thread, which both
    /// low-level hooks share: a synchronous open/append/close per line, behind a lock every log
    /// shared, meant that a rotation running for another log - or an antivirus scan of this one -
    /// stalled the next context-menu line in the very message turn that had already spent up to
    /// 90 ms collecting the selection. <see cref="Flush"/> is how the session end and the support
    /// bundle make sure what was queued is on disk before they go on.</para>
    ///
    /// <para><b>Rotation</b>: these files never had a cap, and <c>context-menu.log</c> alone writes a
    /// line per menu opening and per click. The writer checks a file's size on its first write of
    /// the session and again every <see cref="RotationCheckEvery"/> lines - an autostarted tray
    /// session lasts weeks, so "once per session" alone was not a cap. The <b>tail</b> survives (the
    /// recent past is the interesting part), it starts on a line boundary, and it is introduced by a
    /// marker line: a silently shortened log reads as a complete one, which is how a reader concludes
    /// that something "never happened".</para>
    /// </summary>
    internal static class DiagnosticLog
    {
        /// <summary>Above this, the file is rotated.</summary>
        public const long MaxBytes = 2L * 1024 * 1024;

        /// <summary>How much of the tail survives rotation.</summary>
        public const int KeepBytes = 512 * 1024;

        /// <summary>How many appends to one file between two size checks, after the first.</summary>
        public const int RotationCheckEvery = 1000;

        private static readonly object Lock = new object();

        private static string? _overrideFolder;

        private static readonly Writer Shared = new Writer(MaxBytes, KeepBytes, RotationCheckEvery);

        /// <summary>
        /// The folder every CyrFlip log lives in - <see cref="DataFolder.Current"/>: <c>%LOCALAPPDATA%\CyrFlip</c>,
        /// or the package's own per-user folder when packaged, addressed by its real path so an outside
        /// reader (the support bundle's mail client) finds it. Never the machine-wide %ProgramData%,
        /// where another account could read these logs (ticket S0016).
        /// </summary>
        internal static string ProductionFolder => DataFolder.Current;

        /// <summary>
        /// Test-only destination for diagnostics. It may be assigned once, before the tests which
        /// exercise logging run; production never assigns it and always uses <see cref="ProductionFolder"/>.
        /// </summary>
        internal static string? OverrideFolder
        {
            get { lock (Lock) return _overrideFolder; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A log override folder is required.", nameof(value));
                lock (Lock)
                {
                    if (_overrideFolder != null && !string.Equals(_overrideFolder, value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("DiagnosticLog.OverrideFolder can only be assigned once.");
                    _overrideFolder = value;
                }
            }
        }

        public static string Path(string fileName)
        {
            lock (Lock)
                return System.IO.Path.Combine(_overrideFolder ?? ProductionFolder, fileName);
        }

        /// <summary>
        /// Queue one line for <paramref name="path"/>. Returns at once, without touching the file
        /// system; the background writer creates the folder, rotates and appends. Every failure is
        /// swallowed: diagnostics must never affect the app.
        /// </summary>
        public static void Append(string path, string line) => Shared.Append(path, line);

        /// <summary>
        /// Wait until every line queued so far is written, or <paramref name="timeout"/> passes;
        /// false on timeout. For the session end, the process exit and the support bundle.
        /// </summary>
        public static bool Flush(TimeSpan timeout) => Shared.Flush(timeout);

        /// <summary>
        /// Cut <paramref name="path"/> down to its last <paramref name="keepBytes"/> bytes when it
        /// exceeds <paramref name="maxBytes"/>, keeping whole lines and prefixing a marker that says
        /// what was dropped. Internal (with the sizes as arguments) so the tests need not write 2 MB.
        /// </summary>
        internal static void Rotate(string path, long maxBytes, int keepBytes)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length <= maxBytes) return;

                long length = file.Length;
                byte[] tail = new byte[keepBytes];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    stream.Seek(length - keepBytes, SeekOrigin.Begin);
                    int read = 0;
                    while (read < tail.Length)
                    {
                        int chunk = stream.Read(tail, read, tail.Length - read);
                        if (chunk <= 0) break;
                        read += chunk;
                    }
                }

                // Start just past the first newline, so the file never opens on half a line.
                int start = Array.IndexOf(tail, (byte)'\n');
                start = start < 0 ? 0 : start + 1;
                long dropped = length - (keepBytes - start);

                byte[] marker = Encoding.UTF8.GetBytes(CompactedMarker(dropped, keepBytes - start) + Environment.NewLine);

                // Rewritten in place rather than through a temp file that replaces it: replacing
                // means deleting, and a file another process holds open cannot be deleted - which is
                // exactly the case that matters, since SupportBundle reads these very logs (with
                // FileShare.ReadWrite) to build an archive, and the user may well have one open too.
                // A crash mid-rewrite leaves a truncated diagnostic log, which is an acceptable
                // trade for rotation that actually happens.
                using (var output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    output.Seek(0, SeekOrigin.Begin);
                    output.Write(marker, 0, marker.Length);
                    output.Write(tail, start, keepBytes - start);
                    output.SetLength(output.Position);
                }
            }
            catch { /* a log we cannot rotate is a log we simply keep appending to */ }
        }

        /// <summary>
        /// The line a rotated log opens with - <c>DIAGNOSTIC-REPORT</c> rule 4's compaction marker. CyrFlip
        /// keeps the tail only (the recent past; the configuration a head would hold is in the bundle's
        /// report), so the dropped "middle" is the front of the file and the kept head is 0 bytes.
        /// Older builds wrote <c>--- rotated ...</c>; such a line may still open a log on disk.
        /// </summary>
        internal static string CompactedMarker(long droppedBytes, long keptTailBytes) =>
            MarkerLine("LOG COMPACTED", droppedBytes, keptTailBytes);

        /// <summary>The same marker's shape for a file cut on its way into the log bundle (rule 4's archive guard).</summary>
        internal static string TruncatedMarker(long droppedBytes, long keptTailBytes) =>
            MarkerLine("LOG TRUNCATED", droppedBytes, keptTailBytes);

        /// <summary>What every marker line starts with, old or new, so a scrub can let it through.</summary>
        internal const string MarkerPrefix = "[Diag] ";

        private static string MarkerLine(string what, long droppedBytes, long keptTailBytes) =>
            MarkerPrefix + what + " | dropped_middle_bytes=" + droppedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " | kept_head_bytes=0 | kept_tail_bytes=" + keptTailBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Test seam: forget which files were already checked for rotation in this process.</summary>
        internal static void ResetRotationState() => Shared.ResetRotationState();

        /// <summary>
        /// The queue and its one drain. An instance rather than static state so a test can give it
        /// small limits and a fake sink without touching the logs every other test writes.
        /// </summary>
        internal sealed class Writer
        {
            private readonly long _maxBytes;
            private readonly int _keepBytes;
            private readonly int _checkEvery;
            private readonly Action<string, List<string>>? _sink;
            private readonly object _gate = new object();
            private List<(string Path, string Line)> _queue = new List<(string, string)>();
            private bool _draining;

            /// <summary>Appends per file since its last size check; absent = never checked this session.</summary>
            private readonly Dictionary<string, int> _sinceCheck = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            /// <param name="sink">Replaces the file write (folder, rotation, append) in tests.</param>
            internal Writer(long maxBytes, int keepBytes, int checkEvery, Action<string, List<string>>? sink = null)
            {
                _maxBytes = maxBytes;
                _keepBytes = keepBytes;
                _checkEvery = Math.Max(1, checkEvery);
                _sink = sink;
            }

            internal void Append(string path, string line)
            {
                try
                {
                    lock (_gate)
                    {
                        _queue.Add((path, line));
                        if (_draining) return; // the running drain picks it up before it stops
                        _draining = true;
                    }
                    ThreadPool.QueueUserWorkItem(_ => Drain());
                }
                catch
                {
                    lock (_gate) _draining = false;
                }
            }

            internal bool Flush(TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow + timeout;
                lock (_gate)
                {
                    while (_draining || _queue.Count > 0)
                    {
                        TimeSpan left = deadline - DateTime.UtcNow;
                        if (left <= TimeSpan.Zero) return false;
                        Monitor.Wait(_gate, left);
                    }
                    return true;
                }
            }

            internal void ResetRotationState()
            {
                lock (_gate) _sinceCheck.Clear();
            }

            private void Drain()
            {
                while (true)
                {
                    List<(string Path, string Line)> batch;
                    lock (_gate)
                    {
                        if (_queue.Count == 0)
                        {
                            _draining = false;
                            Monitor.PulseAll(_gate);
                            return;
                        }
                        batch = _queue;
                        _queue = new List<(string, string)>();
                    }

                    // One open per file per batch; the order of lines within a file is kept.
                    var byFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var order = new List<string>();
                    foreach ((string path, string line) in batch)
                    {
                        if (!byFile.TryGetValue(path, out List<string>? lines))
                        {
                            lines = new List<string>();
                            byFile.Add(path, lines);
                            order.Add(path);
                        }
                        lines.Add(line);
                    }
                    foreach (string path in order)
                    {
                        try
                        {
                            if (_sink != null) _sink(path, byFile[path]);
                            else Write(path, byFile[path]);
                        }
                        catch { /* diagnostics must never affect the app */ }
                    }
                }
            }

            private void Write(string path, List<string> lines)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                if (DueForCheck(path, lines.Count))
                    Rotate(path, _maxBytes, _keepBytes);
                var text = new StringBuilder();
                foreach (string line in lines)
                    text.Append(line).Append(Environment.NewLine);
                File.AppendAllText(path, text.ToString(), Encoding.UTF8);
            }

            /// <summary>True on a file's first write of the session and then every <c>checkEvery</c> lines.</summary>
            private bool DueForCheck(string path, int count)
            {
                lock (_gate)
                {
                    if (!_sinceCheck.TryGetValue(path, out int since))
                    {
                        _sinceCheck[path] = count;
                        return true;
                    }
                    since += count;
                    if (since >= _checkEvery)
                    {
                        _sinceCheck[path] = 0;
                        return true;
                    }
                    _sinceCheck[path] = since;
                    return false;
                }
            }
        }
    }
}
