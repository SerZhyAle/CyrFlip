using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// The notes sync's place in the tray app (ticket S0105, <c>DEVICE-EXCHANGE</c> 1.13 item FM, assumption A3). The
    /// ledger is bound to the quick notes service whenever the service exists - option on or off - so an edit made
    /// while the sync is off is still marked. A pass runs only while <see cref="NotesSyncGate.Live"/> holds: on start
    /// and on switching it on, 3 s after the last local save or delete, when the notes window is shown, and on
    /// "Sync now". Nothing here holds the clipboard guard or touches a file on the UI thread.
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        private const int NotesSyncDelayMs = 3000;

        private Task<ExchangeNotesSyncLedger>? _notesLedgerLoad;
        private ExchangeNotesSyncLedger? _notesLedger;
        private QuickNotesService? _notesBoundService;
        private IDisposable? _notesBinding;
        /// <summary>Marks raised before the ledger was read; replayed into it when it binds.</summary>
        private readonly List<KeyValuePair<Guid, bool>> _notesEarlyMarks = new List<KeyValuePair<Guid, bool>>();
        private System.Windows.Forms.Timer? _notesSyncTimer;
        private CancellationTokenSource? _notesSyncCancel;
        private bool _notesSyncRunning;
        private bool _notesSyncAgain;
        private bool _notesSyncPendingStart;
        private bool _notesSyncWasLive;
        private ExchangeNotesSyncReport? _notesLastReport;
        private DateTime _notesLastAtUtc;

        private bool NotesSyncLive => NotesSyncGate.Live(_config);

        /// <summary>Called once per new service, on the UI thread, before its replay starts.</summary>
        private void AttachNotesSync(QuickNotesService service)
        {
            _notesLedgerLoad ??= Task.Run(() => ExchangeNotesSyncLedger.Load(ExchangeNotesSyncStore.DefaultPath));
            service.LocalChanged += id => OnLocalNoteChange(service, id, deleted: false);
            service.LocalDeleted += id => OnLocalNoteChange(service, id, deleted: true);
            service.Loaded += (_, _) => { if (ReferenceEquals(service, _quickNotes)) RunPendingNotesSync(); };
            _notesSyncPendingStart = true;   // item FM: "on connecting" - a new service is the start of this PC's session
            Task<ExchangeNotesSyncLedger> load = _notesLedgerLoad;
            load.ContinueWith(_ => _ui?.Post(__ => BindNotesLedger(service, load), null), TaskScheduler.Default);
        }

        private void BindNotesLedger(QuickNotesService service, Task<ExchangeNotesSyncLedger> load)
        {
            if (_disposed || !ReferenceEquals(service, _quickNotes)) return;
            if (load.Status != TaskStatus.RanToCompletion)
            {
                QuickNotesLog.Log("sync state not read: " + (load.Exception?.InnerException?.GetType().Name ?? "unknown"));
                return;
            }
            _notesLedger = load.Result;
            _notesBinding?.Dispose();
            _notesBinding = QuickNotesSyncLocal.Bind(service, _notesLedger);
            _notesBoundService = service;
            foreach (KeyValuePair<Guid, bool> mark in _notesEarlyMarks)
            {
                if (mark.Value) _notesLedger.MarkDeleted(mark.Key);
                else _notesLedger.MarkChanged(mark.Key);
            }
            _notesEarlyMarks.Clear();
            RunPendingNotesSync();
        }

        private void OnLocalNoteChange(QuickNotesService service, Guid id, bool deleted)
        {
            if (!ReferenceEquals(service, _notesBoundService)) _notesEarlyMarks.Add(new KeyValuePair<Guid, bool>(id, deleted));
            ScheduleNotesSync();
        }

        /// <summary>Unbind from a service that is going away (switched off, or the app closing); the ledger stays.</summary>
        private void DetachNotesSync()
        {
            _notesBinding?.Dispose();
            _notesBinding = null;
            _notesBoundService = null;
            _notesEarlyMarks.Clear();
            _notesSyncTimer?.Stop();
            _notesSyncCancel?.Cancel();
            _notesSyncAgain = false;
            _notesSyncPendingStart = false;
        }

        /// <summary>The option, a module switch or the account list changed: start on going live, stop on going dark.</summary>
        private void OnNotesSyncConditionsChanged()
        {
            bool live = NotesSyncLive;
            if (live && !_notesSyncWasLive)
            {
                _notesSyncPendingStart = true;
                if (EnsureQuickNotesService() != null) RunPendingNotesSync();
            }
            else if (!live)
            {
                _notesSyncTimer?.Stop();
                _notesSyncCancel?.Cancel();
                _notesSyncAgain = false;
            }
            _notesSyncWasLive = live;
            _settings?.RefreshQuickNotesSyncStatus();
        }

        private void RunPendingNotesSync()
        {
            if (!_notesSyncPendingStart) return;
            QuickNotesService? service = _quickNotes;
            if (service == null || !service.IsLoaded || !ReferenceEquals(service, _notesBoundService)) return;
            _notesSyncPendingStart = false;
            StartNotesSync();
        }

        /// <summary>After a local save or delete: one pass 3 s after the last of a burst (item FM, coalesced).</summary>
        private void ScheduleNotesSync()
        {
            if (_disposed || !NotesSyncLive) return;
            if (_notesSyncTimer == null)
            {
                _notesSyncTimer = new System.Windows.Forms.Timer { Interval = NotesSyncDelayMs };
                _notesSyncTimer.Tick += (_, _) => { _notesSyncTimer?.Stop(); StartNotesSync(); };
            }
            _notesSyncTimer.Stop();
            _notesSyncTimer.Start();
        }

        /// <summary>One pass now, or one more right after the running one. Does nothing while the sync is not live.</summary>
        private void StartNotesSync()
        {
            if (_disposed || !NotesSyncLive) return;
            QuickNotesService? service = _quickNotes ?? EnsureQuickNotesService();
            ExchangeNotesSyncLedger? ledger = _notesLedger;
            ExchangeAccountEntry? entry = NotesSyncGate.Entry(_config);
            if (service == null || entry == null) return;
            if (!service.IsLoaded || ledger == null || !ReferenceEquals(service, _notesBoundService))
            {
                _notesSyncPendingStart = true;   // runs when the notes are read and the ledger is bound
                return;
            }
            if (_notesSyncRunning) { _notesSyncAgain = true; return; }

            _notesSyncTimer?.Stop();
            _notesSyncRunning = true;
            var cancel = new CancellationTokenSource();
            _notesSyncCancel = cancel;
            var local = new QuickNotesSyncLocal(service, ledger, _ui,
                () => !_disposed && ReferenceEquals(service, _quickNotes) && NotesSyncLive);
            var sync = new ExchangeNotesSync(new ExchangeClient(), () => _config.SaveExchangeAccounts());
            _settings?.RefreshQuickNotesSyncStatus();

            Task.Run(() => ledger.RunPassAsync(sync, entry, local, cancel.Token))
                .ContinueWith(done => _ui?.Post(_ => FinishNotesSync(done, cancel), null), TaskScheduler.Default);
        }

        private void FinishNotesSync(Task<ExchangeNotesSyncReport?> done, CancellationTokenSource cancel)
        {
            _notesSyncRunning = false;
            if (ReferenceEquals(_notesSyncCancel, cancel)) _notesSyncCancel = null;
            cancel.Dispose();

            ExchangeNotesSyncReport? report;
            if (done.Status == TaskStatus.RanToCompletion) report = done.Result;
            else
            {
                QuickNotesLog.Log("sync pass failed: " + (done.Exception?.InnerException?.GetType().Name ?? "unknown"));
                report = new ExchangeNotesSyncReport { Code = ExchangeCodes.Unanswered };
            }
            if (report != null)
            {
                _notesLastReport = report;
                _notesLastAtUtc = DateTime.UtcNow;
                // counts and the code only - never a title or a text
                QuickNotesLog.Log(string.Format(CultureInfo.InvariantCulture,
                    "sync pass: code={0} received={1} deleted={2} sent={3} conflicts={4} tooLarge={5} unsupported={6} unreadable={7} limit={8}",
                    report.Ok ? "ok" : report.Code, report.Received, report.Deleted, report.Sent, report.Conflicts,
                    report.TooLarge, report.Unsupported, report.Unreadable, report.LimitReached ? 1 : 0));
                if (!report.Ok && ExchangeCodes.AsksForPassword(report.Code)) _settings?.RefreshState();
            }
            if (_disposed) return;
            _settings?.RefreshQuickNotesSyncStatus();
            if (_notesSyncAgain)
            {
                _notesSyncAgain = false;
                StartNotesSync();
            }
        }

        /// <summary>The status line of the settings group, in the interface language; null while the option is off.</summary>
        private string? NotesSyncStatusText()
            => NotesSyncGate.StatusText(_config, _notesSyncRunning, _notesLastReport, _notesLastAtUtc, T);
    }

    /// <summary>
    /// When the notes sync may send a byte, and what its status line says - the pure half of the context's wiring,
    /// so both are unit-tested without a tray (ticket S0105, done criterion "zero notes envelopes while off").
    /// </summary>
    internal static class NotesSyncGate
    {
        /// <summary>The quick notes on, the exchange server module on, the option on, and one entry to sync with (item FK).</summary>
        public static bool Live(AppConfig config)
            => config.EnableQuickNotes && config.EnableExchangeClipboard && config.QuickNotesSync && Entry(config) != null;

        /// <summary>The entries the notes may sync with: every member entry (a guest cannot, item AO).</summary>
        public static List<ExchangeAccountEntry> Candidates(AppConfig config)
        {
            var result = new List<ExchangeAccountEntry>();
            foreach (ExchangeAccountEntry e in config.ExchangeClipboardAccounts)
                if (!e.IsGuest) result.Add(e);
            return result;
        }

        /// <summary>The picked entry while it still exists, else the only member entry, else none (item FK: one entry only).</summary>
        public static ExchangeAccountEntry? Entry(AppConfig config)
        {
            List<ExchangeAccountEntry> candidates = Candidates(config);
            foreach (ExchangeAccountEntry e in candidates)
                if (e.Id == config.QuickNotesSyncEntry) return e;
            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static string? StatusText(AppConfig config, bool running, ExchangeNotesSyncReport? last, DateTime lastAtUtc,
            Func<string, string> t)
        {
            if (!config.QuickNotesSync || !config.EnableQuickNotes) return null;
            if (!config.EnableExchangeClipboard)
                return t("The exchange server is turned off. Turn it on on the \"Exchange server\" page.");
            if (Candidates(config).Count == 0)
                return t("There is no account on an exchange server yet. Add one on the \"Exchange server\" page.");
            if (Entry(config) == null) return t("Choose the account for the notes.");
            if (running) return t("Syncing the notes..");
            if (last == null) return t("The notes have not been synced yet.");
            if (!last.Ok) return ExchangeMessages.Describe(last.Code, ExchangeContext.Notes, t).Text;

            var lines = new List<string>
            {
                Localization.Format(t, "Synced at {0}.", lastAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)),
            };
            if (last.Conflicts > 0) lines.Add(ExchangeMessages.Describe(ExchangeCodes.NoteConflict, ExchangeContext.Notes, t).What);
            if (last.LimitReached) lines.Add(ExchangeMessages.Describe(ExchangeCodes.LimitReached, ExchangeContext.Notes, t).What);
            if (last.TooLarge > 0) lines.Add(Localization.Format(t, "Notes too large to sync: {0}. They stay on this computer.", last.TooLarge));
            if (last.Unsupported > 0) lines.Add(Localization.Format(t, "Notes from a newer program, left as they are: {0}.", last.Unsupported));
            if (last.Unreadable > 0) lines.Add(Localization.Format(t, "Notes that could not be opened on this computer: {0}.", last.Unreadable));
            return string.Join("\n", lines);
        }
    }
}
