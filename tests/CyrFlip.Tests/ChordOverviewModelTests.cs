using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure model behind the hotkeys overview (ticket S0056 sections 3-4, 8): one row per binding,
    /// every state reached, a clash marked on both rows, and the inventory gate that fails the build when
    /// a chord is added without a row.
    /// </summary>
    public class ChordOverviewModelTests
    {
        private static readonly Func<string, string> Layout = klid => klid;

        private static List<ChordOverviewRow> Build(AppConfig config, IEnumerable<LauncherScenario>? scenarios = null,
            IEnumerable<LanguageHotkeys.Entry>? windows = null, string? toggle = null)
            => ChordOverviewModel.Build(config, scenarios, windows, toggle, "English", Layout);

        private static ChordOverviewRow Row(IEnumerable<ChordOverviewRow> rows, ChordKind kind, string id = "")
            => rows.Single(r => r.Kind == kind && r.Id == id);

        private static AppConfig WithRows()
        {
            var config = new AppConfig();
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            { Id = "seed", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true });
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            { Id = "mine", SourceKlid = "00000409", TargetKlid = "00000407", Hotkey = "Ctrl+Shift+F8", Enabled = true });
            config.TranslateProfiles.Add(new TranslationProfile { Id = "tr", TargetLang = TranslationLanguages.UiToken, Hotkey = "Ctrl+Shift+F9", Enabled = true });
            return config;
        }

        private static LauncherScenario Scenario(string name, string chord)
            => new LauncherScenario { Id = Guid.NewGuid(), Name = name, Path = "calc.exe", Hotkey = chord };

        private static LanguageHotkeys.Entry WindowsEntry(string chord)
        {
            Hotkey parsed = Hotkey.Parse(chord);
            uint modifiers = (parsed.Ctrl ? LanguageHotkeys.MOD_CONTROL : 0) | (parsed.Shift ? LanguageHotkeys.MOD_SHIFT : 0);
            return new LanguageHotkeys.Entry { Id = 0x101, TargetHkl = new IntPtr(0x04090409), Modifiers = modifiers, VirtualKey = (uint)parsed.Vk };
        }

        [Fact]
        public void ADefaultConfigListsEveryFixedChordAndTheMouseChord()
        {
            List<ChordOverviewRow> rows = Build(new AppConfig());
            Assert.Equal(13, rows.Count(r => r.Group == ChordGroup.Fixed));
            Assert.Single(rows, r => r.Group == ChordGroup.Mouse);
            Assert.All(rows.Where(r => r.Group == ChordGroup.Fixed), r => Assert.True(r.HasSwitch));
            Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Caption)));
        }

        [Fact]
        public void OneRowPerBindingInSixGroups()
        {
            var scenario = Scenario("Calc", "Ctrl+Shift+F6");
            var withoutChord = Scenario("Silent", "");
            List<ChordOverviewRow> rows = Build(WithRows(), new[] { scenario, withoutChord },
                new[] { WindowsEntry("Ctrl+Shift+5") }, "1");

            Assert.Equal(2, rows.Count(r => r.Group == ChordGroup.Conversions));
            Assert.Single(rows, r => r.Group == ChordGroup.Translations);
            // Only a scenario that has a chord is a binding.
            ChordOverviewRow launcher = Assert.Single(rows, r => r.Group == ChordGroup.Launcher);
            Assert.Equal("Calc", launcher.Caption);
            // Windows: the direct-language chord and the layout-cycle line.
            Assert.Equal(2, rows.Count(r => r.Group == ChordGroup.Windows));
            Assert.Equal(Enum.GetValues(typeof(ChordGroup)).Length, rows.Select(r => r.Group).Distinct().Count());
        }

        [Fact]
        public void EveryChordKindHasExactlyOneGroupAndAFullyPopulatedConfigListsEachOfThem()
        {
            // The inventory gate (section 8): a ChordKind added without a place in the overview fails here.
            AppConfig config = WithRows();
            var scenario = Scenario("Calc", "Ctrl+Shift+F6");
            List<ChordOverviewRow> rows = Build(config, new[] { scenario }, new[] { WindowsEntry("Ctrl+Shift+5") });

            foreach (ChordKind kind in Enum.GetValues(typeof(ChordKind)))
            {
                ChordGroup group = ChordGroups.Of(kind);
                Assert.Contains(rows, r => r.Kind == kind && r.Group == group);
                Assert.Equal(1, Enum.GetValues(typeof(ChordGroup)).Cast<ChordGroup>().Count(g => g == group));
            }
        }

        [Fact]
        public void EveryChordBearingConfigPropertyIsInARowOrOnTheReasonedExclusionList()
        {
            // Stored chord tokens on AppConfig: each is one row of the overview, or explained here.
            var shown = new HashSet<string>
            {
                nameof(AppConfig.CaseHotkey), nameof(AppConfig.ClipboardHistoryHotkey), nameof(AppConfig.QuickNotesHotkey),
                nameof(AppConfig.SymbolsHotkey), nameof(AppConfig.TrafficLightHotkey), nameof(AppConfig.ScreenshotHotkey), nameof(AppConfig.ScreenshotFolderHotkey),
                nameof(AppConfig.ScreenshotFullHotkey), nameof(AppConfig.ScreenshotAnnotateAreaHotkey), nameof(AppConfig.ScreenshotFullAnnotateHotkey),
                nameof(AppConfig.ExchangeClipboardSendHotkey), nameof(AppConfig.ExchangeClipboardGetHotkey),
                nameof(AppConfig.TextMenuHotkey), nameof(AppConfig.ContextMenuChord),
                nameof(AppConfig.LayoutConversionProfiles), nameof(AppConfig.TranslateProfiles),
                // The per-chord switches: each is the switch of one fixed row (ChordConfigAccess).
                nameof(AppConfig.EnableCaseHotkey), nameof(AppConfig.EnableHistoryHotkey), nameof(AppConfig.EnableQuickNotesHotkey),
                nameof(AppConfig.EnableSymbolsHotkey), nameof(AppConfig.EnableTrafficLightHotkey), nameof(AppConfig.EnableScreenshotHotkey),
                nameof(AppConfig.EnableScreenshotFolderHotkey), nameof(AppConfig.EnableTextMenuHotkey),
                nameof(AppConfig.EnableScreenshotFullHotkey), nameof(AppConfig.EnableScreenshotAnnotateAreaHotkey), nameof(AppConfig.EnableScreenshotFullAnnotateHotkey),
                nameof(AppConfig.EnableExchangeClipboardSendHotkey), nameof(AppConfig.EnableExchangeClipboardGetHotkey),
            };
            var excluded = new Dictionary<string, string>();   // none today: every chord is listed
            var found = typeof(AppConfig).GetProperties()
                .Where(p => p.Name.EndsWith("Hotkey", StringComparison.Ordinal) || p.Name.EndsWith("Chord", StringComparison.Ordinal)
                            || p.Name.EndsWith("Profiles", StringComparison.Ordinal))
                .Select(p => p.Name).ToList();
            Assert.NotEmpty(found);
            Assert.True(found.All(n => shown.Contains(n) || excluded.ContainsKey(n)),
                "A chord on AppConfig without a row in the overview: " + string.Join(", ", found.Where(n => !shown.Contains(n) && !excluded.ContainsKey(n))));
        }

        [Fact]
        public void EveryStateOfSectionFourIsReached()
        {
            var config = WithRows();
            config.EnableCaseHotkey = false;                                                   // OffByRow
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "nochord", SourceKlid = "00000409", TargetKlid = "0000040C", Hotkey = "" });
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "bad", SourceKlid = "00000409", TargetKlid = "00000410", Hotkey = "Ctrl+Shift+Hyper+F5" });
            List<ChordOverviewRow> rows = Build(config);

            Assert.Equal(ChordBindingState.OffByRow, Row(rows, ChordKind.Case).State);
            Assert.Equal(ChordBindingState.OffByModule, Row(rows, ChordKind.QuickNotes).State);   // notes are off by default
            Assert.Equal(ChordBindingState.OffByModule, Row(rows, ChordKind.Translation, "tr").State);
            Assert.Equal(ChordBindingState.Active, Row(rows, ChordKind.History).State);
            Assert.Equal(ChordBindingState.NoChord, Row(rows, ChordKind.Conversion, "nochord").State);
            ChordOverviewRow bad = Row(rows, ChordKind.Conversion, "bad");
            Assert.Equal(ChordBindingState.Unparsable, bad.State);
            // An unparsable token is shown as stored, never as a working chord (INPUT-CHORD).
            Assert.Equal("Ctrl+Shift+Hyper+F5", bad.ChordText);

            config.EnableHotkeys = false;
            rows = Build(config);
            Assert.Equal(ChordBindingState.OffByMaster, Row(rows, ChordKind.History).State);
            // A row that is off for its own reason says that reason first, not the master switch.
            Assert.Equal(ChordBindingState.OffByRow, Row(rows, ChordKind.Case).State);
            // The mouse chord is not a keyboard chord: the master switch does not gate it.
            Assert.Equal(ChordBindingState.OffByModule, rows.Single(r => r.IsMouse).State);
        }

        [Fact]
        public void AClashMarksBothRowsAndANamedHolder()
        {
            var config = WithRows();
            config.LayoutConversionProfiles[1].Hotkey = config.CaseHotkey;   // the conversion takes the case flip's chord
            List<ChordOverviewRow> rows = Build(config);

            ChordOverviewRow conversion = Row(rows, ChordKind.Conversion, "mine");
            ChordOverviewRow caseFlip = Row(rows, ChordKind.Case);
            Assert.NotNull(conversion.ClashWith);
            Assert.NotNull(caseFlip.ClashWith);
            Assert.Contains("Case flip", conversion.StateText(s => Localization.Translate("English", s)));
            // The state that says why it does not fire stays what it was; the clash is a second fact.
            Assert.Equal(ChordBindingState.Active, caseFlip.State);
            Assert.StartsWith("same as", caseFlip.StateText(s => s));
            Assert.Null(Row(rows, ChordKind.History).ClashWith);
        }

        [Fact]
        public void AClashIsReportedEvenWhenOneRowIsSwitchedOff()
        {
            var config = WithRows();
            config.EnableCaseHotkey = false;
            config.LayoutConversionProfiles[1].Hotkey = config.CaseHotkey;
            List<ChordOverviewRow> rows = Build(config);
            // A switched-off chord still owns its chord (ChordRegistry), so both rows warn.
            Assert.NotNull(Row(rows, ChordKind.Case).ClashWith);
            Assert.NotNull(Row(rows, ChordKind.Conversion, "mine").ClashWith);
            // "Why it does not fire" comes first, the clash second.
            string text = Row(rows, ChordKind.Case).StateText(s => s);
            Assert.True(text.IndexOf("off", StringComparison.Ordinal) < text.IndexOf("same as", StringComparison.Ordinal), text);
        }

        [Fact]
        public void AModuleOffRowKeepsItsChord()
        {
            List<ChordOverviewRow> rows = Build(WithRows());
            ChordOverviewRow translation = Row(rows, ChordKind.Translation, "tr");
            Assert.Equal(ChordBindingState.OffByModule, translation.State);
            Assert.Equal("Ctrl+Shift+F9", translation.ChordText);   // "the chord I would get if I turn the translator on"
            Assert.Equal(SettingsPageKey.Translate, translation.ModulePage);
        }

        [Fact]
        public void EveryFixedChordNamesThePageOfItsFeature()
        {
            // S0062 D1/D14: a chord lives on the page of the feature it starts, and the overview says which.
            List<ChordOverviewRow> rows = Build(new AppConfig());
            Assert.Equal(SettingsPageKey.Conversions, Row(rows, ChordKind.Case).ModulePage);
            Assert.Equal(SettingsPageKey.Clipboard, Row(rows, ChordKind.History).ModulePage);
            Assert.Equal(SettingsPageKey.QuickNotes, Row(rows, ChordKind.QuickNotes).ModulePage);
            Assert.Equal(SettingsPageKey.Symbols, Row(rows, ChordKind.Symbols).ModulePage);
            Assert.Equal(SettingsPageKey.TrafficLight, Row(rows, ChordKind.TrafficLight).ModulePage);
            Assert.Equal(SettingsPageKey.Graphics, Row(rows, ChordKind.Screenshot).ModulePage);
            Assert.Equal(SettingsPageKey.Graphics, Row(rows, ChordKind.ScreenshotFull).ModulePage);
            Assert.Equal(SettingsPageKey.Graphics, Row(rows, ChordKind.ScreenshotAnnotateArea).ModulePage);
            Assert.Equal(SettingsPageKey.Graphics, Row(rows, ChordKind.ScreenshotFullAnnotate).ModulePage);
            Assert.Equal(SettingsPageKey.Graphics, Row(rows, ChordKind.ScreenshotFolder).ModulePage);
            Assert.Equal(SettingsPageKey.SelectionMenu, Row(rows, ChordKind.TextMenu).ModulePage);
            Assert.Equal(SettingsPageKey.SelectionMenu, rows.Single(r => r.IsMouse).ModulePage);
            Assert.All(rows.Where(r => r.Group == ChordGroup.Fixed), r => Assert.NotNull(r.ModulePage));
        }

        [Fact]
        public void DifferingFromTheShippedStateIsDecidedPerRow()
        {
            var config = WithRows();
            List<ChordOverviewRow> rows = Build(config);
            Assert.All(rows.Where(r => r.Group == ChordGroup.Fixed), r => Assert.False(r.Differs, r.Key));
            Assert.False(Row(rows, ChordKind.Conversion, "seed").Differs);
            Assert.True(Row(rows, ChordKind.Conversion, "mine").Differs);      // a user row ships with no chord
            Assert.False(Row(rows, ChordKind.Translation, "tr").Differs);

            config.CaseHotkey = "Ctrl+Alt+Z";
            config.EnableHistoryHotkey = false;
            config.ContextMenuChord = "Shift+RightClick";
            rows = Build(config);
            Assert.True(Row(rows, ChordKind.Case).Differs);
            Assert.True(Row(rows, ChordKind.History).Differs);                 // the chord is the shipped one, the switch is not
            Assert.True(rows.Single(r => r.IsMouse).Differs);
        }

        [Fact]
        public void AChordTokenIsComparedAsAChordNotAsText()
        {
            var config = new AppConfig { CaseHotkey = "shift+ctrl+f11" };
            Assert.False(Row(Build(config), ChordKind.Case).Differs);
        }

        [Fact]
        public void WindowsRowsAreListedAndReadOnly()
        {
            List<ChordOverviewRow> rows = Build(new AppConfig(), null, new[] { WindowsEntry("Ctrl+Shift+5") }, "2");
            List<ChordOverviewRow> windows = rows.Where(r => r.Group == ChordGroup.Windows).ToList();
            Assert.Equal(2, windows.Count);
            Assert.All(windows, r => { Assert.True(r.ReadOnly); Assert.False(r.CanReset); Assert.False(r.HasSwitch); });
            Assert.Contains(windows, r => r.ChordText == "Ctrl+Shift");
        }

        [Fact]
        public void TheDefaultsTableHoldsTheValuesANewConfigStartsWith()
        {
            // R-defaults: the reset reads ChordDefaults, so it has to say what AppConfig ships.
            var config = new AppConfig();
            foreach (ChordKind kind in Enum.GetValues(typeof(ChordKind)).Cast<ChordKind>().Where(ChordDefaults.IsFixed))
            {
                Assert.True(ChordDefaults.SameChordToken(ChordConfigAccess.GetChord(config, kind), ChordDefaults.FixedChord(kind)), kind.ToString());
                Assert.True(ChordConfigAccess.GetSwitch(config, kind), kind + " switch");
            }
            Assert.Equal(MouseChord.Default.Token, config.ContextMenuChord);
            Assert.True(ChordDefaults.SameChordToken(Hotkey.CaseDefault.Display, AppConfig.DefaultCaseHotkey));
        }

        [Fact]
        public void EveryFixedKindIsCoveredByTheDefaultsAndTheConfigAccess()
        {
            var config = new AppConfig();
            foreach (ChordKind kind in Enum.GetValues(typeof(ChordKind)))
            {
                if (ChordDefaults.IsFixed(kind))
                {
                    string before = ChordConfigAccess.GetChord(config, kind);
                    ChordConfigAccess.SetChord(config, kind, "Ctrl+Alt+Q");
                    Assert.Equal("Ctrl+Alt+Q", ChordConfigAccess.GetChord(config, kind));
                    ChordConfigAccess.SetChord(config, kind, before);
                    ChordConfigAccess.SetSwitch(config, kind, false);
                    Assert.False(ChordConfigAccess.GetSwitch(config, kind));
                }
                else
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => ChordDefaults.FixedChord(kind));
                }
            }
        }

        [Fact]
        public void SearchTextCarriesTheCaptionTheChordAndTheState()
        {
            ChordOverviewRow row = Row(Build(new AppConfig { EnableCaseHotkey = false }), ChordKind.Case);
            string text = row.SearchText(s => s);
            Assert.Contains("Ctrl+Shift+F11", text);
            Assert.Contains("off", text);
        }

        [Fact]
        public void ClearingIsOnlyForRowsThatCanBeLeftWithoutAChord()
        {
            List<ChordOverviewRow> rows = Build(WithRows(), new[] { Scenario("Calc", "Ctrl+Shift+F6") });
            Assert.False(Row(rows, ChordKind.Case).CanClear);
            Assert.False(rows.Single(r => r.IsMouse).CanClear);
            Assert.True(Row(rows, ChordKind.Conversion, "mine").CanClear);
            Assert.True(rows.Single(r => r.Group == ChordGroup.Launcher).CanClear);
        }
    }
}
