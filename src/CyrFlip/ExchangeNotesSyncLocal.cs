using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// The owner of the quick notes' <see cref="ExchangeNotesSyncState"/> on this PC (ticket S0105, phase 3): the local
    /// marks (<c>MarkChanged</c> / <c>MarkDeleted</c>) arrive on the UI thread at any time, a sync pass mutates the state
    /// on the pool, and the file <c>quick-notes-sync.json</c> follows both. A mark that arrives while a pass runs waits in
    /// a queue and is applied when the pass ends, so the state is only ever touched by one thread at a time; the adapter
    /// asks <see cref="IsMarkPending"/> so the pass never writes over a note the user changed meanwhile.
    ///
    /// <para>The marks are recorded whether or not the sync is switched on: a note synced once and edited while the
    /// option was off must still be put - and must not be overwritten by the account's older copy - when it comes back on.</para>
    /// </summary>
    internal sealed class ExchangeNotesSyncLedger
    {
        private readonly object _gate = new object();
        private readonly object _fileGate = new object();
        private readonly string _path;
        private readonly ExchangeNotesSyncState _state;
        private readonly List<KeyValuePair<Guid, bool>> _pending = new List<KeyValuePair<Guid, bool>>();
        private readonly Action<Action> _background;
        private bool _passRunning;
        private int _saveQueued;

        /// <param name="background">Where a save after a mark runs; null is the pool (the UI thread is the hooks' thread).</param>
        public ExchangeNotesSyncLedger(string path, ExchangeNotesSyncState state, Action<Action>? background = null)
        {
            _path = path;
            _state = state;
            _background = background ?? (work => Task.Run(work));
        }

        /// <summary>Reads the file - I/O, so off the UI thread.</summary>
        public static ExchangeNotesSyncLedger Load(string path, Action<Action>? background = null)
            => new ExchangeNotesSyncLedger(path, ExchangeNotesSyncStore.Load(path), background);

        /// <summary>For tests: the state itself. Read it only while no pass runs.</summary>
        internal ExchangeNotesSyncState State => _state;

        public bool PassRunning
        {
            get { lock (_gate) return _passRunning; }
        }

        public void MarkChanged(Guid id) => Mark(id, deleted: false);

        public void MarkDeleted(Guid id) => Mark(id, deleted: true);

        private void Mark(Guid id, bool deleted)
        {
            lock (_gate)
            {
                if (_passRunning) { _pending.Add(new KeyValuePair<Guid, bool>(id, deleted)); return; }
                ApplyMark(id, deleted);
            }
            QueueSave();
        }

        private void ApplyMark(Guid id, bool deleted)
        {
            if (deleted) _state.MarkDeleted(id);
            else _state.MarkChanged(id);
        }

        /// <summary>A local change of this note waits for the end of the running pass.</summary>
        public bool IsMarkPending(Guid id)
        {
            lock (_gate)
            {
                foreach (KeyValuePair<Guid, bool> mark in _pending)
                    if (mark.Key == id) return true;
                return false;
            }
        }

        /// <summary>The state for one pass, or null while another runs (passes never overlap).</summary>
        public ExchangeNotesSyncState? BeginPass()
        {
            lock (_gate)
            {
                if (_passRunning) return null;
                _passRunning = true;
                return _state;
            }
        }

        /// <summary>Applies the marks that waited and writes the file - on the pass's thread, which is the pool.</summary>
        public void EndPass()
        {
            string json;
            lock (_gate)
            {
                foreach (KeyValuePair<Guid, bool> mark in _pending) ApplyMark(mark.Key, mark.Value);
                _pending.Clear();
                _passRunning = false;
                json = ExchangeNotesSyncStore.Serialize(_state);
            }
            Write(json);
        }

        /// <summary>
        /// One pass of <paramref name="sync"/> over this ledger's state. Null when a pass already runs. A cancel, or the
        /// UI thread going away under the adapter, ends the pass as <see cref="ExchangeCodes.Cancelled"/>; the marks and
        /// the revisions reached so far are kept.
        /// </summary>
        public async Task<ExchangeNotesSyncReport?> RunPassAsync(ExchangeNotesSync sync, ExchangeAccountEntry entry,
            IExchangeNotesLocal local, CancellationToken ct)
        {
            ExchangeNotesSyncState? state = BeginPass();
            if (state == null) return null;
            try
            {
                return await sync.SyncAsync(entry, local, state, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ExchangeNotesSyncReport { Code = ExchangeCodes.Cancelled };
            }
            finally
            {
                EndPass();
            }
        }

        private void QueueSave()
        {
            if (Interlocked.Exchange(ref _saveQueued, 1) == 1) return;
            _background(() =>
            {
                string? json;
                lock (_gate)
                {
                    Interlocked.Exchange(ref _saveQueued, 0);
                    // a running pass writes the file itself when it ends
                    json = _passRunning ? null : ExchangeNotesSyncStore.Serialize(_state);
                }
                if (json != null) Write(json);
            });
        }

        private void Write(string json)
        {
            bool saved;
            lock (_fileGate) saved = ExchangeNotesSyncStore.SaveText(_path, json);
            if (!saved) QuickNotesLog.Log("sync state not saved");
        }
    }

    /// <summary>
    /// <see cref="IExchangeNotesLocal"/> over the live <see cref="QuickNotesService"/> (ticket S0105, phase 3). The sync
    /// runs on the pool; the notes list belongs to the UI thread, so every call is sent there and works on copies. A
    /// change the user is making right now - an edit the debounce has not written, a mark the ledger still holds - makes
    /// <see cref="ApplyRemote"/> and <see cref="DeleteRemote"/> refuse, and the sync keeps both versions (item FJ).
    /// </summary>
    internal sealed class QuickNotesSyncLocal : IExchangeNotesLocal
    {
        private readonly QuickNotesService _service;
        private readonly ExchangeNotesSyncLedger _ledger;
        private readonly SynchronizationContext? _owner;
        private readonly Func<bool>? _alive;

        /// <param name="owner">The service's thread; null runs every call where it is made (the tests' shape).</param>
        /// <param name="alive">
        /// Asked on the owner's thread before every call: false - the service was switched off or the app is closing -
        /// ends the pass as cancelled, so a retired journal is never written by a pass still on its way.
        /// </param>
        public QuickNotesSyncLocal(QuickNotesService service, ExchangeNotesSyncLedger ledger, SynchronizationContext? owner,
            Func<bool>? alive = null)
        {
            _service = service;
            _ledger = ledger;
            _owner = owner;
            _alive = alive;
        }

        /// <summary>
        /// Routes the service's local changes into the ledger's marks. Dispose to stop. The service raises both events on
        /// its own thread, never for a change the sync itself applied.
        /// </summary>
        public static IDisposable Bind(QuickNotesService service, ExchangeNotesSyncLedger ledger)
        {
            Action<Guid> changed = ledger.MarkChanged;
            Action<Guid> deleted = ledger.MarkDeleted;
            service.LocalChanged += changed;
            service.LocalDeleted += deleted;
            return new Unbind(() =>
            {
                service.LocalChanged -= changed;
                service.LocalDeleted -= deleted;
            });
        }

        public IReadOnlyList<QuickNote> Snapshot() => OnOwner(() => _service.CloneNotes());

        public QuickNote? Find(Guid id) => OnOwner(() => _service.CloneOf(id));

        public bool ApplyRemote(QuickNote note)
            => OnOwner(() => !_ledger.IsMarkPending(note.Id) && _service.ApplyRemote(note));

        public bool DeleteRemote(Guid id, bool evenIfEdited)
            => OnOwner(() => (evenIfEdited || !_ledger.IsMarkPending(id)) && _service.DeleteRemote(id, evenIfEdited));

        public void AddLocal(QuickNote note) => OnOwner(() => { _service.AddLocal(note); return true; });

        private T OnOwner<T>(Func<T> guarded)
        {
            Func<T> work = () =>
            {
                if (_alive != null && !_alive()) throw new OperationCanceledException("quick notes service retired");
                return guarded();
            };
            if (_owner == null || SynchronizationContext.Current == _owner) return work();
            T result = default!;
            ExceptionDispatchInfo? failure = null;
            try
            {
                _owner.Send(_ =>
                {
                    try { result = work(); }
                    catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
                }, null);
            }
            catch (Exception ex) when (ex is InvalidAsynchronousStateException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                // the UI thread is gone (the app is closing): the pass ends here, nothing half-applied
                throw new OperationCanceledException("quick notes owner thread unavailable", ex);
            }
            failure?.Throw();
            return result;
        }

        private sealed class Unbind : IDisposable
        {
            private Action? _undo;
            public Unbind(Action undo) { _undo = undo; }
            public void Dispose() { Interlocked.Exchange(ref _undo, null)?.Invoke(); }
        }
    }
}
