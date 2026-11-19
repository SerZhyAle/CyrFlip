using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0135, step 1.5: a changed value is refreshed by <see cref="SettingsForm.RefreshState"/> instead of the
    /// whole <see cref="SettingsForm.Reload"/>, and what a person sees and hears must not change. Two windows are built
    /// from the same configuration; the same change is made to both configurations; one window runs the full pass and
    /// the other the state refresh; every control of the two trees is then compared - text, enabled state, the control's
    /// own visibility, checked state, selection, value, accessible name and description, tab order - and the first
    /// difference is named by its path. Geometry is not compared: the refresh deliberately does not lay out the whole
    /// window again.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsRefreshEquivalenceTests
    {
        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("EXCEPTION: " + failure);
        }

        private static AppConfig Config(string language)
            => new AppConfig { UiLanguage = language, SettingsGroupsState = "", SettingsViewport = "" };

        private static string LanguageOf(string code) => Localization.Names[Array.IndexOf(Localization.Codes, code)];

        /// <summary>The changes a person makes while the window is open: switches, chords, values, a table row, a module turning on and off.</summary>
        private static readonly (string Name, Action<AppConfig> Apply)[] Scenarios =
        {
            ("the quick notes on", c => c.EnableQuickNotes = true),
            ("the translator on", c => c.EnableTranslate = true),
            ("the translator endpoint typed", c => c.TranslateEndpoint = "http://localhost:11434"),
            ("the symbols on", c => c.EnableSymbols = true),
            ("the context menu on", c => c.EnableContextMenu = true),
            ("the traffic light on", c => c.EnableTrafficLight = true),
            ("the traffic light columns", c => c.TrafficLightColumns = 5),
            ("the exchange clipboard on", c => c.EnableExchangeClipboard = true),
            ("the case chord changed", c => c.CaseHotkey = "Ctrl+Shift+F8"),
            ("the screenshot chord changed", c => c.ScreenshotHotkey = "Ctrl+Shift+F7"),
            ("the notes chord changed", c => c.QuickNotesHotkey = "Ctrl+Shift+Alt+B"),
            ("the text menu chord changed", c => c.TextMenuHotkey = "Ctrl+Shift+Alt+K"),
            ("the symbols chord changed", c => c.SymbolsHotkey = "Ctrl+Shift+F6"),
            ("the traffic light chord changed", c => c.TrafficLightHotkey = "Ctrl+Shift+Alt+L"),
            ("the master switch off", c => c.EnableHotkeys = false),
            ("the case switch off", c => c.EnableCaseHotkey = false),
            ("the history opacity", c => c.ClipboardHistoryOpacity = 85),
            ("the marker size", c => c.CursorSize = 32),
            ("the screenshot saving off", c => c.ScreenshotSaveEnabled = false),
            ("the annotate module off", c => c.EnableScreenshotAnnotate = false),
            ("the theme chosen", c => c.Theme = ThemeModes.Token(ThemeMode.Dark)),
            ("a conversion row added", c => c.LayoutConversionProfiles.Add(new LayoutConversionProfile
            {
                SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F5", Enabled = true,
            })),
            ("the master switch on again", c => c.EnableHotkeys = true),
        };

        /// <summary>What a person can see or a screen reader can say about the whole tree, one line per control, in tree order.</summary>
        internal static List<string> Snapshot(Control root)
        {
            var lines = new List<string>();
            Visit(root, "", lines);
            return lines;
        }

        private static void Visit(Control control, string path, List<string> lines)
        {
            var line = new StringBuilder(path).Append(control.GetType().Name);
            line.Append(" | text=").Append(control.Text);
            line.Append(" | enabled=").Append(control.Enabled);
            line.Append(" | shown=").Append(OwnVisibility.Of(control));
            line.Append(" | name=").Append(control.AccessibleName);
            line.Append(" | description=").Append(control.AccessibleDescription);
            line.Append(" | tab=").Append(control.TabIndex).Append('/').Append(control.TabStop);
            if (control is CheckBox check) line.Append(" | checked=").Append(check.Checked);
            if (control is ComboBox combo) line.Append(" | selected=").Append(combo.SelectedIndex).Append(" of ").Append(combo.Items.Count);
            if (control is NumericUpDown numeric) line.Append(" | value=").Append(numeric.Value);
            if (control is TrackBar track) line.Append(" | value=").Append(track.Value);
            lines.Add(line.ToString());
            for (int i = 0; i < control.Controls.Count; i++)
                Visit(control.Controls[i], path + i + "/", lines);
        }

        /// <summary>The first line the two trees disagree on, with both versions; null when they are the same.</summary>
        internal static string? FirstDifference(List<string> full, List<string> refreshed)
        {
            int common = Math.Min(full.Count, refreshed.Count);
            for (int i = 0; i < common; i++)
                if (full[i] != refreshed[i])
                    return "control " + i + ":\n  Reload:       " + full[i] + "\n  RefreshState: " + refreshed[i];
            return full.Count == refreshed.Count
                ? null
                : "the trees differ in size: Reload " + full.Count + " controls, RefreshState " + refreshed.Count;
        }

        [UiSweepFact]
        public void ARefreshedWindowShowsWhatAFullReloadShows()
        {
            OnUiThread(() =>
            {
                foreach (string code in new[] { "en", "ru" })
                {
                    string language = LanguageOf(code);
                    AppConfig fullConfig = Config(language), refreshConfig = Config(language);
                    SettingsForm full = TestForms.NewSettings(fullConfig);
                    SettingsForm refreshed = TestForms.NewSettings(refreshConfig);
                    try
                    {
                        Assert.Null(FirstDifference(Snapshot(full), Snapshot(refreshed)));
                        foreach ((string Name, Action<AppConfig> Apply) scenario in Scenarios)
                        {
                            scenario.Apply(fullConfig);
                            scenario.Apply(refreshConfig);
                            full.Reload();
                            refreshed.RefreshState();
                            string? difference = FirstDifference(Snapshot(full), Snapshot(refreshed));
                            Assert.True(difference == null, code + ", after \"" + scenario.Name + "\": " + difference);
                        }
                    }
                    finally
                    {
                        full.Dispose();
                        refreshed.Dispose();
                    }
                }
            });
        }

        /// <summary>
        /// The guard that the comparison can fail: a window that was not refreshed at all after the same changes differs
        /// from one that was, and the walk names it. Without this, an equality that never looked at anything would pass.
        /// </summary>
        [UiSweepFact]
        public void TheComparisonCanSeeAWindowThatWasNotRefreshed()
        {
            OnUiThread(() =>
            {
                string language = LanguageOf("en");
                AppConfig fullConfig = Config(language), staleConfig = Config(language);
                SettingsForm full = TestForms.NewSettings(fullConfig);
                SettingsForm stale = TestForms.NewSettings(staleConfig);
                try
                {
                    foreach ((string Name, Action<AppConfig> Apply) scenario in Scenarios)
                    {
                        scenario.Apply(fullConfig);
                        scenario.Apply(staleConfig);
                    }
                    full.Reload();
                    Assert.NotNull(FirstDifference(Snapshot(full), Snapshot(stale)));
                }
                finally
                {
                    full.Dispose();
                    stale.Dispose();
                }
            });
        }
    }
}
