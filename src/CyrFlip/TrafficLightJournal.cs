using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CyrFlip
{
    /// <summary>The file system under the journal, as a seam: the tests fail a write on purpose.</summary>
    internal interface ITrafficJournalFiles
    {
        /// <summary>The size of the file, or 0 when it is not there.</summary>
        long LengthOf(string path);
        void Append(string path, string text);
        void Rename(string from, string to);
        bool Exists(string path);
        void Delete(string path);
        IEnumerable<string> Enumerate(string folder, string pattern);
    }

    internal sealed class DiskTrafficJournalFiles : ITrafficJournalFiles
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public long LengthOf(string path)
        {
            try { var info = new FileInfo(path); return info.Exists ? info.Length : 0; }
            catch { return 0; }
        }

        public void Append(string path, string text)
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                byte[] bytes = Utf8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        public void Rename(string from, string to) => File.Move(from, to);
        public bool Exists(string path) => File.Exists(path);
        public void Delete(string path) => File.Delete(path);

        public IEnumerable<string> Enumerate(string folder, string pattern)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern) : new string[0]; }
            catch { return new string[0]; }
        }
    }

    /// <summary>The marker lines of section 6 (written when at least one target records).</summary>
    internal enum TrafficJournalMarker
    {
        Started,
        Offline,
        Online,
    }

    /// <summary>
    /// The per-target change journal (ticket S0069 D19): one plain-text file in the data folder that opens in
    /// the user's text editor. <b>Plain unencrypted text on purpose</b> - the page that offers it says so - and
    /// <b>never</b> in a support bundle, a settings export or a diagnostic log.
    ///
    /// <para>One background writer drains one queue, in order, one open per batch - never the UI or the hook's
    /// thread - and it is not <see cref="DiagnosticLog"/>, whose rotation cuts a file to its tail at 2 MB and
    /// would drop history. Nothing is deleted automatically; a file past 5 MB is renamed with the date and a
    /// new one starts, so each file still opens in an editor. A failed write is kept and retried with the next
    /// record, the queue bounded at <see cref="MaxQueuedLines"/>, and reported once per session.</para>
    /// </summary>
    internal sealed class TrafficLightJournal : IDisposable
    {
        public const string FileName = "traffic-light-history.txt";
        public const string RotatedPattern = "traffic-light-history-*.txt";
        public const long SplitAtBytes = 5L * 1024 * 1024;
        public const int MaxQueuedLines = 1000;

        private readonly string _folder;
        private readonly ITrafficJournalFiles _files;
        private readonly Func<DateTime> _utcNow;
        private readonly TimeZoneInfo _zone;
        private readonly object _gate = new object();
        private readonly List<string> _pending = new List<string>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private Thread? _worker;
        private bool _disposed;
        private bool _failureReported;
        private int _busy;
        private int _written;

        public TrafficLightJournal(string folder, ITrafficJournalFiles? files = null, Func<DateTime>? utcNow = null, TimeZoneInfo? zone = null)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _files = files ?? new DiskTrafficJournalFiles();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _zone = zone ?? TimeZoneInfo.Local;
        }

        /// <summary>The live file.</summary>
        public string FilePath => Path.Combine(_folder, FileName);

        /// <summary>The first failed write of the session, once. Raised on the writer thread.</summary>
        public event Action<string>? WriteFailed;

        /// <summary>Lines written so far (the tests wait on it).</summary>
        public int WrittenLines => Volatile.Read(ref _written);

        // ---- the format (pure) ----

        /// <summary>
        /// One line, section 6: local time with its offset, the state word padded to nine, the target as
        /// <c>[label] kind address</c>, then invariant <c>key=value</c> pairs - never a localized word, so the file
        /// reads the same in every UI language and can be searched.
        /// </summary>
        public static string FormatLine(TrafficLightChange change, TimeZoneInfo zone)
        {
            TrafficLightTarget t = change.Target;
            string word;
            var parts = new List<string>();
            bool first = change.IsBaseline;
            if (first)
            {
                word = "START";
                parts.Add("state=" + StateWord(change.To));
            }
            else
            {
                word = StateWord(change.To);
            }

            if (change.To == TrafficState.Down || change.To == TrafficState.Degraded)
                parts.Add("reason=" + (change.Reason.Length > 0 ? change.Reason : TrafficReasons.Failed));
            if (change.LatencyMs.HasValue && change.To != TrafficState.Down)
                parts.Add("latency=" + change.LatencyMs.Value.ToString(CultureInfo.InvariantCulture) + " ms");
            if (!first && change.WasFor.HasValue)
                parts.Add("was=" + StateWord(change.From) + " for " + FormatDuration(change.WasFor.Value));
            if (change.To == TrafficState.Down && change.FirstFailureUtc.HasValue)
                parts.Add("first-failure=" + Local(change.FirstFailureUtc.Value, zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture));

            return Stamp(change.AtUtc, zone) + "  " + word.PadRight(9) + " [" + Clean(t.Label) + "] " + t.Kind + " " + Clean(t.Address)
                + (parts.Count > 0 ? "  " + string.Join("  ", parts) : "");
        }

        public static string FormatMarker(TrafficJournalMarker marker, DateTime utc, TimeZoneInfo zone)
        {
            string text;
            switch (marker)
            {
                case TrafficJournalMarker.Started: text = "-- CyrFlip started, monitoring resumed --"; break;
                case TrafficJournalMarker.Offline: text = "-- computer offline, checks paused --"; break;
                default: text = "-- computer online --"; break;
            }
            return Stamp(utc, zone) + "  " + text;
        }

        /// <summary><c>3d 4h 12m</c>, <c>2m 28s</c>, <c>45s</c>: the two largest units that apply.</summary>
        public static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            long totalSeconds = (long)span.TotalSeconds;
            long days = totalSeconds / 86400;
            long hours = totalSeconds % 86400 / 3600;
            long minutes = totalSeconds % 3600 / 60;
            long seconds = totalSeconds % 60;
            if (days > 0) return days + "d " + hours + "h " + minutes + "m";
            if (hours > 0) return hours + "h " + minutes + "m";
            if (minutes > 0) return minutes + "m " + seconds + "s";
            return seconds + "s";
        }

        private static string StateWord(TrafficState state)
        {
            switch (state)
            {
                case TrafficState.Ok: return "OK";
                case TrafficState.Degraded: return "DEGRADED";
                case TrafficState.Down: return "DOWN";
                default: return "UNKNOWN";
            }
        }

        private static DateTime Local(DateTime utc, TimeZoneInfo zone)
            => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

        private static string Stamp(DateTime utc, TimeZoneInfo zone)
        {
            DateTime u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(u, zone);
            TimeSpan offset = zone.GetUtcOffset(u);
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            TimeSpan abs = offset.Duration();
            return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + sign
                + abs.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + abs.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        // One record is one line: a label or an address with a line break in it would split it.
        private static string Clean(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char c in text) builder.Append(char.IsControl(c) ? ' ' : c);
            return builder.ToString();
        }

        // ---- recording ----

        /// <summary>
        /// Writes the change when its target asked for it. Called on whichever thread finished the check; it
        /// only queues.
        /// </summary>
        public void Record(TrafficLightChange change)
        {
            if (!change.Target.RecordHistory) return;
            Enqueue(FormatLine(change, _zone));
        }

        public void Marker(TrafficJournalMarker marker) => Enqueue(FormatMarker(marker, _utcNow(), _zone));

        private void Enqueue(string line)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _pending.Add(line);
                if (_pending.Count > MaxQueuedLines) _pending.RemoveRange(0, _pending.Count - MaxQueuedLines);
                if (_worker == null)
                {
                    _worker = new Thread(Work) { IsBackground = true, Name = "CyrFlip traffic light journal" };
                    _worker.Start();
                }
            }
            _signal.Set();
        }

        private void Work()
        {
            while (true)
            {
                _signal.WaitOne();
                lock (_gate) if (_disposed && _pending.Count == 0) return;
                Drain();
                lock (_gate) if (_disposed) return;
            }
        }

        /// <summary>One open per batch. A failed batch stays queued, in order, for the next record.</summary>
        private void Drain()
        {
            string[] batch;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                batch = _pending.ToArray();
            }

            Interlocked.Exchange(ref _busy, 1);
            try
            {
                Rotate();
                var text = new StringBuilder();
                foreach (string line in batch) text.Append(line).Append("\r\n");
                _files.Append(FilePath, text.ToString());
                lock (_gate) _pending.RemoveRange(0, Math.Min(batch.Length, _pending.Count));
                Interlocked.Add(ref _written, batch.Length);
            }
            catch (Exception ex)
            {
                string reason = ex.GetType().Name;
                bool report;
                lock (_gate) { report = !_failureReported; _failureReported = true; }
                if (report)
                {
                    try { WriteFailed?.Invoke(reason); } catch (Exception inner) { TrafficLightLog.Fault("journal-subscriber", inner); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        /// <summary>A live file past the limit is renamed with the date; the next append starts a new one.</summary>
        private void Rotate()
        {
            if (_files.LengthOf(FilePath) < SplitAtBytes) return;
            string stamp = _utcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string target = Path.Combine(_folder, "traffic-light-history-" + stamp + ".txt");
            for (int n = 2; _files.Exists(target); n++)
                target = Path.Combine(_folder, "traffic-light-history-" + stamp + "-" + n.ToString(CultureInfo.InvariantCulture) + ".txt");
            _files.Rename(FilePath, target);
        }

        // ---- lifetime ----

        /// <summary>Waits until everything queued so far was written (or the wait ran out). True when empty.</summary>
        public bool Flush(int timeoutMs)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            // Waits only. A write that failed is retried with the next record (or at dispose), never by a loop.
            while (DateTime.UtcNow < until)
            {
                lock (_gate) if (_pending.Count == 0 && Volatile.Read(ref _busy) == 0) return true;
                Thread.Sleep(10);
            }
            lock (_gate) return _pending.Count == 0;
        }

        /// <summary>
        /// "Clear history...": the live file and every rotated one. Never run on its own - the page asks first.
        /// Returns false when a file could not be removed.
        /// </summary>
        public bool Clear()
        {
            Flush(2000);
            bool ok = true;
            var paths = new List<string> { FilePath };
            paths.AddRange(_files.Enumerate(_folder, RotatedPattern));
            foreach (string path in paths)
            {
                try { if (_files.Exists(path)) _files.Delete(path); }
                catch { ok = false; }
            }
            return ok;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _signal.Set();
            Flush(2000);
            _signal.Set();
        }
    }
}
