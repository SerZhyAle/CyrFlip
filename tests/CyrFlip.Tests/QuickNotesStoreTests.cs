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
            // Whole base64 that this cipher opens but that is no record - a later format, or a
            // ciphertext that may open again. A fragment is another matter (QN2-2, below).
            string unreadable = Convert.ToBase64String(Encoding.UTF8.GetBytes("UNREADABLE-RECORD-KEPT-VERBATIM"));
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
            File.AppendAllText(_path, Environment.NewLine + Convert.ToBase64String(Encoding.UTF8.GetBytes("UNREADABLE")));
            QuickNotesStore reopened = Store();
            reopened.Load();

            reopened.DeleteEverything();
            reopened.Compact(new List<QuickNote> { Note("new") });

            Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("UNREADABLE")), File.ReadAllText(_path));
        }

        // ---- Ticket S0035 ----

        /// <summary>
        /// A cipher that knows its own shape: "W:" + base64 + "#". "W:LOST#" is whole but will not
        /// open (a DPAPI key lost to a password reset); anything without the closing "#" is a fragment.
        /// </summary>
        private sealed class ShapedCipher : IQuickNotesCipher, IQuickNotesCipherShape
        {
            public string Protect(string plain) => "W:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain)) + "#";

            public string? Unprotect(string cipher)
            {
                if (!IsWhole(cipher) || !cipher.StartsWith("W:", StringComparison.Ordinal) || cipher == "W:LOST#") return null;
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(2, cipher.Length - 3))); }
                catch { return null; }
            }

            public bool IsWhole(string cipher) => cipher.EndsWith("#", StringComparison.Ordinal);
        }

        /// <summary>
        /// QN2-2: a kill mid-append leaves a fragment that can never be read. Carried verbatim like a
        /// record whose key is lost, it made every start warn "not read: 1" for good. A whole record
        /// that does not open is still carried; the fragment goes with the next compaction - which
        /// the load itself asks for.
        /// </summary>
        [Fact]
        public void AFragmentIsDroppedAtTheNextCompactionWhileAWholeUnreadableRecordIsKept()
        {
            string path = _path;
            var writer = new QuickNotesStore(path, new ShapedCipher());
            writer.Create(Note("readable"));
            File.AppendAllText(path, Environment.NewLine + "W:LOST#" + Environment.NewLine + "W:eyJBY3Rpb24iOiJ1cG");

            var reopened = new QuickNotesStore(path, new ShapedCipher());
            List<QuickNote> notes = reopened.Load();
            Assert.Equal(2, reopened.SkippedRecords);
            Assert.Equal(1, reopened.TornRecords);
            Assert.True(reopened.NeedsCompaction, "a load that met a fragment asks for the compaction that drops it");

            reopened.Compact(notes);

            string[] lines = File.ReadAllLines(path);
            Assert.Contains("W:LOST#", lines);
            Assert.DoesNotContain("W:eyJBY3Rpb24iOiJ1cG", lines);
            var reader = new QuickNotesStore(path, new ShapedCipher());
            Assert.Equal("readable", Assert.Single(reader.Load()).RawText);
            Assert.Equal(1, reader.SkippedRecords);
            Assert.Equal(0, reader.TornRecords);
            Assert.False(reader.NeedsCompaction);
        }

        /// <summary>A cipher that does not know its shape: a line that is not base64 at all is a fragment.</summary>
        [Fact]
        public void WithoutAShapeALineThatIsNotBase64IsAFragment()
        {
            Store().Create(Note("x"));
            File.AppendAllText(_path, Environment.NewLine + "eyJBY3Rpb24iOiJ1cGRhd");

            QuickNotesStore reopened = Store();
            reopened.Load();

            Assert.Equal(1, reopened.TornRecords);
        }

        /// <summary>
        /// QN2-2: one real <c>CryptProtectData</c> blob, captured once (it is never decrypted here, so
        /// the test needs no DPAPI and no account): whole it is whole, and every prefix of it is a
        /// fragment - the structure walk runs out of bytes on the way.
        /// </summary>
        [Fact]
        public void TheDpapiBlobWalkTellsAWholeBlobFromEveryPrefixOfIt()
        {
            byte[] blob = Convert.FromBase64String(
                "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAqjviM3qfSUS2RdX6ycOGTwAAAAACAAAAAAAQZgAAAAEAACAAAABrl2V3a6yL6dIBq/bE8Yd8tL50QPbjo1KdL6cUkUzbhAAAAAAOgAAAAAIAACAAAACDGP5gEH0Hxs0W/mlL1W/uELqFO8oOz033/2cSLgFOvSAAAAD+UC8RSF3t4XS2KIxoUHrmCG/keX2qYEsLhTcN4+//gkAAAADhMYMyDYC6pz3AVR2A9m4+pulDcUCvx/DdwYK0oKHAll04K89ibUlnOQxrpEEQELbmRkC3HdSFkchHEUAHTMA0");

            Assert.True(QuickNotesCipher.IsWholeBlob(blob));
            for (int length = 0; length < blob.Length; length++)
            {
                byte[] prefix = new byte[length];
                Array.Copy(blob, prefix, length);
                Assert.False(QuickNotesCipher.IsWholeBlob(prefix), "a prefix of " + length + " bytes read as whole");
            }
            // As journal lines: the whole one, and a cut one whose base64 happens to stay valid.
            Assert.True(QuickNotesCipher.Dpapi.IsWhole(Convert.ToBase64String(blob)));
            Assert.False(QuickNotesCipher.Dpapi.IsWhole(Convert.ToBase64String(blob).Substring(0, 200)));
            Assert.False(QuickNotesCipher.Dpapi.IsWhole("AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAqjviM3q"));
        }

        /// <summary>QN2-3: "delete every note" also takes the crash-left snapshot and the migration's kept journals.</summary>
        [Fact]
        public void DeletingEverythingTakesTheTempSnapshotAndTheMigratedJournalsToo()
        {
            QuickNotesStore store = Store();
            store.Create(Note("secret"));
            string folder = Path.GetDirectoryName(_path)!;
            string[] others =
            {
                _path + ".tmp",
                Path.Combine(folder, "quick-notes.migrated-20260926.log"),
                Path.Combine(folder, "quick-notes.migrated-20260926.log.bak"),
                Path.Combine(folder, "quick-notes.migrated-20260926.log.merged"),
                Path.Combine(folder, "quick-notes.migrated-20260927-2.log"),
            };
            foreach (string other in others) File.WriteAllText(other, "secret");
            string unrelated = Path.Combine(folder, "quick-notes-diagnostics.log");
            File.WriteAllText(unrelated, "counts");

            store.DeleteEverything();

            foreach (string other in others) Assert.False(File.Exists(other), other + " survived");
            Assert.True(File.Exists(unrelated));
        }

        /// <summary>
        /// QN2-8: a compaction that failed is not tried again on the next save - only after another
        /// CompactAfterOperations records. And a journal whose snapshot alone is past the threshold
        /// (500 notes) is not compacted on every save either.
        /// </summary>
        [Fact]
        public void AFailedCompactionBacksOffAndABigSnapshotIsNotRecompactedOnEverySave()
        {
            QuickNotesStore store = Store();
            QuickNote note = Note("x");
            store.Create(note);
            for (int i = 0; i < QuickNotesStore.CompactAfterOperations; i++) store.Update(note);
            Assert.True(store.NeedsCompaction);

            store.ReplaceFile = (_, _, _) => throw new IOException("held without delete sharing");
            store.Compact(new List<QuickNote> { note });
            Assert.False(store.NeedsCompaction);
            store.Update(note);
            Assert.False(store.NeedsCompaction);
            for (int i = 0; i < QuickNotesStore.CompactAfterOperations; i++) store.Update(note);
            Assert.True(store.NeedsCompaction);

            store.ReplaceFile = File.Replace;
            var many = new List<QuickNote>();
            for (int i = 0; i < QuickNotesStore.CompactAfterOperations + 100; i++) many.Add(Note("n" + i));
            store.Compact(many);
            store.Update(many[0]);
            Assert.False(store.NeedsCompaction);

            QuickNotesStore reopened = Store();
            reopened.Load();
            Assert.False(reopened.NeedsCompaction);
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
