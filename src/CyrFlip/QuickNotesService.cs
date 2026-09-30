using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// The live set of quick notes: the in-memory list in its standard order, the CRUD the window
    /// drives, and the debounced save that keeps <c>Ctrl+S</c> from being something the user has to
    /// remember (spec §3.4). The window never touches a file and the store never hears about
    /// WinForms; everything in between that is a rule rather than plumbing lives in
    /// <see cref="QuickNotesOrder"/> and <see cref="QuickNote"/>, where it is testable.
    ///
    /// <para>A brand-new note is <b>not</b> in the list. It becomes real at its first
    /// <see cref="Commit"/> with something in it - which is also when <c>CreatedAtUtc</c> is
    /// assigned, so a note created from a selection is dated when the user confirmed it and not when
    /// CyrFlip copied the text (spec §3.5).</para>
    ///
    /// <para><b>One thread owns the list</b> (ticket S0006, QN-8): the thread the service was built
    /// on - the UI thread in the app. The debounce timer fires on the pool, so it only posts the
    /// flush back; a commit on the pool used to insert into the list while the UI thread was
    /// enumerating it for a search. Two things do run elsewhere, and both only read under the lock:
    /// the replay (<see cref="LoadAsync"/>), whose result is applied on the owner thread, and a
    /// compaction, whose snapshot is copied under the lock.</para>
    /// </summary>
    internal sealed class QuickNotesService : IDisposable
    {
        /// <summary>Quiet time after the last edit before the note is written (spec §3.4).</summary>
        public const int DebounceMs = 750;

        /// <summary>How long after a failed write it is tried again, typing or not (S0035 QN2-1).</summary>
        public const int RetryAfterFailureMs = 5000;

        private readonly QuickNotesStore _store;
        private readonly List<QuickNote> _notes = new List<QuickNote>();
        // Notes the journal already knows about. A note absent from here is still a draft, so its
        // first write has to be a "create" carrying its dates - not an "update" of nothing.
        private readonly HashSet<Guid> _persisted = new HashSet<Guid>();
        private readonly HashSet<Guid> _dirty = new HashSet<Guid>();
        // The dirty notes themselves. A draft is not in _notes, so without this there would be
        // nothing to look one up by when the debounce timer fires.
        private readonly Dictionary<Guid, QuickNote> _pending = new Dictionary<Guid, QuickNote>();
        // Ids deleted this session, by Delete or DeleteAll. A commit of one is refused: the window
        // (or anything else) still holding the object must not write a deleted note back as a new
        // one - which is exactly what "delete all" followed by closing the window used to do (QN-1).
        private readonly HashSet<Guid> _deletedIds = new HashSet<Guid>();
        // Writes that failed and wait for their retry (S0035 QN2-1): notes whose record did not reach
        // the journal, and deletes that did not. Until both are empty the failure is shown, however
        // often the window repaints its dates.
        private readonly HashSet<Guid> _failed = new HashSet<Guid>();
        private readonly HashSet<Guid> _failedDeletes = new HashSet<Guid>();
        private readonly object _gate = new object();
        private readonly Timer _debounce;
        private readonly SynchronizationContext? _owner;
        private readonly TaskCompletionSource<bool> _loadDone = new TaskCompletionSource<bool>();
        private Task<List<QuickNote>>? _replay;
        private Task? _compaction;
        private int _refusedCommits;
        private bool _disposed;

        /// <summary>Raised after the set of notes changes - a note added, deleted or reordered.</summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Raised by <see cref="DeleteAll"/>. Distinct from <see cref="Changed"/> because it asks for
        /// more than a repaint: whoever holds a note open has to let go of it (QN-1).
        /// </summary>
        public event EventHandler? Cleared;

        /// <summary>A note was written for the first time - the one place a creation is counted (QN-8).</summary>
        public event EventHandler? Created;

        /// <summary>The replay has been applied; <see cref="IsLoaded"/> is true from here on.</summary>
        public event EventHandler? Loaded;

        /// <summary>A write to the journal failed. The note is still in memory, but not on disk.</summary>
        public event EventHandler? SaveFailed;

        /// <summary>Every write that had failed has now reached the journal.</summary>
        public event EventHandler? SaveRecovered;

        /// <summary>
        /// True while a write that failed has not yet been retried successfully (S0035 QN2-1). A
        /// failure used to be shown for an instant - the next repaint of the note's dates overwrote
        /// it - and never retried, so the edit was gone after a restart.
        /// </summary>
        public bool SaveFailing
        {
            get { lock (_gate) return _failed.Count > 0 || _failedDeletes.Count > 0; }
        }

        /// <summary>The app's service: nothing is read until <see cref="LoadAsync"/>.</summary>
        public QuickNotesService() : this(new QuickNotesStore(), loadNow: false) { }

        /// <summary>
        /// <paramref name="loadNow"/> replays the journal on the calling thread before returning -
        /// the tests' shape. The app passes false and calls <see cref="LoadAsync"/>, because the
        /// replay decrypts every line and the thread that builds the service also runs the keyboard
        /// hook (QN-2).
        /// </summary>
        internal QuickNotesService(QuickNotesStore store, bool loadNow = true)
        {
            _store = store;
            _owner = SynchronizationContext.Current;
            // Created stopped, armed only while something is waiting to be written.
            _debounce = new Timer(_ => OnOwnerThread(Flush), null, Timeout.Infinite, Timeout.Infinite);
            if (loadNow)
            {
                _replay = Task.FromResult(_store.Load());
                FinishLoad();
            }
        }

        /// <summary>Notes in the standard order: newest first by creation, ties by id.</summary>
        public IReadOnlyList<QuickNote> Notes => _notes;

        /// <summary>False until the journal has been replayed. The window shows "loading" until then.</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>Journal lines that could not be read at startup. Surfaced once, by the caller.</summary>
        public int SkippedRecords { get; private set; }

        /// <summary>The journal was missing and the notes came back from its <c>.bak</c>. Surfaced once.</summary>
        public bool RecoveredFromBackup { get; private set; }

        public string JournalPath => _store.Path;

        /// <summary>The compaction running in the background, if any. For tests.</summary>
        internal Task? CompactionTask
        {
            get { lock (_gate) return _compaction; }
        }

        /// <summary>
        /// Replay the journal on the pool and apply it on the owner thread. The returned task
        /// completes once <see cref="IsLoaded"/> is true; calling it again returns the same task.
        /// </summary>
        /// <param name="after">
        /// Start the replay only once this has finished - the previous service's background retirement
        /// (<see cref="RetireAsync"/>), which may still be compacting the same journal.
        /// </param>
        public Task LoadAsync(Task? after = null)
        {
            lock (_gate)
            {
                if (IsLoaded || _replay != null) return _loadDone.Task;
                _replay = after == null || after.IsCompleted
                    ? Task.Run(() => _store.Load())
                    : after.ContinueWith(_ => _store.Load(), CancellationToken.None,
                        TaskContinuationOptions.None, TaskScheduler.Default);
            }
            _replay.ContinueWith(_ => OnOwnerThread(FinishLoad), TaskScheduler.Default);
            return _loadDone.Task;
        }

        /// <summary>
        /// Wait for the replay here and now. For tests and for a caller with no message loop: the app
        /// waits for <see cref="LoadAsync"/> behind a <see cref="BusyDialog"/> instead, which keeps
        /// the hooks' thread pumping (ticket S0030 HT-6) - so every method below that calls this finds
        /// the set already loaded there. The replay itself never posts back, so waiting on the owner
        /// thread cannot deadlock; the posted apply then finds nothing to do.
        /// </summary>
        public void EnsureLoaded()
        {
            if (IsLoaded) return;
            LoadAsync();
            Task<List<QuickNote>>? replay;
            lock (_gate) replay = _replay;
            try { replay?.Wait(); } catch { /* FinishLoad treats a faulted replay as an empty set */ }
            FinishLoad();
        }

        private void FinishLoad()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    // Nothing to apply to - but whoever waits for the load must not wait forever.
                    _loadDone.TrySetResult(false);
                    return;
                }
                if (IsLoaded || _replay == null || !_replay.IsCompleted) return;
                List<QuickNote> loaded;
                if (_replay.Status == TaskStatus.RanToCompletion) loaded = _replay.Result;
                else
                {
                    loaded = new List<QuickNote>();
                    QuickNotesLog.Log("replay failed: " + _replay.Exception?.GetBaseException().GetType().Name);
                }
                foreach (QuickNote note in loaded)
                    if (_persisted.Add(note.Id)) QuickNotesOrder.Insert(_notes, note);
                SkippedRecords = _store.SkippedRecords;
                RecoveredFromBackup = _store.RecoveredFromBackup;
                IsLoaded = true;
            }
            Loaded?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
            _loadDone.TrySetResult(true);
            // A journal that was already long when it was read is compacted now, not at an exit
            // that a user who only ever signs out never reaches.
            ScheduleCompactionIfDue();
        }

        private void OnOwnerThread(Action action)
        {
            if (_owner == null) action();
            else _owner.Post(_ => action(), null);
        }

        /// <summary>A draft. It is nowhere yet - not in the list, not in the journal.</summary>
        public QuickNote CreateDraft(QuickNoteKind kind)
        {
            var note = new QuickNote { Kind = kind };
            if (kind == QuickNoteKind.Checklist) note.Items.Add(new QuickNoteItem());
            return note;
        }

        /// <summary>True while the note is still a draft - never yet written to the journal.</summary>
        public bool IsDraft(QuickNote note)
        {
            lock (_gate) return !_persisted.Contains(note.Id);
        }

        /// <summary>
        /// Write the note now - if there is anything to write. An empty draft is <b>not</b> created
        /// (spec §3.3), opening the editor and changing your mind must not leave a row behind; a note
        /// that already exists is written only when it has been <see cref="Touch"/>ed since its last
        /// save (QN-3), so clicking through notes neither appends copies of them nor moves their
        /// "modified" date. Emptying a note that exists is an edit like any other and is saved.
        /// A note deleted this session is refused. Returns true when the note was written.
        /// </summary>
        public bool Commit(QuickNote note) => Commit(note, onlyIfDirty: true);

        private bool Commit(QuickNote note, bool onlyIfDirty)
        {
            bool created;
            lock (_gate)
            {
                bool dirty = _dirty.Remove(note.Id);
                _pending.Remove(note.Id);
                if (_deletedIds.Contains(note.Id))
                {
                    _refusedCommits++;
                    QuickNotesLog.Log("commit of a deleted note refused (" + _refusedCommits + " this session)");
                    return false;
                }
                created = !_persisted.Contains(note.Id);
                if (created && note.IsEmpty) return false;
                if (!created && onlyIfDirty && !dirty) return false;

                note.Title = QuickNote.NormalizeTitle(note.Title);
                note.RenumberItems();
                DateTime now = DateTime.UtcNow;
                if (created)
                {
                    // Assigned once and never again - the standard order is built on it.
                    note.CreatedAtUtc = now;
                    note.UpdatedAtUtc = now;
                    _persisted.Add(note.Id);
                    QuickNotesOrder.Insert(_notes, note);
                }
                else
                {
                    note.UpdatedAtUtc = now;
                }
            }

            // A create that failed is retried as an update: its record carries the creation date, and
            // replay takes an update of an id it has not seen as the note itself.
            bool written = created ? _store.Create(note) : _store.Update(note);
            if (written) NoteWritten(note.Id);
            else RetryLater(note);
            if (created)
            {
                Created?.Invoke(this, EventArgs.Empty);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            ScheduleCompactionIfDue();
            return true;
        }

        /// <summary>
        /// The note has been edited. Nothing is written yet: the save follows
        /// <see cref="DebounceMs"/> after the last change, so typing costs no disk at all, and there
        /// is never a modal "save changes?" standing between the user and their code (spec §3.4).
        /// </summary>
        public void Touch(QuickNote note)
        {
            lock (_gate)
            {
                if (_disposed || _deletedIds.Contains(note.Id)) return;
                _dirty.Add(note.Id);
                _pending[note.Id] = note;
                _debounce.Change(DebounceMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Write everything the debounce is still holding, and retry every write that failed. Called
        /// by the debounce, on close and on exit.
        /// </summary>
        public void Flush()
        {
            List<QuickNote> due;
            List<Guid> deletes;
            lock (_gate)
            {
                if (_dirty.Count == 0 && _failedDeletes.Count == 0) return;
                due = new List<QuickNote>(_dirty.Count);
                foreach (Guid id in _dirty)
                    if (_pending.TryGetValue(id, out QuickNote? note)) due.Add(note);
                _dirty.Clear();
                _pending.Clear();
                deletes = new List<Guid>(_failedDeletes);
            }
            foreach (QuickNote note in due) Commit(note, onlyIfDirty: false);
            foreach (Guid id in deletes)
            {
                if (_store.Delete(id)) DeleteWritten(id);
                else RetryDeleteLater(id);
            }
        }

        private void RetryLater(QuickNote note) => RetryLater(new[] { note });

        private void RetryDeleteLater(Guid id) => RetryLater(new QuickNote[0], id);

        /// <summary>
        /// A write failed: the notes go back among the unsaved ones and the debounce is armed to try
        /// again (QN2-1), so a full disk or a locked journal costs a delay, not the edit. After the
        /// service is disposed there is no later - the failure is only reported.
        /// </summary>
        private void RetryLater(IEnumerable<QuickNote> notes, Guid? deletedId = null)
        {
            lock (_gate)
            {
                foreach (QuickNote note in notes)
                {
                    if (_deletedIds.Contains(note.Id)) continue;
                    _failed.Add(note.Id);
                    if (_disposed) continue;
                    _dirty.Add(note.Id);
                    _pending[note.Id] = note;
                }
                if (deletedId != null) _failedDeletes.Add(deletedId.Value);
                if (!_disposed) _debounce.Change(RetryAfterFailureMs, Timeout.Infinite);
            }
            SaveFailed?.Invoke(this, EventArgs.Empty);
        }

        private void NoteWritten(Guid id)
        {
            bool recovered;
            lock (_gate)
                recovered = _failed.Remove(id) && _failed.Count == 0 && _failedDeletes.Count == 0;
            if (recovered) SaveRecovered?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteWritten(Guid id)
        {
            bool recovered;
            lock (_gate)
                recovered = _failedDeletes.Remove(id) && _failed.Count == 0 && _failedDeletes.Count == 0;
            if (recovered) SaveRecovered?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(QuickNote note)
        {
            bool persisted, recovered;
            lock (_gate)
            {
                _dirty.Remove(note.Id);
                _pending.Remove(note.Id);
                _notes.Remove(note);
                _deletedIds.Add(note.Id);
                // Its own pending write, if one failed, is moot now - the delete is what counts.
                recovered = _failed.Remove(note.Id) && _failed.Count == 0 && _failedDeletes.Count == 0;
                persisted = _persisted.Remove(note.Id);
            }
            if (recovered && !persisted) SaveRecovered?.Invoke(this, EventArgs.Empty);
            // A draft was never written, so there is nothing to record its removal of.
            if (persisted)
            {
                // A delete that did not reach the journal would bring the note back at the next start.
                if (!_store.Delete(note.Id)) RetryDeleteLater(note.Id);
                else if (recovered) SaveRecovered?.Invoke(this, EventArgs.Empty);
                ScheduleCompactionIfDue();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Destroy every note and the files behind them (spec §5.3). There is no undo. The replay is
        /// finished first - a set still loading would come back the moment it arrived.
        /// </summary>
        public void DeleteAll()
        {
            EnsureLoaded();
            lock (_gate)
            {
                foreach (QuickNote note in _notes) _deletedIds.Add(note.Id);
                foreach (Guid id in _pending.Keys) _deletedIds.Add(id);
                _notes.Clear();
                _persisted.Clear();
                _dirty.Clear();
                _pending.Clear();
                // Nothing is left to write: the journal itself is about to go.
                _failed.Clear();
                _failedDeletes.Clear();
            }
            _store.DeleteEverything();
            Cleared?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raised after <see cref="Import"/> changed notes in place. A window holding one of them open
        /// has to reload it from the object - its editor still shows the text from before.
        /// </summary>
        public event EventHandler? Imported;

        /// <summary>
        /// What <see cref="Import"/> would do with these blocks, without doing it (S0023, spec 7.1). The
        /// replay is finished and the debounce flushed first, so the answer is about the notes as they
        /// really are, not about half of them.
        /// </summary>
        public ExchangeMergeReport PlanImport(IEnumerable<ExchangeNote> incoming, bool includeNew)
        {
            EnsureLoaded();
            Flush();
            var report = new ExchangeMergeReport();
            List<QuickNote> snapshot;
            lock (_gate) snapshot = new List<QuickNote>(_notes);
            CyrFlipExchangeMerge.PlanNotes(snapshot, incoming, includeNew, DateTime.UtcNow, report);
            return report;
        }

        /// <summary>
        /// Merge the notes of an exchange file (S0023, spec 7.2): add what is not here, replace a note
        /// by a strictly newer version of itself, delete nothing. The plan is made again here against
        /// the live list - the preview was confirmed in a modal dialog, and the list may have moved.
        /// An imported note keeps the dates it carries: it was created when it was created, wherever.
        ///
        /// <para>The list changes on this thread; the journal records go out as <b>one</b> batched
        /// append (<see cref="QuickNotesStore.AppendBatch"/>) through <paramref name="offThread"/>,
        /// which the app points at a worker behind a <see cref="BusyDialog"/> - a few thousand
        /// encrypted records are seconds of work, and this is the hooks' thread (S0030 HT-2). The
        /// modal dialog is also what keeps the notes unchanged while the worker reads them. Null runs
        /// the write here (the tests' shape).</para>
        /// </summary>
        public ExchangeMergeReport Import(IEnumerable<ExchangeNote> incoming, bool includeNew,
            Func<Func<bool>, bool>? offThread = null)
        {
            EnsureLoaded();
            Flush();
            var report = new ExchangeMergeReport();
            List<CyrFlipExchangeMerge.NoteAction> actions;
            lock (_gate)
            {
                if (_disposed) return report;
                actions = CyrFlipExchangeMerge.PlanNotes(new List<QuickNote>(_notes), incoming, includeNew, DateTime.UtcNow, report);
            }

            var records = new List<KeyValuePair<bool, QuickNote>>(actions.Count);
            foreach (CyrFlipExchangeMerge.NoteAction action in actions)
            {
                QuickNote target;
                bool created = action.Existing == null;
                lock (_gate)
                {
                    if (created)
                    {
                        target = action.Note;
                        // A note deleted this session and brought back by the file is a new decision
                        // of the user's, not a stale window writing it back (QN-1).
                        _deletedIds.Remove(target.Id);
                        _persisted.Add(target.Id);
                        QuickNotesOrder.Insert(_notes, target);
                    }
                    else
                    {
                        // Changed in place: the object in the list is the one any window holds.
                        target = action.Existing!;
                        target.Title = action.Note.Title;
                        target.Kind = action.Note.Kind;
                        target.RawText = action.Note.RawText;
                        target.Items = action.Note.Items;
                        target.UpdatedAtUtc = action.Note.UpdatedAtUtc;
                    }
                }
                records.Add(new KeyValuePair<bool, QuickNote>(created, target));
            }
            if (records.Count > 0)
            {
                Func<bool> write = () => _store.AppendBatch(records);
                bool written;
                try { written = offThread != null ? offThread(write) : write(); }
                catch (Exception ex)
                {
                    QuickNotesLog.Log("import write failed: " + ex.GetType().Name);
                    written = false;
                }
                // Every note the batch carried stays unsaved and is retried one by one (QN2-1).
                if (!written)
                    RetryLater(records.ConvertAll(record => record.Value));
            }
            if (actions.Count > 0)
            {
                QuickNotesLog.Log("imported " + report.NotesAdded + " new, " + report.NotesUpdated + " updated, "
                    + report.NotesNew + " without id");
                Imported?.Invoke(this, EventArgs.Empty);
                Changed?.Invoke(this, EventArgs.Empty);
                ScheduleCompactionIfDue();
            }
            return report;
        }

        /// <summary>The matching notes in the standard order; an empty query means all of them.</summary>
        public List<QuickNote> Search(string? query) => QuickNotesOrder.Filter(_notes, query);

        public QuickNote? Find(Guid id)
        {
            foreach (QuickNote note in _notes)
                if (note.Id == id) return note;
            return null;
        }

        /// <summary>
        /// Start a compaction on the pool once the journal has grown past its threshold (QN-2). It
        /// used to run only from <see cref="Dispose"/>, i.e. on a clean exit, which a user who signs
        /// out instead of choosing "Exit" never makes - and whose journal therefore never shrank.
        /// </summary>
        private void ScheduleCompactionIfDue()
        {
            lock (_gate)
            {
                if (_disposed || !IsLoaded || !_store.NeedsCompaction) return;
                if (_compaction != null && !_compaction.IsCompleted) return;
                _compaction = Task.Run(CompactNow);
            }
        }

        /// <summary>Snapshot the journal when it has grown enough to be worth rewriting (spec §5.2).</summary>
        public void CompactIfNeeded()
        {
            if (!_store.NeedsCompaction) return;
            CompactNow();
        }

        private void CompactNow()
        {
            // Never before the replay is applied: the list is empty until then, and a snapshot of
            // it would replace every note in the journal with nothing.
            if (!IsLoaded) return;
            int count = 0;
            _store.Compact(() =>
            {
                lock (_gate)
                {
                    count = _notes.Count;
                    return new List<QuickNote>(_notes);
                }
            });
            QuickNotesLog.Log("journal compacted to " + count + " note(s)");
        }

        /// <summary>
        /// <see cref="Dispose"/> for the feature being switched off from the settings (S0030 HT-6):
        /// the last edit is written here - a debounced note or two - while the wait for a running
        /// compaction and the final one go to the pool. Exit and the session end keep the synchronous
        /// <see cref="Dispose"/>, since the process ends right after. The returned task is what a new
        /// service for the same journal has to wait for before it replays it (<see cref="LoadAsync"/>).
        /// </summary>
        public Task RetireAsync()
        {
            Task? compaction;
            lock (_gate)
            {
                if (_disposed) return Task.CompletedTask;
                _disposed = true;
                compaction = _compaction;
            }
            _debounce.Dispose();
            Flush();
            return Task.Run(() =>
            {
                try { compaction?.Wait(2000); } catch { }
                CompactIfNeeded();
            });
        }

        public void Dispose()
        {
            Task? compaction;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                compaction = _compaction;
            }
            // Order matters: stop the timer, then write what it was holding, then snapshot - a
            // compaction that ran before the flush would leave the last edit outside the snapshot
            // and only in the journal line appended after it.
            _debounce.Dispose();
            Flush();
            try { compaction?.Wait(2000); } catch { }
            CompactIfNeeded();
        }
    }
}
