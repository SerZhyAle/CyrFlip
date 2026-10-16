using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0071, phases 1-2: the re-keyed shipped chords, the five-capture chord surface of a
    /// fresh profile, and the one-time ScreenshotKeyMigrationDone migration (spec section 4.3) - all
    /// against an in-memory key; no test touches the developer's real settings.
    /// </summary>
    public class S0071ScreenshotConfigTests
    {
        private sealed class FakeKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        }

        private static AppConfig Load(FakeKey key) => AppConfig.LoadFrom(key, out _);

        private static FakeKey Saved(AppConfig cfg)
        {
            var key = new FakeKey();
            cfg.SaveTo(key);
            return key;
        }

        // ---- The fresh-profile chord surface -----------------------------------------------------

        [Fact]
        public void AFreshProfileParsesAllFiveCaptureChords()
        {
            var config = new AppConfig();
            foreach (string token in new[]
                     {
                         config.ScreenshotHotkey, config.ScreenshotFullHotkey, config.ScreenshotAnnotateAreaHotkey,
                         config.ScreenshotFullAnnotateHotkey, config.ScreenshotFolderHotkey,
                     })
                Assert.True(Hotkey.TryParse(token, out _), token);
        }

        [Fact]
        public void TheFiveCaptureChordsAreDistinctFromEachOtherAndFromEveryOtherShippedChord()
        {
            var config = new AppConfig();
            var capture = new[]
            {
                config.ScreenshotHotkey, config.ScreenshotFullHotkey, config.ScreenshotAnnotateAreaHotkey,
                config.ScreenshotFullAnnotateHotkey, config.ScreenshotFolderHotkey,
            };
            var others = new List<string>();
            foreach (ChordKind kind in Enum.GetValues(typeof(ChordKind)).Cast<ChordKind>().Where(ChordDefaults.IsFixed))
                if (kind is not (ChordKind.Screenshot or ChordKind.ScreenshotFull or ChordKind.ScreenshotAnnotateArea
                    or ChordKind.ScreenshotFullAnnotate or ChordKind.ScreenshotFolder))
                    others.Add(ChordDefaults.FixedChord(kind));
            others.Add(ChordDefaults.SeededConversionChord);
            others.Add(ChordDefaults.SeededTranslationChord);
            others.Add(ChordDefaults.MouseChordToken);

            for (int i = 0; i < capture.Length; i++)
            {
                for (int j = i + 1; j < capture.Length; j++)
                    Assert.False(ChordDefaults.SameChordToken(capture[i], capture[j]), $"{capture[i]} == {capture[j]}");
                foreach (string other in others)
                    Assert.False(ChordDefaults.SameChordToken(capture[i], other), $"{capture[i]} == {other}");
            }
        }

        [Fact]
        public void AFreshProfileStartsWithTheDrawModuleAndSavingOn()
        {
            var config = new AppConfig();
            Assert.True(config.EnableScreenshotAnnotate);
            Assert.True(config.ScreenshotSaveEnabled);
            Assert.True(config.EnableScreenshotFullHotkey);
            Assert.True(config.EnableScreenshotAnnotateAreaHotkey);
            Assert.True(config.EnableScreenshotFullAnnotateHotkey);
        }

        // ---- The one-time migration (spec 4.3) ---------------------------------------------------

        [Fact]
        public void TheLegacyChordsMoveToTheNewDefaults()
        {
            var key = new FakeKey();
            key.Values["ScreenshotHotkey"] = AppConfig.LegacyScreenshotHotkey;
            key.Values["ScreenshotFolderHotkey"] = AppConfig.LegacyScreenshotFolderHotkey;

            AppConfig cfg = Load(key);
            Assert.Equal(AppConfig.DefaultScreenshotHotkey, cfg.ScreenshotHotkey);
            Assert.Equal(AppConfig.DefaultScreenshotFolderHotkey, cfg.ScreenshotFolderHotkey);
            Assert.True(cfg.ScreenshotKeyMigrationDone);
        }

        [Fact]
        public void ACustomisedChordIsNeverTouched()
        {
            var key = new FakeKey();
            key.Values["ScreenshotHotkey"] = "Ctrl+Shift+F7";
            key.Values["ScreenshotFolderHotkey"] = "Ctrl+Alt+O";

            AppConfig cfg = Load(key);
            Assert.Equal("Ctrl+Shift+F7", cfg.ScreenshotHotkey);
            Assert.Equal("Ctrl+Alt+O", cfg.ScreenshotFolderHotkey);
            Assert.True(cfg.ScreenshotKeyMigrationDone);
        }

        [Fact]
        public void AProfileAlreadyOnTheNewDefaultsIsUntouchedAndStillGetsTheMarker()
        {
            var key = new FakeKey();
            key.Values["ScreenshotHotkey"] = AppConfig.DefaultScreenshotHotkey;
            key.Values["ScreenshotFolderHotkey"] = AppConfig.DefaultScreenshotFolderHotkey;

            AppConfig cfg = Load(key);
            Assert.Equal(AppConfig.DefaultScreenshotHotkey, cfg.ScreenshotHotkey);
            Assert.True(cfg.ScreenshotKeyMigrationDone);
        }

        [Fact]
        public void EverySwitchAndTheMasterSwitchSurviveTheMigration()
        {
            var key = new FakeKey();
            key.Values["ScreenshotHotkey"] = AppConfig.LegacyScreenshotHotkey;
            key.Values["ScreenshotFolderHotkey"] = AppConfig.LegacyScreenshotFolderHotkey;
            key.Values["EnableScreenshotHotkey"] = 0;
            key.Values["EnableScreenshotFolderHotkey"] = 0;
            key.Values["EnableScreenshotFullHotkey"] = 0;
            key.Values["EnableScreenshotAnnotate"] = 0;
            key.Values["EnableScreenshotAnnotateAreaHotkey"] = 0;
            key.Values["EnableScreenshotFullAnnotateHotkey"] = 0;
            key.Values["EnableHotkeys"] = 0;
            // A profile that had saving off keeps it off: the save-default flip is for fresh profiles only (Q2).
            key.Values["ScreenshotSaveEnabled"] = 0;

            AppConfig cfg = Load(key);
            Assert.False(cfg.EnableScreenshotHotkey);
            Assert.False(cfg.EnableScreenshotFolderHotkey);
            Assert.False(cfg.EnableScreenshotFullHotkey);
            Assert.False(cfg.EnableScreenshotAnnotate);
            Assert.False(cfg.EnableScreenshotAnnotateAreaHotkey);
            Assert.False(cfg.EnableScreenshotFullAnnotateHotkey);
            Assert.False(cfg.EnableHotkeys);
            Assert.False(cfg.ScreenshotSaveEnabled);
        }

        [Fact]
        public void TheCaseFlipAndTheSeededRowChordsAreNeverRewritten()
        {
            var key = new FakeKey();
            key.Values["CaseHotkey"] = "Ctrl+Shift+F11";
            key.Values["ScreenshotHotkey"] = AppConfig.LegacyScreenshotHotkey;
            key.Values["LayoutConversionProfiles"] =
                "[{\"Id\":\"a\",\"SourceKlid\":\"00000409\",\"TargetKlid\":\"00000419\",\"Hotkey\":\"Ctrl+Shift+F12\",\"Enabled\":true}]";

            AppConfig cfg = Load(key);
            Assert.Equal("Ctrl+Shift+F11", cfg.CaseHotkey);
            Assert.Equal("Ctrl+Shift+F12", cfg.LayoutConversionProfiles[0].Hotkey);
        }

        [Fact]
        public void TheMigrationRunsExactlyOnce()
        {
            var key = new FakeKey();
            key.Values["ScreenshotHotkey"] = AppConfig.LegacyScreenshotHotkey;

            AppConfig first = Load(key);
            Assert.Equal(AppConfig.DefaultScreenshotHotkey, first.ScreenshotHotkey);

            FakeKey saved = Saved(first);
            Assert.Equal(1, saved.Values["ScreenshotKeyMigrationDone"]);

            // A second load - the block does not run again: a user who re-picked the legacy chord
            // afterwards keeps it.
            saved.Values["ScreenshotHotkey"] = AppConfig.LegacyScreenshotHotkey;
            AppConfig second = Load(saved);
            Assert.Equal(AppConfig.LegacyScreenshotHotkey, second.ScreenshotHotkey);
        }

        [Fact]
        public void TheNeedsMarkerGateIsTheMarker()
        {
            Assert.True(AppConfig.NeedsScreenshotKeyMigration(markerPresent: false));
            Assert.False(AppConfig.NeedsScreenshotKeyMigration(markerPresent: true));
        }

        [Fact]
        public void AMovePersistsAtOnceAndANothingMoveDoesNotForceASave()
        {
            // A profile table must be present, or the seeded flip row forces a save on its own.
            string oneRow = "[{\"Id\":\"a\",\"SourceKlid\":\"00000409\",\"TargetKlid\":\"00000419\",\"Hotkey\":\"Ctrl+Shift+F12\",\"Enabled\":true}]";

            var moved = new FakeKey();
            moved.Values["LayoutConversionProfiles"] = oneRow;
            moved.Values["ScreenshotHotkey"] = AppConfig.LegacyScreenshotHotkey;
            AppConfig.LoadFrom(moved, out bool mustSaveMoved);
            Assert.True(mustSaveMoved);

            var untouched = new FakeKey();
            untouched.Values["LayoutConversionProfiles"] = oneRow;
            AppConfig.LoadFrom(untouched, out bool mustSaveNothing);   // nothing to move
            Assert.False(mustSaveNothing);

            var custom = new FakeKey();
            custom.Values["LayoutConversionProfiles"] = oneRow;
            custom.Values["ScreenshotHotkey"] = "Ctrl+Shift+F7";
            AppConfig.LoadFrom(custom, out bool mustSaveCustom);
            Assert.False(mustSaveCustom);
        }

        // ---- Config round-trip and the settings-file surface -------------------------------------

        [Fact]
        public void TheSevenNewValuesSurviveSaveAndLoad()
        {
            var config = new AppConfig
            {
                ScreenshotFullHotkey = "Ctrl+Alt+F7",
                EnableScreenshotFullHotkey = false,
                EnableScreenshotAnnotate = false,
                ScreenshotAnnotateAreaHotkey = "Ctrl+Alt+F8",
                EnableScreenshotAnnotateAreaHotkey = false,
                ScreenshotFullAnnotateHotkey = "Ctrl+Alt+F9",
                EnableScreenshotFullAnnotateHotkey = false,
            };

            AppConfig round = Load(Saved(config));
            Assert.Equal("Ctrl+Alt+F7", round.ScreenshotFullHotkey);
            Assert.False(round.EnableScreenshotFullHotkey);
            Assert.False(round.EnableScreenshotAnnotate);
            Assert.Equal("Ctrl+Alt+F8", round.ScreenshotAnnotateAreaHotkey);
            Assert.False(round.EnableScreenshotAnnotateAreaHotkey);
            Assert.Equal("Ctrl+Alt+F9", round.ScreenshotFullAnnotateHotkey);
            Assert.False(round.EnableScreenshotFullAnnotateHotkey);
        }

        [Fact]
        public void TheSevenNewKeysAreExportedPreferencesInTheCaptureGroup()
        {
            string[] keys =
            {
                "EnableScreenshotFullHotkey", "ScreenshotFullHotkey", "EnableScreenshotAnnotate",
                "EnableScreenshotAnnotateAreaHotkey", "ScreenshotAnnotateAreaHotkey",
                "EnableScreenshotFullAnnotateHotkey", "ScreenshotFullAnnotateHotkey",
            };
            foreach (string key in keys)
            {
                SettingsFileEntry entry = SettingsFileInventory.Exported.Single(e => e.Key == key);
                Assert.Equal("graphics/capture", entry.Group);
                Assert.False(SettingsFileInventory.Excluded.ContainsKey(key));
            }
        }

        [Fact]
        public void TheMigrationMarkerIsMachineHistoryAndNeverExports()
        {
            Assert.True(SettingsFileInventory.Excluded["ScreenshotKeyMigrationDone"] == "machine history");
            Assert.DoesNotContain(SettingsFileInventory.Exported, e => e.Key == "ScreenshotKeyMigrationDone");
        }

        [Fact]
        public void ASettingsFileWithoutTheNewKeysImportsIntoTheDefaults()
        {
            // A v1 exchange file carries none of the S0071 values: the additive rule - what is not
            // there imports as "leave the local value", so the local defaults stand.
            var key = new FakeKey();
            key.Values["UiLanguage"] = "English";
            key.Values["ScreenshotHotkey"] = "Ctrl+F12";

            AppConfig local = Load(key);
            var document = new SettingsFileDocument();
            document.Values["UiLanguage"] = "English";
            SettingsImportPlan plan = SettingsImportPlan.Build(local, document);
            Assert.DoesNotContain(plan.Changes, c => c.Entry.Key.StartsWith("Screenshot", StringComparison.Ordinal) && c.Entry.Key != "ScreenshotHotkey");
        }
    }
}
