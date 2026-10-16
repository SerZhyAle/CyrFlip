using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The update-preserves golden (S0072 G2): a dump of the registry hive - every value at a
    /// non-default - loaded by the current build answers value-identical. An update resets nothing
    /// (S0072 R1): no load may re-default a stored value. The dump is refreshed at each release:
    /// run the suite with <c>CYRFLIP_REGEN_GOLDEN=1</c> to rewrite it from the builder below.
    /// </summary>
    public class UpdatePreservesGoldenTests
    {
        internal static string DumpPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "tests", "CyrFlip.Tests", "Fixtures", "ConfigDump", "update-golden.json");

        /// <summary>Every value of every home at a deliberate non-default (the load-time homes of
        /// S0072 2.1: the registry hive; the scenario documents ride in the settings-file tests).</summary>
        internal static AppConfig NonDefaultConfig()
        {
            var config = new AppConfig();
            // Strings: a marker each, except the ones the loader parses - those get valid non-defaults.
            foreach (PropertyInfo property in typeof(AppConfig).GetProperties())
            {
                if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0) continue;
                switch (property.Name)
                {
                    case "Theme": config.Theme = "dark"; break;
                    case "ContextMenuChord": config.ContextMenuChord = "Alt+RightClick"; break;
                    case "SettingsGroupsState": config.SettingsGroupsState = "conversions/case=1;general/language=1"; break;
                    case "SettingsViewport": config.SettingsViewport = "General|hotkeys/switches|17"; break;
                    case "UiLanguage": config.UiLanguage = "Українська"; break;
                    case "SymbolsCategory": config.SymbolsCategory = "Arrows"; break;
                    case "QuickNotesSelected": config.QuickNotesSelected = "note-9"; break;
                    case "BackupRestoreDeclined": config.BackupRestoreDeclined = "CyrFlip-settings-26-10-01_0900.json"; break;
                    case "CaseHotkey": config.CaseHotkey = "Ctrl+Alt+F1"; break;
                    case "ClipboardHistoryHotkey": config.ClipboardHistoryHotkey = "Ctrl+Alt+F2"; break;
                    case "TextMenuHotkey": config.TextMenuHotkey = "Ctrl+Alt+F6"; break;
                    case "QuickNotesHotkey": config.QuickNotesHotkey = "Ctrl+Alt+F9"; break;
                    case "ScreenshotHotkey": config.ScreenshotHotkey = "Ctrl+Alt+F3"; break;
                    case "ScreenshotFullHotkey": config.ScreenshotFullHotkey = "Ctrl+Alt+F4"; break;
                    case "ScreenshotAnnotateAreaHotkey": config.ScreenshotAnnotateAreaHotkey = "Ctrl+Shift+PrintScreen"; break;
                    case "ScreenshotFullAnnotateHotkey": config.ScreenshotFullAnnotateHotkey = "Ctrl+Alt+PrintScreen"; break;
                    case "ScreenshotFolderHotkey": config.ScreenshotFolderHotkey = "Ctrl+Alt+F5"; break;
                    case "SymbolsHotkey": config.SymbolsHotkey = "Ctrl+Alt+F7"; break;
                    case "TrafficLightHotkey": config.TrafficLightHotkey = "Ctrl+Alt+F8"; break;
                    case "ExchangeClipboardSendHotkey": config.ExchangeClipboardSendHotkey = "Ctrl+Alt+F10"; break;
                    case "ExchangeClipboardGetHotkey": config.ExchangeClipboardGetHotkey = "Ctrl+Alt+F11"; break;
                    default: property.SetValue(config, "S0072:" + property.Name); break;
                }
            }
            // Bools: every one flipped from its default.
            foreach (PropertyInfo property in typeof(AppConfig).GetProperties())
                if (property.PropertyType == typeof(bool) && property.CanWrite && property.GetIndexParameters().Length == 0)
                    property.SetValue(config, !((bool)property.GetValue(config)!));
            // Ints: within each loader clamp, but never the default.
            var ints = new Dictionary<string, int>
            {
                ["SettingsTab"] = 5,
                ["SettingsWindowX"] = 11, ["SettingsWindowY"] = 12, ["SettingsWindowWidth"] = 1024, ["SettingsWindowHeight"] = 700,
                ["ClipboardHistoryX"] = 21, ["ClipboardHistoryY"] = 22, ["ClipboardHistoryWidth"] = 300, ["ClipboardHistoryHeight"] = 420,
                ["ClipboardHistoryOpacity"] = 70,
                ["CursorSize"] = 36,
                ["TranslateTimeoutSeconds"] = 240, ["TranslateKeepAliveMinutes"] = 45,
                ["TranslateWindowWidth"] = 720, ["TranslateWindowHeight"] = 480, ["TranslateWindowOpacity"] = 80, ["TranslateWindowTimeout"] = 30,
                ["QuickNotesX"] = 31, ["QuickNotesY"] = 32, ["QuickNotesWidth"] = 520, ["QuickNotesHeight"] = 380,
                ["SymbolsX"] = 41, ["SymbolsY"] = 42, ["SymbolsWidth"] = 640, ["SymbolsHeight"] = 480,
                ["TrafficLightTileSize"] = 2, ["TrafficLightColumns"] = 5, ["TrafficLightWindowTransparency"] = 60,
                ["TrafficLightX"] = 51, ["TrafficLightY"] = 52,
                ["FlipCount"] = 3, ["CaseFlipCount"] = 4, ["TranslateCount"] = 5, ["QuickNoteCount"] = 6, ["ScreenshotCount"] = 7, ["SymbolsCount"] = 8,
            };
            foreach (PropertyInfo property in typeof(AppConfig).GetProperties())
                if (property.PropertyType == typeof(int) && property.CanWrite && property.GetIndexParameters().Length == 0)
                    property.SetValue(config, ints.TryGetValue(property.Name, out int value) ? value : 12345);
            // Tables: one row each, non-default; the seeded row is not what ships.
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            { Id = "goldena", Hotkey = "Ctrl+Alt+F7", SourceKlid = "00000409", TargetKlid = "00000422", Enabled = true });
            config.TranslateProfiles.Add(new TranslationProfile
            { Id = "goldenb", Hotkey = "Ctrl+Alt+F8", TargetLang = "de", Enabled = true });
            config.TrafficLightTargets.Add(new TrafficLightTarget
            { Id = "goldenc", Kind = "tcp", Address = "nas.local:445", Label = "NAS", IntervalSeconds = 45 });
            config.ExchangeClipboardAccounts.Add(new ExchangeAccountEntry
            { Id = "goldend", Host = "exchange.test", Port = 22022, Login = "golden", DisplayName = "Golden" });
            return config;
        }

        private static Dictionary<string, object> DumpOf(AppConfig config)
            => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                new JavaScriptSerializer().Serialize(MemoryConfigKey.Capture(config).Values));

        [Fact]
        public void ScenarioHomeLoadsWithoutRewritingAnyDocument()
        {
            string fixtures = Path.Combine(Path.GetDirectoryName(DumpPath)!, "scenarios");
            string folder = Path.Combine(Path.GetTempPath(), "S0072-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                string[] files = Directory.GetFiles(fixtures, "*.xml");
                Assert.NotEmpty(files);
                foreach (string file in files) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
                var store = new LauncherScenarioStore(folder);
                Assert.Empty(store.LoadErrors);
                Assert.Equal(files.Length, store.Count);
                Assert.Equal(files.Length, Directory.GetFiles(folder).Length);
                foreach (string file in files)
                    Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(folder, Path.GetFileName(file))));
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void TheDumpLoadsValueIdentical()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            if (Environment.GetEnvironmentVariable("CYRFLIP_REGEN_GOLDEN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DumpPath)!);
                File.WriteAllText(DumpPath, new JavaScriptSerializer().Serialize(DumpOf(NonDefaultConfig())), new UTF8Encoding(false));
            }
            Assert.True(File.Exists(DumpPath), "the golden dump is missing - run with CYRFLIP_REGEN_GOLDEN=1 once");
            var dump = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(DumpPath, Encoding.UTF8));
            Assert.True(dump.Count > 90, "the dump should hold the whole hive, got " + dump.Count);

            var key = new MemoryConfigKey();
            foreach (var pair in dump) key.Values[pair.Key] = pair.Value;
            var loaded = AppConfig.LoadFrom(key, out bool mustSave);
            Assert.False(mustSave); // the migration registry: nothing moves for a dump like this
            var answered = MemoryConfigKey.Capture(loaded);
            foreach (string name in dump.Keys)
                Assert.True(answered.Values.TryGetValue(name, out object? value) && Equals(dump[name], value),
                    "value re-defaulted or altered at load: " + name);
        }
    }
}
