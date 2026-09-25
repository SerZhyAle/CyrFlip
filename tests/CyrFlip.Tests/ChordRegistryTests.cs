using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>One chord, one owner - whatever is switched off (ticket S0004, KC-6).</summary>
    public class ChordRegistryTests
    {
        private static readonly ChordKind[] CyrFlipKinds =
        {
            ChordKind.Case, ChordKind.History, ChordKind.QuickNotes,
            ChordKind.Conversion, ChordKind.Translation, ChordKind.Launcher,
        };

        private static AppConfig EmptyConfig()
        {
            var config = new AppConfig
            {
                CaseHotkey = "Ctrl+Shift+F1",
                ClipboardHistoryHotkey = "Ctrl+Shift+F2",
                QuickNotesHotkey = "Ctrl+Shift+F3",
            };
            config.LayoutConversionProfiles.Clear();
            config.TranslateProfiles.Clear();
            return config;
        }

        /// <summary>Give <paramref name="chord"/> to one owner of <paramref name="kind"/> (switched off where it can be).</summary>
        private static string Own(AppConfig config, List<LauncherScenario> scenarios, ChordKind kind, string chord)
        {
            switch (kind)
            {
                case ChordKind.Case: config.CaseHotkey = chord; config.EnableCaseHotkey = false; return "";
                case ChordKind.History: config.ClipboardHistoryHotkey = chord; config.EnableHistoryHotkey = false; return "";
                case ChordKind.QuickNotes: config.QuickNotesHotkey = chord; config.EnableQuickNotes = false; return "";
                case ChordKind.Conversion:
                    var conversion = new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = chord, Enabled = false };
                    config.LayoutConversionProfiles.Add(conversion);
                    return conversion.Id;
                case ChordKind.Translation:
                    var translation = new TranslationProfile { TargetLang = "en", Hotkey = chord, Enabled = false };
                    config.TranslateProfiles.Add(translation);
                    config.EnableTranslate = false;
                    return translation.Id;
                default:
                    var scenario = new LauncherScenario { Name = "Calc", Hotkey = chord };
                    scenarios.Add(scenario);
                    config.EnableScenarioLauncher = false;
                    return scenario.Id.ToString();
            }
        }

        [Fact]
        public void TheReproIsRefusedAtItsSecondStep()
        {
            // Untick the case flip, then try to give its Ctrl+Shift+F11 to the EN <-> RU row.
            AppConfig config = EmptyConfig();
            config.CaseHotkey = "Ctrl+Shift+F11";
            config.EnableCaseHotkey = false;
            config.EnableHotkeys = false; // the master switch no longer turns the check off either
            ChordRegistry registry = ChordRegistry.Build(config, new LauncherScenario[0], new LanguageHotkeys.Entry[0]);

            ChordOwner? owner = registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Shift+F11"), ChordKind.Conversion, null);
            Assert.NotNull(owner);
            Assert.Equal(ChordKind.Case, owner!.Kind);
        }

        [Fact]
        public void AnOwnerIsNotItsOwnClash()
        {
            AppConfig config = EmptyConfig();
            var scenarios = new List<LauncherScenario>();
            string id = Own(config, scenarios, ChordKind.Conversion, "Ctrl+Alt+F5");
            ChordRegistry registry = ChordRegistry.Build(config, scenarios, new LanguageHotkeys.Entry[0]);

            Assert.Null(registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Alt+F5"), ChordKind.Conversion, id));
            Assert.Null(registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Shift+F1"), ChordKind.Case));
            // A new row (no id yet) has no self: the existing row owns the chord.
            Assert.NotNull(registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Alt+F5"), ChordKind.Conversion, null));
        }

        [Fact]
        public void AWindowsHotkeyIsAQuestionNotARefusal()
        {
            AppConfig config = EmptyConfig();
            var windows = new LanguageHotkeys.Entry
            {
                Id = 0x100, TargetHkl = (IntPtr)0x04190419,
                Modifiers = LanguageHotkeys.MOD_CONTROL | LanguageHotkeys.MOD_LEFT | LanguageHotkeys.MOD_RIGHT,
                VirtualKey = 0x31,
            };
            ChordRegistry registry = ChordRegistry.Build(config, new LauncherScenario[0], new[] { windows });

            Hotkey ctrl1 = Hotkey.Parse("Ctrl+1");
            Assert.Null(registry.CyrFlipOwnerOf(ctrl1, ChordKind.Launcher));
            ChordOwner? owner = registry.WindowsOwnerOf(ctrl1);
            Assert.NotNull(owner);
            Assert.True(owner!.IsWindows);
            Assert.Empty(registry.Duplicates()); // Windows owners never count as CyrFlip duplicates
        }

        [Fact]
        public void EveryKindIsCheckedAgainstEveryOtherKind()
        {
            int pairs = 0;
            foreach (ChordKind holder in CyrFlipKinds)
                foreach (ChordKind asker in CyrFlipKinds)
                {
                    AppConfig config = EmptyConfig();
                    var scenarios = new List<LauncherScenario>();
                    Own(config, scenarios, holder, "Ctrl+Alt+F7");
                    ChordRegistry registry = ChordRegistry.Build(config, scenarios, new LanguageHotkeys.Entry[0]);

                    // The asker is a different action of its kind: a fixed chord is asked about as
                    // itself only when it is not the holder; a row is always a new row here.
                    bool sameFixed = holder == asker && (asker == ChordKind.Case || asker == ChordKind.History || asker == ChordKind.QuickNotes);
                    ChordOwner? owner = registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Alt+F7"), asker, null);
                    if (sameFixed)
                        Assert.Null(owner);
                    else
                    {
                        Assert.True(owner != null, asker + " asked about " + holder + "'s chord");
                        Assert.Equal(holder, owner!.Kind);
                        pairs++;
                    }
                }
            Assert.Equal(6 * 6 - 3, pairs);
        }

        [Fact]
        public void DuplicatesFromAnOldConfigAreFound()
        {
            AppConfig config = EmptyConfig();
            var scenarios = new List<LauncherScenario>();
            Own(config, scenarios, ChordKind.Conversion, "Ctrl+Shift+F1"); // the case chord
            ChordRegistry registry = ChordRegistry.Build(config, scenarios, new LanguageHotkeys.Entry[0]);

            List<KeyValuePair<ChordOwner, ChordOwner>> duplicates = registry.Duplicates();
            Assert.Single(duplicates);
            Assert.Equal(ChordKind.Case, duplicates[0].Key.Kind);
            Assert.Equal(ChordKind.Conversion, duplicates[0].Value.Kind);
        }

        [Fact]
        public void AnUnparsableChordOwnsNothing()
        {
            AppConfig config = EmptyConfig();
            var scenarios = new List<LauncherScenario> { new LauncherScenario { Name = "x", Hotkey = "" } };
            ChordRegistry registry = ChordRegistry.Build(config, scenarios, new LanguageHotkeys.Entry[0]);
            Assert.DoesNotContain(registry.Owners, o => o.Kind == ChordKind.Launcher);
        }

        [Fact]
        public void LabelsNameTheOwner()
        {
            AppConfig config = EmptyConfig();
            var scenarios = new List<LauncherScenario>();
            Own(config, scenarios, ChordKind.Launcher, "Ctrl+Alt+F8");
            ChordRegistry registry = ChordRegistry.Build(config, scenarios, new LanguageHotkeys.Entry[0]);
            ChordOwner owner = registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Alt+F8"), ChordKind.Case)!;
            Assert.Equal("Calc", ChordRegistry.Label(owner, "English", klid => klid));

            ChordOwner caseOwner = registry.CyrFlipOwnerOf(Hotkey.Parse("Ctrl+Shift+F1"), ChordKind.History)!;
            Assert.Equal(Localization.Translate("English", "Исправить CapsLock"), ChordRegistry.Label(caseOwner, "English", klid => klid));
        }
    }
}
