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
        public Task LoadAsync()
        {
            lock (_gate)
            {
                if (IsLoaded || _replay != null) return _loadDone.Task;
                _replay = Task.Run(() => _store.Load());
            }
            _replay.ContinueWith(_ => OnOwnerThread(FinishLoad), TaskScheduler.Default);
            return _loadDone.Task;
        }

        /// <summary>
        /// Wait for the replay here and now. For the two commands that cannot act on a half-loaded
        /// set - "delete everything" and "export everything". The replay itself never posts back, so
        /// waiting on the owner thread cannot deadlock; the posted apply then finds nothing to do.
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
                if (IsLoaded || _disposed || _replay == null || !_replay.IsCompleted) return;
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

            bool written = created ? _store.Create(note) : _store.Update(note);
            if (!written) SaveFailed?.Invoke(this, EventArgs.Empty);
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

        /// <summary>Write everything the debounce is still holding. Called on close and on exit.</summary>
        public void Flush()
        {
            List<QuickNote> due;
            lock (_gate)
            {
                if (_dirty.Count == 0) return;
                due = new List<QuickNote>(_dirty.Count);
                foreach (Guid id in _dirty)
                    if (_pending.TryGetValue(id, out QuickNote? note)) due.Add(note);
                _dirty.Clear();
                _pending.Clear();
            }
            foreach (QuickNote note in due) Commit(note, onlyIfDirty: false);
        }

        public void Delete(QuickNote note)
        {
            bool persisted;
            lock (_gate)
            {
                _dirty.Remove(note.Id);
                _pending.Remove(note.Id);
                _notes.Remove(note);
                _deletedIds.Add(note.Id);
                persisted = _persisted.Remove(note.Id);
            }
            // A draft was never written, so there is nothing to record its removal of.
            if (persisted)
            {
                if (!_store.Delete(note.Id)) SaveFailed?.Invoke(this, EventArgs.Empty);
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
            }
            _store.DeleteEverything();
            Cleared?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
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
