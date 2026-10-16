using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0056, rule B1: the predicates that decide whether a chord is bound were extracted into
    /// <see cref="ChordBinding"/>, and the hook and the overview must answer from them. The reference formulas
    /// here are written out from the code as it stood before the extraction (the <c>*ChordLive</c> properties of
    /// <c>CyrFlipContext</c> and the <c>Is*Active</c> predicates of <c>ChordBinding</c>), not called, so the test
    /// cannot pass by comparing the code with itself. The second half holds the overview
    /// (<see cref="ChordBinding.Evaluate"/>) to the same answer, so the page and the hook cannot disagree.
    /// </summary>
    public class ChordBindingTests
    {
        // A valid chord, no chord, and a chord with an unknown token: every chord text the matrix varies over.
        private static readonly string[] Chords = { "Ctrl+Shift+F7", "", "Ctrl+Shift+Hyper+F7" };

        // The kinds travel as names: ChordKind is internal, and a public test method cannot take an internal parameter.
        public static IEnumerable<object[]> FixedKinds()
        {
            yield return new object[] { nameof(ChordKind.Case) };
            yield return new object[] { nameof(ChordKind.History) };
            yield return new object[] { nameof(ChordKind.QuickNotes) };
            yield return new object[] { nameof(ChordKind.Symbols) };
            yield return new object[] { nameof(ChordKind.Screenshot) };
            yield return new object[] { nameof(ChordKind.ScreenshotFull) };
            yield return new object[] { nameof(ChordKind.ScreenshotAnnotateArea) };
            yield return new object[] { nameof(ChordKind.ScreenshotFullAnnotate) };
            yield return new object[] { nameof(ChordKind.ScreenshotFolder) };
            yield return new object[] { nameof(ChordKind.TextMenu) };
        }

        private static ChordKind Kind(string name) => (ChordKind)Enum.Parse(typeof(ChordKind), name);

        /// <summary>Master switch x module switch x the chord's own switch x chord text.</summary>
        private static IEnumerable<(bool Master, bool Module, bool Row, string Chord)> Matrix()
        {
            foreach (bool master in new[] { false, true })
                foreach (bool module in new[] { false, true })
                    foreach (bool row in new[] { false, true })
                        foreach (string chord in Chords)
                            yield return (master, module, row, chord);
        }

        // ---- The five *ChordLive properties of CyrFlipContext, before ticket S0056 ----

        private static bool OldSymbolsLive(AppConfig c) => c.EnableSymbols && c.EnableSymbolsHotkey;
        private static bool OldScreenshotLive(AppConfig c) => c.EnableScreenshotHotkey;
        private static bool OldScreenshotFullLive(AppConfig c) => c.EnableScreenshotFullHotkey;
        private static bool OldScreenshotAnnotateAreaLive(AppConfig c) => c.EnableScreenshotAnnotate && c.EnableScreenshotAnnotateAreaHotkey;
        private static bool OldScreenshotFullAnnotateLive(AppConfig c) => c.EnableScreenshotAnnotate && c.EnableScreenshotFullAnnotateHotkey;
        private static bool OldScreenshotFolderLive(AppConfig c) => c.ScreenshotSaveEnabled && c.EnableScreenshotFolderHotkey;
        private static bool OldTextMenuLive(AppConfig c) => c.EnableContextMenu && c.EnableTextMenuHotkey;
        private static bool OldQuickNotesLive(AppConfig c) => c.EnableQuickNotes && c.EnableQuickNotesHotkey;

        // ---- The Is*Active predicates of ChordBinding, before ticket S0056 (the hook's own condition) ----

        private static bool OldIsActive(ChordKind kind, AppConfig c) => kind switch
        {
            ChordKind.Case => c.EnableHotkeys && c.EnableCaseHotkey && Hotkey.TryParse(c.CaseHotkey, out _),
            ChordKind.History => c.EnableHotkeys && c.EnableHistoryHotkey && Hotkey.TryParse(c.ClipboardHistoryHotkey, out _),
            ChordKind.QuickNotes => c.EnableHotkeys && c.EnableQuickNotes && c.EnableQuickNotesHotkey && Hotkey.TryParse(c.QuickNotesHotkey, out _),
            ChordKind.Symbols => c.EnableHotkeys && c.EnableSymbols && c.EnableSymbolsHotkey && Hotkey.TryParse(c.SymbolsHotkey, out _),
            ChordKind.Screenshot => c.EnableHotkeys && c.EnableScreenshotHotkey && Hotkey.TryParse(c.ScreenshotHotkey, out _),
            ChordKind.ScreenshotFull => c.EnableHotkeys && c.EnableScreenshotFullHotkey && Hotkey.TryParse(c.ScreenshotFullHotkey, out _),
            ChordKind.ScreenshotAnnotateArea => c.EnableHotkeys && c.EnableScreenshotAnnotate && c.EnableScreenshotAnnotateAreaHotkey && Hotkey.TryParse(c.ScreenshotAnnotateAreaHotkey, out _),
            ChordKind.ScreenshotFullAnnotate => c.EnableHotkeys && c.EnableScreenshotAnnotate && c.EnableScreenshotFullAnnotateHotkey && Hotkey.TryParse(c.ScreenshotFullAnnotateHotkey, out _),
            ChordKind.ScreenshotFolder => c.EnableHotkeys && c.ScreenshotSaveEnabled && c.EnableScreenshotFolderHotkey && Hotkey.TryParse(c.ScreenshotFolderHotkey, out _),
            ChordKind.TextMenu => c.EnableHotkeys && c.EnableContextMenu && c.EnableTextMenuHotkey && Hotkey.TryParse(c.TextMenuHotkey, out _),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        private static bool NewIsActive(ChordKind kind, AppConfig c) => kind switch
        {
            ChordKind.Case => ChordBinding.IsCaseActive(c),
            ChordKind.History => ChordBinding.IsHistoryActive(c),
            ChordKind.QuickNotes => ChordBinding.IsQuickNotesActive(c),
            ChordKind.Symbols => ChordBinding.IsSymbolsActive(c),
            ChordKind.Screenshot => ChordBinding.IsScreenshotActive(c),
            ChordKind.ScreenshotFull => ChordBinding.IsScreenshotFullActive(c),
            ChordKind.ScreenshotAnnotateArea => ChordBinding.IsScreenshotAnnotateAreaActive(c),
            ChordKind.ScreenshotFullAnnotate => ChordBinding.IsScreenshotFullAnnotateActive(c),
            ChordKind.ScreenshotFolder => ChordBinding.IsScreenshotFolderActive(c),
            ChordKind.TextMenu => ChordBinding.IsTextMenuActive(c),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>The module switch as the kind's predicate reads it; a kind with no module switch always has one.</summary>
        private static bool ModuleOf(ChordKind kind, (bool Master, bool Module, bool Row, string Chord) m)
            => kind is ChordKind.QuickNotes or ChordKind.Symbols or ChordKind.ScreenshotFolder or ChordKind.TextMenu
                or ChordKind.ScreenshotAnnotateArea or ChordKind.ScreenshotFullAnnotate
                ? m.Module
                : true;

        /// <summary>A configuration whose switches and chord text are exactly the matrix point.</summary>
        private static AppConfig Config(ChordKind kind, (bool Master, bool Module, bool Row, string Chord) m)
        {
            var c = new AppConfig { EnableHotkeys = m.Master };
            switch (kind)
            {
                case ChordKind.Case: c.EnableCaseHotkey = m.Row; c.CaseHotkey = m.Chord; break;
                case ChordKind.History: c.EnableHistoryHotkey = m.Row; c.ClipboardHistoryHotkey = m.Chord; break;
                case ChordKind.QuickNotes: c.EnableQuickNotes = m.Module; c.EnableQuickNotesHotkey = m.Row; c.QuickNotesHotkey = m.Chord; break;
                case ChordKind.Symbols: c.EnableSymbols = m.Module; c.EnableSymbolsHotkey = m.Row; c.SymbolsHotkey = m.Chord; break;
                case ChordKind.Screenshot: c.EnableScreenshotHotkey = m.Row; c.ScreenshotHotkey = m.Chord; break;
                case ChordKind.ScreenshotFull: c.EnableScreenshotFullHotkey = m.Row; c.ScreenshotFullHotkey = m.Chord; break;
                case ChordKind.ScreenshotAnnotateArea: c.EnableScreenshotAnnotate = m.Module; c.EnableScreenshotAnnotateAreaHotkey = m.Row; c.ScreenshotAnnotateAreaHotkey = m.Chord; break;
                case ChordKind.ScreenshotFullAnnotate: c.EnableScreenshotAnnotate = m.Module; c.EnableScreenshotFullAnnotateHotkey = m.Row; c.ScreenshotFullAnnotateHotkey = m.Chord; break;
                case ChordKind.ScreenshotFolder: c.ScreenshotSaveEnabled = m.Module; c.EnableScreenshotFolderHotkey = m.Row; c.ScreenshotFolderHotkey = m.Chord; break;
                case ChordKind.TextMenu: c.EnableContextMenu = m.Module; c.EnableTextMenuHotkey = m.Row; c.TextMenuHotkey = m.Chord; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return c;
        }

        [Fact]
        public void TheFiveLiveConditionsAreTheOldOnesOverTheWholeSwitchMatrix()
        {
            foreach (var m in Matrix())
            {
                var c = Config(ChordKind.Symbols, m);
                Assert.Equal(OldSymbolsLive(c), ChordBinding.SymbolsLive(c));
                c = Config(ChordKind.Screenshot, m);
                Assert.Equal(OldScreenshotLive(c), ChordBinding.ScreenshotLive(c));
                c = Config(ChordKind.ScreenshotFull, m);
                Assert.Equal(OldScreenshotFullLive(c), ChordBinding.ScreenshotFullLive(c));
                c = Config(ChordKind.ScreenshotAnnotateArea, m);
                Assert.Equal(OldScreenshotAnnotateAreaLive(c), ChordBinding.ScreenshotAnnotateAreaLive(c));
                c = Config(ChordKind.ScreenshotFullAnnotate, m);
                Assert.Equal(OldScreenshotFullAnnotateLive(c), ChordBinding.ScreenshotFullAnnotateLive(c));
                c = Config(ChordKind.ScreenshotFolder, m);
                Assert.Equal(OldScreenshotFolderLive(c), ChordBinding.ScreenshotFolderLive(c));
                c = Config(ChordKind.TextMenu, m);
                Assert.Equal(OldTextMenuLive(c), ChordBinding.TextMenuLive(c));
                c = Config(ChordKind.QuickNotes, m);
                Assert.Equal(OldQuickNotesLive(c), ChordBinding.QuickNotesLive(c));
            }
        }

        [Theory]
        [MemberData(nameof(FixedKinds))]
        public void TheFixedPredicateIsTheOldFormulaOverTheWholeSwitchMatrix(string name)
        {
            ChordKind kind = Kind(name);
            foreach (var m in Matrix())
            {
                AppConfig c = Config(kind, m);
                Assert.True(OldIsActive(kind, c) == NewIsActive(kind, c),
                    $"{kind}: master={m.Master} module={m.Module} row={m.Row} chord='{m.Chord}'");
            }
        }

        [Theory]
        [MemberData(nameof(FixedKinds))]
        public void TheOverviewCallsAFixedChordActiveExactlyWhenTheHookBindsIt(string name)
        {
            ChordKind kind = Kind(name);
            foreach (var m in Matrix())
            {
                AppConfig c = Config(kind, m);
                ChordBinding binding = ChordBinding.Evaluate(c, kind, "", m.Chord, m.Row, ModuleOf(kind, m));
                Assert.True(NewIsActive(kind, c) == binding.IsActive,
                    $"{kind}: master={m.Master} module={m.Module} row={m.Row} chord='{m.Chord}' -> {binding.State}");
            }
        }

        [Fact]
        public void AConversionRowIsBoundExactlyWhenItIsUsableEnabledAndThePredicateSaysSo()
        {
            foreach (bool master in new[] { false, true })
                foreach (bool enabled in new[] { false, true })
                    foreach (bool usable in new[] { false, true })
                        foreach (string chord in Chords)
                        {
                            var c = new AppConfig { EnableHotkeys = master };
                            var profile = new LayoutConversionProfile
                            {
                                Id = "row", SourceKlid = usable ? "00000409" : "", TargetKlid = "00000419",
                                Hotkey = chord, Enabled = enabled,
                            };
                            bool hook = ChordBinding.IsConversionActive(c, profile);
                            ChordBinding binding = ChordBinding.Evaluate(c, ChordKind.Conversion, profile.Id, chord,
                                enabled, true, null, profile, usable: profile.IsUsable);

                            Assert.Equal(master && enabled && usable && Hotkey.TryParse(chord, out _), hook);
                            Assert.True(hook == binding.IsActive, $"master={master} enabled={enabled} usable={usable} chord='{chord}' -> {binding.State}");
                        }
        }

        [Fact]
        public void ATranslationRowIsBoundExactlyWhenItIsUsableEnabledAndTheTranslatorIsOn()
        {
            foreach (bool master in new[] { false, true })
                foreach (bool module in new[] { false, true })
                    foreach (bool enabled in new[] { false, true })
                        foreach (bool usable in new[] { false, true })
                            foreach (string chord in Chords)
                            {
                                var c = new AppConfig { EnableHotkeys = master, EnableTranslate = module };
                                var profile = new TranslationProfile
                                {
                                    Id = "row", TargetLang = usable ? "ru" : "", Hotkey = chord, Enabled = enabled,
                                };
                                bool hook = ChordBinding.IsTranslationActive(c, profile);
                                ChordBinding binding = ChordBinding.Evaluate(c, ChordKind.Translation, profile.Id, chord,
                                    enabled, module, null, profile, usable: profile.IsUsable);

                                Assert.Equal(master && module && enabled && usable && Hotkey.TryParse(chord, out _), hook);
                                Assert.True(hook == binding.IsActive, $"master={master} module={module} enabled={enabled} usable={usable} chord='{chord}' -> {binding.State}");
                            }
        }

        [Fact]
        public void ALauncherScenarioIsBoundExactlyWhenTheLauncherIsOn()
        {
            foreach (bool master in new[] { false, true })
                foreach (bool module in new[] { false, true })
                    foreach (string chord in Chords)
                    {
                        var c = new AppConfig { EnableHotkeys = master, EnableScenarioLauncher = module };
                        var scenario = new LauncherScenario { Id = Guid.NewGuid(), Name = "Calc", Path = "calc.exe", Hotkey = chord };
                        bool hook = ChordBinding.IsLauncherActive(c, scenario);
                        ChordBinding binding = ChordBinding.Evaluate(c, ChordKind.Launcher, scenario.Id.ToString(), chord,
                            true, module, null, scenario);

                        Assert.Equal(hook, binding.IsActive);
                    }
        }

        [Fact]
        public void AConversionRowWithoutItsLanguagesIsIncompleteInTheOverviewNotActive()
        {
            var config = new AppConfig();
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            { Id = "broken", SourceKlid = "", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F8", Enabled = true });

            ChordOverviewRow row = ChordOverviewModel.Build(config, null, null, null, "English", klid => klid)
                .Single(r => r.Kind == ChordKind.Conversion && r.Id == "broken");

            Assert.Equal(ChordBindingState.Incomplete, row.State);
            Assert.False(ChordBinding.IsConversionActive(config, config.LayoutConversionProfiles[0]));
        }

        [Fact]
        public void ATranslationRowWithoutATargetIsIncompleteInTheOverviewNotActive()
        {
            var config = new AppConfig { EnableTranslate = true };
            config.TranslateProfiles.Add(new TranslationProfile { Id = "broken", TargetLang = "", Hotkey = "Ctrl+Shift+F9", Enabled = true });

            ChordOverviewRow row = ChordOverviewModel.Build(config, null, null, null, "English", klid => klid)
                .Single(r => r.Kind == ChordKind.Translation && r.Id == "broken");

            Assert.Equal(ChordBindingState.Incomplete, row.State);
            Assert.False(ChordBinding.IsTranslationActive(config, config.TranslateProfiles[0]));
        }
    }
}
