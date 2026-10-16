using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>The settings file v2's scenarios section end to end on a temp store (S0072 5.3, 6.3).</summary>
    public class SettingsScenarioTransferTests
    {
        private static string TempFolder(string name) => Path.Combine(Path.GetTempPath(), "S0072-" + name + "-" + Guid.NewGuid().ToString("N"));

        private static LauncherScenario Scenario(string name, string path, string hotkey = "")
            => new LauncherScenario { Id = Guid.NewGuid(), Name = name, Path = path, Hotkey = hotkey };

        private static SettingsFileDocument DocumentWithScenarios(IReadOnlyList<LauncherScenario> scenarios)
        {
            var document = SettingsFileReaderTests.Read("{}", "{}", version: 2);
            document.Scenarios = scenarios.Select(s => LauncherScenarioStore.ToXml(s)).ToList();
            return document;
        }

        [Fact]
        public void OversizedScenarioCostsItselfOnReadAndCannotBeWrittenByUs()
        {
            string tooLarge = new string('x', SettingsFileReader.MaxScenarioBytes * 7);
            var document = SettingsFileReaderTests.Read(tables: "{\"scenarios\":[\"" + tooLarge + "\",\"<AppItem/>\"]}", version: 2);
            Assert.Equal("<AppItem/>", Assert.Single(document.Scenarios!));
            Assert.Contains(document.Issues, i => i.Key == "scenarios" && i.Reason == "limit");
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() =>
                SettingsFileWriter.Write(new AppConfig(), DateTime.UtcNow, "1.0", new[] { tooLarge })).Code);
        }

        [Fact]
        public void ScenarioSnapshotFailureStopsTheTransactionBeforeAnyWrite()
        {
            var local = new AppConfig();
            var document = DocumentWithScenarios(new[] { Scenario("After", "calc.exe") });
            document.Values["Theme"] = "dark";
            var plan = SettingsImportPlan.Build(local, document);
            var registry = MemoryConfigKey.Capture(local);
            bool applied = false, refreshed = false;
            Assert.Throws<IOException>(() => SettingsSnapshot.Commit(local, plan, registry,
                () => refreshed = true, _ => { }, () => throw new IOException(), () => applied = true));
            Assert.False(applied); Assert.False(refreshed);
            Assert.Equal("system", local.Theme);
            Assert.Equal("system", registry.GetValue("Theme"));
        }

        [Fact]
        public void SuccessfulScenarioImportRefreshesTheLiveConsumersOnce()
        {
            string folder = TempFolder("refresh"), backups = TempFolder("snapshot");
            try
            {
                var store = new LauncherScenarioStore(folder);
                var local = new AppConfig();
                var plan = SettingsImportPlan.Build(local, DocumentWithScenarios(new[] { Scenario("After", "calc.exe") }));
                int refreshed = 0;
                SettingsSnapshot.Commit(local, plan, MemoryConfigKey.Capture(local), () => refreshed++, _ => { },
                    () => SettingsSnapshot.CreateScenarios(backups, store),
                    () => { if (store.ReplaceSet(plan.Scenarios!) is Exception error) throw error; });
                Assert.Equal(1, refreshed);
                Assert.Equal("After", Assert.Single(store.All).Name);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); if (Directory.Exists(backups)) Directory.Delete(backups, true); }
        }

        [Fact]
        public void ImportedFilenameCannotEscapeTheStore()
        {
            string folder = TempFolder("filename");
            string outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
            try
            {
                var scenario = Scenario("Untrusted filename", "calc.exe");
                scenario.Filename = outside;
                var doc = DocumentWithScenarios(new[] { scenario });
                var plan = SettingsImportPlan.Build(new AppConfig(), doc);
                var store = new LauncherScenarioStore(folder);
                Assert.Null(store.ReplaceSet(plan.Scenarios!));
                Assert.False(File.Exists(outside));
                Assert.Equal(scenario.Id + ".xml", Assert.Single(store.All).Filename);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); if (File.Exists(outside)) File.Delete(outside); }
        }

        [Fact]
        public void TransferPreservesVerbatimXmlAndUndoSnapshotCarriesTheOriginalSet()
        {
            string folder = TempFolder("verbatim"), backups = TempFolder("undo");
            try
            {
                var original = Scenario("Original", "calc.exe");
                string xml = LauncherScenarioStore.ToXml(original).Replace("<Name>", "<!-- preserved comment -->\n  <Name>");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, original.Id + ".xml"), xml);
                var store = new LauncherScenarioStore(folder);
                Assert.Equal(xml, Assert.Single(store.ReadDocuments()));
                string snapshot = SettingsSnapshot.Create(backups, new AppConfig(), store.ReadDocuments());
                var doc = SettingsFileReader.ReadFile(snapshot);
                Assert.Equal(xml, Assert.Single(doc.Scenarios!));
                Assert.Null(store.ReplaceSet(new[] { Scenario("Other", "notepad.exe") }));
                var plan = SettingsImportPlan.Build(new AppConfig(), doc, scenarios: store.All);
                Assert.Null(store.ReplaceSet(plan.Scenarios!));
                Assert.Equal(xml, Assert.Single(store.ReadDocuments()));
            }
            finally { Directory.Delete(folder, true); Directory.Delete(backups, true); }
        }

        [Fact]
        public void RollbackPreservesUnrecognizedFilesToo()
        {
            string folder = TempFolder("unknown"), backups = TempFolder("undo");
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "future.xml");
                File.WriteAllText(path, "<FutureScenario />");
                var store = new LauncherScenarioStore(folder);
                Action restore = SettingsSnapshot.CreateScenarios(backups, store);
                File.Delete(path);
                restore();
                Assert.Equal("<FutureScenario />", File.ReadAllText(path));
            }
            finally { Directory.Delete(folder, true); Directory.Delete(backups, true); }
        }

        [Fact]
        public void TryReadXmlIsTheFileReaderOverAString()
        {
            var scenario = Scenario("Calculator", "calc.exe");
            string folder = TempFolder("xml");
            try
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "s.xml");
                File.WriteAllText(file, LauncherScenarioStore.ToXml(scenario));
                var fromFile = LauncherScenarioStore.TryRead(file, out Exception? fileError);
                var fromXml = LauncherScenarioStore.TryReadXml(File.ReadAllText(file), out Exception? xmlError);
                Assert.Null(fileError); Assert.Null(xmlError);
                Assert.Equal(fromFile!.Id, fromXml!.Id);
                Assert.Equal(fromFile.Name, fromXml.Name);
                // An unrecognized document is refused whole: wrong root, nothing read, nothing run.
                Assert.Null(LauncherScenarioStore.TryReadXml("<NotAppItem/>", out Exception? refused));
                Assert.NotNull(refused);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void AnUnrecognizedDocumentIsSkippedAndItsPathIsNeverRead()
        {
            var document = DocumentWithScenarios(new[] { Scenario("Good", "calc.exe") });
            document.Scenarios!.Insert(0, "<NotAppItem><Path>C:\\Windows\\System32\\calc.exe</Path></NotAppItem>");
            var plan = SettingsImportPlan.Build(new AppConfig(), document);
            Assert.Single(plan.Scenarios!);
            Assert.Equal("Good", plan.Scenarios![0].Name);
            Assert.Contains(plan.Issues, i => i.Key == "scenarios" && i.Reason == "scenario");
        }

        [Fact]
        public void ExportWipeImportRoundTripsTheWholeStore()
        {
            string storeFolder = TempFolder("store");
            try
            {
                var store = new LauncherScenarioStore(storeFolder);
                Assert.Null(store.Add(Scenario("Calculator", "calc.exe")));
                Assert.Null(store.Add(Scenario("Editor", "notepad.exe", hotkey: "Ctrl+Shift+Alt+D")));
                var originals = store.All;

                var document = DocumentWithScenarios(originals);
                foreach (LauncherScenario scenario in originals.ToArray())
                    Assert.Null(store.Remove(scenario.Id));
                Assert.Equal(0, store.Count);

                var plan = SettingsImportPlan.Build(new AppConfig(), document);
                Assert.NotNull(plan.Scenarios);
                Assert.Null(store.ReplaceSet(plan.Scenarios!));
                Assert.Equal(originals.Count, store.Count);
                foreach (LauncherScenario original in originals)
                {
                    LauncherScenario? reloaded = store.All.Single(s => s.Id == original.Id);
                    Assert.Equal(original.Name, reloaded.Name);
                    Assert.Equal(original.Path, reloaded.Path);
                    Assert.Equal(original.Hotkey, reloaded.Hotkey);
                }
                // I6 parity: importing the very set the store holds is no change at all.
                var again = SettingsImportPlan.Build(new AppConfig(), DocumentWithScenarios(originals), scenarios: store.All);
                Assert.Null(again.Scenarios);
            }
            finally { Directory.Delete(storeFolder, true); }
        }

        [Fact]
        public void ReplaceSetRegeneratesCollidingIdsAndDeletesRemovedFiles()
        {
            string storeFolder = TempFolder("replace");
            try
            {
                var store = new LauncherScenarioStore(storeFolder);
                Assert.Null(store.Add(Scenario("Old", "calc.exe")));
                Guid oldId = store.All[0].Id;
                Assert.Null(store.Add(Scenario("Gone", "mspaint.exe")));
                string goneFile = Path.Combine(storeFolder, store.All[1].Filename);

                Guid shared = Guid.NewGuid();
                var incoming = new List<LauncherScenario>
                {
                    Scenario("New", "mspaint.exe"),
                    Scenario("TwinA", "calc.exe"),
                    Scenario("TwinB", "calc.exe"),
                };
                incoming[1].Id = shared;
                incoming[2].Id = shared; // collides with TwinA: regenerated on import
                Assert.Null(store.ReplaceSet(incoming));

                Assert.Equal(3, store.Count);
                Assert.Equal(incoming[0].Id, store.All.Single(s => s.Name == "New").Id);
                Assert.Equal(shared, store.All.Single(s => s.Name == "TwinA").Id);
                Assert.NotEqual(shared, store.All.Single(s => s.Name == "TwinB").Id);
                Assert.DoesNotContain(store.All, s => s.Id == oldId); // the replaced set is gone
                Assert.False(File.Exists(goneFile)); // the removed scenario's file is gone
            }
            finally { Directory.Delete(storeFolder, true); }
        }

        [Fact]
        public void CommitSnapshotsTheStoreAndRollsItBackByteForByte()
        {
            string storeFolder = TempFolder("rollback");
            string backups = TempFolder("backups");
            try
            {
                var store = new LauncherScenarioStore(storeFolder);
                Assert.Null(store.Add(Scenario("Before", "calc.exe")));
                string beforeFile = Path.Combine(storeFolder, store.All[0].Filename);
                byte[] beforeBytes = File.ReadAllBytes(beforeFile);

                var local = new AppConfig();
                var document = DocumentWithScenarios(new[] { Scenario("After", "mspaint.exe") });
                document.Values["Theme"] = "dark";
                var plan = SettingsImportPlan.Build(local, document, scenarios: store.All);
                Assert.NotNull(plan.Scenarios);

                int refreshes = 0;
                var thrown = Assert.Throws<SettingsFileException>(() => SettingsSnapshot.Commit(local, plan,
                    new MemoryRegistryKey(),
                    () => { if (++refreshes == 1) throw new IOException(); }, // the first live refresh fails: roll back
                    _ => SettingsSnapshot.Create(backups, local),
                    () => SettingsSnapshot.CreateScenarios(backups, store),
                    () => { Exception? failure = store.ReplaceSet(plan.Scenarios!); if (failure != null) throw new SettingsFileException("io"); }));
                Assert.Equal("rolled-back", thrown.Code);

                Assert.Equal(beforeBytes, File.ReadAllBytes(beforeFile)); // byte-for-byte
                Assert.Single(store.All);
                Assert.Equal("Before", store.All[0].Name);
            }
            finally { try { Directory.Delete(storeFolder, true); } catch { } try { Directory.Delete(backups, true); } catch { } }
        }

        private sealed class MemoryRegistryKey : IConfigKey
        {
            private readonly MemoryConfigKey inner = new MemoryConfigKey();
            public object? GetValue(string name) => inner.GetValue(name);
            public void SetValue(string name, object value, RegistryValueKind kind) => inner.SetValue(name, value, kind);
        }
    }
}
