using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The open exchange file, "CyrFlip Exchange Text v1" (ticket S0023). What these tests hold the
    /// format to is the spec's list in section 9: every payload comes back byte for byte, however much
    /// Markdown or how many backticks it carries; a file converted to CRLF still imports; a second
    /// import adds nothing; a note is replaced only by a newer version of itself; a clipboard text
    /// that does not hash to its id is refused; a broken block costs only itself; and the file is
    /// plain text a person can read - never base64, never encrypted.
    /// </summary>
    [Collection(DiagnosticLogCollection.Name)]
    public class CyrFlipExchangeTests : IDisposable
    {
        private static readonly DateTime Created = new DateTime(2026, 8, 9, 10, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime Updated = new DateTime(2026, 8, 11, 14, 18, 3, DateTimeKind.Utc);
        private static readonly DateTime Now = new DateTime(2026, 8, 11, 14, 23, 11, DateTimeKind.Utc);

        /// <summary>Everything that tends to break a text format, in one payload.</summary>
        private const string Hostile = "Здравствуйте! 👋\r\n\tindented\n\n```csharp\nvar x = 1;\n```\n````\ntrailing spaces   \n";

        private readonly string _root;

        public CyrFlipExchangeTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-exchange-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
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

        private static QuickNote TextNote(string text, string? title = "Ответ клиенту") => new QuickNote
        {
            Title = title, Kind = QuickNoteKind.Text, RawText = text, CreatedAtUtc = Created, UpdatedAtUtc = Updated,
        };

        private static QuickNote ListNote() => new QuickNote
        {
            Title = null, Kind = QuickNoteKind.Checklist, CreatedAtUtc = Created, UpdatedAtUtc = Updated,
            Items = new List<QuickNoteItem>
            {
                new QuickNoteItem { Text = "молоко", IsChecked = true, Position = 0 },
                new QuickNoteItem { Text = "", Position = 1 },
                new QuickNoteItem { Text = "- [x] not a tick, a literal", Position = 2 },
            },
        };

        private static ClipboardHistoryEntry Entry(string text, bool pinned = false, DateTime? at = null) => new ClipboardHistoryEntry
        {
            Uuid = CyrFlipExchange.HistoryId(text), Text = text, CreatedAt = at ?? Updated, IsPinned = pinned,
            SourceApp = "notepad", SourceTitle = "Черновик\r\n- Блокнот",
        };

        // ---- The format ----

        [Theory]
        [InlineData("")]
        [InlineData("one line")]
        [InlineData("ends in a newline\n")]
        [InlineData("\n")]
        [InlineData("crlf\r\nlines\r\n")]
        [InlineData("lone\rcarriage return")]
        [InlineData(Hostile)]
        public void ANoteComesBackByteForByte(string text)
        {
            string file = CyrFlipExchangeWriter.ToText(Now, new[] { TextNote(text) }, null);
            ExchangeReadResult read = CyrFlipExchangeReader.Parse(file);

            Assert.Null(read.Fatal);
            Assert.Empty(read.Problems);
            ExchangeNote note = Assert.Single(read.Notes);
            Assert.Equal(text, note.RawText);
            Assert.Equal("Ответ клиенту", note.Title);
            Assert.Equal(Created, note.CreatedAtUtc);
            Assert.Equal(Updated, note.UpdatedAtUtc);
        }

        [Fact]
        public void AChecklistKeepsItsTicksAndItsEmptyItems()
        {
            QuickNote original = ListNote();
            ExchangeNote note = Assert.Single(CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, new[] { original }, null)).Notes);

            Assert.Equal(QuickNoteKind.Checklist, note.Kind);
            Assert.Null(note.Title);
            Assert.Equal(original.Items.Select(i => (i.Text, i.IsChecked)), note.Items.Select(i => (i.Text, i.IsChecked)));
        }

        [Fact]
        public void AnEmptyChecklistStaysEmpty()
        {
            var empty = new QuickNote { Kind = QuickNoteKind.Checklist, Title = "t", CreatedAtUtc = Created, UpdatedAtUtc = Updated };
            ExchangeNote note = Assert.Single(CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, new[] { empty }, null)).Notes);
            Assert.Empty(note.Items);
        }

        [Theory]
        [InlineData("plain")]
        [InlineData(Hostile)]
        [InlineData("crlf\r\ntext\r\n")]
        public void AClipboardEntryComesBackWithItsIdAndMetadata(string text)
        {
            ClipboardHistoryEntry entry = Entry(text, pinned: true);
            ExchangeClipboardItem item = Assert.Single(CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, null, new[] { entry })).Clipboard);

            Assert.Equal(text, item.Text);
            Assert.Equal(entry.Uuid, item.Uuid);
            Assert.True(item.Pinned);
            Assert.Equal(Updated, item.CopiedAtUtc);
            Assert.Equal("notepad", item.SourceApp);
            // A line break in a metadata value becomes a space: a field is one line by definition.
            Assert.Equal("Черновик  - Блокнот", item.SourceTitle);
        }

        [Fact]
        public void TheFileIsPlainReadableText()
        {
            string secret = "пароль от Wi-Fi: hunter2";
            string file = CyrFlipExchangeWriter.ToText(Now, new[] { TextNote(secret) }, new[] { Entry(secret + " (copy)") });

            Assert.StartsWith(CyrFlipExchange.Marker + "\n", file, StringComparison.Ordinal);
            // Verbatim, not base64 and not encrypted: a person opening it on a phone reads it.
            Assert.Contains("\n" + secret + "\n", file);
            Assert.Contains("\n" + secret + " (copy)\n", file);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), file);
            Assert.Contains("Title: Ответ клиенту\n", file);
            Assert.Contains("Includes: quick-notes, clipboard-history\n", file);
            // The writer's own framing is LF; a CR appears only where a payload carried one.
            Assert.DoesNotContain("\r", file);
        }

        [Fact]
        public void TheFileOnDiskIsUtf8WithoutABom()
        {
            string path = Path.Combine(_root, "CyrFlip-2026-08-11.txt");
            CyrFlipExchangeWriter.WriteFile(path, CyrFlipExchangeWriter.ToText(Now, new[] { TextNote("ё") }, null));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Equal("ё", Assert.Single(CyrFlipExchangeReader.ReadFile(path).Notes).RawText);
        }

        [Fact]
        public void AFileSavedWithABomStillReads()
        {
            string path = Path.Combine(_root, "notepad.txt");
            File.WriteAllText(path, CyrFlipExchangeWriter.ToText(Now, new[] { TextNote("x") }, null), new UTF8Encoding(true));
            Assert.Single(CyrFlipExchangeReader.ReadFile(path).Notes);
        }

        [Fact]
        public void TheFenceIsLongerThanAnyRunInTheText()
        {
            Assert.Equal("```", CyrFlipExchangeWriter.Fence("no backticks"));
            Assert.Equal("```", CyrFlipExchangeWriter.Fence("one ` two ``"));
            Assert.Equal("````", CyrFlipExchangeWriter.Fence("```"));
            Assert.Equal("``````", CyrFlipExchangeWriter.Fence("a ````` b"));
        }

        [Fact]
        public void DatesAreTruncatedToTheSecondNeverRoundedUp()
        {
            var at = new DateTime(2026, 8, 11, 14, 18, 3, 999, DateTimeKind.Utc);
            Assert.Equal("2026-08-11T14:18:03Z", CyrFlipExchangeWriter.Date(at));
        }

        [Fact]
        public void AFileConvertedToCrlfStillImports()
        {
            // An editor or a mail client turned every LF into CRLF - the payloads' included.
            string lf = CyrFlipExchangeWriter.ToText(Now, new[] { TextNote("a\nb\n") },
                new[] { Entry("lf only\ntext"), Entry("crlf\r\ntext") });
            string crlf = lf.Replace("\r\n", "\n").Replace("\n", "\r\n");

            ExchangeReadResult read = CyrFlipExchangeReader.Parse(crlf);

            Assert.Empty(read.Problems);
            Assert.Equal("a\nb\n", Assert.Single(read.Notes).RawText);
            // The hash decides which ending each clipboard text originally had.
            Assert.Equal(new[] { "lf only\ntext", "crlf\r\ntext" }, read.Clipboard.Select(c => c.Text));
        }

        // ---- What is refused ----

        [Fact]
        public void AFileWithoutTheMarkerIsRefusedAsAWhole()
        {
            Assert.Equal(ExchangeProblemKind.NoMarker, CyrFlipExchangeReader.Parse("hello\n## Note\n").Fatal);
            Assert.Equal(ExchangeProblemKind.NoMarker, CyrFlipExchangeReader.Parse("").Fatal);
        }

        [Fact]
        public void ALaterVersionIsNamedAsSuch()
        {
            Assert.Equal(ExchangeProblemKind.UnknownVersion, CyrFlipExchangeReader.Parse("\n\n# CyrFlip Exchange Text v2\n").Fatal);
        }

        [Fact]
        public void AFileOverTheCapIsNotRead()
        {
            string path = Path.Combine(_root, "big.txt");
            using (var stream = File.Create(path)) stream.SetLength(CyrFlipExchange.MaxFileBytes + 1);
            Assert.Equal(ExchangeProblemKind.FileTooLarge, CyrFlipExchangeReader.ReadFile(path).Fatal);
        }

        [Fact]
        public void AnEditedClipboardTextIsRefusedByItsHash()
        {
            string file = CyrFlipExchangeWriter.ToText(Now, null, new[] { Entry("original text") });
            ExchangeReadResult read = CyrFlipExchangeReader.Parse(file.Replace("original text", "edited text"));

            Assert.Empty(read.Clipboard);
            Assert.Equal(ExchangeProblemKind.HashMismatch, Assert.Single(read.Problems).Kind);
        }

        [Fact]
        public void ABrokenBlockDoesNotStopTheNextOne()
        {
            string file = CyrFlipExchange.Marker + "\n"
                + "free text a person added\n"
                + "## Note\nId: not-a-guid\nCreated-At: 2026-08-09T10:30:00Z\nUpdated-At: 2026-08-09T10:30:00Z\n\n```text\nbad\n```\n"
                + "## Note\nId: 7c2f0d04-0c09-4e11-af08-d61bb0748ab9\nTitle: no body\n"
                + "## Clipboard\nId: sha256:00\nCopied-At: 2026-08-11T14:21:45Z\n\n```text\nx\n```\n"
                + "# Some section of my own\n"
                + "## Note\nId: 7c2f0d04-0c09-4e11-af08-d61bb0748ab9\nKind: text\nCreated-At: 2026-08-09T10:30:00Z\nUpdated-At: 2026-08-11T14:18:03Z\nUnknown-Field: ignored\n\n```text\ngood\n```\n";

            ExchangeReadResult read = CyrFlipExchangeReader.Parse(file);

            Assert.Equal("good", Assert.Single(read.Notes).RawText);
            Assert.Equal(new[] { ExchangeProblemKind.BadField, ExchangeProblemKind.NoBody, ExchangeProblemKind.BadField },
                read.Problems.Select(p => p.Kind));
            Assert.Equal(new[] { "Id", "Id" }, read.Problems.Where(p => p.Kind == ExchangeProblemKind.BadField).Select(p => p.Detail));
        }

        [Fact]
        public void AnUnclosedFenceIsReported()
        {
            string file = CyrFlipExchangeWriter.ToText(Now, new[] { TextNote("first") }, null)
                + "\n## Note\nKind: text\n\n```text\nnever closed\n";
            ExchangeReadResult read = CyrFlipExchangeReader.Parse(file);

            Assert.Single(read.Notes);
            Assert.Equal(ExchangeProblemKind.UnclosedFence, Assert.Single(read.Problems).Kind);
        }

        [Fact]
        public void OversizedPayloadsAndMetadataAreRefused()
        {
            string big = new string('я', QuickNote.MaxBytes / 2 + 1);   // two UTF-8 bytes each
            string tooLong = new string('t', CyrFlipExchange.MaxMetadataChars + 1);
            string file = CyrFlipExchange.Marker + "\n"
                + "## Note\nKind: text\n\n```text\n" + big + "\n```\n"
                + "## Note\nTitle: " + tooLong + "\n\n```text\nx\n```\n";

            ExchangeReadResult read = CyrFlipExchangeReader.Parse(file);

            Assert.Empty(read.Notes);
            Assert.Equal(new[] { ExchangeProblemKind.TooLarge, ExchangeProblemKind.MetadataTooLong }, read.Problems.Select(p => p.Kind));
        }

        [Fact]
        public void AHandWrittenNoteHasNoIdAndATolerantChecklist()
        {
            string file = CyrFlipExchange.Marker + "\n\n## Note\nTitle: Купить\nKind: checklist\n\n```\n- [ ] хлеб\n- [X] сыр\nбез галочки\n```\n";
            ExchangeNote note = Assert.Single(CyrFlipExchangeReader.Parse(file).Notes);

            Assert.Null(note.Id);
            Assert.Equal(new[] { ("хлеб", false), ("сыр", true), ("без галочки", false) }, note.Items.Select(i => (i.Text, i.IsChecked)));
        }

        // ---- Selection ----

        [Fact]
        public void TheHistorySelectionFollowsTheDialog()
        {
            var entries = new List<ClipboardHistoryEntry> { Entry("p1", pinned: true), Entry("p2", pinned: true) };
            for (int i = 0; i < 120; i++) entries.Add(Entry("e" + i, at: Now.AddDays(-i * 0.1)));

            Assert.Equal(2, CyrFlipExchangeWriter.SelectHistory(entries, true, false, ExchangeHistoryScope.All, Now).Count);
            Assert.Equal(52, CyrFlipExchangeWriter.SelectHistory(entries, true, true, ExchangeHistoryScope.Last50, Now).Count);
            Assert.Equal(100, CyrFlipExchangeWriter.SelectHistory(entries, false, true, ExchangeHistoryScope.Last100, Now).Count);
            Assert.Equal(71, CyrFlipExchangeWriter.SelectHistory(entries, false, true, ExchangeHistoryScope.Last7Days, Now).Count);
            Assert.Equal(122, CyrFlipExchangeWriter.SelectHistory(entries, true, true, ExchangeHistoryScope.All, Now).Count);
            Assert.Empty(CyrFlipExchangeWriter.SelectHistory(entries, false, false, ExchangeHistoryScope.All, Now));
        }

        // ---- Merge ----

        [Fact]
        public void ANoteIsReplacedOnlyByANewerVersionOfItself()
        {
            QuickNote local = TextNote("local");
            QuickNote newer = local.Clone(); newer.RawText = "newer"; newer.UpdatedAtUtc = Updated.AddMinutes(1);
            QuickNote tie = local.Clone(); tie.RawText = "same time";
            QuickNote older = local.Clone(); older.RawText = "older"; older.UpdatedAtUtc = Updated.AddMinutes(-1);

            foreach ((QuickNote incoming, bool replaces) in new[] { (newer, true), (tie, false), (older, false) })
            {
                var report = new ExchangeMergeReport();
                List<ExchangeNote> read = CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, new[] { incoming }, null)).Notes;
                List<CyrFlipExchangeMerge.NoteAction> actions = CyrFlipExchangeMerge.PlanNotes(new[] { local }, read, false, Now, report);

                Assert.Equal(replaces ? 1 : 0, actions.Count);
                Assert.Equal(replaces ? 1 : 0, report.NotesUpdated);
                Assert.Equal(replaces ? 0 : 1, report.NotesSkipped);
                if (replaces)
                {
                    Assert.Same(local, actions[0].Existing);
                    Assert.Equal("newer", actions[0].Note.RawText);
                    Assert.Equal(Created, actions[0].Note.CreatedAtUtc);
                }
            }
        }

        [Fact]
        public void TwoNotesWithTheSameTextButDifferentIdsAreBothKept()
        {
            QuickNote local = TextNote("same");
            QuickNote other = TextNote("same");
            var report = new ExchangeMergeReport();
            List<ExchangeNote> read = CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, new[] { other }, null)).Notes;
            Assert.Single(CyrFlipExchangeMerge.PlanNotes(new[] { local }, read, false, Now, report));
            Assert.Equal(1, report.NotesAdded);
        }

        [Fact]
        public void ANoteWithoutAnIdIsTakenOnlyWhenTheUserAgreed()
        {
            string file = CyrFlipExchange.Marker + "\n## Note\n\n```text\nwritten on a phone\n```\n";
            List<ExchangeNote> read = CyrFlipExchangeReader.Parse(file).Notes;

            var refused = new ExchangeMergeReport();
            Assert.Empty(CyrFlipExchangeMerge.PlanNotes(new QuickNote[0], read, includeNew: false, Now, refused));

            var agreed = new ExchangeMergeReport();
            CyrFlipExchangeMerge.NoteAction action = Assert.Single(CyrFlipExchangeMerge.PlanNotes(new QuickNote[0], read, includeNew: true, Now, agreed));
            Assert.Equal(1, agreed.NotesNew);
            Assert.NotEqual(Guid.Empty, action.Note.Id);
            Assert.Equal(Now, action.Note.CreatedAtUtc);
        }

        [Fact]
        public void AnImportedPinNeverUnpinsAndANewerCopyRaises()
        {
            ClipboardHistoryEntry local = Entry("text", pinned: true, at: Updated);
            var byId = new Dictionary<string, ClipboardHistoryEntry> { [local.Uuid] = local };
            ClipboardHistoryEntry? Find(string id) => byId.TryGetValue(id, out var e) ? e : null;

            var sameUnpinned = new ExchangeMergeReport();
            Assert.Empty(CyrFlipExchangeMerge.PlanClipboard(Find, Read(Entry("text", pinned: false, at: Updated)), sameUnpinned));
            Assert.Equal(1, sameUnpinned.ClipboardExisting);

            var newer = new ExchangeMergeReport();
            CyrFlipExchangeMerge.ClipboardAction raise = Assert.Single(CyrFlipExchangeMerge.PlanClipboard(Find, Read(Entry("text", at: Now)), newer));
            Assert.Equal(Now, raise.CopiedAtUtc);
            Assert.True(raise.Pinned);

            var added = new ExchangeMergeReport();
            CyrFlipExchangeMerge.ClipboardAction add = Assert.Single(CyrFlipExchangeMerge.PlanClipboard(Find, Read(Entry("other", pinned: true)), added));
            Assert.Null(add.Existing);
            Assert.Equal(1, added.ClipboardAdded);

            static List<ExchangeClipboardItem> Read(ClipboardHistoryEntry entry)
                => CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, null, new[] { entry })).Clipboard;
        }

        // ---- Through the real services ----

        [Fact]
        public void ImportingTheSameFileTwiceAddsNothingTheSecondTime()
        {
            string file = CyrFlipExchangeWriter.ToText(Now, new[] { TextNote(Hostile), ListNote() },
                new[] { Entry("b", pinned: true), Entry("a") });   // the history's own order: pinned first
            ExchangeReadResult read = CyrFlipExchangeReader.Parse(file);

            using (QuickNotesService notes = Notes("notes.log"))
            using (ClipboardHistoryService history = History("history"))
            {
                ExchangeMergeReport first = notes.Import(read.Notes, includeNew: false);
                ExchangeMergeReport firstHistory = history.Import(read.Clipboard);
                Assert.Equal(2, first.NotesAdded);
                Assert.Equal(2, firstHistory.ClipboardAdded);

                ExchangeMergeReport second = notes.PlanImport(read.Notes, includeNew: false);
                ExchangeMergeReport secondHistory = history.PlanImport(read.Clipboard);
                Assert.Equal(0, second.NotesAdded + second.NotesUpdated);
                Assert.Equal(2, second.NotesSkipped);
                Assert.Equal(0, secondHistory.ClipboardAdded + secondHistory.ClipboardRaised);
                Assert.Equal(2, notes.Notes.Count);
                Assert.Equal(2, history.Entries.Count);
                Assert.True(history.WaitForPendingWrites(TimeSpan.FromSeconds(5)));
            }

            // And both survive a restart, byte for byte, pin included.
            using (QuickNotesService notes = Notes("notes.log"))
            using (ClipboardHistoryService history = History("history"))
            {
                Assert.Contains(notes.Notes, n => n.RawText == Hostile && n.CreatedAtUtc == Created);
                Assert.Contains(history.Entries, e => e.Text == "b" && e.IsPinned);
                // An export of what was imported is the same file again.
                Assert.Equal(file, CyrFlipExchangeWriter.ToText(Now,
                    notes.Notes.OrderBy(n => n.Kind).ToList(), history.Entries));
            }
        }

        [Fact]
        public void ANewerImportUpdatesTheNoteInPlaceAndRaisesImported()
        {
            using QuickNotesService notes = Notes("notes.log");
            QuickNote original = TextNote("v1");
            notes.Import(CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, new[] { original }, null)).Notes, false);
            QuickNote held = notes.Notes.Single();
            int imported = 0;
            notes.Imported += (_, _) => imported++;

            QuickNote newer = original.Clone(); newer.RawText = "v2"; newer.UpdatedAtUtc = Now;
            ExchangeMergeReport report = notes.Import(CyrFlipExchangeReader.Parse(CyrFlipExchangeWriter.ToText(Now, new[] { newer }, null)).Notes, false);

            Assert.Equal(1, report.NotesUpdated);
            Assert.Equal(1, imported);
            Assert.Same(held, notes.Notes.Single());   // the object a window holds is the one changed
            Assert.Equal("v2", held.RawText);
            Assert.Equal(Created, held.CreatedAtUtc);
        }

        [Fact]
        public void TheAtomicWriteReplacesAnExistingFileAndLeavesNoTemp()
        {
            string path = Path.Combine(_root, "out.txt");
            File.WriteAllText(path, "old");
            CyrFlipExchangeWriter.WriteFile(path, CyrFlipExchange.Marker + "\n");
            Assert.Equal(CyrFlipExchange.Marker + "\n", File.ReadAllText(path));
            Assert.Equal(new[] { "out.txt" }, Directory.GetFiles(_root).Select(Path.GetFileName));
        }

        private QuickNotesService Notes(string name)
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new QuickNotesService(new QuickNotesStore(Path.Combine(_root, name), new FakeCipher())); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private ClipboardHistoryService History(string name)
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { return new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(_root, name), new FakeCipher()); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
}
