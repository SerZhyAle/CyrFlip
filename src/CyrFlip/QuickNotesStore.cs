using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// How one journal line is protected on its way to disk. It is an interface for exactly one
    /// reason: the store's own logic - replay, a corrupt line, compaction, the backup - must be
    /// testable without DPAPI, which is a per-user machine secret and has no business inside a unit
    /// test. The app only ever uses <see cref="QuickNotesCipher.Dpapi"/>.
    /// </summary>
    internal interface IQuickNotesCipher
    {
        string Protect(string plain);
        /// <summary>Null for a line this cipher cannot read - a corrupt or foreign record.</summary>
        string? Unprotect(string cipher);
    }

    /// <summary>
    /// A cipher that can tell a whole ciphertext it cannot open from a <b>fragment</b> of one (S0035
    /// QN2-2). The first may open again - a DPAPI key lost to an admin password reset comes back when
    /// that is undone - and is carried; the second, a record a kill cut off mid-append, never will.
    /// Optional: a cipher without it is judged by whether the line is base64 at all.
    /// </summary>
    internal interface IQuickNotesCipherShape
    {
        bool IsWhole(string cipher);
    }

    /// <summary>DPAPI, scoped to the current Windows user (spec §5.3).</summary>
    internal sealed class QuickNotesCipher : IQuickNotesCipher, IQuickNotesCipherShape
    {
        public static readonly QuickNotesCipher Dpapi = new QuickNotesCipher();

        /// <summary>The DPAPI provider every <c>CryptProtectData</c> blob names: df9d8cd0-1501-11d1-8c7a-00c04fc297eb.</summary>
        private static readonly Guid DpapiProvider = new Guid("df9d8cd0-1501-11d1-8c7a-00c04fc297eb");

        public bool IsWhole(string cipher)
        {
            byte[] blob;
            try { blob = Convert.FromBase64String(cipher); }
            catch { return false; } // not even base64 - a fragment cut mid-character
            return IsWholeBlob(blob);
        }

        /// <summary>
        /// Walks the blob <c>CryptProtectData</c> writes - version, provider, master key version and id,
        /// flags, then the description, cipher, salt, HMAC key, hash, second HMAC key, data and
        /// signature, each length-prefixed - and says whether every field is all there. A fragment of a
        /// blob runs out of bytes on the way; a whole one does not. Anything that does not start like a
        /// DPAPI blob is called whole: the rule only ever drops what it is sure of.
        /// </summary>
        internal static bool IsWholeBlob(byte[] blob)
        {
            int at = 0;
            bool Take(int count)
            {
                if (count < 0 || blob.Length - at < count) return false;
                at += count;
                return true;
            }
            bool Sized()
            {
                if (blob.Length - at < 4) return false;
                int length = BitConverter.ToInt32(blob, at);
                at += 4;
                return Take(length);
            }

            if (blob.Length < 20) return false;
            if (BitConverter.ToInt32(blob, 0) != 1) return true;
            var provider = new byte[16];
            Array.Copy(blob, 4, provider, 0, 16);
            if (new Guid(provider) != DpapiProvider) return true;
            at = 20;
            return Take(4 + 16 + 4)         // master key version, master key id, flags
                && Sized()                  // description
                && Take(4 + 4)              // cipher algorithm, its key length
                && Sized()                  // salt
                && Sized()                  // HMAC key
                && Take(4 + 4)              // hash algorithm, its length
                && Sized()                  // second HMAC key
                && Sized()                  // the data
                && Sized();                 // the signature
        }

        public string Protect(string plain)
            => Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

        public string? Unprotect(string cipher)
        {
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The notes on disk: an append-only journal of <c>create</c> / <c>update</c> / <c>delete</c>
    /// records, one per line, each line the DPAPI-protected snapshot of that operation (spec §5.2).
    /// Replayed into memory at startup, compacted into a fresh snapshot once it has grown past
    /// <see cref="CompactAfterOperations"/> operations or when the app exits.
    ///
    /// <para><b>Not SQLite, on purpose.</b> The project is on net48 with no NuGet runtime
    /// dependency at all, and for a personal notepad an append-only journal is both trivially
    /// recoverable and trivially inspectable. The price is the compaction below, which is a
    /// temp-file-then-<c>File.Replace</c>, so the previous journal survives as <c>.bak</c> and a
    /// crash mid-write can never leave a half-written file in place of the real one.</para>
    ///
    /// <para><b>The whole record is encrypted, not just its payload.</b> The clipboard history
    /// protects only the text and keeps the metadata in the clear; here the title is as likely to
    /// name a customer or a server as the body is to hold a token, so nothing readable is written
    /// beside it.</para>
    ///
    /// <para>A line that cannot be read is <b>skipped and counted</b>, never fatal: one damaged
    /// record must not cost the user every other note (spec §8, acceptance criteria).</para>
    /// </summary>
    internal sealed class QuickNotesStore
    {
        /// <summary>Operations appended since the last snapshot before a compaction is worth doing.</summary>
        public const int CompactAfterOperations = 500;

        /// <summary>The journal's file name, in the same MSIX-aware folder as <c>layout.txt</c>.</summary>
        public const string FileName = "quick-notes.log";

        private const string ActionCreate = "create";
        private const string ActionUpdate = "update";
        private const string ActionDelete = "delete";

        private readonly string _path;
        private readonly IQuickNotesCipher _cipher;
        // The default MaxJsonLength is 2 MB, and a long checklist carries ~90 bytes of JSON per item on
        // top of its text: past that Serialize threw, the append swallowed it, and a note the window
        // showed as saved was gone after a restart (ticket S0006, QN-6).
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        // Every write to the journal goes through this. Saves arrive from the debounce timer's
        // thread pool thread while the UI thread may be committing an edit of its own.
        private readonly object _gate = new object();
        private int _operations;
        // The operation count at which the next compaction is due: the size of a fresh snapshot plus
        // CompactAfterOperations, i.e. once that many records are more than the notes need (S0035
        // QN2-8). A plain ">= 500 records" rewrote every note on every save for anyone with 500 notes,
        // and after a failed compaction the same again every 750 ms of typing.
        private int _compactAt = CompactAfterOperations;
        // The last Load met fragments of records (a kill mid-append): one compaction drops them, so
        // the next start does not warn about them again (S0035 QN2-2).
        private bool _dropFragments;
        // Ids whose last record in the replayed journal is a delete - what a merge must not bring back
        // (S0035 QN2-7).
        private readonly HashSet<Guid> _deleted = new HashSet<Guid>();
        // The last Load stopped part way through the file (locked, an I/O error). What it replayed is
        // only the head of the journal, so a snapshot of it must never replace the journal.
        private bool _readFailed;
        // Lines the last Load could not read, exactly as they are on disk. A compaction carries them
        // through verbatim: they are already encrypted text, and a record DPAPI cannot open today
        // (the master key lost to an admin password reset) may open again once that is undone.
        // Dropping them made the second compaction - the one that overwrites the .bak - the moment
        // they were gone for good (ticket S0006, QN-5).
        private readonly List<string> _unreadable = new List<string>();

        /// <summary>
        /// The <see cref="File.Replace(string,string,string)"/> call of a compaction. A seam for one
        /// test: the failure that leaves the journal renamed to <c>.bak</c> and the replacement not
        /// moved in cannot be produced on demand any other way.
        /// </summary>
        internal Action<string, string, string> ReplaceFile = (source, destination, backup)
            => File.Replace(source, destination, backup);

        public QuickNotesStore() : this(DiagnosticLog.Path(FileName), null) { }

        internal QuickNotesStore(string path, IQuickNotesCipher? cipher = null)
        {
            _path = path;
            _cipher = cipher ?? QuickNotesCipher.Dpapi;
        }

        public string Path => _path;
        public string BackupPath => _path + ".bak";

        /// <summary>Journal lines the last <see cref="Load"/> could not read. Diagnostics only.</summary>
        public int SkippedRecords { get; private set; }

        /// <summary>
        /// Of <see cref="SkippedRecords"/>, the fragments of records cut off mid-append - never
        /// readable, so not carried by a compaction (S0035 QN2-2).
        /// </summary>
        public int TornRecords { get; private set; }

        /// <summary>Ids the last <see cref="Load"/> saw deleted and not created again.</summary>
        public IReadOnlyCollection<Guid> DeletedIds
        {
            get { lock (_gate) return new List<Guid>(_deleted); }
        }

        /// <summary>
        /// True when the last <see cref="Load"/> found no journal but a <c>.bak</c>, and brought the
        /// notes back from it. Said once, by the caller - a silent empty list is the failure it avoids.
        /// </summary>
        public bool RecoveredFromBackup { get; private set; }

        /// <summary>Operations appended since the journal was last written whole.</summary>
        public int PendingOperations => _operations;

        /// <summary>
        /// True once enough has been appended that a snapshot would pay for itself, or when the last
        /// load met fragments a snapshot would drop.
        /// </summary>
        public bool NeedsCompaction => !_readFailed && (_operations >= _compactAt || _dropFragments);

        /// <summary>
        /// Replay the journal into the live set of notes, newest first. A missing file is an empty
        /// set, not an error - that is simply the first launch.
        /// </summary>
        public List<QuickNote> Load()
        {
            SkippedRecords = 0;
            TornRecords = 0;
            RecoveredFromBackup = false;
            var byId = new Dictionary<Guid, QuickNote>();
            var order = new List<Guid>();
            lock (_gate)
            {
                _operations = 0;
                _readFailed = false;
                _dropFragments = false;
                _compactAt = CompactAfterOperations;
                _unreadable.Clear();
                _deleted.Clear();
                if (!File.Exists(_path) && !TryRestoreBackup()) return new List<QuickNote>();
                try
                {
                    foreach (string line in File.ReadLines(_path))
                    {
                        if (line.Length == 0) continue;
                        _operations++;
                        Record? record = ReadRecord(line, out bool opened);
                        if (record == null || !Guid.TryParse(record.Id, out Guid id))
                        {
                            SkippedRecords++;
                            // What opens but is no record may be a later format; a whole ciphertext
                            // may open again. Only a fragment is let go of (QN2-2).
                            if (opened || IsWhole(line)) _unreadable.Add(line);
                            else TornRecords++;
                            continue;
                        }

                        if (record.Action == ActionDelete)
                        {
                            if (byId.Remove(id)) order.Remove(id);
                            _deleted.Add(id);
                            continue;
                        }
                        QuickNote note = ToNote(record, id);
                        if (!byId.ContainsKey(id)) order.Add(id);
                        byId[id] = note;
                        _deleted.Remove(id);
                    }
                }
                catch (Exception ex)
                {
                    // An unreadable journal file (locked, truncated mid-line) leaves whatever was
                    // replayed so far standing; the alternative is to hand the user an empty app.
                    _readFailed = true;
                    QuickNotesLog.Log("journal read failed: " + ex.GetType().Name + " " + ex.Message);
                }
                _compactAt = byId.Count + _unreadable.Count + CompactAfterOperations;
                _dropFragments = TornRecords > 0;
            }

            if (SkippedRecords > 0)
                QuickNotesLog.Log("replay skipped " + SkippedRecords + " unreadable record(s) of " + _operations
                    + (TornRecords > 0 ? ", " + TornRecords + " of them fragments dropped at the next compaction" : ""));

            var notes = new List<QuickNote>(byId.Count);
            foreach (Guid id in order)
            {
                QuickNote note = byId[id];
                note.SortItems();
                notes.Add(note);
            }
            QuickNotesOrder.Sort(notes);
            return notes;
        }

        /// <summary>
        /// The journal is missing but its <c>.bak</c> is not - a compaction whose replace went half
        /// way, or a journal deleted by hand. The backup is copied back into place rather than merely
        /// read: the next append would otherwise start a fresh journal holding only itself, and the
        /// restart after that would ignore the backup and show just that one note.
        /// Called under <see cref="_gate"/>.
        /// </summary>
        private bool TryRestoreBackup()
        {
            if (!File.Exists(BackupPath)) return false;
            try
            {
                File.Copy(BackupPath, _path, overwrite: false);
                RecoveredFromBackup = true;
                QuickNotesLog.Log("journal missing - restored from the backup");
                return true;
            }
            catch (Exception ex)
            {
                QuickNotesLog.Log("journal restore from the backup failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>Record the note's creation. The caller has already assigned its dates. False when nothing was written.</summary>
        public bool Create(QuickNote note) => Append(ActionCreate, note);

        /// <summary>Record the note's current state; replay keeps the last one it sees. False when nothing was written.</summary>
        public bool Update(QuickNote note) => Append(ActionUpdate, note);

        public bool Delete(Guid id)
        {
            var record = new Record { Action = ActionDelete, Id = id.ToString("D") };
            return WriteLine(record);
        }

        /// <summary>
        /// Record many creations and updates in <b>one</b> append - an import of a few thousand notes
        /// (ticket S0030 HT-2). Each record is encrypted on the calling thread, which the caller makes
        /// a worker; the file is opened once. The notes must not change while this runs. False when
        /// nothing was written; the records are then in memory only, as with a failed single append.
        /// </summary>
        public bool AppendBatch(IEnumerable<KeyValuePair<bool, QuickNote>> createdAndNotes)
        {
            try
            {
                lock (_gate) // the serializer is shared with every other write, all of them under this lock
                {
                    var text = new StringBuilder();
                    int count = 0;
                    foreach (KeyValuePair<bool, QuickNote> pair in createdAndNotes)
                    {
                        // Leading line break, as in WriteLine (QN-7).
                        text.Append(Environment.NewLine)
                            .Append(_cipher.Protect(_json.Serialize(ToRecord(pair.Key ? ActionCreate : ActionUpdate, pair.Value))));
                        count++;
                    }
                    if (count == 0) return true;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                    File.AppendAllText(_path, text.ToString(), Encoding.UTF8);
                    _operations += count;
                    return true;
                }
            }
            catch (Exception ex)
            {
                QuickNotesLog.Log("batch append failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private bool Append(string action, QuickNote note)
        {
            Record record = ToRecord(action, note);
            return WriteLine(record);
        }

        private bool WriteLine(Record record)
        {
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                    // The line break goes first, not last (ticket S0006, QN-7). A crash mid-append
                    // leaves the file without a final newline; a record written after it with only a
                    // trailing break was glued onto that fragment and lost with it - a create never
                    // followed by an update gone, a delete undone. A leading break costs at most an
                    // empty line, and replay skips those.
                    File.AppendAllText(_path, Environment.NewLine + _cipher.Protect(_json.Serialize(record)),
                        Encoding.UTF8);
                    _operations++;
                    return true;
                }
            }
            catch (Exception ex)
            {
                QuickNotesLog.Log("append failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Rewrite the journal as one <c>create</c> record per live note. The new file is built
        /// beside the old one and put in its place with <see cref="File.Replace(string,string,string)"/>,
        /// which is atomic and hands the previous journal over as the <c>.bak</c> in the same call -
        /// so at no instant is there no complete file on disk.
        ///
        /// <para>Lines the last <see cref="Load"/> could not read go first, verbatim: a snapshot record
        /// that follows them still wins on replay, and they are not the store's to destroy.</para>
        /// </summary>
        public void Compact(IEnumerable<QuickNote> notes) => Compact(() => notes);

        /// <summary>
        /// The same, with the snapshot taken <b>inside</b> the journal lock. That is what makes a
        /// background compaction safe: every change reaches the in-memory list before its record is
        /// appended, so a snapshot taken under the lock that also orders the appends never misses a
        /// record that ends up in the file it replaces - a delete appended to the old journal while
        /// the snapshot still held the note would otherwise bring that note back.
        /// </summary>
        public void Compact(Func<IEnumerable<QuickNote>> snapshot)
        {
            string temp = _path + ".tmp";
            lock (_gate)
            {
                if (_readFailed)
                {
                    QuickNotesLog.Log("compaction skipped: the journal was only partly read");
                    return;
                }
                int count = 0;
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                    var text = new StringBuilder();
                    foreach (string line in _unreadable)
                    {
                        text.Append(line).Append(Environment.NewLine);
                        count++;
                    }
                    foreach (QuickNote note in snapshot())
                    {
                        text.Append(_cipher.Protect(_json.Serialize(ToRecord(ActionCreate, note))))
                            .Append(Environment.NewLine);
                        count++;
                    }
                    File.WriteAllText(temp, text.ToString(), Encoding.UTF8);

                    if (File.Exists(_path)) ReplaceFile(temp, _path, BackupPath);
                    else File.Move(temp, _path);
                    _operations = count;
                    _compactAt = count + CompactAfterOperations;
                    _dropFragments = false;
                }
                catch (Exception ex)
                {
                    QuickNotesLog.Log("compaction failed: " + ex.GetType().Name + " " + ex.Message);
                    // Not again on the next save (QN2-8): a replace that fails once - a backup tool
                    // holding the journal without delete sharing - tends to fail every time, and each
                    // try re-encrypts and rewrites every note.
                    _compactAt = _operations + CompactAfterOperations;
                    _dropFragments = false;
                    // File.Replace can fail half way: the journal already renamed to .bak and the
                    // replacement not moved in (an antivirus holding the .tmp). Deleting the .tmp
                    // then left no journal at all, and the next start showed an empty list without
                    // a word. Whatever is complete goes back under the journal's name first.
                    try
                    {
                        if (!File.Exists(_path))
                        {
                            if (File.Exists(temp)) { File.Move(temp, _path); _operations = count; }
                            else if (File.Exists(BackupPath)) File.Move(BackupPath, _path);
                        }
                    }
                    catch (Exception again)
                    {
                        QuickNotesLog.Log("compaction recovery failed: " + again.GetType().Name + " " + again.Message);
                    }
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }
        }

        /// <summary>
        /// "Delete every quick note": the journal and its backup both go (spec §5.3). Leaving the
        /// <c>.bak</c> behind would mean the notes the user just destroyed are still on disk - and so
        /// would the <c>.tmp</c> a crash mid-compaction leaves (a whole snapshot) and the journals the
        /// S0016 migration kept beside this one, <c>quick-notes.migrated-&lt;date&gt;.log</c> with their
        /// <c>.bak</c> and merge marker (S0035 QN2-3).
        /// </summary>
        public void DeleteEverything()
        {
            lock (_gate)
            {
                try { if (File.Exists(_path)) File.Delete(_path); } catch { }
                try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
                try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
                foreach (string kept in MigratedJournals(System.IO.Path.GetDirectoryName(_path) ?? "", "*"))
                    try { File.Delete(kept); } catch { }
                _operations = 0;
                _compactAt = CompactAfterOperations;
                _readFailed = false;
                _dropFragments = false;
                // "Everything" includes what could not be read: carrying it into the next journal
                // would keep on disk exactly what the user asked to destroy.
                _unreadable.Clear();
                _deleted.Clear();
            }
        }

        /// <summary>The name the S0016 migration gives a journal it keeps: <c>quick-notes.migrated-yyyyMMdd[-n].log</c>.</summary>
        internal static string MigratedPrefix => System.IO.Path.GetFileNameWithoutExtension(FileName) + ".migrated-";

        /// <summary>
        /// The migration's kept journals in <paramref name="folder"/>, by <paramref name="suffix"/>
        /// (<c>*.log</c> for the journals themselves, <c>*</c> for them and everything beside them).
        /// </summary>
        internal static string[] MigratedJournals(string folder, string suffix)
        {
            try
            {
                return Directory.Exists(folder)
                    ? Directory.GetFiles(folder, MigratedPrefix + suffix)
                    : new string[0];
            }
            catch { return new string[0]; }
        }

        /// <summary>A line the cipher cannot open: whole (may open later) or a fragment (never will)?</summary>
        private bool IsWhole(string line)
        {
            if (_cipher is IQuickNotesCipherShape shape) return shape.IsWhole(line);
            try { Convert.FromBase64String(line); return true; }
            catch { return false; }
        }

        private Record? ReadRecord(string line, out bool opened)
        {
            opened = false;
            try
            {
                string? plain = _cipher.Unprotect(line);
                if (plain == null) return null;
                opened = true;
                Record? record = _json.Deserialize<Record>(plain);
                if (record == null || string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Action))
                    return null;
                return record;
            }
            catch
            {
                return null;
            }
        }

        private static Record ToRecord(string action, QuickNote note)
        {
            var record = new Record
            {
                Action = action,
                Id = note.Id.ToString("D"),
                Created = note.CreatedAtUtc.Ticks,
                Updated = note.UpdatedAtUtc.Ticks,
                Title = note.Title,
                Kind = note.Kind == QuickNoteKind.Checklist ? "checklist" : "text",
                Raw = note.RawText,
                Items = new List<ItemRecord>(note.Items.Count),
            };
            foreach (QuickNoteItem item in note.Items)
                record.Items.Add(new ItemRecord
                {
                    Id = item.Id.ToString("D"),
                    Text = item.Text,
                    Checked = item.IsChecked,
                    Position = item.Position,
                });
            return record;
        }

        private static QuickNote ToNote(Record record, Guid id)
        {
            var note = new QuickNote
            {
                Id = id,
                CreatedAtUtc = new DateTime(record.Created, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(record.Updated, DateTimeKind.Utc),
                Title = QuickNote.NormalizeTitle(record.Title),
                Kind = record.Kind == "checklist" ? QuickNoteKind.Checklist : QuickNoteKind.Text,
                RawText = record.Raw ?? "",
            };
            if (record.Items == null) return note;
            foreach (ItemRecord item in record.Items)
                note.Items.Add(new QuickNoteItem
                {
                    Id = Guid.TryParse(item.Id, out Guid itemId) ? itemId : Guid.NewGuid(),
                    Text = item.Text ?? "",
                    IsChecked = item.Checked,
                    Position = item.Position,
                });
            return note;
        }

        /// <summary>
        /// The on-disk shape, deliberately separate from <see cref="QuickNote"/>: dates travel as
        /// ticks and ids as strings, so the format does not depend on how a serializer happens to
        /// render a <c>Guid</c> or a <c>DateTime</c> today.
        /// </summary>
        private sealed class Record
        {
            public string Action { get; set; } = "";
            public string Id { get; set; } = "";
            public long Created { get; set; }
            public long Updated { get; set; }
            public string? Title { get; set; }
            public string Kind { get; set; } = "text";
            public string? Raw { get; set; }
            public List<ItemRecord>? Items { get; set; }
        }

        private sealed class ItemRecord
        {
            public string Id { get; set; } = "";
            public string? Text { get; set; }
            public bool Checked { get; set; }
            public int Position { get; set; }
        }
    }
}
