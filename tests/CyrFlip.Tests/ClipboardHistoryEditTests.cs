using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0089: an edit of a history entry's text, the service half. An id is the hash of its text, so the
    /// edit is the old entry erased the way "x" erases it plus an entry written under the new text - no
    /// new journal action - and a text another entry already holds merges into that entry. A fake cipher
    /// and a temporary folder keep the tests off DPAPI and the user's real history.
    /// </summary>
    public sealed class ClipboardHistoryEditTests
    {
        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

            public string? Unprotect(string cipher) =>
                cipher.StartsWith("B64:", StringComparison.Ordinal)
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(4)))
                    : null;
        }

        [Fact]
        public void An_edit_writes_the_entry_again_under_its_new_text_with_its_date_pin_and_source() => WithService((service, dir) =>
        {
            ClipboardHistoryEntry original = Seed(service, "first draft", DateTime.UtcNow.AddHours(-2), pinned: true, source: "Code");
            Seed(service, "newer", DateTime.UtcNow, pinned: false, source: "");
            DateTime created = original.CreatedAt;

            ClipboardHistoryEntry? edited = service.UpdateText(original, "final\r\ntext");

            Assert.NotNull(edited);
            Assert.Equal(ClipboardHistoryService.Hash("final\r\ntext"), edited!.Uuid);
            Assert.Equal("final\r\ntext", edited.Text);
            Assert.Equal(created, edited.CreatedAt);
            Assert.True(edited.IsPinned);
            Assert.Equal("Code", edited.SourceApp);
            Assert.Equal("final text", edited.Preview);
            Assert.False(edited.IsCurrent);   // the clipboard itself is not touched
            Assert.DoesNotContain(service.Entries, e => e.Uuid == original.Uuid);
            Assert.Equal(2, service.Entries.Count);
            Assert.Same(edited, service.Entries[0]);   // pinned first: the place the entry had
        });

        [Fact]
        public void The_edit_survives_a_restart_and_the_old_text_is_erased_from_the_file() => WithService((service, dir) =>
        {
            ClipboardHistoryEntry original = Seed(service, "secret-token-12345", DateTime.UtcNow.AddMinutes(-5), pinned: false, source: "");
            string oldId = original.Uuid;
            Assert.NotNull(service.UpdateText(original, "trimmed"));
            Assert.Equal(0, service.FlushJournal(TimeSpan.FromSeconds(10)));

            var cipher = new FakeCipher();
            string path = Path.Combine(dir, "clipboard-history.log");
            var replayed = new ClipboardHistoryOrder();
            using (var journal = new ClipboardHistoryJournal(path, cipher))
                Assert.Equal(0, journal.Load(replayed));
            Assert.Equal(new[] { "trimmed" }, replayed.Entries.Select(e => e.Text).ToArray());
            Assert.Equal(ClipboardHistoryService.Hash("trimmed"), replayed.Entries[0].Uuid);

            // "x" semantics: no line of the old entry is left, not even a tombstone that names it.
            foreach (string line in ReadLinesWhileOpen(path).Where(l => l.Trim().Length > 0))
            {
                Match blob = Regex.Match(line, "\"blob\":\"([^\"]+)\"");
                Assert.True(blob.Success, line);
                string plain = cipher.Unprotect(blob.Groups[1].Value) ?? "";
                Assert.DoesNotContain(oldId, plain);
                Assert.DoesNotContain("secret-token-12345", plain);
            }
        });

        [Fact]
        public void An_edit_to_a_text_another_entry_holds_merges_into_that_entry() => WithService((service, dir) =>
        {
            ClipboardHistoryEntry older = Seed(service, "same", DateTime.UtcNow.AddHours(-3), pinned: false, source: "");
            ClipboardHistoryEntry draft = Seed(service, "draft", DateTime.UtcNow.AddHours(-1), pinned: true, source: "");
            DateTime kept = older.CreatedAt;

            ClipboardHistoryEntry? survivor = service.UpdateText(draft, "same");

            Assert.Same(older, survivor);
            Assert.Same(older, service.Entries.Single());
            Assert.Equal(kept, older.CreatedAt);   // its own date and place
            Assert.True(older.IsPinned);           // pinned because either was
            Assert.Equal(0, service.FlushJournal(TimeSpan.FromSeconds(10)));
            var replayed = new ClipboardHistoryOrder();
            using (var journal = new ClipboardHistoryJournal(Path.Combine(dir, "clipboard-history.log"), new FakeCipher()))
                journal.Load(replayed);
            Assert.Equal("same", replayed.Entries.Single().Text);
            Assert.True(replayed.Entries.Single().IsPinned);
        });

        [Theory]
        [InlineData("")]
        [InlineData("  \r\n\t ")]
        [InlineData("keep me")]
        public void An_empty_or_unchanged_text_leaves_the_entry_as_it_was(string text) => WithService((service, dir) =>
        {
            ClipboardHistoryEntry entry = Seed(service, "keep me", DateTime.UtcNow, pinned: false, source: "");
            int changed = 0;
            service.Changed += (_, _) => changed++;

            Assert.Null(service.UpdateText(entry, text));

            Assert.Same(entry, service.Entries.Single());
            Assert.Equal("keep me", entry.Text);
            Assert.Equal(0, changed);
        });

        [Fact]
        public void A_text_over_the_capture_cap_leaves_the_entry_and_says_so() => WithService((service, dir) =>
        {
            ClipboardHistoryEntry entry = Seed(service, "short", DateTime.UtcNow, pinned: false, source: "");
            int tooLarge = 0;
            service.ItemTooLarge += (_, _) => tooLarge++;

            Assert.Null(service.UpdateText(entry, new string('x', ClipboardHistoryService.MaxTextChars + 1)));
            Assert.Equal(1, tooLarge);
            Assert.Same(entry, service.Entries.Single());

            // Exactly the cap is what a capture may hold, so an edit may hold it too.
            Assert.NotNull(service.UpdateText(entry, new string('y', ClipboardHistoryService.MaxTextChars)));
            Assert.Equal(1, tooLarge);
        });

        [Fact]
        public void An_entry_edited_before_the_replay_is_merged_does_not_come_back_with_it()
        {
            string dir = NewDir();
            try
            {
                OnStaThread(() =>
                {
                    using (var first = new ClipboardHistoryService(enabled: false, paused: false, dir, new FakeCipher()))
                    {
                        Seed(first, "old text", DateTime.UtcNow.AddMinutes(-1), pinned: false, source: "");
                        Assert.Equal(0, first.FlushJournal(TimeSpan.FromSeconds(10)));
                    }
                    using var service = new ClipboardHistoryService(enabled: false, paused: false, dir, new FakeCipher());
                    // The pool has read the file - "old text" is in its result - but the result is not merged yet.
                    service.ReplayAsync().Wait();
                    ClipboardHistoryEntry live = Seed(service, "old text", DateTime.UtcNow, pinned: false, source: "");
                    Assert.NotNull(service.UpdateText(live, "new text"));

                    service.CompleteReplay();

                    Assert.Equal(new[] { "new text" }, service.Entries.Select(e => e.Text).ToArray());
                });
            }
            finally { DeleteDir(dir); }
        }

        [Fact]
        public void A_clear_before_the_replay_is_merged_does_not_bring_the_entries_back()
        {
            string dir = NewDir();
            try
            {
                OnStaThread(() =>
                {
                    using (var first = new ClipboardHistoryService(enabled: false, paused: false, dir, new FakeCipher()))
                    {
                        Seed(first, "old text", DateTime.UtcNow.AddMinutes(-1), pinned: false, source: "");
                        Assert.Equal(0, first.FlushJournal(TimeSpan.FromSeconds(10)));
                    }
                    using var service = new ClipboardHistoryService(enabled: false, paused: false, dir, new FakeCipher());
                    // The pool has read the file - "old text" is in its result - and the user clears the history before the merge.
                    service.ReplayAsync().Wait();
                    service.Clear();
                    Seed(service, "new text", DateTime.UtcNow, pinned: false, source: "");

                    service.CompleteReplay();

                    Assert.Equal(new[] { "new text" }, service.Entries.Select(e => e.Text).ToArray());
                });
            }
            finally { DeleteDir(dir); }
        }

        private static ClipboardHistoryEntry Seed(ClipboardHistoryService service, string text, DateTime copiedAtUtc, bool pinned, string source)
        {
            service.Import(new[]
            {
                new ExchangeClipboardItem
                {
                    Uuid = ClipboardHistoryService.Hash(text), Text = text, CopiedAtUtc = copiedAtUtc, Pinned = pinned, SourceApp = source,
                },
            });
            return service.Entries.Single(e => e.Text == text);
        }

        private static void WithService(Action<ClipboardHistoryService, string> body)
        {
            string dir = NewDir();
            try
            {
                OnStaThread(() =>
                {
                    using var service = new ClipboardHistoryService(enabled: false, paused: false, dir, new FakeCipher());
                    body(service, dir);
                });
            }
            finally { DeleteDir(dir); }
        }

        /// <summary>The journal's writer keeps the file open for appending while the service lives, and File.ReadAllLines
        /// asks for a share mode that a handle with write access refuses - so the tests read it the way a second reader can.</summary>
        private static string[] ReadLinesWhileOpen(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        private static string NewDir() => Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));

        private static void DeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (IOException) { /* a writer thread may still hold the file for a moment; the temp folder is the test's own */ }
        }

        private static void OnStaThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("owner-thread failure", failure);
        }
    }
}
