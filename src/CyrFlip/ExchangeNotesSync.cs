using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// What this device remembers about one note of the account's store (<c>DEVICE-EXCHANGE</c> 1.13, item FM): the
    /// revision it last saw and whether the local copy changed since. No title, no text - the state never leaves the
    /// device and needs no secret (ticket S0105, assumption A2).
    /// </summary>
    internal sealed class ExchangeNoteSyncState
    {
        /// <summary>The <c>revision</c> last seen for the note; 0 while the server never stored it.</summary>
        public long Revision { get; set; }
        /// <summary>The local copy changed since <see cref="Revision"/> and waits to be put.</summary>
        public bool Changed { get; set; }
        /// <summary>The note was deleted here and the delete waits to be put.</summary>
        public bool DeletedLocally { get; set; }
        /// <summary>The record at <see cref="Revision"/> has a format this build does not read (item FH, assumption A6).</summary>
        public bool Unsupported { get; set; }
        /// <summary>The payload is above the limit of item FH; the note stays on this device (assumption A4).</summary>
        public bool TooLarge { get; set; }
        /// <summary>The devices the stored box addresses (its <c>keys[].to</c> and <c>from</c>) - what decides a reseal (item FJ).</summary>
        public List<string> Recipients { get; set; } = new List<string>();
    }

    /// <summary>The sync state of one account entry (item FK: one entry only). Mutated by the sync and by the local hooks.</summary>
    internal sealed class ExchangeNotesSyncState
    {
        public string EntryId { get; set; } = "";
        /// <summary>The account's <c>notesRevision</c> after the last complete listing - the next <c>since</c>.</summary>
        public long NotesRevision { get; set; }
        public Dictionary<Guid, ExchangeNoteSyncState> Notes { get; set; } = new Dictionary<Guid, ExchangeNoteSyncState>();

        public ExchangeNoteSyncState Of(Guid id)
        {
            if (!Notes.TryGetValue(id, out ExchangeNoteSyncState? st))
            {
                st = new ExchangeNoteSyncState();
                Notes[id] = st;
            }
            return st;
        }

        /// <summary>
        /// A local save: the note waits to be put. A note the server never stored needs no mark - it has no state. A note
        /// deleted here and brought back under its id (an exchange-file import) is an edit again, not a delete.
        /// </summary>
        public void MarkChanged(Guid id)
        {
            if (Notes.TryGetValue(id, out ExchangeNoteSyncState? st)) { st.Changed = true; st.TooLarge = false; st.DeletedLocally = false; }
        }

        /// <summary>A local delete: a stored note waits for its tombstone, a never-stored one is simply forgotten.</summary>
        public void MarkDeleted(Guid id)
        {
            if (!Notes.TryGetValue(id, out ExchangeNoteSyncState? st)) return;
            if (st.Revision == 0) { Notes.Remove(id); return; }
            st.DeletedLocally = true;
            st.Changed = false;
        }

        /// <summary>Binds the state to <paramref name="entryId"/>; another entry starts over (notes are never merged across servers).</summary>
        public bool BindTo(string entryId)
        {
            if (EntryId == entryId) return false;
            EntryId = entryId;
            NotesRevision = 0;
            Notes.Clear();
            return true;
        }
    }

    /// <summary>
    /// The local notes as the sync sees them. Implemented over <see cref="QuickNotesService"/> by the journal binding
    /// (phase 3) and by a list in the tests. Every call returns or takes copies; <see cref="ApplyRemote"/> keeps the
    /// <c>CreatedAtUtc</c> of a note that already exists.
    /// </summary>
    internal interface IExchangeNotesLocal
    {
        IReadOnlyList<QuickNote> Snapshot();
        QuickNote? Find(Guid id);
        /// <summary>
        /// Creates the note, or replaces the content of the note with its id. Never marks it changed. False when the
        /// local copy is being edited right now (an unsaved edit, a mark not yet seen by the sync): nothing changed.
        /// </summary>
        bool ApplyRemote(QuickNote note);
        /// <summary>
        /// Removes the note because the account deleted it. Never marks it deleted. False, and nothing removed, while
        /// the note is being edited - unless <paramref name="evenIfEdited"/>: a re-key whose text already lives on.
        /// </summary>
        bool DeleteRemote(Guid id, bool evenIfEdited);
        /// <summary>A new note this device made (a conflict's surviving version). The sync puts it itself.</summary>
        void AddLocal(QuickNote note);
    }

    internal sealed class ExchangeNotesSyncReport
    {
        /// <summary>Empty when the pass ran to its end; else the code that stopped it.</summary>
        public string Code = "";
        public int Received;
        public int Deleted;
        public int Sent;
        /// <summary>Conflicts resolved by item FJ - never a failure.</summary>
        public int Conflicts;
        public int TooLarge;
        public int Unsupported;
        /// <summary>Records whose box this device cannot open or whose payload is damaged.</summary>
        public int Unreadable;
        /// <summary>The server keeps no more notes: new notes stay local (item FL).</summary>
        public bool LimitReached;
        public bool Ok => Code.Length == 0;
    }

    /// <summary>
    /// One sync pass of the quick notes with the notes store of one account entry (<c>DEVICE-EXCHANGE</c> 1.13, items
    /// FG to FM, ticket S0105): list what changed since the last pass, apply it where the local copy did not change,
    /// then put every local change - each note sealed for the devices of the account that hold the role <c>notes</c>
    /// and a trusted key, this device included (assumption A9). Conflicts are resolved by item FJ and no text is ever
    /// lost: whenever two versions meet, both survive. Nothing here knows WinForms, the journal or the clock of the UI.
    /// </summary>
    internal sealed class ExchangeNotesSync
    {
        /// <summary>The server does not keep notes (no <c>notes</c> feature).</summary>
        public const string ReasonServerNoNotes = ExchangeCodes.ServerNoNotes;
        /// <summary>This entry was enrolled without the role <c>notes</c> and must be signed in again (A1).</summary>
        public const string ReasonRoleMissing = ExchangeCodes.NotesRoleMissing;

        /// <summary>A safety bound on pages of one listing and on puts of one pass.</summary>
        private const int MaxSteps = 10000;

        private readonly ExchangeClient _client;
        private readonly Action _saveAccounts;
        private readonly Func<DateTime> _utcNow;

        public ExchangeNotesSync(ExchangeClient client, Action saveAccounts, Func<DateTime>? utcNow = null)
        {
            _client = client;
            _saveAccounts = saveAccounts;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>The checks the <c>welcome</c> alone answers (items AO, FI, FK): a guest, no <c>sealed</c>, no <c>notes</c>.</summary>
        public static string? PrecheckWelcome(ExchangeWelcome welcome)
        {
            if (welcome.IsGuest) return ExchangeCodes.NotPermitted;
            if (!welcome.Has(ExchangeWire.FeatureSealed)) return ExchangeCodes.ServerNoE2e;
            if (!welcome.Has(ExchangeWire.FeatureNotes)) return ReasonServerNoNotes;
            return null;
        }

        public async Task<ExchangeNotesSyncReport> SyncAsync(ExchangeAccountEntry entry, IExchangeNotesLocal local,
            ExchangeNotesSyncState state, CancellationToken ct)
        {
            var report = new ExchangeNotesSyncReport();
            if (entry.NeedsSignIn) { report.Code = ExchangeAccountEntry.SignInToken(entry.SignIn); return report; }
            if (entry.IsGuest) { report.Code = ExchangeCodes.NotPermitted; return report; }
            state.BindTo(entry.Id);

            using (ExchangeDeviceKey? key = ExchangeClipboardService.LoadDeviceKey(entry))
            {
                if (key == null) { report.Code = ExchangeCodes.KeyLost; return report; }

                ExchangeOutcome<ExchangeSession> opened = await _client.OpenAsync(entry, ct).ConfigureAwait(false);
                if (!opened.Ok)
                {
                    if (ExchangeClipboardService.LearnFrom(entry, opened.Code, null)) _saveAccounts();
                    report.Code = opened.Code;
                    return report;
                }
                using (ExchangeSession session = opened.Value!)
                {
                    if (ExchangeClipboardService.LearnFrom(entry, null, session)) _saveAccounts();
                    string? blocked = PrecheckWelcome(session.Welcome);
                    if (blocked != null) { report.Code = blocked; return report; }

                    ExchangeOutcome<List<ExchangePeer>> listed = await _client.ListAsync(session, ct).ConfigureAwait(false);
                    if (!listed.Ok) { report.Code = listed.Code; return report; }
                    List<ExchangePeer> peers = listed.Value!;
                    bool learned;
                    lock (entry) learned = ExchangeRecipients.Learn(entry.DeviceId, peers, entry.TrustedKeys);
                    if (learned) _saveAccounts();

                    ExchangePeer? self = peers.FirstOrDefault(p => p.DeviceId == entry.DeviceId);
                    if (self == null || !self.HasRole(ExchangeWire.RoleNotes)) { report.Code = ReasonRoleMissing; return report; }

                    var pass = new Pass(this, session, entry, key, local, state, report, Recipients(entry, peers),
                        self.Name.Length > 0 ? ExchangeRecipients.NameOf(self) : Environment.MachineName);

                    string code = await pass.PullAsync(ct).ConfigureAwait(false);
                    if (code.Length == 0) code = await pass.PushAsync(ct).ConfigureAwait(false);
                    // a notes_changed that arrived on this stream meanwhile: one more listing, never a loop (item FI)
                    if (code.Length == 0 && session.NotesChangedRevision > state.NotesRevision)
                        code = await pass.PullAsync(ct).ConfigureAwait(false);
                    report.Code = code;
                    return report;
                }
            }
        }

        /// <summary>
        /// The recipients of a note's box (item FI): this device first, then the account's other devices that hold the
        /// role <c>notes</c> and a trusted key, the most recently seen when there are more than 40 in all (item DH).
        /// </summary>
        internal static List<ExchangeE2eRecipient> Recipients(ExchangeAccountEntry entry, IEnumerable<ExchangePeer> peers)
        {
            var list = new List<ExchangeE2eRecipient>();
            byte[]? own = ExchangeClipboardService.DecodeKem(entry.DeviceKem);
            if (own != null) list.Add(new ExchangeE2eRecipient(entry.DeviceId, own));
            List<ExchangePeer> others = ExchangeRecipients.Select(entry.DeviceId,
                peers.Where(p => p.HasRole(ExchangeWire.RoleNotes)), entry.TrustedKeys, ExchangeNotes.MaxRecipients - list.Count);
            foreach (ExchangePeer p in others)
            {
                byte[]? kem = ExchangeClipboardService.DecodeKem(p.Kem);
                if (kem != null) list.Add(new ExchangeE2eRecipient(p.DeviceId, kem));
            }
            return list;
        }

        /// <summary>The devices a box addresses: every <c>keys[].to</c> and its <c>from</c>. Empty for a box that does not read.</summary>
        internal static List<string> RecipientsOf(string boxJson)
        {
            var ids = new List<string>();
            if (string.IsNullOrEmpty(boxJson) || boxJson.Length > ExchangeE2e.MaxBoxChars) return ids;
            try
            {
                if (!(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(boxJson) is Dictionary<string, object> root)) return ids;
                if (root.TryGetValue("from", out object? from) && from is string f && f.Length > 0) ids.Add(f);
                if (root.TryGetValue("keys", out object? keys) && keys is object[] arr)
                    foreach (object k in arr)
                        if (k is Dictionary<string, object> e && e.TryGetValue("to", out object? to) && to is string t && t.Length > 0 && !ids.Contains(t))
                            ids.Add(t);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { ids.Clear(); }
            return ids;
        }

        /// <summary>The name of a conflict's surviving local version (item FJ, assumption A5): its title or first line, then this device.</summary>
        internal static string ConflictTitle(QuickNote note, string deviceName)
        {
            string head = note.Preview(60);
            string device = deviceName.Trim();
            if (device.Length == 0) return head.Length > 0 ? head : device;
            return head.Length > 0 ? head + " (" + device + ")" : "(" + device + ")";
        }

        /// <summary>Codes of the stream itself: the pass stops at once, everything not yet sent stays marked.</summary>
        private static bool StopsThePass(string code)
            => code != ExchangeCodes.LimitReached && code != ExchangeCodes.InvalidPayload;

        /// <summary>The state of one running pass, so the steps share it without a dozen parameters.</summary>
        private sealed class Pass
        {
            private readonly ExchangeNotesSync _owner;
            private readonly ExchangeSession _session;
            private readonly ExchangeAccountEntry _entry;
            private readonly ExchangeDeviceKey _key;
            private readonly IExchangeNotesLocal _local;
            private readonly ExchangeNotesSyncState _state;
            private readonly ExchangeNotesSyncReport _report;
            private readonly List<ExchangeE2eRecipient> _recipients;
            private readonly List<string> _recipientIds;
            private readonly string _deviceName;
            /// <summary>New notes made during the pass (a conflict's surviving version): put before the pass ends.</summary>
            private readonly Queue<Guid> _created = new Queue<Guid>();

            public Pass(ExchangeNotesSync owner, ExchangeSession session, ExchangeAccountEntry entry, ExchangeDeviceKey key,
                IExchangeNotesLocal local, ExchangeNotesSyncState state, ExchangeNotesSyncReport report,
                List<ExchangeE2eRecipient> recipients, string deviceName)
            {
                _owner = owner;
                _session = session;
                _entry = entry;
                _key = key;
                _local = local;
                _state = state;
                _report = report;
                _recipients = recipients;
                _recipientIds = recipients.Select(r => r.DeviceId).ToList();
                _deviceName = deviceName;
            }

            // ============ pull ============

            public async Task<string> PullAsync(CancellationToken ct)
            {
                long since = _state.NotesRevision;
                long top = since;
                bool full = false;
                var latest = new Dictionary<string, ExchangeNoteRecord>(StringComparer.Ordinal);
                for (int step = 0; step < MaxSteps; step++)
                {
                    ExchangeOutcome<ExchangeNotesChanges> got = await _owner._client.NotesListAsync(_session, since, ct).ConfigureAwait(false);
                    if (!got.Ok) return got.Code;
                    ExchangeNotesChanges page = got.Value!;
                    if (page.Full && !full)
                    {
                        full = true;
                        if (since != 0) { since = 0; latest.Clear(); continue; } // a full listing is read from the start (A8)
                    }
                    foreach (ExchangeNoteRecord r in page.Records) latest[r.NoteId] = r; // ascending: the last one wins
                    if (page.NotesRevision > top) top = page.NotesRevision;
                    long last = page.Records.Count > 0 ? page.Records[page.Records.Count - 1].Revision : since;
                    if (!page.More || last <= since) break;
                    since = last;
                }

                var seen = new HashSet<Guid>();
                foreach (ExchangeNoteRecord record in latest.Values.OrderBy(r => r.Revision))
                {
                    if (!ExchangeNotes.TryParseNoteId(record.NoteId, out Guid id)) { _report.Unreadable++; continue; }
                    seen.Add(id);
                    Take(record, id);
                }
                if (full) ForgetWhatTheServerNoLongerHolds(seen);
                _state.NotesRevision = top;
                return "";
            }

            /// <summary>One listed record, applied unless the local copy changed - then the put's conflict decides (item FJ).</summary>
            private void Take(ExchangeNoteRecord record, Guid id)
            {
                bool known = _state.Notes.TryGetValue(id, out ExchangeNoteSyncState? st);
                if (known && record.Revision <= st!.Revision) return;
                if (known && (st!.Changed || st.DeletedLocally)) return;

                QuickNote? mine = _local.Find(id);
                if (!known && mine != null)
                {
                    // a note this device holds without a state (a lost state file): never overwritten unseen
                    if (!record.Deleted && TryRead(record, id, out QuickNote? stored, out _) && ExchangeNotePayload.SameContent(stored!, mine))
                    {
                        ExchangeNoteSyncState adopted = _state.Of(id);
                        adopted.Revision = record.Revision;
                        adopted.Recipients = RecipientsOf(record.BoxJson);
                        return;
                    }
                    _state.Of(id).Changed = true; // the put conflicts with this record and keeps both versions
                    return;
                }
                Apply(record, id);
            }

            /// <summary>The server's record wins here: the local copy did not change since the last pass.</summary>
            private void Apply(ExchangeNoteRecord record, Guid id)
            {
                if (record.Deleted)
                {
                    if (_local.Find(id) != null)
                    {
                        // edited this very moment: the put meets the tombstone and the text survives (item FJ)
                        if (!_local.DeleteRemote(id, false)) { _state.Of(id).Changed = true; return; }
                        _report.Deleted++;
                    }
                    _state.Notes.Remove(id); // a noteId is never reused: nothing of it is needed any more
                    return;
                }
                ExchangeNoteSyncState st = _state.Of(id);
                if (!TryRead(record, id, out QuickNote? note, out string failure))
                {
                    if (failure == ExchangeCodes.FormatUnsupported)
                    {
                        st.Unsupported = true; // the local note stays as it is (item FH)
                        st.Revision = record.Revision;
                        st.Changed = false;
                        _report.Unsupported++;
                    }
                    else _report.Unreadable++; // its revision stays: the next readable record of the note is taken
                    return;
                }
                if (!_local.ApplyRemote(note!)) { st.Changed = true; return; } // edited meanwhile: the put keeps both
                st.Revision = record.Revision;
                st.Changed = false;
                st.Unsupported = false;
                st.TooLarge = false;
                st.Recipients = RecipientsOf(record.BoxJson);
                _report.Received++;
            }

            /// <summary>
            /// A <c>full</c> listing (item FI): a note this device synced before and found in no record was deleted on the
            /// server. One changed here survives as a new note, as against a tombstone (item FJ).
            /// </summary>
            private void ForgetWhatTheServerNoLongerHolds(HashSet<Guid> seen)
            {
                foreach (KeyValuePair<Guid, ExchangeNoteSyncState> pair in _state.Notes.ToList())
                {
                    if (seen.Contains(pair.Key) || pair.Value.Revision == 0) continue;
                    QuickNote? mine = _local.Find(pair.Key);
                    _state.Notes.Remove(pair.Key);
                    if (mine == null || pair.Value.DeletedLocally) continue;
                    if (pair.Value.Changed) SurviveAsNew(mine, mine.Title, pair.Key);
                    else if (_local.DeleteRemote(pair.Key, false)) _report.Deleted++;
                    else SurviveAsNew(mine, mine.Title, pair.Key); // edited this very moment
                }
            }

            // ============ push ============

            public async Task<string> PushAsync(CancellationToken ct)
            {
                foreach (QuickNote note in _local.Snapshot())
                {
                    string code = await PutContentAsync(note, ct).ConfigureAwait(false);
                    if (code.Length > 0) return code;
                }
                foreach (KeyValuePair<Guid, ExchangeNoteSyncState> pair in _state.Notes.Where(p => p.Value.DeletedLocally).ToList())
                {
                    string code = await PutDeleteAsync(pair.Key, pair.Value, ct).ConfigureAwait(false);
                    if (code.Length > 0) return code;
                }
                for (int step = 0; step < MaxSteps && _created.Count > 0; step++)
                {
                    QuickNote? note = _local.Find(_created.Dequeue());
                    if (note == null) continue;
                    string code = await PutContentAsync(note, ct).ConfigureAwait(false);
                    if (code.Length > 0) return code;
                }
                return "";
            }

            private async Task<string> PutContentAsync(QuickNote note, CancellationToken ct)
            {
                _state.Notes.TryGetValue(note.Id, out ExchangeNoteSyncState? st);
                if (st != null && (st.Unsupported || st.DeletedLocally)) return "";
                bool create = st == null || st.Revision == 0;
                bool changed = create || st!.Changed;
                bool reseal = !changed && !_recipientIds.All(st!.Recipients.Contains);
                if (!changed && !reseal) return "";
                if (create && (note.IsEmpty || _report.LimitReached)) return "";

                byte[] payload = ExchangeNotePayload.Encode(note);
                if (ExchangeNotePayload.ExceedsLimit(payload))
                {
                    _state.Of(note.Id).TooLarge = true; // stays here; never cut (item FH, A4)
                    _report.TooLarge++;
                    return "";
                }
                string box;
                try
                {
                    box = ExchangeE2e.SealBox(ExchangeNotes.BoxContext(note.Id), payload, _entry.DeviceId, _key, _recipients, out byte[] contentKey);
                    ExchangeE2e.Wipe(contentKey);
                }
                finally { Array.Clear(payload, 0, payload.Length); }

                long baseRevision = create ? 0 : st!.Revision;
                ExchangeOutcome<ExchangeNotePutResult> put = await _owner._client.NotesPutAsync(_session,
                    ExchangeNotes.NoteIdOf(note.Id), baseRevision, false, box, ct).ConfigureAwait(false);
                if (!put.Ok)
                {
                    if (put.Code == ExchangeCodes.LimitReached && create) { _report.LimitReached = true; return ""; }
                    if (put.Code == ExchangeNotes.ReasonNoteConflict && !create)
                    {
                        // a conflict with no record to carry: the note's record was purged, i.e. it was deleted (item FG)
                        Resolve(new ExchangeNoteRecord { NoteId = ExchangeNotes.NoteIdOf(note.Id), Deleted = true }, note, changed);
                        return "";
                    }
                    return StopsThePass(put.Code) ? put.Code : "";
                }

                ExchangeNotePutResult result = put.Value!;
                if (result.Stored)
                {
                    ExchangeNoteSyncState done = _state.Of(note.Id);
                    done.Revision = result.Revision;
                    done.TooLarge = false;
                    done.Recipients = new List<string>(_recipientIds);
                    QuickNote? now = _local.Find(note.Id);
                    done.Changed = now != null && !ExchangeNotePayload.SameContent(now, note); // edited while it travelled
                    if (changed) _report.Sent++;
                    return "";
                }
                Resolve(result.Conflict!, note, changed);
                return "";
            }

            /// <summary>Item FJ for a put of content that met another record.</summary>
            private void Resolve(ExchangeNoteRecord current, QuickNote mine, bool changed)
            {
                Guid id = mine.Id;
                if (!changed)
                {
                    // a reseal (or an unchanged note) met a newer record: take it, send nothing
                    _state.Of(id).Recipients.Clear();
                    Apply(current, id);
                    return;
                }
                if (current.Deleted)
                {
                    _report.Conflicts++;
                    _state.Notes.Remove(id);
                    SurviveAsNew(mine, mine.Title, id); // the local text lives on as a new note
                    return;
                }
                if (!TryRead(current, id, out QuickNote? stored, out string failure))
                {
                    _report.Conflicts++;
                    ExchangeNoteSyncState st = _state.Of(id);
                    if (failure == ExchangeCodes.FormatUnsupported)
                    {
                        st.Unsupported = true; // the local note stays, unsent (item FH)
                        st.Revision = current.Revision;
                        st.Changed = false;
                        _report.Unsupported++;
                        return;
                    }
                    // a version this device cannot read keeps the noteId; the local text lives on as a new note
                    st.Revision = current.Revision;
                    st.Changed = false;
                    _report.Unreadable++;
                    SurviveAsNew(mine, ConflictTitle(mine, _deviceName), id);
                    return;
                }
                ExchangeNoteSyncState kept = _state.Of(id);
                if (ExchangeNotePayload.SameContent(stored!, mine))
                {
                    // the server already holds this very content (an earlier put whose answer was lost)
                    kept.Revision = current.Revision;
                    kept.Changed = false;
                    kept.Recipients = RecipientsOf(current.BoxJson);
                    return;
                }
                // the stored version keeps the noteId, the local one survives under a new id and the device's name
                QuickNote local = (_local.Find(id) ?? mine).Clone();
                if (!_local.ApplyRemote(stored!)) { kept.Changed = true; return; } // edited meanwhile: the next pass meets it again
                _report.Conflicts++;
                kept.Revision = current.Revision;
                kept.Changed = false;
                kept.Recipients = RecipientsOf(current.BoxJson);
                _report.Received++;
                AddCopy(local, ConflictTitle(local, _deviceName));
            }

            private async Task<string> PutDeleteAsync(Guid id, ExchangeNoteSyncState st, CancellationToken ct)
            {
                ExchangeOutcome<ExchangeNotePutResult> put = await _owner._client.NotesPutAsync(_session,
                    ExchangeNotes.NoteIdOf(id), st.Revision, true, null, ct).ConfigureAwait(false);
                if (!put.Ok)
                {
                    if (put.Code == ExchangeNotes.ReasonNoteConflict) { _state.Notes.Remove(id); return ""; } // purged: gone everywhere
                    return StopsThePass(put.Code) ? put.Code : "";
                }

                ExchangeNotePutResult result = put.Value!;
                if (result.Stored || result.Conflict!.Deleted)
                {
                    _state.Notes.Remove(id); // deleted everywhere
                    if (result.Stored) _report.Sent++;
                    return "";
                }
                // the local act was a delete and the stored record is a live edit: the edit wins, the note stays (item FJ)
                _report.Conflicts++;
                st.DeletedLocally = false;
                st.Changed = false;
                Apply(result.Conflict!, id);
                return "";
            }

            /// <summary>Re-keys a local note: the text survives under a new noteId and the old one leaves this device.</summary>
            private void SurviveAsNew(QuickNote mine, string? title, Guid oldId)
            {
                QuickNote copy = (_local.Find(oldId) ?? mine).Clone(); // the latest text, typed during the pass included
                _local.DeleteRemote(oldId, true);
                AddCopy(copy, title);
            }

            private void AddCopy(QuickNote source, string? title)
            {
                QuickNote copy = source.Clone();
                copy.Id = Guid.NewGuid();
                copy.Title = QuickNote.NormalizeTitle(title);
                DateTime now = _owner._utcNow();
                copy.CreatedAtUtc = now;
                if (copy.UpdatedAtUtc == default) copy.UpdatedAtUtc = now;
                _local.AddLocal(copy);
                _created.Enqueue(copy.Id);
            }

            /// <summary>Opens a record's box under its note's context and reads the payload (items FG, FH).</summary>
            private bool TryRead(ExchangeNoteRecord record, Guid id, out QuickNote? note, out string failure)
            {
                note = null;
                failure = ExchangeCodes.SealedFailed;
                if (record.BoxJson.Length == 0) return false;
                bool opened = ExchangeE2e.TryOpenBox(ExchangeNotes.BoxContext(id), record.BoxJson, _entry.DeviceId, _key,
                    from => from == _entry.DeviceId
                        ? ExchangeClipboardService.DecodeKem(_entry.DeviceKem)
                        : ExchangeRecipients.TrustedKemOf(_entry.TrustedKeys, from, ExchangeClipboardService.DecodeKem),
                    out byte[]? payload, out byte[]? contentKey, out _);
                ExchangeE2e.Wipe(contentKey);
                if (!opened || payload == null) return false;
                try { return ExchangeNotePayload.TryDecode(payload, id, out note, out failure); }
                finally { Array.Clear(payload, 0, payload.Length); }
            }
        }
    }
}
