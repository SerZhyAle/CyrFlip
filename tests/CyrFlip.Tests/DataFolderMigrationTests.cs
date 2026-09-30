using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The Store build's one-time move out of the machine-wide <c>%ProgramData%\CyrFlip</c> (ticket S0016
    /// section 4). Ownership is a seam - a set of paths "another account" owns - because a real second
    /// Windows account cannot be conjured inside a unit test; <c>IsOwnedByCurrentUser</c> itself is checked
    /// against a file this process just created.
    /// </summary>
    public sealed class DataFolderMigrationTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "CyrFlipMigrationTests-" + Guid.NewGuid().ToString("N"));
        private readonly HashSet<string> _foreign = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly DateTime Today = new DateTime(2026, 9, 26);

        private string Legacy => Path.Combine(_root, "ProgramData", "CyrFlip");
        private string Current => Path.Combine(_root, "Packages", "LocalCache", "Local", "CyrFlip");

        public DataFolderMigrationTests()
        {
            Directory.CreateDirectory(Legacy);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

            public string? Unprotect(string cipher)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch { return null; }
            }
        }

        private DataFolderMigration.Result Migrate()
            => new DataFolderMigration(Legacy, Current, path => !_foreign.Contains(path), new FakeCipher(), Today).Run();

        private static QuickNote Note(string raw, DateTime updated, Guid? id = null) => new QuickNote
        {
            Id = id ?? Guid.NewGuid(),
            RawText = raw,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = updated,
        };

        private static QuickNotesStore Journal(string folder) => new QuickNotesStore(Path.Combine(folder, QuickNotesStore.FileName), new FakeCipher());

        [Fact]
        public void TheUsersOwnJournalIsMovedByteForByte()
        {
            var store = Journal(Legacy);
            store.Create(Note("token = 42\r\n\tindented", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            File.WriteAllText(store.BackupPath, "older");
            byte[] before = File.ReadAllBytes(store.Path);

            var result = Migrate();

            Assert.True(result.JournalMoved);
            Assert.True(result.Complete);
            Assert.False(File.Exists(store.Path));
            Assert.False(File.Exists(store.BackupPath));
            string moved = Path.Combine(Current, QuickNotesStore.FileName);
            Assert.Equal(before, File.ReadAllBytes(moved));
            Assert.Equal("older", File.ReadAllText(moved + ".bak"));
            Assert.Equal("token = 42\r\n\tindented", Journal(Current).Load().Single().RawText);
        }

        [Fact]
        public void AnotherAccountsJournalIsNeverTouched()
        {
            var store = Journal(Legacy);
            store.Create(Note("theirs", DateTime.UtcNow));
            _foreign.Add(store.Path);
            byte[] before = File.ReadAllBytes(store.Path);

            var result = Migrate();

            Assert.False(result.JournalMoved);
            Assert.Equal(1, result.ForeignSkipped);
            Assert.True(result.Complete); // theirs to migrate on their own start - not a failure to retry
            Assert.Equal(before, File.ReadAllBytes(store.Path));
            Assert.False(File.Exists(Path.Combine(Current, QuickNotesStore.FileName)));
        }

        [Fact]
        public void TwoJournalsAreBothKeptAndMergedNewerWins()
        {
            Guid shared = Guid.NewGuid();
            var early = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var late = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

            var old = Journal(Legacy);
            old.Create(Note("old side, newer edit", late, shared));
            old.Create(Note("only in the old journal", early));
            var deletedThere = Note("deleted in the old journal", early);
            old.Create(deletedThere);
            old.Delete(deletedThere.Id);
            Guid staleId = Guid.NewGuid();
            old.Create(Note("old side, stale", early, staleId));

            var live = Journal(Current);
            live.Create(Note("live side, older edit", early, shared));
            live.Create(Note("live side, current", late, staleId));
            live.Create(Note("only in the live journal", early));

            var result = Migrate();

            Assert.True(result.Complete);
            Assert.False(result.JournalMoved);
            Assert.NotNull(result.MergedJournal);
            Assert.Equal("quick-notes.migrated-20260926.log", Path.GetFileName(result.MergedJournal));
            Assert.True(File.Exists(result.MergedJournal)); // kept, not consumed
            Assert.False(File.Exists(old.Path));
            Assert.Equal(2, result.NotesMerged);

            var texts = Journal(Current).Load().ToDictionary(n => n.RawText, n => n.Id);
            Assert.Equal(4, texts.Count);
            Assert.Equal(shared, texts["old side, newer edit"]);
            Assert.Equal(staleId, texts["live side, current"]);
            Assert.Contains("only in the old journal", texts.Keys);
            Assert.Contains("only in the live journal", texts.Keys);
            Assert.DoesNotContain("deleted in the old journal", texts.Keys);
        }

        [Fact]
        public void ASecondRunChangesNothing()
        {
            var old = Journal(Legacy);
            old.Create(Note("one", DateTime.UtcNow));
            Journal(Current).Create(Note("two", DateTime.UtcNow));

            Migrate();
            string live = Path.Combine(Current, QuickNotesStore.FileName);
            byte[] after = File.ReadAllBytes(live);
            var second = Migrate();

            Assert.Equal(0, second.NotesMerged);
            Assert.Null(second.MergedJournal);
            Assert.Equal(after, File.ReadAllBytes(live));
        }

        /// <summary>
        /// S0035 QN2-4: a merge that fails once (the live journal held by a scan) is not the end of
        /// those notes - the next pass finds the kept journal unmarked and merges it.
        /// </summary>
        [Fact]
        public void AFailedMergeIsRetriedOnTheNextPass()
        {
            Journal(Legacy).Create(Note("only in the old journal", DateTime.UtcNow));
            var live = Journal(Current);
            live.Create(Note("only in the live journal", DateTime.UtcNow));

            DataFolderMigration.Result first;
            using (new FileStream(live.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                first = Migrate();

            Assert.False(first.Complete);
            Assert.Null(first.MergedJournal);
            string kept = Path.Combine(Current, "quick-notes.migrated-20260926.log");
            Assert.True(File.Exists(kept));
            Assert.False(File.Exists(kept + DataFolderMigration.MergedMarker));

            var second = Migrate();

            Assert.True(second.Complete);
            Assert.Equal(kept, second.MergedJournal);
            Assert.True(File.Exists(kept + DataFolderMigration.MergedMarker));
            Assert.Contains(Journal(Current).Load(), n => n.RawText == "only in the old journal");
        }

        /// <summary>S0035 QN2-7: deletes are applied as recorded - on the live side too.</summary>
        [Fact]
        public void TheMergeDoesNotBringBackANoteTheLiveJournalDeleted()
        {
            Guid id = Guid.NewGuid();
            Journal(Legacy).Create(Note("deleted here since", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), id));
            var live = Journal(Current);
            live.Create(Note("deleted here since", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), id));
            live.Delete(id);
            live.Create(Note("still here", DateTime.UtcNow));

            var result = Migrate();

            Assert.True(result.Complete);
            Assert.Equal(0, result.NotesMerged);
            Assert.Equal("still here", Journal(Current).Load().Single().RawText);
        }

        /// <summary>
        /// S0035 QN2-5: a same-volume move keeps the ACEs a file inherited from the shared folder. The
        /// moved file must carry what its new folder passes down, and nothing of the old one's - here a
        /// "Guests may read" the legacy folder hands to every file in it.
        /// </summary>
        [Fact]
        public void AMovedFileTakesItsNewFoldersAclNotTheSharedOnes()
        {
            var guests = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinGuestsSid, null);
            var folder = Directory.GetAccessControl(Legacy);
            folder.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(guests,
                System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                System.Security.AccessControl.InheritanceFlags.ObjectInherit | System.Security.AccessControl.InheritanceFlags.ContainerInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            Directory.SetAccessControl(Legacy, folder);
            string reports = Path.Combine(Legacy, DataFolderMigration.ReportsFolderName);
            Directory.CreateDirectory(reports);
            string archive = Path.Combine(reports, "CyrFlip-logs-1.zip");
            File.WriteAllText(archive, "logs");
            Assert.True(HasAce(archive, guests), "the setup must give the file the shared folder's ACE");

            var result = Migrate();

            string moved = Path.Combine(Current, DataFolderMigration.ReportsFolderName, "CyrFlip-logs-1.zip");
            Assert.Equal(1, result.ReportsMoved);
            Assert.False(HasAce(moved, guests), "the moved archive still lets Guests read it");
            Assert.Equal("logs", File.ReadAllText(moved)); // and it is still ours to read
        }

        private static bool HasAce(string path, System.Security.Principal.SecurityIdentifier sid)
        {
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in File.GetAccessControl(path)
                         .GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)))
                if (rule.IdentityReference.Equals(sid)) return true;
            return false;
        }

        [Fact]
        public void OwnLogsAreDeletedForeignOnesKeptAndLayoutFilesLeftToTheMirror()
        {
            foreach (string name in DataFolderMigration.DiagnosticFiles)
                File.WriteAllText(Path.Combine(Legacy, name), "diagnostics");
            string theirs = Path.Combine(Legacy, "launcher.log");
            _foreign.Add(theirs);
            File.WriteAllText(Path.Combine(Legacy, LayoutPublisher.CodeFileName), "EN");
            File.WriteAllText(Path.Combine(Legacy, EditorCaretSignal.ClaimFileName), "EN");

            var result = Migrate();

            Assert.Equal(DataFolderMigration.DiagnosticFiles.Length - 1, result.LogsDeleted);
            Assert.Equal(1, result.ForeignSkipped);
            Assert.True(File.Exists(theirs));
            Assert.True(File.Exists(Path.Combine(Legacy, LayoutPublisher.CodeFileName)));
            Assert.True(File.Exists(Path.Combine(Legacy, EditorCaretSignal.ClaimFileName)));
            // Logs are deleted, never carried along: the old copies are exactly what other accounts could read.
            Assert.False(Directory.Exists(Current) && Directory.GetFiles(Current).Any(f => f.EndsWith(".log")));
        }

        [Fact]
        public void OwnReportsAreMovedForeignOnesStay()
        {
            string reports = Path.Combine(Legacy, DataFolderMigration.ReportsFolderName);
            Directory.CreateDirectory(reports);
            string mine = Path.Combine(reports, "CyrFlip-logs-1.zip");
            string theirs = Path.Combine(reports, "CyrFlip-logs-2.zip");
            File.WriteAllText(mine, "mine");
            File.WriteAllText(theirs, "theirs");
            _foreign.Add(theirs);

            var result = Migrate();

            Assert.Equal(1, result.ReportsMoved);
            Assert.Equal("mine", File.ReadAllText(Path.Combine(Current, DataFolderMigration.ReportsFolderName, "CyrFlip-logs-1.zip")));
            Assert.True(File.Exists(theirs));
            Assert.False(File.Exists(mine));
        }

        [Fact]
        public void NoLegacyFolderIsNothingToDo()
        {
            Directory.Delete(Legacy, true);
            var result = Migrate();
            Assert.True(result.Complete);
            Assert.False(Directory.Exists(Current));
        }

        /// <summary>Normally the creator owns the file. An elevated administrator's files are owned by the
        /// Administrators group instead (the default on a CI runner) - and those must count as not ours,
        /// since that group is shared with every other administrator on the machine.</summary>
        [Fact]
        public void OwnershipIsTheCurrentUsersSidAndNothingElse()
        {
            string file = Path.Combine(Legacy, "probe.txt");
            File.WriteAllText(file, "x");
            var owner = File.GetAccessControl(file, System.Security.AccessControl.AccessControlSections.Owner)
                .GetOwner(typeof(System.Security.Principal.SecurityIdentifier));
            using var me = System.Security.Principal.WindowsIdentity.GetCurrent();

            Assert.Equal(owner!.Equals(me.User), DataFolderMigration.IsOwnedByCurrentUser(file));
            Assert.False(DataFolderMigration.IsOwnedByCurrentUser(Path.Combine(Legacy, "absent.txt")));
        }
    }
}
