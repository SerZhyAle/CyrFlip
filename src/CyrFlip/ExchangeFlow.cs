using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The export and the import of the open exchange file (ticket S0023), from whichever surface they
    /// were asked for - the quick-notes window, the history search, or the settings pages. The format,
    /// the selection and the merge rules live in the <c>CyrFlipExchange*</c> classes, where they are
    /// unit-tested; this class is only the dialogs around them.
    ///
    /// <para>Two promises hold on every path. <b>Nothing is written without the user seeing it
    /// first</b>: the export states that the file is plain text before the file exists, and the
    /// import shows what it would add, update and skip before a note or an entry changes.
    /// <b>Nothing is deleted</b>: an import only adds and updates. And CyrFlip never sends, opens or
    /// watches the file - where it goes next is the user's business.</para>
    /// </summary>
    internal sealed class ExchangeFlow
    {
        /// <summary>How many unreadable blocks the preview names one by one before it only counts.</summary>
        private const int ProblemLinesShown = 8;
        /// <summary>How long the import waits for its history records to reach the disk before it reports.</summary>
        private static readonly TimeSpan HistoryImportFlush = TimeSpan.FromMinutes(2);

        private readonly AppConfig _config;
        private readonly ClipboardHistoryService _history;
        private readonly Func<QuickNotesService?> _notes;
        private readonly Action _commitOpenNote;

        /// <param name="notes">The quick-notes service, built on demand; null while the feature is off.</param>
        /// <param name="commitOpenNote">Write what the notes window is still holding in its editor.</param>
        public ExchangeFlow(AppConfig config, ClipboardHistoryService history, Func<QuickNotesService?> notes, Action commitOpenNote)
        {
            _config = config;
            _history = history;
            _notes = notes;
            _commitOpenNote = commitOpenNote;
        }

        private string T(string en) => Localization.Translate(_config.UiLanguage, en);

        /// <summary>The file dialogs' filter: the exchange file first, any text file second.</summary>
        private string Filter => T("CyrFlip exchange file") + " (*.cyrflip.txt;*.txt)|*.cyrflip.txt;*.txt|" + T("All files") + " (*.*)|*.*";

        public void Run(IWin32Window? owner, bool import)
        {
            if (import) Import(owner);
            else Export(owner);
        }

        // ---- Export ----

        public void Export(IWin32Window? owner)
        {
            QuickNotesService? notes = _config.EnableQuickNotes ? _notes() : null;
            bool pinned, rest;
            ExchangeHistoryScope scope;
            bool includeNotes;
            using (var dialog = new ExchangeExportDialog(_config.UiLanguage, notes != null, _config.ExchangeNotesWarningOff))
            {
                if (Show(dialog, owner) != DialogResult.OK) return;
                if (dialog.NotesWarningOff != _config.ExchangeNotesWarningOff)
                {
                    _config.ExchangeNotesWarningOff = dialog.NotesWarningOff;
                    _config.Save();
                }
                includeNotes = dialog.IncludeNotes;
                pinned = dialog.IncludePinned;
                rest = dialog.IncludeRest;
                scope = dialog.Scope;
            }

            string path;
            using (var save = new SaveFileDialog
            {
                Title = T("Export to exchange file"),
                Filter = Filter,
                FileName = "CyrFlip-" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt",
                DefaultExt = "txt",
                OverwritePrompt = true,
            })
            {
                if ((owner != null ? save.ShowDialog(owner) : save.ShowDialog()) != DialogResult.OK) return;
                path = save.FileName;
            }

            List<QuickNote>? noteList = null;
            if (includeNotes && notes != null)
            {
                // "Everything" means everything: the open editor's text and the replay, both finished
                // first - the replay waited for with the message loop running (S0030 HT-6).
                _commitOpenNote();
                WaitForNotes(owner, notes);
                notes.Flush();
                noteList = new List<QuickNote>(notes.Notes);
            }
            // Snapshots taken here; selecting, formatting and writing them is the worker's (S0030 HT-2).
            // An export must see the file's records even while the history is switched off, so the
            // lazy replay (S0064 H-5) is completed here, on the thread that owns the list - the one
            // caller that pays for it synchronously, by explicit user request.
            if (pinned || rest) _history.EnsureReplayed();
            List<ClipboardHistoryEntry>? history = pinned || rest ? new List<ClipboardHistoryEntry>(_history.Entries) : null;
            List<ClipboardHistoryEntry>? selected = null;
            int trimmed = 0;

            // Phase one - plan (S0066 L-9): select and cap, cheap enough to run before anybody is
            // committed to anything. The caps are the importer's own (25 MB, 10 000 objects):
            // "history: All" used to write a file this program refuses to read back, and the
            // person found out at the other machine (LK-3b-07).
            try
            {
                BusyDialog.Run(owner, _config.UiLanguage, () =>
                {
                    DateTime now = DateTime.UtcNow;
                    selected = history != null
                        ? CyrFlipExchangeWriter.SelectHistory(history, pinned, rest, scope, now)
                        : null;
                    trimmed = CyrFlipExchangeWriter.TrimToImportCaps(noteList, selected);
                });
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(owner, _config.UiLanguage, Localization.Format(T, "Could not save the file: {0}", FailureCause.Describe(ex, _config.UiLanguage)),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Say so *before* writing: the cap is told while cancelling still costs nothing.
            if (trimmed > 0
                && ConfirmDialog.Show(owner, _config.UiLanguage,
                        Localization.Format(T, "The export is larger than CyrFlip's import accepts (25 MB, 10,000 objects). The oldest {0} entries will be left out of the file. Continue?", trimmed),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            // Phase two - write.
            try
            {
                BusyDialog.Run(owner, _config.UiLanguage, () =>
                    CyrFlipExchangeWriter.WriteFile(path, CyrFlipExchangeWriter.ToText(DateTime.UtcNow, noteList, selected)));
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(owner, _config.UiLanguage, Localization.Format(T, "Could not save the file: {0}", FailureCause.Describe(ex, _config.UiLanguage)),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ConfirmDialog.Show(owner, _config.UiLanguage,
                Localization.Format(T, "Exported notes: {0}, history entries: {1}.", noteList?.Count ?? 0, selected?.Count ?? 0)
                + "\n\n" + path,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- Import ----

        public void Import(IWin32Window? owner)
        {
            string path;
            using (var open = new OpenFileDialog
            {
                Title = T("Import from exchange file"),
                Filter = Filter,
                CheckFileExists = true,
            })
            {
                if ((owner != null ? open.ShowDialog(owner) : open.ShowDialog()) != DialogResult.OK) return;
                path = open.FileName;
            }

            // Read and parsed on the pool (S0030 HT-2): a legal file is up to 25 MB.
            ExchangeReadResult read;
            try
            {
                read = BusyDialog.Run(owner, _config.UiLanguage, () => CyrFlipExchangeReader.ReadFile(path));
            }
            catch (Exception ex)
            {
                Tell(owner, Localization.Format(T, "Could not read the file: {0}", FailureCause.Describe(ex, _config.UiLanguage)), MessageBoxIcon.Warning);
                return;
            }

            if (read.Fatal != null)
            {
                Tell(owner, FatalText(read.Fatal.Value), MessageBoxIcon.Warning);
                return;
            }
            if (read.ValidBlocks == 0)
            {
                Tell(owner, T("The file contains no note or history entry that can be imported.")
                    + Problems(read.Problems), MessageBoxIcon.Warning);
                return;
            }

            QuickNotesService? notes = _config.EnableQuickNotes ? _notes() : null;
            bool history = _config.EnableClipboardHistory;
            bool includeNew = false;
            if (notes != null && read.NotesWithoutId > 0)
                includeNew = ConfirmDialog.Show(owner, _config.UiLanguage,
                    Localization.Format(T, "Notes without an Id in the file: {0} - they look hand-written. Import them as new notes?", read.NotesWithoutId),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            if (notes != null)
            {
                _commitOpenNote();
                WaitForNotes(owner, notes); // the plan is about all the notes, not about the replay so far
            }
            ExchangeMergeReport noteplan = notes?.PlanImport(read.Notes, includeNew) ?? new ExchangeMergeReport();
            ExchangeMergeReport historyplan = history ? _history.PlanImport(read.Clipboard) : new ExchangeMergeReport();

            var preview = new StringBuilder();
            preview.Append(Localization.Format(T, "Found notes: {0}, history entries: {1}.", read.Notes.Count, read.Clipboard.Count));
            if (notes == null && read.Notes.Count > 0)
                preview.Append('\n').Append(T("Quick notes are off - the notes in the file will be skipped."));
            if (!history && read.Clipboard.Count > 0)
                preview.Append('\n').Append(T("Clipboard history is off - the history entries will be skipped."));
            preview.Append("\n\n").Append(Localization.Format(T, "To be added: {0} notes, {1} history entries.",
                noteplan.NotesAdded + noteplan.NotesNew, historyplan.ClipboardAdded));
            preview.Append('\n').Append(Localization.Format(T, "Notes to be updated (the file has a newer version): {0}.", noteplan.NotesUpdated));
            preview.Append('\n').Append(Localization.Format(T, "Already here: {0} notes, {1} history entries.",
                noteplan.NotesSkipped, historyplan.ClipboardExisting));
            if (read.Problems.Count > 0)
                preview.Append('\n').Append(Localization.Format(T, "Blocks that could not be read: {0}.", read.Problems.Count))
                    .Append(Problems(read.Problems));

            int changes = noteplan.NotesAdded + noteplan.NotesNew + noteplan.NotesUpdated + historyplan.ClipboardAdded + historyplan.ClipboardRaised;
            if (changes == 0)
            {
                Tell(owner, preview.Append("\n\n").Append(T("Nothing to import.")).ToString(), MessageBoxIcon.Information);
                return;
            }
            preview.Append("\n\n").Append(T("The import only adds and updates - nothing already here is deleted. Import?"));
            if (ConfirmDialog.Show(owner, _config.UiLanguage, preview.ToString(), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            // Planned again inside each service, against the live state: the preview was modal, not frozen.
            // The list changes here; the thousands of encrypted records go out on the pool (S0030 HT-2).
            ExchangeMergeReport done = notes?.Import(read.Notes, includeNew,
                write => BusyDialog.Run(owner, _config.UiLanguage, write)) ?? new ExchangeMergeReport();
            ExchangeMergeReport doneHistory = history ? _history.Import(read.Clipboard) : new ExchangeMergeReport();
            // "Done" only once the records are on disk (S0031 CH2-6): a quick exit after the report used
            // to abandon the tail of a large import's queue without a word.
            int unwritten = history && doneHistory.ClipboardAdded + doneHistory.ClipboardExisting > 0
                ? BusyDialog.Run(owner, _config.UiLanguage, () => _history.FlushJournal(HistoryImportFlush))
                : 0;
            string message = done.NotesUnwritten > 0
                ? Localization.Format(T, "Notes not yet saved to disk: {0}. CyrFlip will retry while it is running.", done.NotesUnwritten)
                : Localization.Format(T, "Import finished: {0} notes added, {1} updated, {2} history entries added.",
                done.NotesAdded + done.NotesNew, done.NotesUpdated, doneHistory.ClipboardAdded);
            if (unwritten > 0)
                message += "\n\n" + Localization.Format(T, "History entries not yet written to disk: {0}. They will be written while CyrFlip keeps running.", unwritten);
            Tell(owner, message, unwritten > 0 || done.NotesUnwritten > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        /// <summary>Wait for the notes replay with the message loop running - never <c>Task.Wait</c> on this thread.</summary>
        private void WaitForNotes(IWin32Window? owner, QuickNotesService notes)
        {
            if (!notes.IsLoaded) BusyDialog.Wait(owner, _config.UiLanguage, notes.LoadAsync());
        }

        private void Tell(IWin32Window? owner, string message, MessageBoxIcon icon)
            => ConfirmDialog.Show(owner, _config.UiLanguage, message, MessageBoxButtons.OK, icon);

        private static DialogResult Show(Form dialog, IWin32Window? owner)
        {
            if (owner != null) return dialog.ShowDialog(owner);
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Shown += (_, _) => ForegroundActivator.Activate(dialog);
            return dialog.ShowDialog();
        }

        internal string FatalText(ExchangeProblemKind kind)
        {
            switch (kind)
            {
                case ExchangeProblemKind.UnknownVersion:
                    return T("The file was written in another version of the CyrFlip exchange format. Update CyrFlip to import it.");
                case ExchangeProblemKind.FileTooLarge:
                    return Localization.Format(T, "The file is larger than {0} MB - such a file is not imported.", CyrFlipExchange.MaxFileBytes / (1024 * 1024));
                default:
                    return Localization.Format(T, "The marker “{0}” was not found - this is not a CyrFlip exchange file.", CyrFlipExchange.Marker);
            }
        }

        /// <summary>One line per unreadable block, with its line number, up to <see cref="ProblemLinesShown"/>.</summary>
        private string Problems(List<ExchangeProblem> problems)
        {
            if (problems.Count == 0) return "";
            var text = new StringBuilder();
            for (int i = 0; i < problems.Count && i < ProblemLinesShown; i++)
                text.Append('\n').Append(Localization.Format(T, "Line {0}: {1}", problems[i].Line, Reason(problems[i])));
            if (problems.Count > ProblemLinesShown)
                text.Append('\n').Append(Localization.Format(T, "and {0} more", problems.Count - ProblemLinesShown));
            return text.ToString();
        }

        internal string Reason(ExchangeProblem problem)
        {
            switch (problem.Kind)
            {
                case ExchangeProblemKind.NoBody: return T("the block has no text inside a backtick fence");
                case ExchangeProblemKind.UnclosedFence: return T("the text fence is never closed - the rest of the file was not read");
                case ExchangeProblemKind.BadField: return Localization.Format(T, "the “{0}” field is missing or malformed", problem.Detail);
                case ExchangeProblemKind.HashMismatch: return T("the text does not match its Id - it was changed or damaged");
                case ExchangeProblemKind.TooLarge: return T("the text is larger than allowed");
                case ExchangeProblemKind.MetadataTooLong: return Localization.Format(T, "a metadata line is longer than {0} characters", CyrFlipExchange.MaxMetadataChars);
                case ExchangeProblemKind.TooManyObjects: return Localization.Format(T, "more than {0} objects - the rest were not imported", CyrFlipExchange.MaxObjects);
                default: return FatalText(problem.Kind);
            }
        }
    }
}
