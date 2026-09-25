using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The clipboard history's append-only file: the replay at startup and the one writer behind every
    /// record (spec S0005 CH-2, CH-3).
    ///
    /// <para><b>One writer, in order.</b> Every record goes through one queue drained by one dedicated
    /// thread that owns the <see cref="FileStream"/>. Records used to be separate thread-pool items: a
    /// lock serialized them but did not order them, so a <c>delete X</c> could land before its
    /// <c>add X</c> and X came back on restart, and a <c>Clear</c> could run while an append held the
    /// file open. <see cref="Clear"/> is now a queued command like any record, in order with the
    /// appends around it. Records are snapshots taken by the caller (<see cref="Record"/>), never read
    /// from a live entry by the writer.</para>
    ///
    /// <para><b>A bad line costs one entry.</b> The replay reads line by line, and a line that will not
    /// read - torn by a crash, undecryptable after an administrator password reset, not JSON - is
    /// skipped and counted. It used to empty the whole history, on every start, forever. Every record
    /// is written as <c>"\n" + json</c>, so the record after a torn tail starts a line of its own
    /// instead of being glued onto it. Skipped lines are never removed from disk (the history is
    /// unbounded by decision: nothing is dropped from the file).</para>
    /// </summary>
    internal sealed class ClipboardHistoryJournal : IDisposable
    {
        /// <summary>How long <see cref="Dispose"/> waits for the queue to drain.</summary>
        public static readonly TimeSpan DisposeDrain = TimeSpan.FromSeconds(2);

        /// <summary>One record, snapshotted by the caller. <see cref="Text"/> is set for <c>add</c> only.</summary>
        internal sealed class Record
        {
            public string Action = "";
            public string Uuid = "";
            public long CreatedAt;
            public bool IsPinned;
            public string? Text;
            public string? SourceApp;
            public string? SourceTitle;

            public static Record Of(string action, ClipboardHistoryEntry entry)
            {
                var record = new Record { Action = action, Uuid = entry.Uuid, CreatedAt = entry.CreatedAt.Ticks, IsPinned = entry.IsPinned };
                if (action == "add")
                {
                    record.Text = entry.Text;
                    record.SourceApp = entry.SourceApp;
                    record.SourceTitle = entry.SourceTitle;
                }
                return record;
            }
        }

        private sealed class Command
        {
            public Record? Record;
            public Action<bool>? ClearDone;   // set for a Clear command
        }

        private readonly string _path;
        private readonly IQuickNotesCipher _cipher;
        private readonly Action? _beforeCommand;
        private readonly BlockingCollection<Command> _queue = new BlockingCollection<Command>();
        private readonly JavaScriptSerializer _writerJson = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly Thread _writer;
        private FileStream? _stream;
        private int _pending;
        private int _disposed;

        /// <param name="beforeCommand">Test seam: runs on the writer thread before each command (e.g. a random yield).</param>
        internal ClipboardHistoryJournal(string path, IQuickNotesCipher cipher, Action? beforeCommand = null)
        {
            _path = path;
            _cipher = cipher;
            _beforeCommand = beforeCommand;
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "CyrFlip clipboard history writer" };
            _writer.Start();
        }

        public string Path => _path;

        /// <summary>Queue one record. Safe from any thread; ignored after <see cref="Dispose"/>.</summary>
        public void Append(Record record) => Enqueue(new Command { Record = record });

        /// <summary>
        /// Queue "close the file, delete it, reopen on the next record". <paramref name="done"/> runs on
        /// the writer thread with false when the file could not be deleted (another program holds it).
        /// </summary>
        public void Clear(Action<bool> done) => Enqueue(new Command { ClearDone = done });

        private void Enqueue(Command command)
        {
            Interlocked.Increment(ref _pending);
            try { _queue.Add(command); }
            catch (InvalidOperationException) { Interlocked.Decrement(ref _pending); } // disposed: adding completed
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> for everything queued so far to reach the disk; true
        /// when nothing is left. At sign-out the process is terminated right after <c>WM_ENDSESSION</c>,
        /// so a record still in the queue would otherwise be abandoned.
        /// </summary>
        public bool Drain(TimeSpan timeout) =>
            SpinWait.SpinUntil(() => Volatile.Read(ref _pending) == 0, timeout);

        private void WriterLoop()
        {
            foreach (Command command in _queue.GetConsumingEnumerable())
            {
                try
                {
                    _beforeCommand?.Invoke();
                    if (command.Record != null) Write(command.Record);
                    else ExecuteClear(command.ClearDone);
                }
                catch { /* history must never affect the clipboard or crash */ }
                finally { Interlocked.Decrement(ref _pending); }
            }
            CloseStream();
        }

        private void Write(Record record)
        {
            var line = new HistoryLine { Action = record.Action, Uuid = record.Uuid, CreatedAt = record.CreatedAt, IsPinned = record.IsPinned };
            if (record.Action == "add")
            {
                line.Payload = _cipher.Protect(record.Text ?? "");
                line.SourceApp = record.SourceApp;
                line.SourceTitle = record.SourceTitle;
            }
            byte[] bytes = Encoding.UTF8.GetBytes("\n" + _writerJson.Serialize(line));
            try
            {
                _stream ??= new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch
            {
                // Reopen on the next record rather than keep writing through a broken handle.
                CloseStream();
                throw;
            }
        }

        private void ExecuteClear(Action<bool>? done)
        {
            CloseStream();
            bool deleted;
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
                deleted = !File.Exists(_path);
            }
            catch { deleted = false; }
            try { done?.Invoke(deleted); } catch { }
        }

        private void CloseStream()
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }

        /// <summary>
        /// Replays the file into <paramref name="order"/>. Returns how many non-empty lines could not be
        /// read; each of those costs only itself. Call before the first record is queued.
        /// </summary>
        public int Load(ClipboardHistoryOrder order)
        {
            if (!File.Exists(_path)) return 0;
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            int skipped = 0;
            try
            {
                foreach (string line in File.ReadLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        if (!Replay(json, line, order)) skipped++;
                    }
                    catch { skipped++; }
                }
            }
            catch { /* the file could not be read at all: keep what replayed */ }
            return skipped;
        }

        /// <summary>False for a line that parsed but is not a usable record.</summary>
        private bool Replay(JavaScriptSerializer json, string line, ClipboardHistoryOrder order)
        {
            HistoryLine? r = json.Deserialize<HistoryLine>(line);
            if (r == null || string.IsNullOrEmpty(r.Uuid)) return false;
            ClipboardHistoryEntry? e = order.Find(r.Uuid);
            switch (r.Action)
            {
                case "delete":
                    if (e != null) order.Remove(e);
                    return true;
                case "add":
                    if (string.IsNullOrEmpty(r.Payload)) return false;
                    if (e != null) return true; // already present: a repeated add changes nothing
                    string? text = _cipher.Unprotect(r.Payload!);
                    if (text == null) return false;
                    order.Add(new ClipboardHistoryEntry
                    {
                        Uuid = r.Uuid,
                        Text = text,
                        CreatedAt = new DateTime(r.CreatedAt, DateTimeKind.Utc),
                        IsPinned = r.IsPinned,
                        SourceApp = r.SourceApp ?? "",
                        SourceTitle = r.SourceTitle ?? "",
                    });
                    return true;
                case "touch":
                case "pin":
                case "unpin":
                    // All three carry the same two fields, so one call covers them.
                    if (e != null) order.Update(e, new DateTime(r.CreatedAt, DateTimeKind.Utc), r.IsPinned);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Completes the queue and waits up to <see cref="DisposeDrain"/> for the writer to finish it.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.CompleteAdding();
            _writer.Join(DisposeDrain);
        }

        /// <summary>The on-disk shape - unchanged from the files earlier releases wrote.</summary>
        private sealed class HistoryLine
        {
            public string Action { get; set; } = "";
            public string Uuid { get; set; } = "";
            public long CreatedAt { get; set; }
            public bool IsPinned { get; set; }
            public string? Payload { get; set; }
            public string? SourceApp { get; set; }
            public string? SourceTitle { get; set; }
        }
    }
}
