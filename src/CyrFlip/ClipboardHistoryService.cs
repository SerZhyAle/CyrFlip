using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Captures Unicode clipboard text and keeps it in an encrypted append-only per-user log
    /// (<see cref="ClipboardHistoryJournal"/>). Which updates are recorded is decided by
    /// <see cref="ClipboardHistoryGate"/> (CyrFlip's own clipboard traffic) and
    /// <see cref="ClipboardPrivacy"/> (copies another application marked "do not record").
    ///
    /// <para><b>The owning thread by construction</b> (ticket S0031 CH2-2). The list is not thread-safe
    /// and the strip paints from it, so everything a pool or writer thread produces is handed back
    /// through this service's own window - a private message and a queue (<see cref="PostToOwner"/>) -
    /// never through <c>SynchronizationContext.Current</c> read in the constructor, which was right only
    /// because a field initializer elsewhere happened to create a control first.</para>
    /// </summary>
    internal sealed class ClipboardHistoryService : NativeWindow, IDisposable
    {
        private const int WmClipboardUpdate = 0x031D;
        /// <summary>WM_APP + 0x31: "run what is queued in <see cref="_toOwner"/>". Private to this window.</summary>
        private const int WmRunQueued = 0x8000 + 0x31;
        private const int MaxTextBytes = 128 * 1024;
        // The entries, their display order and the Uuid index, all maintained incrementally. The
        // history is unbounded by design, so nothing may cost O(history) per copy - see
        // <see cref="ClipboardHistoryOrder"/> for why that class exists at all.
        private readonly ClipboardHistoryOrder _order = new ClipboardHistoryOrder();
        private readonly ClipboardHistoryGate _gate = new ClipboardHistoryGate();
        private readonly ClipboardHistoryJournal _journal;
        private readonly ConcurrentQueue<Action> _toOwner = new ConcurrentQueue<Action>();
        private int _runQueuedPosted;
        private bool _enabled;
        private bool _paused;
        private bool _disposed;
        private int _skippedMarked;
        private int _summaryLogged;

        public event EventHandler? Changed;
        public event EventHandler? ItemTooLarge;
        /// <summary>A <see cref="Clear"/> could not delete the file - another program holds it. Raised on the UI thread.</summary>
        public event EventHandler? ClearFailed;
        /// <summary>The first journal record of this session that could not be written (S0031 CH2-4). Raised on the UI thread, once.</summary>
        public event EventHandler? WriteFailed;

        /// <summary>
        /// Pinned first, then newest first. Already in that order - <see cref="ClipboardHistoryOrder"/>
        /// keeps it so on every change, instead of the strip re-sorting the whole history on each repaint.
        /// </summary>
        public IReadOnlyList<ClipboardHistoryEntry> Entries => _order.Entries;

        /// <summary>Journal lines that could not be read at startup; each cost only itself (spec S0005 CH-2).</summary>
        public int SkippedRecords { get; }

        public ClipboardHistoryService(bool enabled, bool paused)
            : this(enabled, paused, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyrFlip"))
        {
        }

        /// <summary>A history over <paramref name="dir"/> - the seam that keeps tests off the user's real history.</summary>
        internal ClipboardHistoryService(bool enabled, bool paused, string dir, IQuickNotesCipher? cipher = null)
        {
            _enabled = enabled;
            _paused = paused;
            Directory.CreateDirectory(dir);
            _journal = new ClipboardHistoryJournal(Path.Combine(dir, "clipboard-history.log"), cipher ?? QuickNotesCipher.Dpapi);
            SkippedRecords = _journal.Load(_order);
            if (SkippedRecords > 0)
                ClipboardHistoryLog.Log("history: " + SkippedRecords + " unreadable records skipped on load");
            // Before the journal can fail a write: the handle is where the report is posted to.
            CreateHandle(new CreateParams { Caption = "CyrFlip Clipboard History Listener" });
            _journal.WriteFailed += () => PostToOwner(() =>
            {
                ClipboardHistoryLog.Log("history: a journal record could not be written");
                WriteFailed?.Invoke(this, EventArgs.Empty);
            });
            AddClipboardFormatListener(Handle);
        }

        public void SetEnabled(bool value) { _enabled = value; if (value) Capture(); }
        public void SetPaused(bool value) { _paused = value; }

        /// <summary>
        /// A CyrFlip operation starts borrowing the clipboard: nothing it does is a copy the user made.
        /// Call on the worker's first line and pair with <see cref="SuppressEnd"/> in its <c>finally</c>.
        /// </summary>
        public void SuppressBegin() => _gate.Begin();

        /// <summary>The operation handed the clipboard back; every update up to this point was its own.</summary>
        public void SuppressEnd() => _gate.End(GetClipboardSequenceNumber());

        public bool Restore(ClipboardHistoryEntry entry)
        {
            // A failed write means our own clipboard traffic never happened, so nothing is expected.
            if (!Win32Clipboard.TrySetText(entry.Text)) return false;
            // Exactly this update is ours - by number, so a later real copy can never be taken for it,
            // however long the history stays paused (spec S0005 CH-6).
            _gate.ExpectOwnWrite(GetClipboardSequenceNumber());
            _order.SetCurrent(entry);
            RaiseChanged();
            return true;
        }

        public void TogglePin(ClipboardHistoryEntry entry)
        {
            _order.Update(entry, entry.CreatedAt, !entry.IsPinned);
            _journal.Append(ClipboardHistoryJournal.Record.Of(entry.IsPinned ? "pin" : "unpin", entry));
            RaiseChanged();
        }

        public void Delete(ClipboardHistoryEntry entry)
        {
            _order.Remove(entry);
            _journal.Append(ClipboardHistoryJournal.Record.Of("delete", entry));
            RaiseChanged();
        }

        public void Clear()
        {
            _order.Clear();
            _journal.Clear(deleted =>
            {
                if (!deleted) PostToOwner(() => ClearFailed?.Invoke(this, EventArgs.Empty));
            });
            RaiseChanged();
        }

        /// <summary>What <see cref="Import"/> would do with these entries, without doing it (S0023, spec 7.1).</summary>
        public ExchangeMergeReport PlanImport(IEnumerable<ExchangeClipboardItem> incoming)
        {
            var report = new ExchangeMergeReport();
            CyrFlipExchangeMerge.PlanClipboard(_order.Find, incoming, report);
            return report;
        }

        /// <summary>
        /// Merge the entries of an exchange file (S0023, spec 7.2): a text not in the history is added
        /// with the date and pin it carries, a text already here is raised to the newer date and never
        /// unpinned, nothing is removed. The clipboard itself is not touched - an import is not a copy.
        /// UI thread only, like every other change to the list.
        /// </summary>
        public ExchangeMergeReport Import(IEnumerable<ExchangeClipboardItem> incoming)
        {
            var report = new ExchangeMergeReport();
            if (_disposed) return report;
            List<CyrFlipExchangeMerge.ClipboardAction> actions = CyrFlipExchangeMerge.PlanClipboard(_order.Find, incoming, report);
            foreach (CyrFlipExchangeMerge.ClipboardAction action in actions)
            {
                if (action.Existing == null)
                {
                    var entry = new ClipboardHistoryEntry
                    {
                        Uuid = action.Item.Uuid,
                        Text = action.Item.Text,
                        CreatedAt = action.CopiedAtUtc,
                        IsPinned = action.Pinned,
                        SourceApp = action.Item.SourceApp,
                        SourceTitle = action.Item.SourceTitle,
                    };
                    if (_order.Add(entry)) _journal.Append(ClipboardHistoryJournal.Record.Of("add", entry));
                }
                else
                {
                    // "touch" carries both the date and the pin, and replays as exactly this update.
                    _order.Update(action.Existing, action.CopiedAtUtc, action.Pinned);
                    _journal.Append(ClipboardHistoryJournal.Record.Of("touch", action.Existing));
                }
            }
            if (actions.Count > 0)
            {
                ClipboardHistoryLog.Log("history: imported " + report.ClipboardAdded + " new, " + report.ClipboardExisting + " existing");
                RaiseChanged();
            }
            return report;
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> for the queued records - an import's thousands of them -
        /// to reach the disk and returns how many are still queued (S0031 CH2-6). Any thread; the import
        /// runs it inside <see cref="BusyDialog"/>.
        /// </summary>
        public int FlushJournal(TimeSpan timeout)
        {
            _journal.Drain(timeout);
            return _journal.Pending;
        }

        /// <summary>
        /// Run <paramref name="action"/> on the thread that created this service - the one that owns the
        /// list - from any thread (S0031 CH2-2). Dropped once the service is disposed.
        /// </summary>
        internal void PostToOwner(Action action)
        {
            if (_disposed) return;
            _toOwner.Enqueue(action);
            // One message in flight is enough: the handler empties the whole queue.
            if (Interlocked.Exchange(ref _runQueuedPosted, 1) != 0) return;
            IntPtr handle = Handle;
            if (handle == IntPtr.Zero || PostMessage(handle, WmRunQueued, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero)
                Volatile.Write(ref _runQueuedPosted, 0);
        }

        private void RunQueued()
        {
            Volatile.Write(ref _runQueuedPosted, 0);
            while (_toOwner.TryDequeue(out Action? action))
            {
                if (_disposed) continue;
                try { action(); }
                catch { /* history must never affect the clipboard or crash */ }
            }
        }

        /// <summary>
        /// Tell the windows to repaint. Every change goes through here exactly once: the strip
        /// repaints on this event, so a second, redundant raise doubles the cost of every copy.
        /// </summary>
        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmClipboardUpdate) Capture();
            else if (m.Msg == WmRunQueued) RunQueued();
            base.WndProc(ref m);
        }

        private void Capture()
        {
            if (!_enabled || _paused || !_gate.ShouldCapture(GetClipboardSequenceNumber())) return;
            IntPtr hwnd = GetForegroundWindow();
            ThreadPool.QueueUserWorkItem(_ => CaptureAsync(hwnd));
        }

        private void CaptureAsync(IntPtr hwnd)
        {
            try
            {
                // Text over the cap is refused by its allocation size, before a character of it is
                // marshalled into this process (S0009 FP-6).
                if (!Win32Clipboard.TryReadForHistory(MaxTextBytes, out string text, out ClipboardPrivacyMarkers markers,
                        out uint sequence, out bool tooLarge)) return;
                // The clipboard may have moved on since the message: judge what was actually read.
                if (!_gate.ShouldCapture(sequence)) return;
                if (ClipboardPrivacy.ShouldSkip(markers))
                {
                    Interlocked.Increment(ref _skippedMarked);
                    return;
                }
                if (tooLarge || Encoding.Unicode.GetByteCount(text) > MaxTextBytes)
                {
                    PostToOwner(() => ItemTooLarge?.Invoke(this, EventArgs.Empty));
                    return;
                }
                if (text.Length == 0) return;

                ReadSource(hwnd, out string sourceApp, out string sourceTitle);
                DeliverCapture(sequence, text, sourceApp, sourceTitle);
            }
            catch { /* history must never affect the clipboard or crash */ }
        }

        /// <summary>A capture read off the owning thread, handed to it. Any thread; the seam of the thread test.</summary>
        internal void DeliverCapture(uint sequence, string text, string sourceApp, string sourceTitle)
        {
            string uuid = Hash(text);
            PostToOwner(() => ProcessCaptureResult(sequence, uuid, text, sourceApp, sourceTitle));
        }

        private void ProcessCaptureResult(uint sequence, string uuid, string text, string sourceApp, string sourceTitle)
        {
            // A flip may have begun while this capture sat on the pool, and the user may have flipped a
            // switch meanwhile: both are re-checked on the thread that owns the list.
            if (_disposed || !_enabled || _paused || !_gate.ShouldCapture(sequence)) return;
            ClipboardHistoryEntry? existing = _order.Find(uuid);
            if (existing != null)
            {
                _order.Update(existing, DateTime.UtcNow, existing.IsPinned);
                _journal.Append(ClipboardHistoryJournal.Record.Of("touch", existing));
            }
            else
            {
                existing = new ClipboardHistoryEntry
                {
                    Uuid = uuid,
                    Text = text,
                    CreatedAt = DateTime.UtcNow,
                    SourceApp = sourceApp,
                    SourceTitle = sourceTitle
                };
                _order.Add(existing);
                _journal.Append(ClipboardHistoryJournal.Record.Of("add", existing));
            }
            _order.SetCurrent(existing);
            RaiseChanged();
        }

        /// <summary>The window owning the clipboard when it changed is, in practice, the app the text came from.</summary>
        private static void ReadSource(IntPtr hwnd, out string app, out string title)
        {
            app = ""; title = "";
            try
            {
                if (hwnd == IntPtr.Zero) return;
                var caption = new StringBuilder(256);
                if (GetWindowText(hwnd, caption, caption.Capacity) > 0) title = caption.ToString();
                if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return;
                IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return;
                try
                {
                    var buffer = new StringBuilder(1024);
                    uint size = (uint)buffer.Capacity;
                    if (!QueryFullProcessImageName(h, 0, buffer, ref size)) return;
                    string path = buffer.ToString();
                    int slash = path.LastIndexOf('\\');
                    string file = slash >= 0 ? path.Substring(slash + 1) : path;
                    int dot = file.LastIndexOf('.');
                    app = dot >= 0 ? file.Substring(0, dot) : file;
                }
                finally { CloseHandle(h); }
            }
            catch { /* source metadata is best-effort; never let it affect the clipboard */ }
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> for the queued journal records to land; true when
        /// none is left. At sign-out the process is terminated right after <c>WM_ENDSESSION</c>, so a
        /// record still in the queue would otherwise be abandoned.
        /// </summary>
        public bool WaitForPendingWrites(TimeSpan timeout)
        {
            LogSessionSummary();
            return _journal.Drain(timeout);
        }

        /// <summary>
        /// Once per session: how many privacy-marked copies were not recorded and how many records could
        /// not be written (S0031 CH2-4). Counts, nothing else.
        /// </summary>
        private void LogSessionSummary()
        {
            int skipped = Volatile.Read(ref _skippedMarked);
            int failed = _journal.WriteFailures;
            if ((skipped == 0 && failed == 0) || Interlocked.Exchange(ref _summaryLogged, 1) != 0) return;
            if (skipped > 0) ClipboardHistoryLog.Log("history: skipped " + skipped + " marked entries");
            if (failed > 0) ClipboardHistoryLog.Log("history: " + failed + " journal records could not be written");
        }

        /// <summary>An entry's id: SHA-256 of the UTF-8 text, upper-case hex. The exchange file checks imports against it.</summary>
        internal static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Handle != IntPtr.Zero) RemoveClipboardFormatListener(Handle);
            DestroyHandle();
            _journal.Dispose(); // drains what is queued, up to ClipboardHistoryJournal.DisposeDrain
            LogSessionSummary();
            int abandoned = _journal.Pending;
            if (abandoned > 0)
                ClipboardHistoryLog.Log("history: " + abandoned + " queued records abandoned at exit");
        }
    }
}
