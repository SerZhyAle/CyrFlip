using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The "Горячие клавиши" page built for real (tickets S0056 section 8 and S0062): in all 13 languages with
    /// nothing clipped, the one master switch (S0062 D9 - the overview no longer carries a second one), a value
    /// staying one value with no change handler fired by a refresh, the search finding a row by its chord, and
    /// the empty tables.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class HotkeysOverviewPageTests
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

        private static AppConfig Config(string language) => new AppConfig { UiLanguage = language, SettingsGroupsState = "", SettingsViewport = "" };

        /// <summary>The real constructor with the two callbacks the test watches.</summary>
        private static SettingsForm Build(AppConfig config, Action<bool>? setCase = null, Action<bool>? setMaster = null)
        {
            Action<bool> b = _ => { };
            Action noop = () => { };
            var store = new LauncherScenarioStore(Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N")));
            return new SettingsForm(config, b, b, b, b, b, b, b, b, b, _ => { }, _ => { }, noop, noop, noop, noop, noop,
                setMaster ?? b, setCase ?? b, b, b, b, b, store, b, noop, noop, noop, (_, _) => "", null);
        }

        private static object Field(object owner, string name)
            => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

        private static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in Walk(child)) yield return deeper;
            }
        }

        private static TabPage OverviewPage(SettingsForm form)
            => form.Tabs!.TabPages.Cast<TabPage>().Single(p => Equals(p.Tag, SettingsPageKey.Hotkeys));

        private static IEnumerable<Control> ContentOf(TabPage page)
            => ((Control)page.Controls[0]).Controls.Cast<Control>();

        private static Control FirstRowOf(SettingsForm form, string groupId)
            => form.Groups.Single(g => g.Id == groupId).Body.Controls[0];

        [Fact]
        public void TheOnePageHoldsTheSwitchesGroupAndTheSixOverviewGroups()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = Build(Config("English"));
                // S0062 D8: the overview and the old Hotkeys page are one page, under the Hotkeys key.
                Assert.DoesNotContain(form.Tabs!.TabPages.Cast<TabPage>(), p => Equals(p.Tag, SettingsPageKey.HotkeysOverview));
                Assert.Single(form.Tabs.TabPages.Cast<TabPage>(), p => Equals(p.Tag, SettingsPageKey.Hotkeys));
                string[] onThePage = ContentOf(OverviewPage(form)).OfType<CollapsibleGroup>().Select(g => g.Id).ToArray();
                Assert.Equal(new[] { "hotkeys/switches", "keys/fixed", "keys/mouse", "keys/conversions", "keys/translations", "keys/launcher", "keys/windows" }, onThePage);

                string[] ids = form.Groups.Where(g => g.Id.StartsWith("keys/", StringComparison.Ordinal)).Select(g => g.Id).ToArray();
                Assert.Equal(new[] { "keys/fixed", "keys/mouse", "keys/conversions", "keys/translations", "keys/launcher", "keys/windows" }, ids);
            });
        }

        [Fact]
        public void EveryRowOfTheModelIsOnTheScreen()
        {
            OnUiThread(() =>
            {
                AppConfig config = Config("English");
                config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "a", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12" });
                using SettingsForm form = Build(config);
                foreach (ChordOverviewRow row in form.OverviewRows)
                {
                    CollapsibleGroup group = form.Groups.Single(g => g.Id == ChordGroups.Id(row.Group));
                    if (row.Group == ChordGroup.Windows) continue;   // the Windows group is collapsed and shows the system's own names
                    Assert.Contains(group.Body.Controls.Cast<Control>(), panel => Walk(panel).OfType<Label>().Any(l => l.Text == row.ChordText));
                }
            });
        }

        [Fact]
        public void TheWindowsGroupStartsCollapsedAndTheOthersOpen()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = Build(Config("English"));
                Assert.False(form.Groups.Single(g => g.Id == "keys/windows").Expanded);
                Assert.All(form.Groups.Where(g => g.Id.StartsWith("keys/", StringComparison.Ordinal) && g.Id != "keys/windows"),
                    g => Assert.True(g.Expanded, g.Id));
            });
        }

        [UiSweepFact]
        public void BuiltInEveryLanguageWithNothingClipped()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    AppConfig config = Config(language);
                    config.EnableHotkeys = false;     // the longest state texts: every row says why it does not fire
                    config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "a", SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12" });
                    config.LayoutConversionProfiles.Add(new LayoutConversionProfile { Id = "b", SourceKlid = "00000409", TargetKlid = "00000407", Hotkey = "Ctrl+Shift+F12" });
                    using SettingsForm form = Build(config);
                    var _ = form.Handle;
                    form.Show();
                    form.Tabs!.SelectedTab = OverviewPage(form);
                    form.PerformLayout();
                    foreach (Control control in Walk(OverviewPage(form)).Where(c => (c is Button || c is CheckBox) && c.Visible))
                    {
                        if (control.Text.Length == 0) continue;
                        int need = TextRenderer.MeasureText(control.Text, control.Font).Width;
                        if (control.Width < need) problems.Add($"[{language}] {control.GetType().Name} '{control.Text}' {control.Width} < {need}");
                    }
                    // The chord column has no ellipsis: a chord must never be cut ("Ctrl+Shift+F12" read "Ctrl+Shift+F1" once).
                    foreach (ChordOverviewRow row in form.OverviewRows.Where(r => r.Group == ChordGroup.Fixed))
                    {
                        Label chord = Walk(OverviewPage(form)).OfType<Label>().First(l => l.Text == row.ChordText && l.AutoSize == false);
                        int need = TextRenderer.MeasureText(chord.Text, chord.Font).Width;
                        if (chord.Width < need) problems.Add($"[{language}] chord '{chord.Text}' {chord.Width} < {need}");
                    }
                    form.Hide();
                }
            });
            Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));
        }

        [Fact]
        public void AValueChangedBehindThePageShowsOnTheRowAndARefreshFiresNoHandler()
        {
            OnUiThread(() =>
            {
                AppConfig config = Config("English");
                int caseCalls = 0, masterCalls = 0;
                using SettingsForm form = Build(config, v => { caseCalls++; config.EnableCaseHotkey = v; }, v => { masterCalls++; config.EnableHotkeys = v; });

                // A value changed behind the page's back shows on the row and on the owning page after a refresh.
                config.CaseHotkey = "Ctrl+Alt+Z";
                config.EnableCaseHotkey = false;
                config.EnableHotkeys = false;
                form.Reload();
                ChordOverviewRow row = form.OverviewRows.Single(r => r.Kind == ChordKind.Case);
                Assert.Equal("Ctrl+Alt+Z", row.StoredChord);
                Assert.False(row.SwitchOn);
                Assert.Equal("Ctrl+Alt+Z", ((Label)Field(form, "_caseHotkeyValue")).Text);
                Assert.False(((CheckBox)Field(form, "_caseEnabled")).Checked);
                Assert.False(((CheckBox)Field(form, "_enableHotkeys")).Checked);
                // A refresh is not an edit: no change handler ran.
                Assert.Equal(0, caseCalls);
                Assert.Equal(0, masterCalls);

                // Overview -> old page: the switch on the row goes through the owning page's own handler.
                config.EnableHotkeys = true;
                config.EnableCaseHotkey = true;
                form.Reload();
                Assert.Equal(0, caseCalls);
                var rowSwitch = Walk(FirstRowOf(form, "keys/fixed")).OfType<CheckBox>().First();
                rowSwitch.Checked = false;
                Assert.Equal(1, caseCalls);
                Assert.False(config.EnableCaseHotkey);
                Assert.False(((CheckBox)Field(form, "_caseEnabled")).Checked);
                Assert.False(form.OverviewRows.Single(r => r.Kind == ChordKind.Case).SwitchOn);
            });
        }

        [Fact]
        public void TheMasterSwitchIsOneControlWithOneHandler()
        {
            OnUiThread(() =>
            {
                AppConfig config = Config("English");
                int calls = 0;
                using SettingsForm form = Build(config, null, v => { calls++; config.EnableHotkeys = v; });
                // There is no second switch to keep in step with it (D9).
                Assert.Null(typeof(SettingsForm).GetField("_overviewMaster", BindingFlags.NonPublic | BindingFlags.Instance));
                var master = (CheckBox)Field(form, "_enableHotkeys");
                Assert.Same(master, ContentOf(OverviewPage(form)).OfType<CollapsibleGroup>().First(g => g.Id == "hotkeys/switches").Body.Controls.Cast<Control>().SelectMany(c => Walk(c).Prepend(c)).OfType<CheckBox>().First());
                master.Checked = false;
                Assert.Equal(1, calls);
                Assert.False(config.EnableHotkeys);
                Assert.True(form.OverviewRows.Where(r => r.Group == ChordGroup.Fixed && r.State != ChordBindingState.OffByModule && r.State != ChordBindingState.NoChord)
                    .All(r => r.State == ChordBindingState.OffByMaster));
            });
        }

        [Fact]
        public void AChordFindsItsRowInTheSettingsSearch()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = Build(Config("English"));
                List<SettingsForm.SettingsSearchHit> hits = form.BuildSearchIndex();
                SettingsForm.SettingsSearchHit hit = hits.First(h => Equals(h.Page.Tag, SettingsPageKey.Hotkeys)
                    && SettingsForm.SearchMatchesForTest(h.Texts, "Ctrl+Shift+F10"));
                Assert.Contains("Ctrl+Shift+F10", hit.Texts);
                // A state is searchable too: "off" finds the rows that are off.
                Assert.Contains(hits, h => Equals(h.Page.Tag, SettingsPageKey.Hotkeys)
                    && SettingsForm.SearchMatchesForTest(h.Texts, "module is off"));
            });
        }

        [Fact]
        public void AnEmptyTableStillShowsItsGroupWithANote()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = Build(Config("English"));
                CollapsibleGroup conversions = form.Groups.Single(g => g.Id == "keys/conversions");
                Label note = Assert.IsType<Label>(Assert.Single(conversions.Body.Controls.Cast<Control>()));
                Assert.Equal("There are no hotkeys here yet.", note.Text);
            });
        }

        [Fact]
        public void ARowOfAnOffModuleLinksToThePageOfThatModule()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = Build(Config("English"));
                Control quickNotes = form.Groups.Single(g => g.Id == "keys/fixed").Body.Controls[2];   // case, history, notes
                LinkLabel link = Walk(quickNotes).OfType<LinkLabel>().Single();
                Assert.Equal("module is off", link.Text);
                form.Show();
                form.Tabs!.SelectedTab = OverviewPage(form);
                link.GetType().GetMethod("OnLinkClicked", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(new LinkLabel.Link()) });
                Assert.Equal(SettingsPageKey.QuickNotes, form.Tabs.SelectedTab!.Tag);
                form.Hide();
            });
        }

        [Fact]
        public void EveryButtonOfARowNamesTheRowItActsOn()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = Build(Config("English"));
                foreach (Control row in form.Groups.Single(g => g.Id == "keys/fixed").Body.Controls)
                    foreach (Button button in row.Controls.OfType<Button>())
                        Assert.Contains(" - ", button.AccessibleName);
                // Each switch has no caption of its own, so it is named after its row.
                foreach (Control row in form.Groups.Single(g => g.Id == "keys/fixed").Body.Controls)
                    foreach (CheckBox box in row.Controls.OfType<CheckBox>())
                        Assert.False(string.IsNullOrWhiteSpace(box.AccessibleName));
            });
        }
    }
}
