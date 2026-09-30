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
    /// The clipboard history's file (spec S0005): a bad line costs one entry (CH-2), and one ordered
    /// writer means the replay equals what was in memory, <c>Clear</c> included (CH-3). The cipher is
    /// faked for the same reason as in <see cref="QuickNotesStoreTests"/>: DPAPI is a per-user machine
    /// secret with no place in a unit test, and "a payload this cipher cannot read" is exactly what a
    /// DPAPI master-key change looks like.
    /// </summary>
    public sealed class ClipboardHistoryJournalTests : IDisposable
    {
        private readonly string _root;
        private readonly string _path;

        public ClipboardHistoryJournalTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "cyrflip-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, "clipboard-history.log");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        /// <summary>Base64 of the UTF-8 bytes behind a "B64:" prefix; anything without the prefix is unreadable.</summary>
        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));

            public string? Unprotect(string cipher)
            {
                if (!cipher.StartsWith("B64:", StringComparison.Ordinal)) return null;
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(4))); }
                catch { return null; }
            }
        }

        private ClipboardHistoryJournal Journal(Action? beforeCommand = null) =>
            new ClipboardHistoryJournal(_path, new FakeCipher(), beforeCommand);

        private static ClipboardHistoryEntry Entry(string uuid, long ticks, bool pinned = false) =>
            new ClipboardHistoryEntry { Uuid = uuid, Text = "text of " + uuid, CreatedAt = new DateTime(ticks, DateTimeKind.Utc), IsPinned = pinned };

        /// <summary>One line exactly as the history writes it, old or new format alike.</summary>
        private static string Line(string action, string uuid, long ticks, string? payload = null, bool pinned = false)
        {
            string p = payload == null ? "null" : "\"" + payload + "\"";
            return "{\"Action\":\"" + action + "\",\"Uuid\":\"" + uuid + "\",\"CreatedAt\":" + ticks
                + ",\"IsPinned\":" + (pinned ? "true" : "false") + ",\"Payload\":" + p + ",\"SourceApp\":\"app\",\"SourceTitle\":\"title\"}";
        }

        private static string Payload(string text) => new FakeCipher().Protect(text);

        private (ClipboardHistoryOrder order, int skipped) Replay()
        {
            var order = new ClipboardHistoryOrder();
            using var journal = Journal();
            int skipped = journal.Load(order);
            return (order, skipped);
        }

        // ---- CH-2: a bad line costs one entry ----

        [Fact]
        public void A_half_line_between_two_good_ones_costs_only_itself()
        {
            File.WriteAllText(_path,
                Line("add", "A", 100, Payload("alpha")) + "\r\n"
                + "{\"Action\":\"add\",\"Uu" + "\r\n"
                + Line("add", "B", 200, Payload("beta")) + "\r\n");

            var (order, skipped) = Replay();

            Assert.Equal(1, skipped);
            Assert.Equal(new[] { "B", "A" }, order.Entries.Select(e => e.Uuid));
            Assert.Equal("alpha", order.Find("A")!.Text);
        }

        [Fact]
        public void A_payload_that_will_not_decrypt_is_skipped_and_the_rest_load()
        {
            File.WriteAllText(_path,
                Line("add", "A", 100, Payload("alpha")) + "\r\n"
                + Line("add", "X", 150, "not-our-cipher") + "\r\n"
                + Line("add", "B", 200, Payload("beta")) + "\r\n"
                + Line("pin", "A", 100, pinned: true) + "\r\n");

            var (order, skipped) = Replay();

            Assert.Equal(1, skipped);
            Assert.Equal(new[] { "A", "B" }, order.Entries.Select(e => e.Uuid)); // A is pinned, so first
            Assert.Null(order.Find("X"));
        }

        [Fact]
        public void A_record_appended_after_a_torn_tail_still_loads()
        {
            // A crash mid-write: no line break after the fragment.
            File.WriteAllText(_path, Line("add", "A", 100, Payload("alpha")) + "\r\n" + "{\"Action\":\"add\",\"Uuid\":\"T");

            using (var journal = Journal())
            {
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("B", 200)));
            }

            var (order, skipped) = Replay();

            Assert.Equal(1, skipped);
            Assert.Equal(new[] { "B", "A" }, order.Entries.Select(e => e.Uuid));
            Assert.Equal("text of B", order.Find("B")!.Text);
        }

        [Fact]
        public void Skipped_lines_are_never_removed_from_disk()
        {
            string original = Line("add", "A", 100, Payload("alpha")) + "\r\n" + "garbage\r\n";
            File.WriteAllText(_path, original);

            Replay();

            Assert.Equal(original, File.ReadAllText(_path));
        }

        [Fact]
        public void A_file_written_by_an_earlier_release_loads_unchanged()
        {
            File.WriteAllText(_path,
                Line("add", "A", 100, Payload("alpha")) + "\r\n"
                + Line("add", "B", 200, Payload("beta")) + "\r\n"
                + Line("touch", "A", 300) + "\r\n"
                + Line("delete", "B", 200) + "\r\n",
                new UTF8Encoding(true));

            var (order, skipped) = Replay();

            Assert.Equal(0, skipped);
            ClipboardHistoryEntry a = Assert.Single(order.Entries);
            Assert.Equal("A", a.Uuid);
            Assert.Equal(300, a.CreatedAt.Ticks);
            Assert.Equal("app", a.SourceApp);
        }

        // ---- CH-3: one ordered writer ----

        /// <summary>
        /// A thousand random operations, snapshotted on this thread exactly as the service does, with a
        /// writer that yields at random between records. The replay must equal the in-memory order -
        /// the property the old one-pool-item-per-record writer could not promise (a delete could land
        /// before its add).
        /// </summary>
        [Fact]
        public void Random_operations_replay_to_the_in_memory_order()
        {
            var writerRandom = new Random(7);
            var random = new Random(12345);
            var live = new ClipboardHistoryOrder();
            long clock = 1000;
            int next = 0, adds = 0, touches = 0, pins = 0, deletes = 0;

            using (var journal = Journal(() =>
            {
                int roll = writerRandom.Next(4);
                if (roll == 0) Thread.Yield();
                else if (roll == 1) Thread.Sleep(0);
            }))
            {
                for (int i = 0; i < 1000; i++)
                {
                    int op = live.Count == 0 ? 0 : random.Next(10);
                    if (op < 4)
                    {
                        var entry = Entry("E" + next++, clock++);
                        live.Add(entry);
                        journal.Append(ClipboardHistoryJournal.Record.Of("add", entry));
                        adds++;
                    }
                    else
                    {
                        ClipboardHistoryEntry target = live.Entries[random.Next(live.Count)];
                        if (op < 6)
                        {
                            live.Update(target, new DateTime(clock++, DateTimeKind.Utc), target.IsPinned);
                            journal.Append(ClipboardHistoryJournal.Record.Of("touch", target));
                            touches++;
                        }
                        else if (op < 8)
                        {
                            live.Update(target, target.CreatedAt, !target.IsPinned);
                            journal.Append(ClipboardHistoryJournal.Record.Of(target.IsPinned ? "pin" : "unpin", target));
                            pins++;
                        }
                        else
                        {
                            live.Remove(target);
                            journal.Append(ClipboardHistoryJournal.Record.Of("delete", target));
                            deletes++;
                        }
                    }
                }
                Assert.True(journal.Drain(TimeSpan.FromSeconds(30)));
            }

            Assert.True(adds > 0 && touches > 0 && pins > 0 && deletes > 0);
            var (order, skipped) = Replay();
            Assert.Equal(0, skipped);
            Assert.Equal(
                live.Entries.Select(e => e.Uuid + "|" + e.IsPinned + "|" + e.CreatedAt.Ticks),
                order.Entries.Select(e => e.Uuid + "|" + e.IsPinned + "|" + e.CreatedAt.Ticks));
        }

        [Fact]
        public void Clear_is_ordered_with_the_appends_around_it()
        {
            bool? cleared = null;
            using (var journal = Journal(() => Thread.Sleep(5)))
            {
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("A", 100)));
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("B", 200)));
                journal.Clear(ok => cleared = ok);
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("C", 300)));
            }

            Assert.True(cleared);
            string[] lines = File.ReadAllLines(_path).Where(l => l.Length > 0).ToArray();
            Assert.Single(lines);
            var (order, _) = Replay();
            Assert.Equal(new[] { "C" }, order.Entries.Select(e => e.Uuid));
        }

        [Fact]
        public void A_clear_that_cannot_delete_the_file_says_so()
        {
            File.WriteAllText(_path, Line("add", "A", 100, Payload("alpha")) + "\r\n");
            bool? cleared = null;
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read)) // another program holds it
            using (var journal = Journal())
            {
                journal.Clear(ok => cleared = ok);
                Assert.True(journal.Drain(TimeSpan.FromSeconds(5)));
            }

            Assert.False(cleared);
        }

        [Fact]
        public void Dispose_writes_everything_still_queued()
        {
            using var gate = new ManualResetEventSlim(false);
            var journal = Journal(() => gate.Wait());
            for (int i = 0; i < 50; i++)
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("E" + i, 100 + i)));
            Assert.False(journal.Drain(TimeSpan.Zero)); // really queued, not yet written
            gate.Set();
            journal.Dispose();

            var (order, skipped) = Replay();
            Assert.Equal(0, skipped);
            Assert.Equal(50, order.Count);
        }

        // ---- S0031 CH2-1: nothing readable on disk ----

        private static string[] Needles(string text, string app, string title) =>
            new[] { text, app, title, ClipboardHistoryService.Hash(text) };

        private static ClipboardHistoryEntry Copy(string text, long ticks, string app, string title) =>
            new ClipboardHistoryEntry
            {
                Uuid = ClipboardHistoryService.Hash(text),
                Text = text,
                CreatedAt = new DateTime(ticks, DateTimeKind.Utc),
                SourceApp = app,
                SourceTitle = title,
            };

        /// <summary>
        /// A copy, a touch, a pin and a delete: the file holds none of the text, the window title, the
        /// app name or the SHA-256 id - the id reverses a short code in milliseconds.
        /// </summary>
        [Fact]
        public void After_a_copy_a_touch_a_pin_and_a_delete_the_file_holds_nothing_readable()
        {
            ClipboardHistoryEntry kept = Copy("482913", 100, "SmsCodes", "Invoice 4711 - ACME Corp - Outlook");
            ClipboardHistoryEntry deleted = Copy("hunter2-pin", 200, "BankApp", "Transfer to Jane Roe");
            using (var journal = Journal())
            {
                journal.Append(ClipboardHistoryJournal.Record.Of("add", kept));
                journal.Append(ClipboardHistoryJournal.Record.Of("add", deleted));
                kept.CreatedAt = new DateTime(300, DateTimeKind.Utc);
                journal.Append(ClipboardHistoryJournal.Record.Of("touch", kept));
                kept.IsPinned = true;
                journal.Append(ClipboardHistoryJournal.Record.Of("pin", kept));
                journal.Append(ClipboardHistoryJournal.Record.Of("delete", deleted));
            }

            string file = File.ReadAllText(_path);
            foreach (string needle in Needles(kept.Text, kept.SourceApp, kept.SourceTitle)
                         .Concat(Needles(deleted.Text, deleted.SourceApp, deleted.SourceTitle)))
                Assert.DoesNotContain(needle, file, StringComparison.OrdinalIgnoreCase);
            foreach (string action in new[] { "add", "touch", "pin", "delete" })
                Assert.DoesNotContain("\"" + action + "\"", file);

            var (order, skipped) = Replay();
            Assert.Equal(0, skipped);
            ClipboardHistoryEntry back = Assert.Single(order.Entries);
            Assert.Equal(kept.Uuid, back.Uuid);
            Assert.Equal("482913", back.Text);
            Assert.Equal("SmsCodes", back.SourceApp);
            Assert.Equal("Invoice 4711 - ACME Corp - Outlook", back.SourceTitle);
            Assert.Equal(300, back.CreatedAt.Ticks);
            Assert.True(back.IsPinned);
        }

        [Fact]
        public void Every_line_is_the_version_and_a_blob_and_nothing_else()
        {
            using (var journal = Journal())
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("A", 100)));

            string line = File.ReadAllLines(_path).Single(l => l.Length > 0);
            Assert.Matches("^\\{\"v\":2,\"blob\":\"B64:[A-Za-z0-9+/=]+\"\\}$", line);
        }

        /// <summary>A v1 file as v26.9.16.2132 wrote it, with v2 records appended by this release, replays as one history.</summary>
        [Fact]
        public void A_v1_journal_with_v2_records_appended_replays_as_one_history()
        {
            File.WriteAllText(_path,
                Line("add", "A", 100, Payload("alpha")) + "\r\n"
                + Line("add", "B", 200, Payload("beta")) + "\r\n"
                + Line("pin", "B", 200, pinned: true) + "\r\n");

            using (var journal = Journal())
            {
                journal.Append(ClipboardHistoryJournal.Record.Of("touch", Entry("A", 500)));
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("C", 400)));
            }

            var (order, skipped) = Replay();
            Assert.Equal(0, skipped);
            Assert.Equal(new[] { "B", "A", "C" }, order.Entries.Select(e => e.Uuid));
            Assert.Equal(500, order.Find("A")!.CreatedAt.Ticks);
            Assert.Equal("alpha", order.Find("A")!.Text);
        }

        [Fact]
        public void A_line_of_an_unknown_version_is_skipped_and_kept()
        {
            File.WriteAllText(_path, "{\"v\":3,\"blob\":\"B64:e30=\"}\r\n" + Line("add", "A", 100, Payload("alpha")) + "\r\n");

            var (order, skipped) = Replay();

            Assert.Equal(1, skipped);
            Assert.Equal(new[] { "A" }, order.Entries.Select(e => e.Uuid));
        }

        // ---- S0031 CH2-5: a delete erases ----

        [Fact]
        public void A_delete_erases_every_line_of_the_entry_and_keeps_the_rest()
        {
            File.WriteAllText(_path,
                Line("add", "OLD", 50, Payload("legacy text")) + "\r\n"
                + "garbage that will not read\r\n");
            using (var journal = Journal())
            {
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("A", 100)));
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("B", 200)));
                journal.Append(ClipboardHistoryJournal.Record.Of("touch", Entry("A", 300)));
                journal.Append(ClipboardHistoryJournal.Record.Of("delete", Entry("A", 300)));
                journal.Append(ClipboardHistoryJournal.Record.Of("delete", Entry("OLD", 50)));
                Assert.True(journal.Drain(TimeSpan.FromSeconds(10)));
                Assert.Equal(0, journal.WriteFailures);
            }

            string[] lines = File.ReadAllLines(_path).Where(l => l.Length > 0).ToArray();
            Assert.Equal(2, lines.Length);                              // B's add and the unreadable line
            Assert.Contains("garbage that will not read", lines);        // carried through verbatim
            Assert.DoesNotContain(lines, l => l.Contains("OLD"));        // a v1 line of a deleted entry goes too
            Assert.False(File.Exists(_path + ".purge.tmp"));
            var (order, skipped) = Replay();
            Assert.Equal(1, skipped);
            Assert.Equal(new[] { "B" }, order.Entries.Select(e => e.Uuid));
        }

        [Fact]
        public void A_delete_whose_rewrite_fails_appends_a_tombstone_instead()
        {
            using (var journal = Journal())
            {
                journal.ReplaceFile = (_, _) => throw new IOException("held by another program");
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("A", 100)));
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("B", 200)));
                journal.Append(ClipboardHistoryJournal.Record.Of("delete", Entry("A", 100)));
            }

            Assert.Equal(3, File.ReadAllLines(_path).Count(l => l.Length > 0));
            Assert.False(File.Exists(_path + ".purge.tmp"));
            var (order, _) = Replay();
            Assert.Equal(new[] { "B" }, order.Entries.Select(e => e.Uuid));
        }

        // ---- S0031 CH2-4, CH2-6: losses are counted ----

        private sealed class ThrowingCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => throw new System.Security.Cryptography.CryptographicException("no key");
            public string? Unprotect(string cipher) => null;
        }

        [Fact]
        public void A_failed_write_is_counted_and_reported_once()
        {
            int raised = 0;
            using var journal = new ClipboardHistoryJournal(_path, new ThrowingCipher());
            journal.WriteFailed += () => Interlocked.Increment(ref raised);
            journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("A", 100)));
            journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("B", 200)));
            Assert.True(journal.Drain(TimeSpan.FromSeconds(5)));

            Assert.Equal(2, journal.WriteFailures);
            Assert.Equal(1, raised);
        }

        [Fact]
        public void Dispose_leaves_the_count_it_abandoned_in_pending()
        {
            using var gate = new ManualResetEventSlim(false);
            var journal = Journal(() => gate.Wait());
            for (int i = 0; i < 3; i++)
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("E" + i, 100 + i)));

            journal.Dispose(); // waits DisposeDrain, then gives up

            Assert.Equal(3, journal.Pending);
            gate.Set();
        }

        [Fact]
        public void Every_record_starts_on_a_line_of_its_own()
        {
            using (var journal = Journal())
                journal.Append(ClipboardHistoryJournal.Record.Of("add", Entry("A", 100)));

            Assert.StartsWith("\n{", File.ReadAllText(_path));
        }
    }
}
