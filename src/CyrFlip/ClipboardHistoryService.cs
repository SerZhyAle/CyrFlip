using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        /// <summary>The capture cap in UTF-16 code units - the most an edit of an entry may hold (S0089).</summary>
        internal const int MaxTextChars = MaxTextBytes / sizeof(char);
        // The entries, their display order and the Uuid index, all maintained incrementally. The
        // history is unbounded by design, so nothing may cost O(history) per copy - see
        // <see cref="ClipboardHistoryOrder"/> for why that class exists at all.
        private readonly ClipboardHistoryOrder _order = new ClipboardHistoryOrder();
        private readonly ClipboardHistoryGate _gate = new ClipboardHistoryGate();
        // Comes up on first use (S0064 H-5): a start with the history off creates no folder, no
        // writer thread and reads no journal. Owner thread only, like everything else here.
        private ClipboardHistoryJournal? _journal;
        private readonly string _dir;
        private readonly IQuickNotesCipher _cipher;
        private int _replayQueued;
        // The pool's part of the replay - the decrypt and parse (S0064 H-5, S0082). Null until the
        // first StartReplay; merged into the list on the owner thread, once (_replayApplied).
        private Task<ReplayResult>? _replay;
        private bool _replayApplied;
        // Ids the user erased (x or an edit) before the replay was merged: the pool may have read their
        // lines before the purge, and the merge must not bring them back (S0089).
        private readonly HashSet<string> _erasedBeforeReplay = new HashSet<string>(StringComparer.Ordinal);
        // The list was cleared before the replay was merged: the replay read the file that Clear deleted, so
        // none of its entries may come back (S0097).
        private bool _clearedBeforeReplay;
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
        public event EventHandler? EraseFailed;
        /// <summary>
        /// The startup replay is over (S0064 H-5), on the UI thread, exactly once per service. Carries
        /// no data - read <see cref="SkippedRecords"/>. It replaced reading the count in the caller's
        /// constructor, which the pool replay made too early.
        /// </summary>
        public event EventHandler? ReplayFinished;

        /// <summary>
        /// Pinned first, then newest first. Already in that order - <see cref="ClipboardHistoryOrder"/>
        /// keeps it so on every change, instead of the strip re-sorting the whole history on each repaint.
        /// </summary>
        public IReadOnlyList<ClipboardHistoryEntry> Entries => _order.Entries;

        /// <summary>Journal lines that could not be read by the replay; each cost only itself (spec S0005 CH-2). Final once <see cref="ReplayFinished"/> has run.</summary>
        public int SkippedRecords { get; private set; }

        public ClipboardHistoryService(bool enabled, bool paused)
            : this(enabled, paused, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyrFlip"))
        {
        }

        /// <summary>A history over <paramref name="dir"/> - the seam that keeps tests off the user's real history.</summary>
        internal ClipboardHistoryService(bool enabled, bool paused, string dir, IQuickNotesCipher? cipher = null)
        {
            _enabled = enabled;
            _paused = paused;
            _dir = dir;
            _cipher = cipher ?? QuickNotesCipher.Dpapi;
            // Before the journal can fail a write: the handle is where the report is posted to.
            CreateHandle(new CreateParams { Caption = "CyrFlip Clipboard History Listener" });
            AddClipboardFormatListener(Handle);
            if (enabled) StartReplay();
        }

        /// <summary>
        /// The journal, on first use (S0064 H-5). A <c>%LOCALAPPDATA%</c> that cannot hold the folder
        /// is no longer a fatal box at every start: the failure surfaces on the first write through the
        /// usual <see cref="WriteFailed"/> balloon, and the rest of the history keeps working in memory.
        /// </summary>
        private ClipboardHistoryJournal EnsureJournal()
        {
            if (_journal != null) return _journal;
            try { Directory.CreateDirectory(_dir); }
            catch { /* the write path reports an unwritable folder */ }
            var journal = new ClipboardHistoryJournal(Path.Combine(_dir, "clipboard-history.log"), _cipher);
            journal.WriteFailed += () => PostToOwner(() =>
            {
                ClipboardHistoryLog.Log("history: a journal record could not be written");
                WriteFailed?.Invoke(this, EventArgs.Empty);
            });
            journal.EraseFailed += () => PostToOwner(() => EraseFailed?.Invoke(this, EventArgs.Empty));
            _journal = journal;
            return journal;
        }

        /// <summary>
        /// Replay the journal into the list on the pool, once per service: the decrypt of a large file
        /// never runs on the hooks' thread - not at startup (S0064 H-5), and not when the manager
        /// window or the exchange file asks for the records while the history is off (S0082; at
        /// 131 us a record, 2 300 records already cross <c>LowLevelHooksTimeout</c>). Copies made
        /// while the replay is in flight are already in the list, and the merge skips them by Uuid.
        /// Owner thread only.
        /// </summary>
        internal void StartReplay()
        {
            if (Interlocked.Exchange(ref _replayQueued, 1) != 0) return;
            ClipboardHistoryJournal journal = EnsureJournal();
            _replay = Task.Run(() =>
            {
                var replayed = new ClipboardHistoryOrder();
                int skipped = 0;
                try { skipped = journal.Load(replayed); }
                catch { replayed.Clear(); skipped = 0; }
                return new ReplayResult(replayed, skipped);
            });
            _replay.ContinueWith(_ => PostToOwner(CompleteReplay), TaskScheduler.Default);
        }

        /// <summary>
        /// Start the replay when it has not started and return its pool part - the decrypt and parse,
        /// which completes without the owner thread. A caller that must see every record of the file
        /// (the exchange export and import) waits for it with the message loop running
        /// (<c>BusyDialog.Wait</c>) and then calls <see cref="CompleteReplay"/> (S0082). Owner thread only.
        /// </summary>
        internal Task ReplayAsync()
        {
            StartReplay();
            return _replay ?? Task.CompletedTask;
        }

        /// <summary>
        /// Merge a replay the pool has finished into the list, once: what the posted message does, and
        /// what a caller that has just waited for <see cref="ReplayAsync"/> does at once rather than
        /// wait for that message. A replay still in flight is left alone. Owner thread only; cheap -
        /// the decrypt is already done.
        /// </summary>
        internal void CompleteReplay()
        {
            Task<ReplayResult>? replay = _replay;
            if (_disposed || _replayApplied || replay == null || !replay.IsCompleted) return;
            _replayApplied = true;
            ReplayResult result = replay.Result;
            SkippedRecords = result.Skipped;
            if (result.Skipped > 0)
                ClipboardHistoryLog.Log("history: " + result.Skipped + " unreadable records skipped on load");
            MergeReplayed(result.Entries);
        }

        private sealed class ReplayResult
        {
            public ReplayResult(ClipboardHistoryOrder entries, int skipped) { Entries = entries; Skipped = skipped; }
            public ClipboardHistoryOrder Entries { get; }
            public int Skipped { get; }
        }

        /// <summary>
        /// Move the replayed entries into the live list; a Uuid already captured live stays as it is. When the
        /// list was cleared before the merge, the whole result is dropped (S0097): the file it was read from is gone.
        /// </summary>
        private void MergeReplayed(ClipboardHistoryOrder replayed)
        {
            bool added = false;
            if (!_clearedBeforeReplay)
                foreach (ClipboardHistoryEntry entry in replayed.Entries)
                    if (!_erasedBeforeReplay.Contains(entry.Uuid) && _order.Add(entry)) added = true;
            _erasedBeforeReplay.Clear();
            _clearedBeforeReplay = false;
            if (added) RaiseChanged();
            ReplayFinished?.Invoke(this, EventArgs.Empty);
        }

        public void SetEnabled(bool value)
        {
            _enabled = value;
            if (value) { StartReplay(); Capture(); }
        }
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
            EnsureJournal().Append(ClipboardHistoryJournal.Record.Of(entry.IsPinned ? "pin" : "unpin", entry));
            RaiseChanged();
        }

        public void Delete(ClipboardHistoryEntry entry)
        {
            Erase(entry);
            RaiseChanged();
        }

        /// <summary>Out of the list and out of the file: the journal's delete rewrites it without the entry's lines (CH2-5).</summary>
        private void Erase(ClipboardHistoryEntry entry)
        {
            _order.Remove(entry);
            if (!_replayApplied) _erasedBeforeReplay.Add(entry.Uuid);
            EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("delete", entry));
        }

        /// <summary>
        /// The user's correction of an entry's text (S0089). An id is the hash of its text - the exchange
        /// file refuses one that is not - so the edit is the old entry erased exactly as "x" erases it and
        /// an entry written under the new text, carrying the date, the pin and the source; the journal
        /// gains no action, and a build that predates the edit reads the file as it is. A text another
        /// entry already holds merges into that entry, which keeps its date and becomes pinned when either
        /// was. Nothing changes for an empty or whitespace text (an edit never deletes), the same text, or
        /// a text over the capture cap (<see cref="ItemTooLarge"/> says so). The clipboard is not touched.
        /// Returns the entry that now holds the text, or null when nothing changed. UI thread only.
        /// </summary>
        public ClipboardHistoryEntry? UpdateText(ClipboardHistoryEntry entry, string text)
        {
            if (_disposed || entry == null || !ReferenceEquals(_order.Find(entry.Uuid), entry)) return null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (Encoding.Unicode.GetByteCount(text) > MaxTextBytes)
            {
                ItemTooLarge?.Invoke(this, EventArgs.Empty);
                return null;
            }
            string uuid = Hash(text);
            if (uuid == entry.Uuid) return null;
            ClipboardHistoryEntry? other = _order.Find(uuid);
            Erase(entry);
            if (other != null)
            {
                if (entry.IsPinned && !other.IsPinned)
                {
                    _order.Update(other, other.CreatedAt, true);
                    EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("pin", other));
                }
                RaiseChanged();
                return other;
            }
            var edited = new ClipboardHistoryEntry
            {
                Uuid = uuid,
                Text = text,
                CreatedAt = entry.CreatedAt,
                IsPinned = entry.IsPinned,
                SourceApp = entry.SourceApp,
                SourceTitle = entry.SourceTitle,
            };
            _order.Add(edited);
            EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("add", edited));
            RaiseChanged();
            return edited;
        }

        public void Clear()
        {
            _order.Clear();
            // A replay still in flight read the file this deletes; its entries must not come back (S0097).
            if (!_replayApplied) _clearedBeforeReplay = true;
            EnsureJournal().Clear(deleted =>
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
                    if (_order.Add(entry)) EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("add", entry));
                }
                else
                {
                    // "touch" carries both the date and the pin, and replays as exactly this update.
                    _order.Update(action.Existing, action.CopiedAtUtc, action.Pinned);
                    EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("touch", action.Existing));
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
            ClipboardHistoryJournal? journal = _journal;
            if (journal == null) return 0;
            journal.Drain(timeout);
            return journal.Pending;
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
                EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("touch", existing));
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
                EnsureJournal().Append(ClipboardHistoryJournal.Record.Of("add", existing));
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
            ClipboardHistoryJournal? journal = _journal;
            return journal == null || journal.Drain(timeout);
        }

        /// <summary>
        /// Once per session: how many privacy-marked copies were not recorded and how many records could
        /// not be written (S0031 CH2-4). Counts, nothing else.
        /// </summary>
        private void LogSessionSummary()
        {
            int skipped = Volatile.Read(ref _skippedMarked);
            int failed = _journal?.WriteFailures ?? 0;
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
            _journal?.Dispose(); // drains what is queued, up to ClipboardHistoryJournal.DisposeDrain
            LogSessionSummary();
            int abandoned = _journal?.Pending ?? 0;
            if (abandoned > 0)
                ClipboardHistoryLog.Log("history: " + abandoned + " queued records abandoned at exit");
        }
    }
}
