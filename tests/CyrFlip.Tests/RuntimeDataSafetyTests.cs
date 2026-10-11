using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(DiagnosticLogCollection.Name)]
    public class RuntimeDataSafetyTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "cyrflip-s0063-" + Guid.NewGuid());
        public RuntimeDataSafetyTests() => Directory.CreateDirectory(_folder);
        public void Dispose()
        {
            RuntimeRegistry.UserOverride.Value = null;
            Directory.Delete(_folder, true);
        }
        private string Journal => Path.Combine(_folder, "notes.log");
        private sealed class Cipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher) => Encoding.UTF8.GetString(Convert.FromBase64String(cipher));
        }
        private QuickNotesStore Notes() => new QuickNotesStore(Journal, new Cipher());

        [Fact]
        public void FailedConfigOpenPreservesAllStoredValuesOnSave()
        {
            var root = new FakeRegistry();
            using var key = root.CreateSubKey("config");
            key.SetValue("LayoutConversionProfiles", "original table", RegistryValueKind.String);
            key.SetValue("InputLayoutsBackup", "original layouts", RegistryValueKind.String);
            key.SetValue("LanguageHotkeysBackup", "original chords", RegistryValueKind.String);
            root.FailOpen = true;
            var config = AppConfig.Load(() => root.OpenSubKey("config"));
            root.FailOpen = false;
            config.SaveTo(new RuntimeConfigKey(key));
            Assert.Equal("original table", key.GetValue("LayoutConversionProfiles"));
            Assert.Equal("original layouts", key.GetValue("InputLayoutsBackup"));
            Assert.Equal("original chords", key.GetValue("LanguageHotkeysBackup"));
            Assert.NotEmpty(config.UnreadableValues);
            Assert.True(config.WelcomeShown);
        }

        private FakeRegistry RegistryWithLayouts()
        {
            var root = new FakeRegistry();
            RuntimeRegistry.UserOverride.Value = root;
            using var preload = root.CreateSubKey(@"Keyboard Layout\Preload");
            preload.SetValue("1", "00000409", RegistryValueKind.String);
            using var profile = root.CreateSubKey(@"Control Panel\International\User Profile\en-US");
            profile.SetValue("0409:00000409", 1, RegistryValueKind.DWord);
            return root;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void LayoutWriteFailureRollsBackBothStores(int writes)
        {
            var root = RegistryWithLayouts();
            string before = InputLayouts.BackupAll();
            root.WritesBeforeFailure = writes;
            var result = InputLayouts.Persist(new List<string> { "00000419", "00000409" }, added: "00000419");
            Assert.NotNull(result.Failure);
            Assert.Equal(before, InputLayouts.BackupAll());
        }

        [Fact]
        public void FailedRestoreReturnsFalseAndPreservesWindowsState()
        {
            var root = RegistryWithLayouts();
            string target = InputLayouts.BackupAll();
            using (var key = root.CreateSubKey(@"Keyboard Layout\Preload"))
                key.SetValue("1", "00000419", RegistryValueKind.String);
            string before = InputLayouts.BackupAll();
            root.WritesBeforeFailure = 1;
            Assert.False(InputLayouts.RestoreAll(target));
            Assert.Equal(before, InputLayouts.BackupAll());
        }

        [Fact]
        public void HotkeyBackupReadFailureProducesNoSnapshot()
        {
            var root = RegistryWithLayouts();
            root.FailOpen = true;
            Assert.Equal("", LanguageHotkeys.BackupAll());
        }

        [Fact]
        public void ToggleWriteFailureIsReturnedAndOldChordRemains()
        {
            var root = RegistryWithLayouts();
            using var key = root.CreateSubKey(@"Keyboard Layout\Toggle");
            key.SetValue("Hotkey", "1", RegistryValueKind.String);
            key.SetValue("Language Hotkey", "1", RegistryValueKind.String);
            root.WritesBeforeFailure = 1;
            Assert.False(LanguageHotkeys.SetToggle("2"));
            Assert.Equal("1", LanguageHotkeys.ToggleCode());
            Assert.Equal("1", key.GetValue("Language Hotkey"));
        }

        [Fact]
        public void LockedNotesLoadReportsFailureAndBlocksNewWrites()
        {
            File.WriteAllText(Journal, "data");
            using var held = new FileStream(Journal, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var store = Notes();
            using var service = new QuickNotesService(store);
            Assert.NotNull(service.ReadFailure);
            Assert.False(store.Create(new QuickNote { RawText = "must not append to partial state" }));
        }

        [Fact]
        public void ImportReturnsUnwrittenCountWhenBatchFails()
        {
            using var service = new QuickNotesService(Notes());
            var report = service.Import(new[] { new ExchangeNote { RawText = "imported" } }, true, _ => false);
            Assert.Equal(1, report.NotesUnwritten);
            Assert.True(service.SaveFailing);
        }

        [Fact]
        public void AppendRestoresMissingJournalBeforeAddingAnotherNote()
        {
            var store = Notes();
            var first = new QuickNote { RawText = "first" };
            Assert.True(store.Create(first));
            File.Move(Journal, Journal + ".bak");
            Assert.True(store.Create(new QuickNote { RawText = "second" }));
            Assert.Equal(2, Notes().Load().Count);
        }

        [Fact]
        public void FailedCompactionRecoveryKeepsCompleteTempAndAppendWaitsForIt()
        {
            var store = Notes();
            var first = new QuickNote { RawText = "first" };
            Assert.True(store.Create(first));
            FileStream? held = null;
            store.ReplaceFile = (temp, path, backup) =>
            {
                File.Move(path, backup);
                // Hold the complete snapshot through both replacement and recovery failures.
                held = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("replace failed");
            };
            try
            {
                store.Compact(new[] { first });
                Assert.True(File.Exists(Journal + ".tmp"));
                Assert.False(store.Create(new QuickNote { RawText = "blocked" }));
                Assert.False(File.Exists(Journal));
            }
            finally { held?.Dispose(); }
            Assert.True(store.Create(new QuickNote { RawText = "second" }));
            Assert.Equal(2, Notes().Load().Count);
        }

        [Fact]
        public void LockedHistoryCountsAWholeFileFailure()
        {
            string path = Path.Combine(_folder, "history.log");
            File.WriteAllText(path, "data");
            using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var journal = new ClipboardHistoryJournal(path, new Cipher());
            Assert.True(journal.Load(new ClipboardHistoryOrder()) > 0);
        }

        [Fact]
        public void EveryFailedEraseIsReportedAfterAnEarlierWriteFailure()
        {
            string path = Path.Combine(_folder, "history.log");
            using var journal = new ClipboardHistoryJournal(path, new Cipher());
            journal.Append(new ClipboardHistoryJournal.Record { Action = "add", Uuid = "first", Text = "kept" });
            Assert.True(journal.Drain(TimeSpan.FromSeconds(3)));
            journal.ReplaceFile = (_, __) => throw new IOException("purge fault");
            int failures = 0;
            journal.EraseFailed += () => System.Threading.Interlocked.Increment(ref failures);
            // A read-only handle permits the purge's read but blocks its tombstone append.
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // ReadWrite sharing would still allow appends. Mark read-only and close the writer
            // through a failed purge before queuing any subsequent command.
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                journal.Append(new ClipboardHistoryJournal.Record { Action = "delete", Uuid = "first" });
                Assert.True(journal.Drain(TimeSpan.FromSeconds(3)));
                journal.Append(new ClipboardHistoryJournal.Record { Action = "pin", Uuid = "first" });
                Assert.True(journal.Drain(TimeSpan.FromSeconds(3)));
                journal.Append(new ClipboardHistoryJournal.Record { Action = "delete", Uuid = "first" });
                Assert.True(journal.Drain(TimeSpan.FromSeconds(3)));
                Assert.Equal(2, failures);
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        }

        [Fact]
        public void ScenarioListingFailurePreservesLiveItemsAndIsReported()
        {
            var store = new LauncherScenarioStore(_folder);
            Assert.Null(store.Add(new LauncherScenario { Name = "saved" }));
            store.ListScenarioFiles = _ => throw new IOException("listing fault");
            store.Reload();
            Assert.Single(store.LoadErrors);
            Assert.Equal("saved", Assert.Single(store.All).Name);
        }

        [Fact]
        public void MigrationDoesNotCountAScenarioWhoseSaveFailed()
        {
            string source = Path.Combine(_folder, "source");
            Directory.CreateDirectory(source);
            new LauncherScenarioStore(source).Export(new LauncherScenario { Name = "import", Path = "calc.exe" },
                Path.Combine(source, "import.xml"));
            string target = Path.Combine(_folder, "blocked");
            File.WriteAllText(target, "not a folder");
            var store = new LauncherScenarioStore(target);
            var result = LauncherMigration.Import(store, source);
            Assert.Equal(0, result.Imported);
            Assert.Single(result.SaveFailures);
            Assert.Empty(result.Skipped);
            Assert.Empty(store.All);
        }

        [Fact]
        public void FailedScenarioChordResetIsReportedAndRestoresItsLiveChord()
        {
            var config = new AppConfig { EnableScenarioLauncher = true };
            var scenario = new LauncherScenario { Name = "scenario", Hotkey = "Ctrl+Alt+X" };
            var scenarios = new[] { scenario };
            var windows = Array.Empty<LanguageHotkeys.Entry>();
            Func<string, string> name = klid => klid;
            var row = ChordOverviewModel.Build(config, scenarios, windows, null, "English", name)
                .Single(r => r.Kind == ChordKind.Launcher);
            var plan = ChordResetPlan.ForRow(config, row, scenarios, windows, "English", name);
            var result = plan.Apply(config, scenarios, windows, "English", name,
                _ => throw new IOException("save fault"));
            Assert.Empty(result.Applied);
            Assert.Equal(ChordSkipReason.SaveFailed, Assert.Single(result.Skipped).Reason);
            Assert.Equal("Ctrl+Alt+X", scenario.Hotkey);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CopyFeedbackFollowsTheClipboardResult(bool succeeds)
        {
            int failures = 0;
            Assert.Equal(succeeds, QuickNotesWindow.TryCopyText(_ => succeeds, "note", () => failures++));
            Assert.Equal(succeeds ? 0 : 1, failures);
        }

        [Fact]
        public void FailedBackupRestoreIsReportedAsALoadFailure()
        {
            var store = Notes();
            Assert.True(store.Create(new QuickNote { RawText = "original" }));
            File.Move(Journal, Journal + ".bak");
            Directory.CreateDirectory(Journal);
            using var service = new QuickNotesService(store);
            Assert.NotNull(service.ReadFailure);
            Assert.False(store.Create(new QuickNote { RawText = "unsaved" }));
        }

        [Fact]
        public void FailedScenarioSaveReturnsCauseAndDoesNotAddToMemory()
        {
            string folder = Path.Combine(_folder, "not-a-folder");
            File.WriteAllText(folder, "blocked");
            var store = new LauncherScenarioStore(folder);
            Exception? result = store.Add(new LauncherScenario { Name = "unsaved" });
            Assert.NotNull(result);
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void ScenarioControlCharactersRoundTrip()
        {
            var store = new LauncherScenarioStore(_folder);
            var scenario = new LauncherScenario { Name = "escape\u001b", Arguments = "\u0001\t\r\n" };
            store.Add(scenario);
            store.Reload();
            Assert.Empty(store.LoadErrors);
            Assert.Equal(scenario.Name, Assert.Single(store.All).Name);
            Assert.Equal(scenario.Arguments, store.All[0].Arguments);
        }

        // Registry faults are one-shot, permitting rollback. The complete tree includes unknown
        // values and their kinds; it detects incomplete recovery as well as false success.
        internal sealed class FakeRegistry : IRuntimeRegistryKey
        {
            private readonly FakeRegistry _root;
            private readonly Dictionary<string, FakeRegistry> _children = new Dictionary<string, FakeRegistry>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, (object value, RegistryValueKind kind)> _values = new Dictionary<string, (object, RegistryValueKind)>();
            internal int WritesBeforeFailure = -1;
            internal bool FailOpen;
            public FakeRegistry() { _root = this; }
            private FakeRegistry(FakeRegistry root) { _root = root; }
            public IRuntimeRegistryKey? OpenSubKey(string path, bool writable = false)
            {
                if (_root.FailOpen) throw new IOException("read fault");
                FakeRegistry key = this;
                foreach (string part in path.Split('\\'))
                {
                    if (!key._children.TryGetValue(part, out FakeRegistry next)) return null;
                    key = next;
                }
                return key;
            }
            public IRuntimeRegistryKey CreateSubKey(string path)
            {
                FakeRegistry key = this;
                foreach (string part in path.Split('\\'))
                {
                    if (!key._children.TryGetValue(part, out FakeRegistry next))
                        key._children[part] = next = new FakeRegistry(_root);
                    key = next;
                }
                return key;
            }
            public string[] GetValueNames() => _values.Keys.ToArray();
            public string[] GetSubKeyNames() => _children.Keys.ToArray();
            public object? GetValue(string name, object? fallback = null, RegistryValueOptions options = RegistryValueOptions.None)
                => _values.TryGetValue(name, out var value) ? value.value : fallback;
            public RegistryValueKind GetValueKind(string name) => _values[name].kind;
            public void SetValue(string name, object value, RegistryValueKind kind)
            {
                if (_root.WritesBeforeFailure == 0) { _root.WritesBeforeFailure = -1; throw new IOException("write fault"); }
                if (_root.WritesBeforeFailure > 0) _root.WritesBeforeFailure--;
                _values[name] = (value, kind);
            }
            public void DeleteValue(string name, bool throwOnMissingValue = false) => _values.Remove(name);
            public void DeleteSubKey(string path, bool throwOnMissingSubKey = false) => DeleteSubKeyTree(path, throwOnMissingSubKey);
            public void DeleteSubKeyTree(string path, bool throwOnMissingSubKey = false)
            {
                int split = path.LastIndexOf('\\');
                var parent = split < 0 ? this : (FakeRegistry?)OpenSubKey(path.Substring(0, split));
                parent?._children.Remove(path.Substring(split + 1));
            }
            public void Dispose() { }
        }
    }
}

