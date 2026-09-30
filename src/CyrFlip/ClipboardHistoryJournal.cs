using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    ///
    /// <para><b>Nothing readable on disk</b> (ticket S0031 CH2-1). A record is written as
    /// <c>{"v":2,"blob":"..."}</c>, the blob being the cipher of the whole record - action, id, dates,
    /// text, source app and window title. The id is the SHA-256 of the text, which reverses a short
    /// code or PIN in milliseconds, so it may not stand beside the blob either; inside it, it is exactly
    /// as protected as the text, which is why no keyed hash is needed. Lines an earlier release wrote
    /// (the text encrypted, everything else in clear) are still read and never rewritten; they go with
    /// the next Clear or with their entry's delete.</para>
    ///
    /// <para><b>A delete erases</b> (CH2-5). "×" is the user's own request, not the silent dropping the
    /// unbounded-history decision forbids: the file is rewritten without any line of that entry (lines
    /// that will not read are carried through verbatim), and only when the rewrite fails is a tombstone
    /// appended instead, so the entry still stays deleted on the next start.</para>
    ///
    /// <para><b>A failed write is counted</b> (CH2-4): <see cref="WriteFailures"/>, and
    /// <see cref="WriteFailed"/> for the first one. It used to vanish without a trace.</para>
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
        private int _writeFailures;

        /// <summary>
        /// The <c>File.Replace</c> of a delete's rewrite. A seam for the test of the fallback: a replace
        /// that fails cannot be produced on demand any other way. No backup file - it would keep exactly
        /// the text the user asked to erase.
        /// </summary>
        internal Action<string, string> ReplaceFile = (source, destination) => File.Replace(source, destination, null);

        /// <summary>The first failed write of this journal. Raised once, on the writer thread.</summary>
        public event Action? WriteFailed;

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

        /// <summary>Records that could not be written - disk full, a lock, the cipher throwing (S0031 CH2-4).</summary>
        public int WriteFailures => Volatile.Read(ref _writeFailures);

        /// <summary>Records still queued; after <see cref="Dispose"/>, the ones it abandoned (CH2-6).</summary>
        public int Pending => Volatile.Read(ref _pending);

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
                    if (command.Record == null) ExecuteClear(command.ClearDone);
                    else if (command.Record.Action == "delete") ExecuteDelete(command.Record);
                    else Write(command.Record);
                }
                catch { CountFailure(); /* history must never affect the clipboard or crash */ }
                finally { Interlocked.Decrement(ref _pending); }
            }
            CloseStream();
        }

        private void CountFailure()
        {
            if (Interlocked.Increment(ref _writeFailures) != 1) return;
            try { WriteFailed?.Invoke(); } catch { }
        }

        /// <summary>One v2 line: the whole record under the cipher, nothing beside it but the version.</summary>
        private string Encode(Record record)
        {
            var inner = new SealedRecord
            {
                Action = record.Action,
                Uuid = record.Uuid,
                CreatedAt = record.CreatedAt,
                IsPinned = record.IsPinned,
            };
            if (record.Action == "add")
            {
                inner.Text = record.Text ?? "";
                inner.SourceApp = record.SourceApp;
                inner.SourceTitle = record.SourceTitle;
            }
            string blob = _cipher.Protect(_writerJson.Serialize(inner));
            return _writerJson.Serialize(new Dictionary<string, object> { { "v", 2 }, { "blob", blob } });
        }

        private void Write(Record record)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("\n" + Encode(record));
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

        /// <summary>Erase the entry's lines; when that cannot be done, append the tombstone instead (CH2-5).</summary>
        private void ExecuteDelete(Record record)
        {
            if (TryPurge(record.Uuid)) return;
            Write(record);
        }

        /// <summary>
        /// Rewrite the file without any line of <paramref name="uuid"/>. A line that will not read is
        /// kept verbatim - it may belong to any entry, and a DPAPI key can come back. False when the
        /// rewrite could not be completed; the file is then as it was.
        /// </summary>
        private bool TryPurge(string uuid)
        {
            CloseStream();
            string temp = _path + ".purge.tmp";
            try
            {
                if (!File.Exists(_path)) return true;
                var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                bool removed = false;
                using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                {
                    foreach (string line in File.ReadLines(_path))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        SealedRecord? r = null;
                        try { r = Decode(json, line); } catch { }
                        if (r != null && r.Uuid == uuid) { removed = true; continue; }
                        writer.Write("\n");
                        writer.Write(line);
                    }
                }
                if (!removed)
                {
                    File.Delete(temp);
                    return true;
                }
                ReplaceFile(temp, _path);
                return true;
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
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
            SealedRecord? r = Decode(json, line);
            if (r == null || string.IsNullOrEmpty(r.Uuid)) return false;
            ClipboardHistoryEntry? e = order.Find(r.Uuid);
            switch (r.Action)
            {
                case "delete":
                    if (e != null) order.Remove(e);
                    return true;
                case "add":
                    if (r.Text == null) return false;
                    if (e != null) return true; // already present: a repeated add changes nothing
                    order.Add(new ClipboardHistoryEntry
                    {
                        Uuid = r.Uuid,
                        Text = r.Text,
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

        /// <summary>
        /// One line of either shape as a record with its text in clear; null for a line this journal
        /// cannot read - an unknown version, a blob the cipher refuses, a v1 payload it refuses.
        /// </summary>
        private SealedRecord? Decode(JavaScriptSerializer json, string line)
        {
            var raw = json.Deserialize<Dictionary<string, object>>(line);
            if (raw == null) return null;
            if (raw.TryGetValue("v", out object? version))
            {
                if (!(version is int v) || v != 2) return null;
                if (!raw.TryGetValue("blob", out object? blob) || !(blob is string sealedText)) return null;
                string? inner = _cipher.Unprotect(sealedText);
                return inner == null ? null : json.Deserialize<SealedRecord>(inner);
            }
            // v1, written before S0031: only the payload is encrypted.
            LegacyLine legacy = json.ConvertToType<LegacyLine>(raw);
            var r = new SealedRecord
            {
                Action = legacy.Action,
                Uuid = legacy.Uuid,
                CreatedAt = legacy.CreatedAt,
                IsPinned = legacy.IsPinned,
                SourceApp = legacy.SourceApp,
                SourceTitle = legacy.SourceTitle,
            };
            if (legacy.Action == "add" && !string.IsNullOrEmpty(legacy.Payload))
            {
                r.Text = _cipher.Unprotect(legacy.Payload!);
                if (r.Text == null) return null;
            }
            return r;
        }

        /// <summary>
        /// Completes the queue and waits up to <see cref="DisposeDrain"/> for the writer to finish it;
        /// what it could not wait for stays in <see cref="Pending"/> for the caller to report (CH2-6).
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.CompleteAdding();
            _writer.Join(DisposeDrain);
        }

        /// <summary>What a v2 blob holds: the whole record, the text in clear inside the cipher.</summary>
        private sealed class SealedRecord
        {
            public string Action { get; set; } = "";
            public string Uuid { get; set; } = "";
            public long CreatedAt { get; set; }
            public bool IsPinned { get; set; }
            public string? Text { get; set; }
            public string? SourceApp { get; set; }
            public string? SourceTitle { get; set; }
        }

        /// <summary>The v1 shape earlier releases wrote - read, never written again.</summary>
        private sealed class LegacyLine
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
