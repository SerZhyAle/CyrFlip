using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The journal: replay, a damaged record, compaction, the backup, and the one property the
    /// whole feature is for - a fragment of code comes back byte for byte, CRLF, LF, tabs and all.
    ///
    /// <para>The cipher is faked rather than real. DPAPI is a per-user machine secret: a test that
    /// used it would pass or fail for reasons that have nothing to do with this code, and would be
    /// unrunnable on a CI runner under a different account. The fake also lets the corrupt-line case
    /// be written as "a line this cipher cannot read", which is exactly what a real corruption is.</para>
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class QuickNotesStoreTests : IDisposable
    {
        private readonly string _root;
        private readonly string _path;

        public QuickNotesStoreTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-notes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, QuickNotesStore.FileName);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        /// <summary>Base64 of the UTF-8 bytes - reversible, readable in a failure message, not DPAPI.</summary>
        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

            public string? Unprotect(string cipher)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch { return null; }
            }
        }

        private QuickNotesStore Store() => new QuickNotesStore(_path, new FakeCipher());

        /// <summary>Records, not lines: every append starts with a line break, so empty lines are normal.</summary>
        private static int Records(string path)
        {
            int count = 0;
            foreach (string line in File.ReadAllLines(path)) if (line.Length > 0) count++;
            return count;
        }

        private static QuickNote Note(string raw, string? title = null, DateTime created = default)
        {
            DateTime when = created == default ? new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc) : created;
            return new QuickNote
            {
                CreatedAtUtc = when,
                UpdatedAtUtc = when,
                Title = title,
                RawText = raw,
            };
        }

        [Fact]
        public void AnAbsentJournalIsAnEmptySetAndNotAnError()
        {
            Assert.Empty(Store().Load());
            Assert.False(File.Exists(_path));
        }

        [Fact]
        public void CreateUpdateAndDeleteReplayInOrder()
        {
            QuickNotesStore store = Store();
            QuickNote kept = Note("kept");
            QuickNote doomed = Note("doomed", created: new DateTime(2026, 8, 11, 9, 5, 0, DateTimeKind.Utc));
            store.Create(kept);
            store.Create(doomed);
            kept.RawText = "kept, edited";
            store.Update(kept);
            store.Delete(doomed.Id);

            List<QuickNote> replayed = Store().Load();

            QuickNote only = Assert.Single(replayed);
            Assert.Equal("kept, edited", only.RawText);
            Assert.Equal(kept.Id, only.Id);
        }

        /// <summary>
        /// The acceptance criterion the feature exists for: what went in comes back out, character
        /// for character - mixed line endings, tabs, trailing whitespace and non-BMP text included.
        /// </summary>
        [Fact]
        public void CodeComesBackByteForByte()
        {
            const string code = "if (x) {\r\n\tlock (_gate) { }\n\t\treturn;   \r\n}\r\n\n// хвост 🙂\t";
            QuickNotesStore store = Store();
            store.Create(Note(code, "snippet"));

            QuickNote replayed = Assert.Single(Store().Load());

            Assert.Equal(code, replayed.RawText);
            Assert.Equal("snippet", replayed.Title);
        }

        [Fact]
        public void TheCreationDateSurvivesTheRoundTripAsUtc()
        {
            var created = new DateTime(2026, 8, 11, 9, 30, 15, DateTimeKind.Utc);
            Store().Create(Note("x", created: created));

            QuickNote replayed = Assert.Single(Store().Load());

            Assert.Equal(created, replayed.CreatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, replayed.CreatedAtUtc.Kind);
        }

        [Fact]
        public void AChecklistKeepsItsTicksAndItsOrder()
        {
            var note = new QuickNote
            {
                Kind = QuickNoteKind.Checklist,
                CreatedAtUtc = new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc),
            };
            note.Items.Add(new QuickNoteItem { Text = "first", IsChecked = true, Position = 0 });
            note.Items.Add(new QuickNoteItem { Text = "second", IsChecked = false, Position = 1 });
            Store().Create(note);

            QuickNote replayed = Assert.Single(Store().Load());

            Assert.Equal(QuickNoteKind.Checklist, replayed.Kind);
            Assert.Equal(new[] { "first", "second" }, replayed.Items.ConvertAll(i => i.Text).ToArray());
            Assert.True(replayed.Items[0].IsChecked);
            Assert.False(replayed.Items[1].IsChecked);
        }

        [Fact]
        public void ItemsComeBackInStoredPositionOrderEvenWhenTheFileHasThemJumbled()
        {
            var note = new QuickNote { Kind = QuickNoteKind.Checklist, CreatedAtUtc = DateTime.UtcNow };
            note.Items.Add(new QuickNoteItem { Text = "second", Position = 1 });
            note.Items.Add(new QuickNoteItem { Text = "first", Position = 0 });
            Store().Create(note);

            QuickNote replayed = Assert.Single(Store().Load());

            Assert.Equal(new[] { "first", "second" }, replayed.Items.ConvertAll(i => i.Text).ToArray());
        }

        /// <summary>
        /// One damaged line must not cost the user everything else - the promise made in the
        /// acceptance criteria. The damage sits <b>between</b> two good records on purpose: a
        /// reader that stopped at the first failure would still pass if it were at the end.
        /// </summary>
        [Fact]
        public void ADamagedLineIsSkippedAndCountedWhileEveryOtherNoteSurvives()
        {
            QuickNotesStore store = Store();
            store.Create(Note("before", created: new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc)));
            File.AppendAllText(_path, Environment.NewLine + "this is not base64 of anything!!!");
            store.Create(Note("after", created: new DateTime(2026, 8, 11, 9, 5, 0, DateTimeKind.Utc)));

            QuickNotesStore reader = Store();
            List<QuickNote> replayed = reader.Load();

            Assert.Equal(2, replayed.Count);
            Assert.Equal(1, reader.SkippedRecords);
            Assert.Contains(replayed, n => n.RawText == "before");
            Assert.Contains(replayed, n => n.RawText == "after");
        }

        [Fact]
        public void ARecordThatDecryptsButIsNotAKnownShapeIsSkippedToo()
        {
            Store().Create(Note("good"));
            // Readable, well-formed JSON - and no record at all. The guard is on the content, not
            // only on the cipher, because a truncated write produces exactly this.
            File.AppendAllText(_path,
                Environment.NewLine + new FakeCipher().Protect("{\"Action\":\"\",\"Id\":\"\"}"));

            QuickNotesStore reader = Store();
            Assert.Single(reader.Load());
            Assert.Equal(1, reader.SkippedRecords);
        }

        // ---- Compaction ----

        [Fact]
        public void CompactionRewritesTheJournalAsOneRecordPerNoteAndKeepsABackup()
        {
            QuickNotesStore store = Store();
            QuickNote note = Note("first version");
            store.Create(note);
            for (int i = 0; i < 5; i++) { note.RawText = "version " + i; store.Update(note); }
            int linesBefore = Records(_path);

            store.Compact(new List<QuickNote> { note });

            Assert.Equal(6, linesBefore);
            Assert.Equal(1, Records(_path));
            Assert.True(File.Exists(store.BackupPath), "the previous journal survives as .bak");
            Assert.Equal(6, Records(store.BackupPath));

            QuickNote replayed = Assert.Single(Store().Load());
            Assert.Equal("version 4", replayed.RawText);
        }

        [Fact]
        public void CompactionWorksWhenThereIsNoJournalYet()
        {
            QuickNotesStore store = Store();
            store.Compact(new List<QuickNote> { Note("only") });

            Assert.Equal("only", Assert.Single(Store().Load()).RawText);
            Assert.False(File.Exists(store.BackupPath));
            Assert.False(File.Exists(_path + ".tmp"), "the temp file is never left behind");
        }

        [Fact]
        public void CompactionIsAskedForOnlyOnceTheJournalHasGrown()
        {
            QuickNotesStore store = Store();
            QuickNote note = Note("x");
            store.Create(note);
            Assert.False(store.NeedsCompaction);

            for (int i = 0; i < QuickNotesStore.CompactAfterOperations; i++) store.Update(note);
            Assert.True(store.NeedsCompaction);

            store.Compact(new List<QuickNote> { note });
            Assert.False(store.NeedsCompaction);
        }

        [Fact]
        public void DeletingEverythingTakesTheBackupWithIt()
        {
            QuickNotesStore store = Store();
            QuickNote note = Note("secret fragment");
            store.Create(note);
            store.Compact(new List<QuickNote> { note });   // makes the .bak
            Assert.True(File.Exists(store.BackupPath));

            store.DeleteEverything();

            // Leaving the .bak would mean the notes the user just destroyed are still on disk.
            Assert.False(File.Exists(_path));
            Assert.False(File.Exists(store.BackupPath));
            Assert.Empty(Store().Load());
        }

        [Fact]
        public void NothingReadableIsWrittenBesideTheEncryptedRecord()
        {
            // The whole record is protected, title included: a title names a customer as often as a
            // body holds a token, so no metadata is left in the clear beside it.
            Store().Create(Note("body text", "customer XYZ"));

            string raw = File.ReadAllText(_path);

            Assert.DoesNotContain("customer XYZ", raw);
            Assert.DoesNotContain("body text", raw);
            Assert.DoesNotContain("\"Action\"", raw);
        }

        // ---- Ticket S0006 ----

        /// <summary>
        /// QN-7: a crash mid-append leaves the file without a final newline. The next record used to
        /// be glued onto that fragment and lost with it.
        /// </summary>
        [Fact]
        public void ARecordWrittenAfterATornTailIsNotLostWithIt()
        {
            QuickNotesStore store = Store();
            store.Create(Note("before the crash", created: new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc)));
            File.AppendAllText(_path, Environment.NewLine + "eyJBY3Rpb24iOiJ1cGRh");   // half a record, no newline

            QuickNotesStore reopened = Store();
            reopened.Load();
            reopened.Create(Note("after the crash", created: new DateTime(2026, 8, 11, 9, 5, 0, DateTimeKind.Utc)));

            QuickNotesStore reader = Store();
            List<QuickNote> replayed = reader.Load();
            Assert.Contains(replayed, n => n.RawText == "after the crash");
            Assert.Contains(replayed, n => n.RawText == "before the crash");
            Assert.Equal(1, reader.SkippedRecords);   // only the fragment itself
        }

        /// <summary>
        /// A journal the previous release wrote ends every record with a line break instead of
        /// starting it with one. It still loads, and records appended to it by this release too.
        /// </summary>
        [Fact]
        public void AJournalInThePreviousLayoutStillLoadsAndExtends()
        {
            QuickNotesStore writer = Store();
            writer.Create(Note("old", created: new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc)));
            // Rewrite the file the old way: record, then CRLF.
            string record = File.ReadAllText(_path).Trim();
            File.WriteAllText(_path, record + Environment.NewLine);

            QuickNotesStore reopened = Store();
            Assert.Equal("old", Assert.Single(reopened.Load()).RawText);
            reopened.Create(Note("new", created: new DateTime(2026, 8, 11, 9, 5, 0, DateTimeKind.Utc)));

            QuickNotesStore reader = Store();
            Assert.Equal(2, reader.Load().Count);
            Assert.Equal(0, reader.SkippedRecords);
        }

        /// <summary>
        /// QN-6: past JavaScriptSerializer's default 2 MB a checklist failed to serialize, the
        /// append swallowed the exception, and the note was gone after a restart.
        /// </summary>
        [Fact]
        public void AChecklistWhoseJsonPassesTwoMegabytesSurvivesARestart()
        {
            var note = new QuickNote { Kind = QuickNoteKind.Checklist, CreatedAtUtc = DateTime.UtcNow };
            for (int i = 0; i < 25000; i++)
                note.Items.Add(new QuickNoteItem { Text = "item " + i, Position = i });

            Assert.True(Store().Create(note));

            QuickNote replayed = Assert.Single(Store().Load());
            Assert.Equal(25000, replayed.Items.Count);
            Assert.Equal("item 24999", replayed.Items[24999].Text);
        }

        /// <summary>
        /// QN-5: a record that cannot be read today (a DPAPI key lost to an admin password reset)
        /// may be readable tomorrow. Two compactions used to destroy it: the first moved it to the
        /// .bak, the second overwrote the .bak.
        /// </summary>
        [Fact]
        public void UnreadableRecordsSurviveTwoCompactionsVerbatim()
        {
            const string unreadable = "UNREADABLE-RECORD-KEPT-VERBATIM";
            QuickNotesStore store = Store();
            QuickNote note = Note("readable");
            store.Create(note);
            File.AppendAllText(_path, Environment.NewLine + unreadable);

            QuickNotesStore reopened = Store();
            List<QuickNote> notes = reopened.Load();
            Assert.Equal(1, reopened.SkippedRecords);
            reopened.Compact(notes);
            reopened.Compact(notes);

            Assert.Contains(unreadable, File.ReadAllLines(_path));
            Assert.Contains(unreadable, File.ReadAllLines(reopened.BackupPath));
            QuickNotesStore reader = Store();
            Assert.Equal("readable", Assert.Single(reader.Load()).RawText);
            Assert.Equal(1, reader.SkippedRecords);
        }

        [Fact]
        public void DeletingEverythingDoesNotCarryUnreadableRecordsIntoTheNextJournal()
        {
            QuickNotesStore store = Store();
            store.Create(Note("x"));
            File.AppendAllText(_path, Environment.NewLine + "UNREADABLE");
            QuickNotesStore reopened = Store();
            reopened.Load();

            reopened.DeleteEverything();
            reopened.Compact(new List<QuickNote> { Note("new") });

            Assert.DoesNotContain("UNREADABLE", File.ReadAllText(_path));
        }

        /// <summary>
        /// QN-5: File.Replace can fail after renaming the journal to .bak and before moving the
        /// replacement in. The .tmp used to be deleted then, leaving no journal at all.
        /// </summary>
        [Fact]
        public void AReplaceThatFailsHalfWayLeavesAJournalThatStillHoldsEveryNote()
        {
            QuickNotesStore store = Store();
            QuickNote note = Note("survives");
            store.Create(note);
            store.ReplaceFile = (source, destination, backup) =>
            {
                File.Move(destination, backup);   // what Windows had done before it gave up
                throw new IOException("ERROR_UNABLE_TO_MOVE_REPLACEMENT_2");
            };

            store.Compact(new List<QuickNote> { note });

            Assert.True(File.Exists(_path), "the journal's name is taken again");
            Assert.False(File.Exists(_path + ".tmp"));
            Assert.Equal("survives", Assert.Single(Store().Load()).RawText);
        }

        /// <summary>QN-5: a journal that is missing while its .bak is not is recovered, and says so.</summary>
        [Fact]
        public void AMissingJournalIsRestoredFromItsBackup()
        {
            QuickNotesStore store = Store();
            QuickNote note = Note("in the backup");
            store.Create(note);
            File.Move(_path, store.BackupPath);

            QuickNotesStore reader = Store();
            List<QuickNote> replayed = reader.Load();

            Assert.Equal("in the backup", Assert.Single(replayed).RawText);
            Assert.True(reader.RecoveredFromBackup);
            // Copied back into place: the next append must extend these notes, not start a journal
            // of its own that the restart after would read instead of the backup.
            Assert.True(File.Exists(_path));
            reader.Create(Note("added after recovery", created: new DateTime(2026, 8, 11, 9, 5, 0, DateTimeKind.Utc)));
            QuickNotesStore again = Store();
            Assert.Equal(2, again.Load().Count);
            Assert.False(again.RecoveredFromBackup);
        }
    }
}
