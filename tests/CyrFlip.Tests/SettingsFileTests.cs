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
        [Fact]
        public void EveryRegistryValueHasExactlyOneClassification()
        {
            // SaveTo writes the symbols block only once the module has been enabled, so a plain
            // default capture would never see those values - capture both shapes.
            var written = MemoryConfigKey.Capture(new AppConfig()).Values.Keys
                .Concat(MemoryConfigKey.Capture(new AppConfig { EnableSymbols = true }).Values.Keys).Distinct();
            var classified = SettingsFileInventory.Exported.Select(e => e.Key).Concat(SettingsFileInventory.Excluded.Keys).ToArray();
            Assert.Equal(classified.Length, classified.Distinct().Count());
            Assert.Equal(written.OrderBy(k => k), classified.OrderBy(k => k));
            Assert.All(SettingsFileInventory.Excluded.Values, reason => Assert.NotEmpty(reason));
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
        public void StableNativeTypesAndAllowListRoundTrip()
        {
            var local = new AppConfig { UiLanguage = "Українська", SettingsGroupsState = "SECRET", ScreenshotFolder = "PRIVATE", WelcomeShown = true };
            string output = SettingsFileWriter.Write(local, new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015");
            Assert.DoesNotContain("\r", output); Assert.DoesNotContain("SECRET", output); Assert.DoesNotContain("PRIVATE", output);
            Assert.Contains("\"EnableCaretOverlay\": true", output); Assert.Contains("\"CursorSize\": 24", output);
            Assert.Contains("Українська", output); Assert.DoesNotContain("WelcomeShown", output);
            var read = SettingsFileReader.Read(Encoding.UTF8.GetBytes(output));
            Assert.Equal(SettingsFileInventory.Exported.Length, read.Values.Count);
            var plan = SettingsImportPlan.Build(local, read); Assert.Empty(plan.Changes); Assert.Empty(plan.Issues);
            Assert.Equal(output, SettingsFileWriter.Write(local, new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015"));
        }
        [Fact]
        public void BytesArePinnedByTheV1WriterFixture()
        {
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "SettingsFile", "writer-v1.json");
            Assert.Equal(File.ReadAllText(fixture, Encoding.UTF8), SettingsFileWriter.Write(new AppConfig { UiLanguage = "English" },
                new DateTime(2026, 10, 7, 10, 15, 0, DateTimeKind.Utc), "26.10.7.1015"));
        }
    }
    public class SettingsFileReaderTests
    {
        internal static SettingsFileDocument Read(string settings = "{}", string tables = "{}")
            => SettingsFileReader.Read(Encoding.UTF8.GetBytes("{\"format\":\"CyrFlip Settings\",\"schemaVersion\":1,\"settings\":" + settings + ",\"tables\":" + tables + "}"));
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
            string json = "\ufeff{\r\n\"format\":\"CyrFlip Settings\",\"schemaVersion\":1,\"settings\":{\"UiLanguage\":null}}";
            var local = new AppConfig { UiLanguage = "Deutsch", CursorSize = 48 };
            var plan = SettingsImportPlan.Build(local, SettingsFileReader.Read(Encoding.UTF8.GetBytes(json)));
            Assert.Equal("Deutsch", plan.Candidate.UiLanguage); Assert.Equal(48, plan.Candidate.CursorSize); Assert.Empty(plan.Changes);
        }
        [Theory]
        [InlineData("{\"format\":\"CyrFlip Settings\",\"schemaVersion\":2}", "version")]
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
