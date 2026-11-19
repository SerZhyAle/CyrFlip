using System;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// When the notes sync may open a connection at all (ticket S0105, done criterion "with quick notes off or the
    /// exchange module off, no notes byte is ever sent"): the context starts a pass only while
    /// <see cref="NotesSyncGate.Live"/> holds, so every switch that is off - and a missing or ambiguous entry - keeps it
    /// false. The status line says why in words, never a code.
    /// </summary>
    public class NotesSyncGateTests
    {
        private static AppConfig AllOn(params ExchangeAccountEntry[] entries)
        {
            var config = new AppConfig { EnableQuickNotes = true, EnableExchangeClipboard = true, QuickNotesSync = true };
            config.ExchangeClipboardAccounts.AddRange(entries);
            return config;
        }

        private static ExchangeAccountEntry Member(string id) => new ExchangeAccountEntry { Id = id };
        private static ExchangeAccountEntry Guest(string id) => new ExchangeAccountEntry { Id = id, AccountKind = ExchangeAccountEntry.KindGuest };

        [Fact]
        public void TheSyncIsLiveOnlyWithEverySwitchOnAndOneEntry()
        {
            Assert.True(NotesSyncGate.Live(AllOn(Member("a"))));

            AppConfig off = AllOn(Member("a")); off.EnableQuickNotes = false;
            Assert.False(NotesSyncGate.Live(off));
            off = AllOn(Member("a")); off.EnableExchangeClipboard = false;
            Assert.False(NotesSyncGate.Live(off));
            off = AllOn(Member("a")); off.QuickNotesSync = false;
            Assert.False(NotesSyncGate.Live(off));
            Assert.False(NotesSyncGate.Live(AllOn()));
        }

        [Fact]
        public void TheOptionIsOffOnAFreshProfileAndNeverInTheSettingsFile()
        {
            Assert.False(new AppConfig().QuickNotesSync);
            Assert.Equal("", new AppConfig().QuickNotesSyncEntry);
            Assert.True(SettingsFileInventory.Excluded.ContainsKey("QuickNotesSync"));
            Assert.True(SettingsFileInventory.Excluded.ContainsKey("QuickNotesSyncEntry"));
        }

        [Fact]
        public void OneEntryOnlyAndNeverAGuest()
        {
            Assert.False(NotesSyncGate.Live(AllOn(Guest("g"))));
            Assert.Equal("a", NotesSyncGate.Entry(AllOn(Guest("g"), Member("a")))!.Id);

            AppConfig two = AllOn(Member("a"), Member("b"));
            Assert.Null(NotesSyncGate.Entry(two));          // two member entries and no pick: none (item FK)
            Assert.False(NotesSyncGate.Live(two));
            two.QuickNotesSyncEntry = "b";
            Assert.Equal("b", NotesSyncGate.Entry(two)!.Id);
            two.QuickNotesSyncEntry = "gone";
            Assert.Null(NotesSyncGate.Entry(two));
        }

        [Fact]
        public void TheStatusLineSaysWhyInWords()
        {
            Func<string, string> en = s => s;
            AppConfig config = AllOn(Member("a"));
            config.QuickNotesSync = false;
            Assert.Null(NotesSyncGate.StatusText(config, false, null, default, en));

            config = AllOn(Member("a"));
            config.EnableExchangeClipboard = false;
            Assert.Contains("turned off", NotesSyncGate.StatusText(config, false, null, default, en));
            Assert.Contains("no account", NotesSyncGate.StatusText(AllOn(), false, null, default, en));
            Assert.Equal("Choose the account for the notes.", NotesSyncGate.StatusText(AllOn(Member("a"), Member("b")), false, null, default, en));
            Assert.Equal("Syncing the notes..", NotesSyncGate.StatusText(AllOn(Member("a")), true, null, default, en));

            var conflict = new ExchangeNotesSyncReport { Conflicts = 1, TooLarge = 2 };
            string done = NotesSyncGate.StatusText(AllOn(Member("a")), false, conflict, DateTime.UtcNow, en)!;
            Assert.StartsWith("Synced at ", done);
            Assert.Contains("Both versions are kept.", done);
            Assert.Contains("Notes too large to sync: 2.", done);
            Assert.DoesNotContain(ExchangeCodes.NoteConflict, done);   // item FL: a conflict is never shown as a failure

            var failed = new ExchangeNotesSyncReport { Code = ExchangeCodes.ServerNoNotes };
            Assert.StartsWith("This server does not keep notes.", NotesSyncGate.StatusText(AllOn(Member("a")), false, failed, DateTime.UtcNow, en));
        }

        [Fact]
        public void EveryStatusSentenceIsTranslatedInEveryLanguage()
        {
            AppConfig config = AllOn(Member("a"));
            var report = new ExchangeNotesSyncReport { Conflicts = 1, TooLarge = 1, Unsupported = 1, Unreadable = 1, LimitReached = true };
            foreach (string language in Localization.Names)
            {
                string text = NotesSyncGate.StatusText(config, false, report, DateTime.UtcNow, s => Localization.Translate(language, s))!;
                Assert.False(string.IsNullOrWhiteSpace(text), language);
                if (language != "English") Assert.DoesNotContain("Synced at", text);
            }
        }
    }
}
