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

        private string T(string ru) => Localization.Translate(_config.UiLanguage, ru);

        /// <summary>The file dialogs' filter: the exchange file first, any text file second.</summary>
        private string Filter => T("Файл обмена CyrFlip") + " (*.cyrflip.txt;*.txt)|*.cyrflip.txt;*.txt|" + T("Все файлы") + " (*.*)|*.*";

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
                Title = T("Экспорт в файл обмена"),
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
            List<ClipboardHistoryEntry>? history = pinned || rest ? new List<ClipboardHistoryEntry>(_history.Entries) : null;
            List<ClipboardHistoryEntry>? entries = null;

            try
            {
                entries = BusyDialog.Run(owner, _config.UiLanguage, () =>
                {
                    DateTime now = DateTime.UtcNow;
                    List<ClipboardHistoryEntry>? selected = history != null
                        ? CyrFlipExchangeWriter.SelectHistory(history, pinned, rest, scope, now)
                        : null;
                    CyrFlipExchangeWriter.WriteFile(path, CyrFlipExchangeWriter.ToText(now, noteList, selected));
                    return selected;
                });
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(owner, _config.UiLanguage, string.Format(T("Не удалось сохранить файл: {0}"), FailureCause.Describe(ex, _config.UiLanguage)),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ConfirmDialog.Show(owner, _config.UiLanguage,
                string.Format(T("Экспортировано заметок: {0}, записей истории: {1}."), noteList?.Count ?? 0, entries?.Count ?? 0)
                + "\n\n" + path,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- Import ----

        public void Import(IWin32Window? owner)
        {
            string path;
            using (var open = new OpenFileDialog
            {
                Title = T("Импорт из файла обмена"),
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
                Tell(owner, string.Format(T("Не удалось прочитать файл: {0}"), FailureCause.Describe(ex, _config.UiLanguage)), MessageBoxIcon.Warning);
                return;
            }

            if (read.Fatal != null)
            {
                Tell(owner, FatalText(read.Fatal.Value), MessageBoxIcon.Warning);
                return;
            }
            if (read.ValidBlocks == 0)
            {
                Tell(owner, T("В файле нет ни одной заметки или записи истории, которую можно импортировать.")
                    + Problems(read.Problems), MessageBoxIcon.Warning);
                return;
            }

            QuickNotesService? notes = _config.EnableQuickNotes ? _notes() : null;
            bool history = _config.EnableClipboardHistory;
            bool includeNew = false;
            if (notes != null && read.NotesWithoutId > 0)
                includeNew = ConfirmDialog.Show(owner, _config.UiLanguage,
                    string.Format(T("Заметок без Id в файле: {0} - похоже, они написаны вручную. Импортировать их как новые заметки?"), read.NotesWithoutId),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

            if (notes != null)
            {
                _commitOpenNote();
                WaitForNotes(owner, notes); // the plan is about all the notes, not about the replay so far
            }
            ExchangeMergeReport noteplan = notes?.PlanImport(read.Notes, includeNew) ?? new ExchangeMergeReport();
            ExchangeMergeReport historyplan = history ? _history.PlanImport(read.Clipboard) : new ExchangeMergeReport();

            var preview = new StringBuilder();
            preview.Append(string.Format(T("Найдено заметок: {0}, записей истории: {1}."), read.Notes.Count, read.Clipboard.Count));
            if (notes == null && read.Notes.Count > 0)
                preview.Append('\n').Append(T("Быстрые заметки выключены - заметки из файла будут пропущены."));
            if (!history && read.Clipboard.Count > 0)
                preview.Append('\n').Append(T("История буфера выключена - записи истории будут пропущены."));
            preview.Append("\n\n").Append(string.Format(T("Будет добавлено: заметок {0}, записей истории {1}."),
                noteplan.NotesAdded + noteplan.NotesNew, historyplan.ClipboardAdded));
            preview.Append('\n').Append(string.Format(T("Будет обновлено заметок (в файле более новая версия): {0}."), noteplan.NotesUpdated));
            preview.Append('\n').Append(string.Format(T("Уже есть: заметок {0}, записей истории {1}."),
                noteplan.NotesSkipped, historyplan.ClipboardExisting));
            if (read.Problems.Count > 0)
                preview.Append('\n').Append(string.Format(T("Не удалось прочитать блоков: {0}."), read.Problems.Count))
                    .Append(Problems(read.Problems));

            int changes = noteplan.NotesAdded + noteplan.NotesNew + noteplan.NotesUpdated + historyplan.ClipboardAdded + historyplan.ClipboardRaised;
            if (changes == 0)
            {
                Tell(owner, preview.Append("\n\n").Append(T("Импортировать нечего.")).ToString(), MessageBoxIcon.Information);
                return;
            }
            preview.Append("\n\n").Append(T("Импорт только добавляет и обновляет - ничего из текущих данных не удаляется. Импортировать?"));
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
            string message = string.Format(T("Импорт завершён: добавлено заметок {0}, обновлено {1}, добавлено записей истории {2}."),
                done.NotesAdded + done.NotesNew, done.NotesUpdated, doneHistory.ClipboardAdded);
            if (unwritten > 0)
                message += "\n\n" + string.Format(T("Записей истории ещё не записано на диск: {0}. Они будут дописаны, пока CyrFlip работает."), unwritten);
            Tell(owner, message, unwritten > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
                    return T("Файл записан другой версией формата обмена CyrFlip. Обновите CyrFlip, чтобы его импортировать.");
                case ExchangeProblemKind.FileTooLarge:
                    return string.Format(T("Файл больше {0} МБ - такой файл не импортируется."), CyrFlipExchange.MaxFileBytes / (1024 * 1024));
                default:
                    return string.Format(T("Не найден маркер «{0}» - это не файл обмена CyrFlip."), CyrFlipExchange.Marker);
            }
        }

        /// <summary>One line per unreadable block, with its line number, up to <see cref="ProblemLinesShown"/>.</summary>
        private string Problems(List<ExchangeProblem> problems)
        {
            if (problems.Count == 0) return "";
            var text = new StringBuilder();
            for (int i = 0; i < problems.Count && i < ProblemLinesShown; i++)
                text.Append('\n').Append(string.Format(T("Строка {0}: {1}"), problems[i].Line, Reason(problems[i])));
            if (problems.Count > ProblemLinesShown)
                text.Append('\n').Append(string.Format(T("и ещё {0}"), problems.Count - ProblemLinesShown));
            return text.ToString();
        }

        internal string Reason(ExchangeProblem problem)
        {
            switch (problem.Kind)
            {
                case ExchangeProblemKind.NoBody: return T("у блока нет текста в ограждении из обратных кавычек");
                case ExchangeProblemKind.UnclosedFence: return T("ограждение текста не закрыто - остаток файла не прочитан");
                case ExchangeProblemKind.BadField: return string.Format(T("поле «{0}» отсутствует или записано неверно"), problem.Detail);
                case ExchangeProblemKind.HashMismatch: return T("текст не совпадает со своим Id - он изменён или повреждён");
                case ExchangeProblemKind.TooLarge: return T("текст больше допустимого размера");
                case ExchangeProblemKind.MetadataTooLong: return string.Format(T("строка метаданных длиннее {0} символов"), CyrFlipExchange.MaxMetadataChars);
                case ExchangeProblemKind.TooManyObjects: return string.Format(T("больше {0} объектов - остальные не импортированы"), CyrFlipExchange.MaxObjects);
                default: return FatalText(problem.Kind);
            }
        }
    }
}
