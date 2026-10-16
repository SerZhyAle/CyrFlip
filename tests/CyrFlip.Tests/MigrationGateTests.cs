using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The migration gate (S0072 G1, the persistence rule R3): a load-time migration may rewrite a
    /// stored value only when it equals the old shipped default, and the closed list of migrations is
    /// exactly the registry below. A migration that widens itself - or a new one without a row here -
    /// fails the build.
    /// </summary>
    public class MigrationGateTests
    {
        private static string RepoSource() => File.ReadAllText(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip", "AppConfig.cs"));

        /// <summary>Every migration the loader knows, by the method that guards it. A new migration
        /// method in AppConfig.cs without a row here (and without its own two-direction tests) fails.</summary>
        private static readonly string[] Registered =
        {
            "NeedsFlipRow",                    // the legacy Hotkey/EnableFlipHotkey move into the seeded EN⇄RU row
            "MigrateFromJson",                 // the pre-registry config.json read (the same legacy move's source)
            "NeedsScreenshotKeyMigration",     // S0071 4.3: the two dead PrintScreen chords
            "ApplyScreenshotKeyMigration",
        };

        [Fact]
        public void EveryMigrationInTheLoaderHasARegisteredRow()
        {
            var found = new HashSet<string>(Regex.Matches(RepoSource(),
                    @"\b(?:\w+Migration|NeedsFlipRow|MigrateFromJson|NeedsScreenshotKeyMigration|ApplyScreenshotKeyMigration)\b")
                .Cast<Match>().Select(m => m.Value));
            Assert.Equal(Registered.OrderBy(n => n), found.OrderBy(n => n));
        }

        // --- the legacy flip row ---------------------------------------------------------------

        private static MemoryConfigKey Dump(params (string name, object value)[] values)
        {
            var key = new MemoryConfigKey();
            foreach (var (name, value) in values) key.Values[name] = value;
            return key;
        }

        [Fact]
        public void LegacyFlipRow_ValuesMayOnlyMove_WhenTheTableIsAbsent()
        {
            // A profile the table build already wrote: the legacy values are gone, and an emptied
            // table stays empty - the absence of the two values is NOT the marker.
            var empty = Dump();
            var loaded = AppConfig.LoadFrom(empty, out bool mustSave);
            Assert.True(mustSave);                       // the fresh-profile seed (documented: one seeded row)
            Assert.Single(loaded.LayoutConversionProfiles);
            Assert.Equal(AppConfig.DefaultFlipHotkey, loaded.LayoutConversionProfiles[0].Hotkey);

            // The marker is the pair of legacy values: present, they seed a row - exactly one, from them.
            var legacy = Dump(("Hotkey", "Ctrl+Alt+X"), ("EnableFlipHotkey", 0));
            var migrated = AppConfig.LoadFrom(legacy, out mustSave);
            Assert.True(mustSave);
            Assert.Single(migrated.LayoutConversionProfiles);
            Assert.Equal("Ctrl+Alt+X", migrated.LayoutConversionProfiles[0].Hotkey);
            Assert.False(migrated.LayoutConversionProfiles[0].Enabled);

            // A table the user already had, with the legacy values still beside it (a build that had
            // the table but not yet the merge): the rows are read, nothing is added.
            var both = AppConfig.LoadFrom(Dump(("Hotkey", "Ctrl+Alt+X"), ("EnableFlipHotkey", 0),
                ("LayoutConversionProfiles", "[{\"Id\":\"a\",\"Hotkey\":\"Ctrl+Shift+F8\",\"SourceKlid\":\"00000409\",\"TargetKlid\":\"00000419\",\"Enabled\":true}]")), out mustSave);
            Assert.Single(both.LayoutConversionProfiles);
            Assert.Equal("Ctrl+Shift+F8", both.LayoutConversionProfiles[0].Hotkey);
            Assert.False(mustSave); // nothing moved
        }

        // --- the S0071 chord re-key --------------------------------------------------------------

        [Fact]
        public void ScreenshotReKey_CustomChordsAreNeverTouched()
        {
            var key = Dump(("ScreenshotHotkey", "Ctrl+Shift+Y"), ("ScreenshotFolderHotkey", "Ctrl+Alt+Y"));
            var loaded = AppConfig.LoadFrom(key, out bool mustSave);
            Assert.Equal("Ctrl+Shift+Y", loaded.ScreenshotHotkey);
            Assert.Equal("Ctrl+Alt+Y", loaded.ScreenshotFolderHotkey);
            Assert.True(loaded.ScreenshotKeyMigrationDone); // the pass runs once and marks, even when nothing moved
            Assert.True(mustSave);                          // the marker is persisted (documented)
        }

        [Fact]
        public void ScreenshotReKey_OnlyTheOldShippedDefaultsMove()
        {
            var key = Dump(("ScreenshotHotkey", "Ctrl+PrintScreen"), ("ScreenshotFolderHotkey", "Ctrl+Shift+PrintScreen"),
                ("TranslateModel", "kept-model"));
            var loaded = AppConfig.LoadFrom(key, out bool mustSave);
            Assert.Equal(AppConfig.DefaultScreenshotHotkey, loaded.ScreenshotHotkey);
            Assert.Equal(AppConfig.DefaultScreenshotFolderHotkey, loaded.ScreenshotFolderHotkey);
            Assert.Equal("kept-model", loaded.TranslateModel); // a bystander keeps its stored value
            Assert.True(loaded.ScreenshotKeyMigrationDone);
            Assert.True(mustSave);
        }

        [Fact]
        public void ScreenshotReKey_NeverRunsTwice()
        {
            var key = Dump(("ScreenshotHotkey", "Ctrl+PrintScreen"), ("ScreenshotFolderHotkey", "Ctrl+Shift+PrintScreen"),
                ("ScreenshotKeyMigrationDone", 1), ("LayoutConversionProfiles", "[]"));
            var loaded = AppConfig.LoadFrom(key, out bool mustSave);
            Assert.Equal("Ctrl+PrintScreen", loaded.ScreenshotHotkey); // stored old defaults are theirs now
            Assert.Equal("Ctrl+Shift+PrintScreen", loaded.ScreenshotFolderHotkey);
            Assert.False(mustSave);
        }
    }
}
