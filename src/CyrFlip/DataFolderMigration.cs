using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CyrFlip
{
    /// <summary>
    /// The Store build's one-time move out of the machine-wide <c>%ProgramData%\CyrFlip</c> into the
    /// package's per-user folder (<see cref="DataFolder"/>, ticket S0016 section 4). Canon invariant 10:
    /// the user's data survives the move - above all the encrypted quick-notes journal.
    ///
    /// <para>Four rules, each with its own test (<c>DataFolderMigrationTests</c>):</para>
    /// <list type="number">
    /// <item>A file is touched only when the <b>current user owns it</b>. That folder is shared by every
    /// account on the machine, and another account's journal is theirs to migrate on their own start -
    /// its DPAPI key is theirs too.</item>
    /// <item>The journal (and its <c>.bak</c>) is <b>moved</b> when the new folder has none. When it has one,
    /// neither is lost: the old journal is kept beside it as <c>quick-notes.migrated-&lt;date&gt;.log</c> and
    /// replayed once into the live journal, the newer <c>UpdatedAtUtc</c> winning per note.</item>
    /// <item>Diagnostic logs are not migrated - the user's own old ones are deleted, since they are exactly
    /// what other accounts could read. The <c>reports\</c> archives are moved.</item>
    /// <item>The layout files are left alone: that folder keeps the deprecated mirror
    /// (<c>LAYOUT-SIGNAL</c> 1.1 rule 1), which <see cref="LayoutPublisher"/> owns.</item>
    /// </list>
    ///
    /// <para>Every step is idempotent by construction - a moved file is no longer there to move again - so a
    /// pass that failed half way is simply run again on the next start; the <c>DataFolderMigrated</c> marker
    /// only saves the next starts from looking.</para>
    /// </summary>
    internal sealed class DataFolderMigration
    {
        /// <summary>Diagnostic files a pre-S0016 packaged build wrote to the shared folder.</summary>
        internal static readonly string[] DiagnosticFiles =
        {
            "launcher.log", "translate.log", "context-menu.log", "caret-diagnostics.txt",
            QuickNotesLog.FileName, "clipboard-history-diagnostics.log", "clipboard-flip.log",
        };

        internal const string ReportsFolderName = "reports";

        internal sealed class Result
        {
            public bool JournalMoved;
            /// <summary>The kept copy of an old journal that was merged rather than moved, else null.</summary>
            public string? MergedJournal;
            public int NotesMerged;
            public int LogsDeleted;
            public int ReportsMoved;
            /// <summary>Files left alone because another account owns them.</summary>
            public int ForeignSkipped;
            /// <summary>Steps that failed and are worth another try on the next start.</summary>
            public int Failures;

            public bool Complete => Failures == 0;

            public override string ToString()
                => "journal moved=" + JournalMoved + " merged=" + (MergedJournal != null) + " notes merged=" + NotesMerged
                    + " logs deleted=" + LogsDeleted + " reports moved=" + ReportsMoved
                    + " foreign skipped=" + ForeignSkipped + " failures=" + Failures;
        }

        private readonly string _legacy;
        private readonly string _current;
        private readonly Func<string, bool> _isOwn;
        private readonly IQuickNotesCipher? _cipher;
        private readonly DateTime _today;

        internal DataFolderMigration(string legacy, string current, Func<string, bool> isOwn,
            IQuickNotesCipher? cipher, DateTime today)
        {
            _legacy = legacy;
            _current = current;
            _isOwn = isOwn;
            _cipher = cipher;
            _today = today;
        }

        /// <summary>
        /// The live app's call, made at startup before anything reads the journal or writes a log: packaged
        /// builds only, once. Never throws - a migration that cannot run leaves the old files where they are.
        /// </summary>
        public static void RunOnce(AppConfig config)
        {
            string? legacy = DataFolder.LegacyShared;
            if (legacy == null || config.DataFolderMigrated)
                return;
            try
            {
                Result result = new DataFolderMigration(legacy, DataFolder.Current, IsOwnedByCurrentUser, null, DateTime.Now).Run();
                QuickNotesLog.Log("data folder migration: " + result);
                if (result.Complete)
                    config.SaveDataFolderMigrated();
            }
            catch (Exception ex)
            {
                QuickNotesLog.Log("data folder migration failed: " + ex.GetType().Name);
            }
        }

        internal Result Run()
        {
            var result = new Result();
            if (string.Equals(Path.GetFullPath(_legacy), Path.GetFullPath(_current), StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(_legacy))
                return result;

            MigrateJournal(result);
            DeleteOwnLogs(result);
            MoveReports(result);
            return result;
        }

        private void MigrateJournal(Result result)
        {
            string source = Path.Combine(_legacy, QuickNotesStore.FileName);
            string target = Path.Combine(_current, QuickNotesStore.FileName);
            string? kept = null;

            if (File.Exists(source))
            {
                if (!_isOwn(source))
                    result.ForeignSkipped++;
                else if (!File.Exists(target) && !File.Exists(target + ".bak"))
                {
                    if (TryMove(source, target, result))
                        result.JournalMoved = true;
                }
                else
                {
                    kept = MigratedName(QuickNotesStore.FileName);
                    if (!TryMove(source, kept, result))
                        kept = null;
                }
            }

            // The backup follows the journal: moved when the new folder has none, else kept beside the kept
            // journal (a .bak is an older state of the same notes, so it is kept, never merged).
            string sourceBak = source + ".bak";
            if (File.Exists(sourceBak))
            {
                if (!_isOwn(sourceBak))
                    result.ForeignSkipped++;
                else if (!File.Exists(target + ".bak") && (result.JournalMoved || !File.Exists(target)))
                    TryMove(sourceBak, target + ".bak", result);
                else
                    TryMove(sourceBak, (kept ?? MigratedName(QuickNotesStore.FileName)) + ".bak", result);
            }

            if (kept != null)
                Merge(kept, target, result);
        }

        /// <summary>Replay the kept journal into the live one, once: a note is written when it is new there
        /// or newer than the live copy. Deletes come along as recorded - a note the kept journal deleted is
        /// simply absent from its replay.</summary>
        private void Merge(string kept, string target, Result result)
        {
            try
            {
                List<QuickNote> incoming = new QuickNotesStore(kept, _cipher).Load();
                var live = new QuickNotesStore(target, _cipher);
                var byId = new Dictionary<Guid, QuickNote>();
                foreach (QuickNote note in live.Load())
                    byId[note.Id] = note;

                foreach (QuickNote note in incoming)
                {
                    if (byId.TryGetValue(note.Id, out QuickNote? existing) && existing.UpdatedAtUtc >= note.UpdatedAtUtc)
                        continue;
                    if (live.Update(note))
                        result.NotesMerged++;
                    else
                        result.Failures++;
                }
                result.MergedJournal = kept;
            }
            catch
            {
                result.Failures++;
            }
        }

        private void DeleteOwnLogs(Result result)
        {
            foreach (string name in DiagnosticFiles)
            {
                string path = Path.Combine(_legacy, name);
                if (!File.Exists(path))
                    continue;
                if (!_isOwn(path))
                {
                    result.ForeignSkipped++;
                    continue;
                }
                try
                {
                    File.Delete(path);
                    result.LogsDeleted++;
                }
                catch
                {
                    result.Failures++;
                }
            }
        }

        private void MoveReports(Result result)
        {
            string source = Path.Combine(_legacy, ReportsFolderName);
            if (!Directory.Exists(source))
                return;
            string target = Path.Combine(_current, ReportsFolderName);
            string[] files;
            try
            {
                files = Directory.GetFiles(source);
            }
            catch
            {
                result.Failures++;
                return;
            }

            foreach (string file in files)
            {
                if (!_isOwn(file))
                {
                    result.ForeignSkipped++;
                    continue;
                }
                string destination = Path.Combine(target, Path.GetFileName(file));
                if (File.Exists(destination))
                    continue; // the same archive name twice: the one already in the new folder stays
                if (TryMove(file, destination, result))
                    result.ReportsMoved++;
            }

            try
            {
                if (Directory.GetFileSystemEntries(source).Length == 0)
                    Directory.Delete(source);
            }
            catch
            {
                // Another account's archives or an open handle: the empty-folder tidy-up is not worth a retry.
            }
        }

        /// <summary><c>quick-notes.migrated-yyyyMMdd.log</c>, numbered when that name is taken.</summary>
        private string MigratedName(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName) + ".migrated-" + _today.ToString("yyyyMMdd");
            string extension = Path.GetExtension(fileName);
            string candidate = Path.Combine(_current, stem + extension);
            for (int n = 2; File.Exists(candidate) || File.Exists(candidate + ".bak"); n++)
                candidate = Path.Combine(_current, stem + "-" + n + extension);
            return candidate;
        }

        private static bool TryMove(string source, string destination, Result result)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination);
                return true;
            }
            catch
            {
                result.Failures++;
                return false;
            }
        }

        /// <summary>
        /// True when the file's owner is the current Windows user. Anything unreadable answers false: a file
        /// we cannot prove is ours is never moved or deleted.
        /// </summary>
        internal static bool IsOwnedByCurrentUser(string path)
        {
            try
            {
                IdentityReference? owner = File.GetAccessControl(path, AccessControlSections.Owner)
                    .GetOwner(typeof(SecurityIdentifier));
                using WindowsIdentity me = WindowsIdentity.GetCurrent();
                return owner != null && me.User != null && owner.Equals(me.User);
            }
            catch
            {
                return false;
            }
        }
    }
}
