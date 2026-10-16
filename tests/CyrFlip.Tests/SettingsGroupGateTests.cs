using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0060 M4 (APP-SETTINGS rule 3, WINDOWS-UI section 3 rule 1): on a page that is made of groups, every row
    /// that carries an editor or a command stands inside a group. A loose switch or button at the foot of a
    /// grouped page is a row the group headers, the expand/collapse commands and the remembered expansion
    /// all skip.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsGroupGateTests
    {
        /// <summary>
        /// The controls a grouped page may still hold outside every group, each with its reason. A banner or a
        /// status line is page chrome, not a setting: it has no editor and no command, so the walk below would
        /// not flag it anyway - this list is for the day one that does must stay loose on purpose. Empty today.
        /// </summary>
        private static readonly (SettingsPageKey Page, string ControlName, string Reason)[] DeclaredLoose = { };

        private static bool IsEditorOrCommand(Control control)
            => control is CheckBox || control is RadioButton || control is Button || control is ComboBox
               || control is NumericUpDown || control is TrackBar || control is TextBox || control is LinkLabel;

        /// <summary>True when the control, or anything under it, is an editor or a command.</summary>
        internal static bool CarriesAnEditor(Control control)
            => IsEditorOrCommand(control) || control.Controls.Cast<Control>().Any(CarriesAnEditor);

        private static string Describe(Control control)
        {
            Control? editor = control;
            if (!IsEditorOrCommand(control))
                editor = RegroupingHarness.Walk(control).FirstOrDefault(IsEditorOrCommand);
            return control.GetType().Name + (control.Name.Length > 0 ? " '" + control.Name + "'" : "")
                + (editor != null ? " holding " + editor.GetType().Name + " \"" + editor.Text + "\"" : "");
        }

        [Fact]
        public void NoPageMadeOfGroupsHoldsALooseEditorOrCommand()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config("English"));
                var problems = new List<string>();
                int grouped = 0;
                foreach (TabPage page in form.Tabs!.TabPages)
                {
                    var panel = (FlowLayoutPanel)page.Controls[0];
                    if (!panel.Controls.OfType<CollapsibleGroup>().Any()) continue;   // a flat page is not a grouping
                    grouped++;
                    var key = (SettingsPageKey)page.Tag!;
                    foreach (Control child in panel.Controls)
                    {
                        if (child is CollapsibleGroup) continue;
                        // The page's own title (hidden, shown by the header band) and purpose line.
                        if (child is Label label && (label.Tag is string tag && tag == SettingsForm.PageDescriptionTag
                            || label.Font.Bold && !label.Visible)) continue;
                        if (DeclaredLoose.Any(d => d.Page == key && d.ControlName == child.Name)) continue;
                        if (CarriesAnEditor(child)) problems.Add(page.Text + ": " + Describe(child));
                    }
                }

                Assert.True(grouped >= 11, "only " + grouped + " pages with groups were found - the walk is looking at the wrong thing");
                Assert.True(problems.Count == 0, "A row with an editor or a command stands outside every group:\n" + string.Join("\n", problems));
            });
        }

        [Fact]
        public void TheGateSeesAnEditorUnderALooseContainer()
        {
            var row = new FlowLayoutPanel();
            row.Controls.Add(new Label());
            Assert.False(CarriesAnEditor(row));
            var inner = new FlowLayoutPanel();
            inner.Controls.Add(new Button());
            row.Controls.Add(inner);
            Assert.True(CarriesAnEditor(row));
            Assert.True(CarriesAnEditor(new LinkLabel()));
            Assert.False(CarriesAnEditor(new Label()));
        }

        [Fact]
        public void TheModulePagesLeadWithAGroupHoldingTheirSwitch()
        {
            // The hotkeys page's precedent (hotkeys/switches) on every module page: the switch is in the first
            // group, found by its stable name, and the page holds no row outside its groups.
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(RegroupingHarness.Config("English"));
                var leaders = new Dictionary<SettingsPageKey, (string Group, string Control)>
                {
                    [SettingsPageKey.Translate] = ("translate/switches", "_translateEnabled"),
                    [SettingsPageKey.QuickNotes] = ("notes/switches", "_quickNotesEnabled"),
                    [SettingsPageKey.Symbols] = ("symbols/switches", "_symbolsEnabled"),
                    [SettingsPageKey.TrafficLight] = ("trafficlight/switches", "_tlEnabled"),
                    [SettingsPageKey.Hotkeys] = ("hotkeys/switches", "_enableHotkeys"),
                };
                foreach (KeyValuePair<SettingsPageKey, (string Group, string Control)> pair in leaders)
                {
                    TabPage page = RegroupingHarness.PageOf(form, pair.Key);
                    CollapsibleGroup first = ((FlowLayoutPanel)page.Controls[0]).Controls.OfType<CollapsibleGroup>().First();
                    Assert.Equal(pair.Value.Group, first.Id);
                    var box = (CheckBox)typeof(SettingsForm).GetField(pair.Value.Control,
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(form)!;
                    Assert.True(RegroupingHarness.Walk(first).Contains(box), pair.Key + ": the switch is not in " + pair.Value.Group);
                }
            });
        }
    }
}
