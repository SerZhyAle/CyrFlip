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
    public class SettingsFileInventoryTests
    {
        // S0072 classifies the current tree: the original spec counted 89/17 before the
        // backup markers and the S0071/S0074 preferences arrived. Credentials remain excluded.
        private const int ExportedV2 = 105, ExcludedV2 = 21;
        [Fact]
        public void EveryAppOwnedOverviewChordIsInTheTransferFile()
        {
            var config = new AppConfig();
            int index = 1;
            foreach (var property in typeof(AppConfig).GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Hotkey")))
                property.SetValue(config, "Ctrl+Alt+F" + index++);
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "conv", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F22", Enabled = true });
            config.TranslateProfiles.Add(new TranslationProfile { Id = "trans", TargetLang = "de", Hotkey = "Ctrl+Shift+F23", Enabled = true });
            var scenario = new LauncherScenario { Name = "Launch", Path = "calc.exe", Hotkey = "Ctrl+Shift+F24" };
            string output = SettingsFileWriter.Write(config, DateTime.UtcNow, "1.0", new[] { LauncherScenarioStore.ToXml(scenario) });
            var document = SettingsFileReader.Read(Encoding.UTF8.GetBytes(output));
            var rows = ChordOverviewModel.Build(config, new[] { scenario }, Array.Empty<LanguageHotkeys.Entry>(), "1", "English", k => k);
            foreach (var row in rows.Where(r => r.Group != ChordGroup.Windows))
            {
                Assert.False(string.IsNullOrEmpty(row.StoredChord));
                if (row.Group == ChordGroup.Fixed || row.Group == ChordGroup.Mouse)
                    Assert.Contains(document.Values, v => v.Value is string token && token == row.StoredChord);
                else if (row.Group == ChordGroup.Launcher)
                    Assert.Contains(document.Scenarios!, xml => LauncherScenarioStore.TryReadXml(xml, out _)!.Hotkey == row.StoredChord);
                else
                {
                    string key = row.Group == ChordGroup.Conversions ? "LayoutConversionProfiles" : "TranslateProfiles";
                    Assert.Contains(row.StoredChord, (string)document.Values[key]);
                }
            }
        }

        [Fact]
        public void EveryRegistryValueHasExactlyOneClassification()
        {
            // SaveTo writes the symbols block only once the module has been enabled, so a plain
            // default capture would never see those values - capture all four shapes.
            var written = MemoryConfigKey.Capture(new AppConfig()).Values.Keys
                .Concat(MemoryConfigKey.Capture(new AppConfig { EnableSymbols = true }).Values.Keys)
                .Concat(MemoryConfigKey.Capture(new AppConfig { EnableTrafficLight = true }).Values.Keys)
                .Concat(MemoryConfigKey.Capture(new AppConfig { EnableExchangeClipboard = true }).Values.Keys).Distinct();
            var classified = SettingsFileInventory.Exported.Select(e => e.Key).Concat(SettingsFileInventory.Excluded.Keys).ToArray();
            Assert.Equal(classified.Length, classified.Distinct().Count());
            Assert.Equal(written.OrderBy(k => k), classified.OrderBy(k => k));
            Assert.All(SettingsFileInventory.Excluded.Values, reason => Assert.NotEmpty(reason));
        }
        [Fact]
        public void TheV2ArithmeticHolds()
        {
            Assert.Equal(ExportedV2, SettingsFileInventory.Exported.Length);
            Assert.Equal(ExcludedV2, SettingsFileInventory.Excluded.Count);
            // The machine history keeps the TranslateSeeded marker excluded even though Copy() carries it.
            Assert.Contains("TranslateSeeded", SettingsFileInventory.Excluded.Keys);
            Assert.Equal(3, SettingsFileInventory.Excluded.Values.Count(reason => reason == "Windows state"));
            Assert.Equal(6, SettingsFileInventory.Excluded.Values.Count(reason => reason == "usage"));
            Assert.Equal(1, SettingsFileInventory.Excluded.Values.Count(reason => reason == "credentials"));
        }
        [Fact]
        public void TheSyntheticScenariosEntryIsNotARegistryValue()
        {
            Assert.Null(SettingsFileInventory.Scenarios.Property);
            Assert.True(SettingsFileInventory.Scenarios.IsTable);
            Assert.DoesNotContain(SettingsFileInventory.Exported, e => e.Key == "scenarios");
        }
    }
    public class SettingsFileWriterTests
    {
        [Fact]
        public void DefaultNameIdentifiesTheProgramAndSaveDateAndTime()
            => Assert.Equal("CyrFlip-settings-26-10-07_2345.json",
                SettingsFileWriter.DefaultFileName(new DateTime(2026, 10, 7, 23, 45, 12)));
        [Fact]
        public void CredentialBearingEndpointCannotLeaveThroughExport()
            => Assert.Throws<SettingsFileException>(() => SettingsFileWriter.Write(
                new AppConfig { TranslateEndpoint = "http://user:password@server:11434" }, DateTime.UtcNow, "1.0"));
        [Fact]
        public void StableNativeTypesAndCompleteAllowListRoundTrip()
        {
            var local = new AppConfig { UiLanguage = "Українська", FlipCount = 31337, LanguageHotkeysBackup = "PRIVATE", WelcomeShown = true };
            local.TrafficLightTargets.Add(new TrafficLightTarget { Id = "t1", Kind = "ping", Address = "nas.local" });
            string output = SettingsFileWriter.Write(local, new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015");
            Assert.DoesNotContain("\r", output); Assert.DoesNotContain("31337", output);
            Assert.DoesNotContain("PRIVATE", output); Assert.DoesNotContain("WelcomeShown", output);
            Assert.Contains("\"EnableCaretOverlay\": true", output); Assert.Contains("\"CursorSize\": 24", output);
            Assert.Contains("Українська", output);
            // Targets are always in the file (S0072 D3); so is the scenarios section, empty here.
            Assert.Contains("nas.local", output); Assert.Contains("\"scenarios\": []", output);
            var read = SettingsFileReader.Read(Encoding.UTF8.GetBytes(output));
            Assert.Equal(SettingsFileInventory.Exported.Length, read.Values.Count);
            Assert.NotNull(read.Scenarios); Assert.Empty(read.Scenarios!);
            var plan = SettingsImportPlan.Build(local, read); Assert.Empty(plan.Changes); Assert.Empty(plan.Issues);
            Assert.Equal(output, SettingsFileWriter.Write(local, new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015",
                Array.Empty<string>()));
        }
        [Fact]
        public void AnOffModuleExportsItsDefaults()
        {
            // SaveTo never wrote the symbols or traffic-light blocks before their first enable (D6);
            // the export falls back to the defaults instead - and importing them keeps the module off.
            string output = SettingsFileWriter.Write(new AppConfig(), DateTime.UtcNow, "1.0");
            Assert.Contains("\"EnableSymbols\": false", output);
            Assert.Contains("\"EnableTrafficLight\": false", output);
            Assert.Contains("\"SymbolsHotkey\"", output);
        }
        [Fact]
        public void BytesArePinnedByTheV1WriterFixture()
        {
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "SettingsFile", "writer-v1.json");
            Assert.Equal(File.ReadAllText(fixture, Encoding.UTF8), SettingsFileWriter.WriteV1(new AppConfig { UiLanguage = "English" },
                new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015"));
        }
        [Fact]
        public void BytesArePinnedByTheV2WriterFixture()
        {
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "SettingsFile", "writer-v2.json");
            Assert.Equal(File.ReadAllText(fixture, Encoding.UTF8), SettingsFileWriter.Write(new AppConfig { UiLanguage = "English" },
                new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015", Array.Empty<string>()));
        }
    }
    public class SettingsFileReaderTests
    {
        internal static SettingsFileDocument Read(string settings = "{}", string tables = "{}", int version = 1)
            => SettingsFileReader.Read(Encoding.UTF8.GetBytes("{\"format\":\"CyrFlip Settings\",\"schemaVersion\":" + version + ",\"settings\":" + settings + ",\"tables\":" + tables + "}"));
        [Fact]
        public void DifferentAppVersionsAcceptIncompleteAndExcessSettings()
        {
            foreach (string version in new[] { "1.0.0.0", "99.1.1.9999" })
            {
                var doc = SettingsFileReader.Read(Encoding.UTF8.GetBytes("{\"format\":\"CyrFlip Settings\",\"schemaVersion\":1,\"app\":\""
                    + version + "\",\"settings\":{\"Theme\":\"dark\",\"RetiredSetting\":true,\"FutureSetting\":42}}"));
                var plan = SettingsImportPlan.Build(new AppConfig { CursorSize = 48, UiLanguage = "Deutsch" }, doc);
                Assert.Equal("dark", plan.Candidate.Theme); Assert.Equal(48, plan.Candidate.CursorSize);
                Assert.Equal("Deutsch", plan.Candidate.UiLanguage); Assert.Equal(2, doc.UnknownKeys);
                Assert.Single(plan.Changes); Assert.Empty(plan.Issues);
            }
        }
        [Fact]
        public void GoldenV1BomCrLfAndAbsentNullKeepLocal()
        {
            var doc = SettingsFileReader.ReadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "SettingsFile", "golden-v1.json"));
            Assert.Equal("Русский", doc.Values["UiLanguage"]);
            Assert.Null(doc.Scenarios); // a v1 file has no scenarios section: keep local (S4)
            string json = "\ufeff{\r\n\"format\":\"CyrFlip Settings\",\"schemaVersion\":1,\"settings\":{\"UiLanguage\":null}}";
            var local = new AppConfig { UiLanguage = "Deutsch", CursorSize = 48 };
            var plan = SettingsImportPlan.Build(local, SettingsFileReader.Read(Encoding.UTF8.GetBytes(json)));
            Assert.Equal("Deutsch", plan.Candidate.UiLanguage); Assert.Equal(48, plan.Candidate.CursorSize); Assert.Empty(plan.Changes);
        }
        [Fact]
        public void GoldenV2ReadsWholeWithScenarioAndTargets()
        {
            var doc = SettingsFileReader.ReadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "SettingsFile", "golden-v2.json"));
            Assert.Equal("Русский", doc.Values["UiLanguage"]);
            Assert.True((int)doc.Values["EnableTrafficLight"] != 0);
            Assert.True((int)doc.Values["EnableSymbols"] != 0);
            Assert.Equal(4, doc.Values["TrafficLightColumns"]);
            Assert.Equal("TrafficLight|hotkeys/switches|24", doc.Values["SettingsViewport"]);
            Assert.NotNull(doc.Scenarios); Assert.Single(doc.Scenarios!);
            var local = new AppConfig();
            var plan = SettingsImportPlan.Build(local, doc,
                installed: new HashSet<string>(new[] { "00000409", "00000419" }));
            Assert.Equal("Ctrl+Shift+F11", plan.Candidate.CaseHotkey);
            Assert.True(plan.Candidate.EnableSymbols);
            Assert.True(plan.Candidate.EnableTrafficLight);
            Assert.Equal(4, plan.Candidate.TrafficLightColumns);
            Assert.Equal(2, plan.Candidate.TrafficLightTargets.Count);
            Assert.Single(plan.Scenarios!);
            Assert.Equal("Calculator", plan.Scenarios![0].Name);
            Assert.Empty(plan.Issues);
        }
        [Theory]
        [InlineData("{\"format\":\"CyrFlip Settings\",\"schemaVersion\":3}", "version")]
        [InlineData("{\"format\":\"CyrFlip Settings\",\"schemaVersion\":1.0}", "format")]
        [InlineData("{\"format\":\"CyrFlip Settings\"}", "format")]
        [InlineData("{\"format\":\"CyrFlip Exchange Text\",\"schemaVersion\":1}", "format")]
        [InlineData("{bad", "format")]
        public void WrongFormatAndNewVersionHaveDistinctRefusals(string text, string reason)
            => Assert.Equal(reason, Assert.Throws<SettingsFileException>(() => SettingsFileReader.Read(Encoding.UTF8.GetBytes(text))).Code);
        [Fact]
        public void WrongKindsAndMalformedRowsCostOnlyTheirKey()
        {
            var doc = Read("{\"CursorSize\":true,\"Theme\":\"dark\",\"unknown\":7}", "{\"TranslateProfiles\":[{\"Hotkey\":123}]}");
            Assert.Equal(1, doc.UnknownKeys); Assert.Equal(2, doc.Issues.Count);
            var candidate = SettingsImportPlan.Build(new AppConfig { CursorSize = 36 }, doc).Candidate;
            Assert.Equal(36, candidate.CursorSize); Assert.Equal("dark", candidate.Theme);
        }
        [Fact]
        public void EveryCapIsEnforced()
        {
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() => SettingsFileReader.Read(new byte[SettingsFileReader.MaxBytes + 1])).Code);
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() => Read("{\"Theme\":\"" + new string('a', 4097) + "\"}")).Code);
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() => Read("{\"Theme\":\"" + new string('Ж', 2049) + "\"}")).Code);
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() => Read("{}", "{\"TranslateProfiles\":[" + string.Join(",", Enumerable.Repeat("{}", 257)) + "]}")).Code);
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() => Read("{" + string.Join(",", Enumerable.Range(0, 2001).Select(i => "\"k" + i + "\":0")) + "}")).Code);
            // The scenarios section's own caps (S0072 5.5): 512 documents, an oversized one costs itself.
            Assert.Equal("limit", Assert.Throws<SettingsFileException>(() => Read("{}", "{\"scenarios\":[" + string.Join(",", Enumerable.Repeat("\"<AppItem/>\"", 513)) + "]}")).Code);
            var over = Read("{}", "{\"scenarios\":[\"" + new string('x', 5000) + "\",\"<AppItem/>\"]}");
            Assert.Contains(over.Issues, i => i.Key == "scenarios" && i.Reason == "limit");
            Assert.NotNull(over.Scenarios); Assert.Single(over.Scenarios);
        }
    }
    public class SettingsImportPlanTests
    {
        [Fact]
        public void WindowsShortcutConflictNeedsExplicitOptIn()
        {
            var windows = new[] { new LanguageHotkeys.Entry { Id = 0x100, Modifiers = LanguageHotkeys.MOD_CONTROL | LanguageHotkeys.MOD_SHIFT, VirtualKey = 0x77 } };
            var doc = SettingsFileReaderTests.Read("{\"CaseHotkey\":\"Ctrl+Shift+F8\"}");
            var plan = SettingsImportPlan.Build(new AppConfig(), doc, windows: windows);
            Assert.True(Assert.Single(plan.Changes).OptIn); Assert.Equal("windows", Assert.Single(plan.Issues).Reason);
            var unselected = SettingsImportPlan.Build(new AppConfig(), doc, windows: windows, selected: new HashSet<string>());
            Assert.Empty(unselected.Changes);
        }
        [Fact]
        public void AnImportedIdCollidingWithARetainedRowIsRegenerated()
        {
            var local = new AppConfig(); local.TranslateProfiles.Add(new TranslationProfile { Id = "stays" });
            var doc = SettingsFileReaderTests.Read("{}", "{\"LayoutConversionProfiles\":[{\"Id\":\"stays\",\"Hotkey\":\"Ctrl+Shift+F8\"}]}");
            var plan = SettingsImportPlan.Build(local, doc);
            Assert.NotEqual("stays", Assert.Single(plan.Candidate.LayoutConversionProfiles).Id);
            Assert.Equal("stays", Assert.Single(plan.Candidate.TranslateProfiles).Id);
        }
        [Fact]
        public void CandidateUsesTheRealLoaderAndClampsNumbers()
        {
            var local = new AppConfig { CursorSize = 36, Theme = "dark" };
            var doc = SettingsFileReaderTests.Read("{\"CursorSize\":999,\"TranslateTimeoutSeconds\":1}");
            var plan = SettingsImportPlan.Build(local, doc);
            var key = MemoryConfigKey.Capture(local); foreach (var pair in doc.Values) key.Values[pair.Key] = pair.Value;
            Assert.Equal(key.Load().CursorSize, plan.Candidate.CursorSize);
            Assert.Equal(2, plan.Issues.Count(i => i.Reason == "clamped")); Assert.Equal("dark", plan.Candidate.Theme);
        }
        [Fact]
        public void RecordersAndRemoteEndpointRequireIndividualOptIn()
        {
            var doc = SettingsFileReaderTests.Read("{\"EnableClipboardHistory\":true,\"EnableQuickNotes\":true,\"TranslateEndpoint\":\"http://server:11434\"}");
            var local = new AppConfig(); var plan = SettingsImportPlan.Build(local, doc);
            Assert.Equal(3, plan.Changes.Count(c => c.OptIn));
            var selected = new HashSet<string>(plan.Changes.Where(c => !c.OptIn).Select(c => c.Entry.Key));
            var candidate = SettingsImportPlan.Build(local, doc, selected: selected).Candidate;
            Assert.False(candidate.EnableClipboardHistory); Assert.False(candidate.EnableQuickNotes);
            var off = SettingsImportPlan.Build(new AppConfig { EnableClipboardHistory = true }, SettingsFileReaderTests.Read("{\"EnableClipboardHistory\":false}"));
            Assert.False(Assert.Single(off.Changes).OptIn);
        }
        [Fact]
        public void InvalidLanguageAndCredentialEndpointAreSkipped()
        {
            var plan = SettingsImportPlan.Build(new AppConfig { UiLanguage = "Deutsch" }, SettingsFileReaderTests.Read("{\"UiLanguage\":\"invalid\",\"TranslateEndpoint\":\"http://user:password@host:11434\"}"));
            Assert.Equal("Deutsch", plan.Candidate.UiLanguage); Assert.Equal(2, plan.Issues.Count);
        }
        [Fact]
        public void ImportedFixedChordCannotTakeAStaysLocalChordOrAnEditingCommand()
        {
            var local = new AppConfig();
            var conflict = SettingsImportPlan.Build(local, SettingsFileReaderTests.Read("{\"CaseHotkey\":\"Ctrl+Shift+F10\"}"));
            Assert.Equal(local.CaseHotkey, conflict.Candidate.CaseHotkey); Assert.Single(conflict.Issues);
            var editing = SettingsImportPlan.Build(local, SettingsFileReaderTests.Read("{\"CaseHotkey\":\"Ctrl+C\"}"));
            Assert.Equal(local.CaseHotkey, editing.Candidate.CaseHotkey); Assert.Single(editing.Issues);
        }
        [Fact]
        public void ChordsAreRecheckedAfterPartialSelection()
        {
            var doc = SettingsFileReaderTests.Read("{\"CaseHotkey\":\"Ctrl+Shift+F10\",\"ClipboardHistoryHotkey\":\"Ctrl+Shift+F8\"}");
            Assert.Equal(2, SettingsImportPlan.Build(new AppConfig(), doc).Changes.Count);
            var partial = SettingsImportPlan.Build(new AppConfig(), doc, selected: new HashSet<string> { "CaseHotkey" });
            Assert.Empty(partial.Changes); Assert.Single(partial.Issues);
        }
        [Fact]
        public void TwoImportedRowsClashAndArriveChordlessDisabled()
        {
            var doc = SettingsFileReaderTests.Read("{}", "{\"LayoutConversionProfiles\":[{\"Id\":\"a\",\"Hotkey\":\"Ctrl+Shift+F8\"},{\"Id\":\"b\",\"Hotkey\":\"Ctrl+Shift+F8\"}]}");
            var plan = SettingsImportPlan.Build(new AppConfig(), doc, installed: new HashSet<string>());
            Assert.All(plan.Candidate.LayoutConversionProfiles, row => { Assert.Equal("", row.Hotkey); Assert.False(row.Enabled); });
            Assert.Equal(2, plan.Issues.Count(i => i.Reason == "chord")); Assert.Equal(2, plan.Issues.Count(i => i.Reason == "layout"));
        }
        [Fact]
        public void TablesReplaceAndEmptyTranslationNeverReseeds()
        {
            var local = new AppConfig(); local.TranslateProfiles.Add(new TranslationProfile());
            var plan = SettingsImportPlan.Build(local, SettingsFileReaderTests.Read("{}", "{\"TranslateProfiles\":[],\"LayoutConversionProfiles\":[]}"));
            Assert.Empty(plan.Candidate.TranslateProfiles); Assert.Empty(plan.Candidate.LayoutConversionProfiles); Assert.True(plan.Candidate.TranslateSeeded);
            plan.Candidate.EnableTranslate = true; Assert.False(AppConfig.SeedTranslateRow(plan.Candidate, _ => true));
        }
        [Fact]
        public void SymbolsAndTrafficLightChordsImportThroughTheFixedChordChecks()
        {
            var doc = SettingsFileReaderTests.Read("{\"SymbolsHotkey\":\"Ctrl+Shift+F11\",\"TrafficLightHotkey\":\"Ctrl+Shift+F10\"}");
            // Ctrl+Shift+F11 is the case flip's, Ctrl+Shift+F10 the history chord's: both stay local.
            var plan = SettingsImportPlan.Build(new AppConfig(), doc);
            Assert.Equal(new AppConfig().SymbolsHotkey, plan.Candidate.SymbolsHotkey);
            Assert.Equal(new AppConfig().TrafficLightHotkey, plan.Candidate.TrafficLightHotkey);
            Assert.Equal(2, plan.Issues.Count(i => i.Reason == "chord"));
            Assert.Empty(plan.Changes);
        }
        [Fact]
        public void AMissingLocalFolderOrScenarioPathIsFlaggedButStillImported()
        {
            string missing = Path.Combine(Path.GetTempPath(), "CyrFlip-S0072-no-such-folder");
            string missingProgram = Path.Combine(Path.GetTempPath(), "CyrFlip-S0072-no-such-program.exe");
            string xml = "<AppItem><Id>4c3b2a1900112233445566778899aabb</Id><Name>x</Name><Path>"
                + missingProgram.Replace("\\", "\\\\") + "</Path></AppItem>";
            var doc = SettingsFileReaderTests.Read("{\"ScreenshotFolder\":\"" + missing.Replace("\\", "\\\\") + "\"}",
                "{\"scenarios\":[\"" + xml.Replace("\"", "\\\"").Replace("\n", "") + "\"]}", version: 2);
            var plan = SettingsImportPlan.Build(new AppConfig(), doc);
            Assert.Equal(missing, plan.Candidate.ScreenshotFolder); // imported anyway, the preview says so
            Assert.Contains(plan.Issues, i => i.Key == "ScreenshotFolder" && i.Reason == "path");
            Assert.Single(plan.Scenarios!);
            Assert.Contains(plan.Issues, i => i.Key == "scenarios" && i.Reason == "path");
        }
        [Fact]
        public void AFieldThatIsNowNavigationImportsAndReachesTheCandidate()
        {
            var doc = SettingsFileReaderTests.Read("{\"SettingsWindowX\":20,\"SettingsWindowY\":30,\"SymbolsCategory\":\"Arrows\",\"TrafficLightVisible\":true}", version: 2);
            var plan = SettingsImportPlan.Build(new AppConfig(), doc);
            Assert.Equal(20, plan.Candidate.SettingsWindowX);
            Assert.Equal("Arrows", plan.Candidate.SymbolsCategory);
            Assert.True(plan.Candidate.TrafficLightVisible);
            Assert.Equal(4, plan.Changes.Count);
        }
    }
    public class SettingsSnapshotTests
    {
        private sealed class ThrowOnceKey : IConfigKey
        {
            internal readonly MemoryConfigKey Inner = new MemoryConfigKey(); internal int Writes; internal int FailAt;
            public object? GetValue(string name) => Inner.GetValue(name);
            public void SetValue(string name, object value, RegistryValueKind kind)
            { if (++Writes == FailAt) throw new IOException(); Inner.SetValue(name, value, kind); }
        }
        [Fact]
        public void SnapshotFailurePrecedesEveryMutation()
        {
            var local = new AppConfig(); var key = new ThrowOnceKey();
            var plan = SettingsImportPlan.Build(local, SettingsFileReaderTests.Read("{\"Theme\":\"dark\"}"));
            Assert.Throws<IOException>(() => SettingsSnapshot.Commit(local, plan, key, () => { }, _ => throw new IOException()));
            Assert.Equal(0, key.Writes); Assert.Equal("system", local.Theme);
        }
        [Fact]
        public void AChangeAfterPreviewCannotBeOverwrittenByAStaleCandidate()
        {
            var live = new AppConfig(); var key = new ThrowOnceKey();
            var plan = SettingsImportPlan.Build(live, SettingsFileReaderTests.Read("{\"Theme\":\"dark\"}"));
            live.CursorSize = 48;
            Assert.Equal("stale", Assert.Throws<SettingsFileException>(() => SettingsSnapshot.Commit(live, plan, key, () => { }, _ => { })).Code);
            Assert.Equal(0, key.Writes); Assert.Equal(48, live.CursorSize);
        }
        [Theory]
        [InlineData(false)] [InlineData(true)]
        public void RegistryOrLiveApplyFailureRollsBack(bool refreshFails)
        {
            var local = new AppConfig(); var key = new ThrowOnceKey { FailAt = refreshFails ? 0 : 4 }; int refreshes = 0;
            var plan = SettingsImportPlan.Build(local, SettingsFileReaderTests.Read("{\"Theme\":\"dark\"}"));
            Assert.Throws<SettingsFileException>(() => SettingsSnapshot.Commit(local, plan, key,
                () => { if (++refreshes == 1 && refreshFails) throw new IOException(); }, _ => Assert.Equal(0, key.Writes)));
            Assert.Equal("system", local.Theme); Assert.Equal("system", key.Inner.GetValue("Theme"));
        }
        [Fact]
        public void ThreeNewestSnapshotsStayAndRestoreThroughTheSameReader()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SettingsSnapshotTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                for (int i = 0; i < 5; i++) SettingsSnapshot.Create(folder, new AppConfig { CursorSize = 24 + i });
                SettingsSnapshot.Prune(folder); Assert.Equal(3, Directory.GetFiles(folder).Length);
                var restored = SettingsImportPlan.Build(new AppConfig(), SettingsFileReader.ReadFile(SettingsSnapshot.Newest(folder)!));
                Assert.Equal(28, restored.Candidate.CursorSize);
                Assert.DoesNotContain(SupportBundle.LogFiles, f => f.Contains("backups"));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
    }
}
