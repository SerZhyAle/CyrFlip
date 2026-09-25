using System;
using System.Collections.Generic;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0007 CF-1 / CF-2: a stored value of the wrong kind costs only itself, a table or
    /// snapshot that cannot be read is never saved over, numbers are clamped to what their consumer
    /// can take, and a hand-edited row with nulls or a duplicate id loads repaired instead of crashing
    /// every start. All against an in-memory key - no test touches the developer's real settings.
    /// </summary>
    public class AppConfigLoadTests
    {
        private sealed class FakeKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Written = new List<string>();

            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;

            public void SetValue(string name, object value, RegistryValueKind kind)
            {
                Values[name] = value;
                Written.Add(name);
            }
        }

        private static AppConfig Load(FakeKey key) => AppConfig.LoadFrom(key, out _);

        private const string OneRow = "[{\"Id\":\"a\",\"SourceKlid\":\"00000409\",\"TargetKlid\":\"00000419\",\"Hotkey\":\"Ctrl+Shift+F12\",\"Enabled\":true}]";

        [Fact]
        public void AValueOfTheWrongKindDoesNotCostTheValuesAfterIt()
        {
            var key = new FakeKey();
            key.Values["SettingsTab"] = "";                 // REG_SZ where a DWORD belongs
            key.Values["EnableCaretOverlay"] = new byte[] { 1, 0, 0, 0 };
            key.Values["CaseHotkey"] = "Ctrl+Shift+F7";
            key.Values["LayoutConversionProfiles"] = OneRow;
            key.Values["InputLayoutsBackup"] = "{\"format\":2}";
            key.Values["QuickNoteCount"] = 7;

            AppConfig cfg = Load(key);

            Assert.Equal(0, cfg.SettingsTab);
            Assert.True(cfg.EnableCaretOverlay);             // its default
            Assert.Equal("Ctrl+Shift+F7", cfg.CaseHotkey);
            Assert.Single(cfg.LayoutConversionProfiles);
            Assert.Equal("{\"format\":2}", cfg.InputLayoutsBackup);
            Assert.Equal(7, cfg.QuickNoteCount);
            Assert.Contains("SettingsTab", cfg.UnreadableValues);
            Assert.Contains("EnableCaretOverlay", cfg.UnreadableValues);
            Assert.Equal(2, cfg.UnreadableValues.Count);
        }

        [Theory]
        [InlineData("5", 5)]
        [InlineData(" 12 ", 12)]
        public void ANumberStoredAsTextStillReads(string stored, int expected)
        {
            var key = new FakeKey();
            key.Values["FlipCount"] = stored;

            AppConfig cfg = Load(key);

            Assert.Equal(expected, cfg.FlipCount);
            Assert.Empty(cfg.UnreadableValues);
        }

        [Fact]
        public void AMalformedTableIsLeftOnDiskUntilTheUserEditsIt()
        {
            var key = new FakeKey();
            key.Values["LayoutConversionProfiles"] = "[{broken";

            AppConfig cfg = Load(key);
            Assert.Empty(cfg.LayoutConversionProfiles);
            Assert.Contains("LayoutConversionProfiles", cfg.UnreadableValues);

            cfg.SaveTo(key);
            Assert.Equal("[{broken", key.Values["LayoutConversionProfiles"]);

            // Once the user adds a row, the table is theirs again and is written.
            cfg.LayoutConversionProfiles.Add(AppConfig.FlipRow(null, true));
            cfg.SaveTo(key);
            Assert.NotEqual("[{broken", key.Values["LayoutConversionProfiles"]);
            Assert.DoesNotContain("LayoutConversionProfiles", cfg.UnreadableValues);
        }

        [Fact]
        public void AnUnreadableTableIsNeverSeededOver()
        {
            var key = new FakeKey();
            key.Values["LayoutConversionProfiles"] = new string[] { "not", "a", "string" };
            key.Values["Hotkey"] = "Ctrl+Shift+F12";   // the legacy marker that would normally seed

            AppConfig cfg = AppConfig.LoadFrom(key, out bool mustSave);

            Assert.False(mustSave);
            Assert.Empty(cfg.LayoutConversionProfiles);
        }

        [Fact]
        public void AnUnreadableBackupIsNeverReplacedByAnEmptyOne()
        {
            var key = new FakeKey();
            byte[] raw = { 1, 2, 3 };
            key.Values["LanguageHotkeysBackup"] = raw;
            key.Values["InputLayoutsBackup"] = raw;

            AppConfig cfg = Load(key);
            cfg.SaveTo(key);

            Assert.Same(raw, key.Values["LanguageHotkeysBackup"]);
            Assert.Same(raw, key.Values["InputLayoutsBackup"]);
        }

        [Fact]
        public void EveryNumberIsClampedToWhatItsConsumerCanTake()
        {
            var key = new FakeKey();
            key.Values["TranslateTimeoutSeconds"] = int.MaxValue;
            key.Values["TranslateWindowTimeout"] = int.MaxValue;
            key.Values["TranslateKeepAliveMinutes"] = int.MaxValue;
            key.Values["CursorSize"] = 0;

            AppConfig cfg = Load(key);

            Assert.Equal(600, cfg.TranslateTimeoutSeconds);
            Assert.Equal(600, cfg.TranslateWindowTimeout);
            Assert.Equal(120, cfg.TranslateKeepAliveMinutes);
            Assert.Equal(12, cfg.CursorSize);
        }

        [Fact]
        public void ANullFieldADroppedRowAndADuplicateIdLoadRepaired()
        {
            var key = new FakeKey();
            key.Values["LayoutConversionProfiles"] =
                "[null,{\"Id\":\"a\",\"SourceKlid\":\"00000409\",\"TargetKlid\":\"00000419\",\"Hotkey\":null},"
                + "{\"Id\":\"a\",\"SourceKlid\":null,\"TargetKlid\":\"00000422\",\"Hotkey\":\"Ctrl+Shift+F8\"}]";
            key.Values["TranslateProfiles"] = "[{\"Id\":\"t\",\"TargetLang\":null,\"Hotkey\":null},null]";

            AppConfig cfg = AppConfig.LoadFrom(key, out bool mustSave);

            Assert.True(mustSave); // the repaired form is written back
            Assert.Equal(2, cfg.LayoutConversionProfiles.Count);
            Assert.Equal("", cfg.LayoutConversionProfiles[0].Hotkey);
            Assert.Equal("", cfg.LayoutConversionProfiles[1].SourceKlid);
            Assert.NotEqual(cfg.LayoutConversionProfiles[0].Id, cfg.LayoutConversionProfiles[1].Id);
            Assert.All(cfg.LayoutConversionProfiles, p => Assert.False(p.IsUsable));

            TranslationProfile translation = Assert.Single(cfg.TranslateProfiles);
            Assert.Equal("", translation.TargetLang);
            Assert.False(translation.IsUsable);
        }

        [Fact]
        public void ACleanTableNeedsNoRepair()
        {
            var key = new FakeKey();
            key.Values["LayoutConversionProfiles"] = OneRow;

            AppConfig.LoadFrom(key, out bool mustSave);

            Assert.False(mustSave);
        }
    }
}
