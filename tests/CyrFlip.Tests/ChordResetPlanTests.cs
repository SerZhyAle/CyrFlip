using System;
using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// "Reset to defaults" (ticket S0056 section 6): what a reset touches (R1) and never touches (R2),
    /// the seeded rows (R3), a shipped chord that is taken (R4), the per-row reset (R6), the plan computed
    /// once and applied once (R7, R8), and a second reset changing nothing.
    /// </summary>
    public class ChordResetPlanTests
    {
        private static readonly Func<string, string> Layout = klid => klid;

        private static List<ChordOverviewRow> Rows(AppConfig config, IEnumerable<LauncherScenario> scenarios, IEnumerable<LanguageHotkeys.Entry>? windows = null)
            => ChordOverviewModel.Build(config, scenarios, windows ?? new LanguageHotkeys.Entry[0], null, "English", Layout);

        private static ChordResetPlan PlanAll(AppConfig config, List<LauncherScenario> scenarios, IEnumerable<LanguageHotkeys.Entry>? windows = null)
            => ChordResetPlan.ForAll(config, Rows(config, scenarios, windows), scenarios,
                windows ?? new LanguageHotkeys.Entry[0], "English", Layout);

        private static ChordResetResult Apply(ChordResetPlan plan, AppConfig config, List<LauncherScenario> scenarios,
            Action<LauncherScenario>? save = null, IEnumerable<LanguageHotkeys.Entry>? windows = null)
            => plan.Apply(config, scenarios, windows ?? new LanguageHotkeys.Entry[0], "English", Layout, save ?? (_ => { }));

        private static AppConfig Seeded()
        {
            var config = new AppConfig();
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            { Id = "seed", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12", Enabled = true });
            config.TranslateProfiles.Add(new TranslationProfile { Id = "tr", TargetLang = TranslationLanguages.UiToken, Hotkey = "Ctrl+Shift+F9", Enabled = true });
            return config;
        }

        private static LauncherScenario Scenario(string name, string chord)
            => new LauncherScenario { Id = Guid.NewGuid(), Name = name, Path = "calc.exe", Hotkey = chord };

        [Fact]
        public void AConfigThatShipsAsIsHasNothingToReset()
        {
            AppConfig config = Seeded();
            ChordResetPlan plan = PlanAll(config, new List<LauncherScenario>());
            Assert.True(plan.IsEmpty);
            Assert.Empty(plan.Skipped);
            Assert.True(plan.AlreadyDefault >= 9);   // 7 fixed + the mouse chord + the two seeded rows
        }

        [Fact]
        public void AResetPutsAChosenChordBackAndASecondResetChangesNothing()
        {
            AppConfig config = Seeded();
            config.CaseHotkey = "Ctrl+Alt+Z";
            config.EnableHistoryHotkey = false;
            var scenarios = new List<LauncherScenario>();

            ChordResetPlan plan = PlanAll(config, scenarios);
            Assert.Equal(2, plan.Steps.Count);
            Assert.True(plan.ChangesChords);
            ChordResetResult result = Apply(plan, config, scenarios);

            Assert.Equal(2, result.Applied.Count);
            Assert.Equal(AppConfig.DefaultCaseHotkey, config.CaseHotkey);
            Assert.True(config.EnableHistoryHotkey);
            Assert.True(PlanAll(config, scenarios).IsEmpty);   // idempotent
        }

        [Fact]
        public void ASwitchOnlyResetIsNotAChordChange()
        {
            AppConfig config = Seeded();
            config.EnableScreenshotHotkey = false;
            ChordResetPlan plan = PlanAll(config, new List<LauncherScenario>());
            Assert.Single(plan.Steps);
            Assert.False(plan.ChangesChords);   // the confirmation does not paint Yes as a danger for this
            Apply(plan, config, new List<LauncherScenario>());
            Assert.True(config.EnableScreenshotHotkey);
        }

        [Fact]
        public void AResetNeverTouchesAModuleSwitchARowsLanguageOrTheNumberOfRows()
        {
            AppConfig config = Seeded();
            config.EnableTranslate = true; config.EnableQuickNotes = true; config.EnableContextMenu = true;
            config.EnableSymbols = false; config.EnableClipboardHistory = true; config.EnableScenarioLauncher = true;
            config.TranslateProfiles[0].TargetLang = "de";                    // no longer the seeded row
            config.TranslateProfiles[0].Hotkey = "Ctrl+Shift+F5";
            config.CaseHotkey = "Ctrl+Alt+Z";
            var scenarios = new List<LauncherScenario> { Scenario("Calc", "Ctrl+Shift+F6") };
            int conversionRows = config.LayoutConversionProfiles.Count, translateRows = config.TranslateProfiles.Count;

            Apply(PlanAll(config, scenarios), config, scenarios);

            Assert.True(config.EnableTranslate && config.EnableQuickNotes && config.EnableContextMenu);
            Assert.False(config.EnableSymbols);
            Assert.True(config.EnableClipboardHistory && config.EnableScenarioLauncher);
            Assert.Equal("de", config.TranslateProfiles[0].TargetLang);       // the language stays
            Assert.Equal("", config.TranslateProfiles[0].Hotkey);             // a user row has no shipped chord: it is cleared
            Assert.Equal(conversionRows, config.LayoutConversionProfiles.Count);
            Assert.Equal(translateRows, config.TranslateProfiles.Count);
            Assert.Single(scenarios);
            Assert.Equal("Calc", scenarios[0].Name);
            Assert.Equal("calc.exe", scenarios[0].Path);
            Assert.Equal("", scenarios[0].Hotkey);
        }

        [Fact]
        public void ASeededRowGetsItsShippedChordAndAUserRowIsOnlyCleared()
        {
            AppConfig config = Seeded();
            config.LayoutConversionProfiles[0].Hotkey = "Ctrl+Alt+F1";
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile
            { Id = "mine", SourceKlid = "00000409", TargetKlid = "00000407", Hotkey = "Ctrl+Shift+F8", Enabled = true });

            ChordResetPlan plan = PlanAll(config, new List<LauncherScenario>());
            ChordResetStep seeded = plan.Steps.Single(s => s.Id == "seed");
            ChordResetStep mine = plan.Steps.Single(s => s.Id == "mine");
            Assert.Equal("Ctrl+Shift+F12", seeded.ToChord);
            Assert.False(seeded.Clears);
            Assert.True(mine.Clears);                        // worded "clear", not "default" (R3)
            Assert.Null(mine.ToSwitch);                      // the row's switch is the user's own
        }

        [Fact]
        public void ARowThatIsNoLongerTheSeededRowIsNotGivenTheSeededChord()
        {
            AppConfig config = Seeded();
            config.LayoutConversionProfiles[0].TargetKlid = "00000407";   // US -> German now
            ChordOverviewRow row = Rows(config, new List<LauncherScenario>()).Single(r => r.Id == "seed");
            Assert.Null(row.DefaultChord);
            Assert.False(row.ResetRestoresDefault);
        }

        [Fact]
        public void AShippedChordAnotherActionHoldsIsNotStolenButReported()
        {
            AppConfig config = Seeded();
            config.CaseHotkey = "Ctrl+Alt+Z";
            config.ClipboardHistoryHotkey = AppConfig.DefaultCaseHotkey;   // the history sits on the case flip's shipped chord
            var scenarios = new List<LauncherScenario>();

            ChordResetPlan plan = PlanAll(config, scenarios);
            ChordResetSkip skip = Assert.Single(plan.Skipped);
            Assert.Equal(ChordSkipReason.Taken, skip.Reason);
            Assert.Equal("Ctrl+Shift+F11", skip.Chord);
            Assert.False(string.IsNullOrEmpty(skip.HeldBy));
            // The rest proceeds: the history goes back to its own chord.
            ChordResetResult result = Apply(plan, config, scenarios);
            Assert.Equal(AppConfig.DefaultHistoryHotkey, config.ClipboardHistoryHotkey);
            Assert.Equal("Ctrl+Alt+Z", config.CaseHotkey);                  // untouched
            Assert.Single(result.Skipped);
            // After that the chord is free, so a second reset converges.
            Assert.Equal(AppConfig.DefaultCaseHotkey, PlanAll(config, scenarios).Steps.Single().ToChord);
        }

        [Fact]
        public void AWindowsHolderIsReportedOncePerRowNotRefused()
        {
            AppConfig config = Seeded();
            config.CaseHotkey = "Ctrl+Alt+Z";
            var windows = new[] { new LanguageHotkeys.Entry { Id = 0x101, TargetHkl = new IntPtr(0x04090409),
                Modifiers = LanguageHotkeys.MOD_CONTROL | LanguageHotkeys.MOD_SHIFT, VirtualKey = (uint)Hotkey.CaseDefault.Vk } };
            ChordResetPlan plan = PlanAll(config, new List<LauncherScenario>(), windows);
            Assert.Single(plan.Steps);
            Assert.Single(plan.WindowsHolds);
            Assert.Empty(plan.Skipped);   // Windows' owners are asked about, never refused
        }

        [Fact]
        public void AStalePlanIsNotAppliedAndSaysWhy()
        {
            AppConfig config = Seeded();
            config.CaseHotkey = "Ctrl+Alt+Z";
            var scenarios = new List<LauncherScenario>();
            ChordResetPlan plan = PlanAll(config, scenarios);

            config.CaseHotkey = "Ctrl+Alt+X";   // the user changed it after the plan was made
            ChordResetResult result = Apply(plan, config, scenarios);

            Assert.Empty(result.Applied);
            Assert.Equal(ChordSkipReason.Changed, Assert.Single(result.Skipped).Reason);
            Assert.Equal("Ctrl+Alt+X", config.CaseHotkey);
        }

        [Fact]
        public void ARowDeletedAfterThePlanIsSkippedAsMissing()
        {
            AppConfig config = Seeded();
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "mine", SourceKlid = "00000409", TargetKlid = "00000407", Hotkey = "Ctrl+Shift+F8" });
            var scenarios = new List<LauncherScenario>();
            ChordResetPlan plan = PlanAll(config, scenarios);
            config.LayoutConversionProfiles.RemoveAll(p => p.Id == "mine");
            ChordResetResult result = Apply(plan, config, scenarios);
            Assert.Equal(ChordSkipReason.Missing, Assert.Single(result.Skipped).Reason);
        }

        [Fact]
        public void AShippedChordTakenBetweenThePlanAndTheApplyIsSkipped()
        {
            AppConfig config = Seeded();
            config.CaseHotkey = "Ctrl+Alt+Z";
            var scenarios = new List<LauncherScenario>();
            ChordResetPlan plan = PlanAll(config, scenarios);
            config.ClipboardHistoryHotkey = AppConfig.DefaultCaseHotkey;   // someone took it meanwhile
            ChordResetResult result = Apply(plan, config, scenarios);
            Assert.Equal(ChordSkipReason.Taken, Assert.Single(result.Skipped).Reason);
            Assert.Equal("Ctrl+Alt+Z", config.CaseHotkey);
        }

        [Fact]
        public void ResetAllTurnsTheMasterSwitchBackOnAndASingleRowResetDoesNot()
        {
            AppConfig config = Seeded();
            config.EnableHotkeys = false;
            config.CaseHotkey = "Ctrl+Alt+Z";
            var scenarios = new List<LauncherScenario>();

            List<ChordOverviewRow> rows = Rows(config, scenarios);
            ChordResetPlan single = ChordResetPlan.ForRow(config, rows.Single(r => r.Kind == ChordKind.Case), scenarios,
                new LanguageHotkeys.Entry[0], "English", Layout);
            Assert.False(single.RestoreMaster);
            Apply(single, config, scenarios);
            Assert.False(config.EnableHotkeys);

            ChordResetPlan all = PlanAll(config, scenarios);
            Assert.True(all.RestoreMaster);
            Assert.False(all.IsEmpty);                       // the master switch alone is something to do
            ChordResetResult result = Apply(all, config, scenarios);
            Assert.True(result.MasterRestored);
            Assert.True(config.EnableHotkeys);
        }

        [Fact]
        public void AScenarioChordIsClearedAndTheScenarioFileWrittenThroughTheCallback()
        {
            AppConfig config = Seeded();
            var scenario = Scenario("Calc", "Ctrl+Shift+F6");
            var scenarios = new List<LauncherScenario> { scenario };
            var saved = new List<LauncherScenario>();

            ChordResetResult result = Apply(PlanAll(config, scenarios), config, scenarios, saved.Add);

            Assert.Equal("", scenario.Hotkey);
            Assert.Same(scenario, Assert.Single(saved));
            Assert.True(result.ScenariosChanged);
        }

        [Fact]
        public void TheMouseChordGoesBackToCtrlRightClick()
        {
            AppConfig config = Seeded();
            config.ContextMenuChord = "MiddleClick";
            var scenarios = new List<LauncherScenario>();
            Apply(PlanAll(config, scenarios), config, scenarios);
            Assert.Equal(MouseChord.Default.Token, config.ContextMenuChord);
        }

        [Fact]
        public void APerRowResetTouchesOnlyThatRow()
        {
            AppConfig config = Seeded();
            config.CaseHotkey = "Ctrl+Alt+Z";
            config.ClipboardHistoryHotkey = "Ctrl+Alt+Y";
            var scenarios = new List<LauncherScenario>();
            ChordOverviewRow row = Rows(config, scenarios).Single(r => r.Kind == ChordKind.Case);
            ChordResetPlan plan = ChordResetPlan.ForRow(config, row, scenarios, new LanguageHotkeys.Entry[0], "English", Layout);
            Apply(plan, config, scenarios);
            Assert.Equal(AppConfig.DefaultCaseHotkey, config.CaseHotkey);
            Assert.Equal("Ctrl+Alt+Y", config.ClipboardHistoryHotkey);
        }

        [Fact]
        public void WindowsRowsAreNeverReset()
        {
            var windows = new[] { new LanguageHotkeys.Entry { Id = 0x101, TargetHkl = new IntPtr(0x04090409),
                Modifiers = LanguageHotkeys.MOD_CONTROL, VirtualKey = 0x31 } };
            AppConfig config = Seeded();
            ChordResetPlan plan = PlanAll(config, new List<LauncherScenario>(), windows);
            Assert.True(plan.IsEmpty);
        }
    }
}
