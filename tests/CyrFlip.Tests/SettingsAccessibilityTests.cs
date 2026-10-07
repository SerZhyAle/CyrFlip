using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// What the settings window tells assistive technology and the keyboard (ticket S0060, phase P1):
    /// <c>APP-SETTINGS</c> rule 10 (a name on every control, a sensible tab order), <c>APP-BEHAVIOUR</c> rule 9
    /// (a name survives a language change) and <c>WINDOWS-UI</c> section 2 rule 3 (a command's state is legible).
    /// The properties are what UI Automation reads; a live screen-reader pass is still a manual step.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsAccessibilityTests
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

        private static AppConfig WithConversionRow()
        {
            AppConfig config = Config("English");
            config.LayoutConversionProfiles.Add(new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12" });
            return config;
        }

        private static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                // The inside of an editor (its text part, its spin halves) is the editor's own business.
                if (child is NumericUpDown or ComboBox or TextBoxBase or ListView or TrackBar) continue;
                foreach (Control deeper in Walk(child)) yield return deeper;
            }
        }

        [Fact]
        public void AGroupHeaderIsNamedInTheCurrentLanguageAfterALiveSwitch()
        {
            OnUiThread(() =>
            {
                var config = Config("Русский");
                using var form = TestForms.NewSettings(config);
                CollapsibleGroup group = form.Groups.First();
                string russian = group.Caption;
                Assert.Equal(russian, group.Controls[0].AccessibilityObject.Name);

                config.UiLanguage = "English";
                form.Reload();

                Assert.NotEqual(russian, group.Caption);
                Assert.Equal(group.Caption, group.Controls[0].AccessibilityObject.Name);
            });
        }

        [Fact]
        public void EveryEditorCarriesAnAccessibleNameInEveryLanguage()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    using var form = TestForms.NewSettings(Config(language));
                    foreach (Control control in Walk(form))
                    {
                        bool editor = control is ComboBox or NumericUpDown or TrackBar or TextBoxBase or ListView;
                        if (editor && string.IsNullOrWhiteSpace(control.AccessibleName))
                            problems.Add($"[{language}] {control.GetType().Name} '{control.Name}' in {control.Parent?.GetType().Name} has no accessible name");
                    }
                }
            });
            Assert.True(problems.Count == 0, "APP-SETTINGS rule 10 - editors without a name:\n" + string.Join("\n", problems.Distinct().Take(40)));
        }

        [Fact]
        public void ASwitchWithoutACaptionIsNamedAfterItsRow()
        {
            int inspected = 0;
            var problems = new List<string>();
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(WithConversionRow());
                foreach (Control control in Walk(form))
                {
                    if (control is not CheckBox { Text.Length: 0 } box) continue;
                    inspected++;
                    if (string.IsNullOrWhiteSpace(box.AccessibleName)) problems.Add("caption-less switch with no name");
                    // Every other button of that row says which row it acts on.
                    if (box.Parent == null) continue;
                    foreach (Button sibling in box.Parent.Controls.OfType<Button>())
                        if (sibling.AccessibleName.IndexOf(" - ", StringComparison.Ordinal) < 0)
                            problems.Add($"row button '{sibling.Text}' does not name its row");
                }
            });
            Assert.True(inspected > 0, "no caption-less row switch was found: the conversion table should have its seeded row");
            Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
        }

        [Fact]
        public void TheHeaderCommandsAndAboutLinksFollowALiveLanguageSwitch()
        {
            OnUiThread(() =>
            {
                // Built in English, as on a non-Russian Windows: the captions must still be keys the language pass finds.
                var config = Config("English");
                using var form = TestForms.NewSettings(config);
                Control find = Walk(form).First(c => c.Name == "settingsSearch");
                Assert.Equal(Localization.Translate("English", "Найти настройку..."), find.Text);

                config.UiLanguage = "Deutsch";
                form.Reload();

                Assert.Equal(Localization.Translate("Deutsch", "Найти настройку..."), find.Text);
                Assert.NotEqual(Localization.Translate("English", "Найти настройку..."), find.Text);
                LinkLabel licence = Walk(form).OfType<LinkLabel>().First(l => l.Text == Localization.Translate("Deutsch", "Лицензия: MIT"));
                Assert.NotNull(licence);
            });
        }

        [Fact]
        public void TheLauncherOrderButtonsFollowTheLanguage()
        {
            OnUiThread(() =>
            {
                var config = Config("Русский");
                using var form = TestForms.NewSettings(config);
                Button[] arrows = Walk(form).OfType<Button>()
                    .Where(b => b.Text.Length == 0 && b.Image != null && b.AccessibleName == "Переместить вверх").ToArray();
                Assert.NotEmpty(arrows);

                config.UiLanguage = "English";
                form.Reload();
                Assert.All(arrows, b => Assert.Equal("Move up", b.AccessibleName.Split(new[] { " - " }, StringSplitOptions.None)[0]));
            });
        }

        [Fact]
        public void ExpandAndCollapseAllAreAvailableOnlyOnAPageWithGroups()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config("English"));
                form.Show();
                Control expand = Walk(form).First(c => c.Name == "settingsExpandAll");
                Control collapse = Walk(form).First(c => c.Name == "settingsCollapseAll");
                TabControl tabs = form.Tabs!;
                int withGroups = 0, without = 0;
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    tabs.SelectedIndex = i;
                    bool hasGroup = Walk(tabs.TabPages[i]).OfType<CollapsibleGroup>().Any();
                    if (hasGroup) withGroups++; else without++;
                    if (expand.Enabled != hasGroup || collapse.Enabled != hasGroup)
                        problems.Add($"page {i}: has groups = {hasGroup}, expand/collapse enabled = {expand.Enabled}/{collapse.Enabled}");
                }
                Assert.True(withGroups > 0 && without > 0, "the guard needs one page of each kind");
            });
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void TheHeaderCommandsComeBeforeThePagesInTheTabOrder()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config("English"));
                Control find = Walk(form).First(c => c.Name == "settingsSearch");
                Control header = find;
                while (header.Parent != null && header.Parent != form) header = header.Parent;
                Assert.True(header.TabIndex < form.Tabs!.TabIndex, $"header {header.TabIndex}, pages {form.Tabs.TabIndex}");
            });
        }
    }
}
