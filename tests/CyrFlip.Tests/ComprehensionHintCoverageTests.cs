using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0060 R3 (APP-SETTINGS rule 3, "a setting without a hint is a setting nobody can reason about"): the runtime
    /// half of the hint gate. <see cref="ComprehensionGateTests.EveryCheckboxOfSettingsHasAHintBesideIt"/> reads the
    /// source for <c>Check(...)</c> fields; this one reads the built window, so a drop-down, a numeric editor, a
    /// slider or a text box that was added without a description fails too. A value editor is any switch, radio
    /// button, drop-down, numeric editor, track bar or text box; its row must hold a non-empty muted description.
    /// Commands (buttons) are operations, not settings (APP-SETTINGS section 1), and table rows - a switch in a
    /// row of a table - stand under the table's own description: both are exempt by type, never by name.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class ComprehensionHintCoverageTests
    {
        /// <summary>The editors that are not settings, each with its reason. Named on purpose: an exemption is a decision.</summary>
        private static readonly Dictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["_launcherSearch"] = "a filter of the scenario table under it, not a setting: it stores nothing and is not applied anywhere",
            ["_tlAddBox"] = "the input field of the \"add a target\" action; the sentence above it in its group is its lead-in",
        };

        private static bool IsValueEditor(Control control)
            => control is CheckBox and not RowCheckBox || control is RadioButton || control is ComboBox
               || control is NumericUpDown || control is TrackBar || control is TextBox;

        private static bool IsEditorWithOwnInside(Control control)
            => control is NumericUpDown || control is ComboBox || control is TextBoxBase || control is TrackBar || control is ListView;

        private static bool IsMutedNonEmptyLabel(Control control)
            => control is Label label && !(label is LinkLabel)
               && label.Text.Trim().Length > 0
               && !SettingsForm.DependencyNoteTag.Equals(label.Tag)
               && ThemePalette.RoleOfDesignColor(label.ForeColor) == ThemeRole.TextMuted;

        private static bool HoldsMutedHint(Control control)
        {
            if (IsMutedNonEmptyLabel(control)) return true;
            if (IsEditorWithOwnInside(control)) return false;
            return control.Controls.Cast<Control>().Any(HoldsMutedHint);
        }

        /// <summary>A row of a page: a direct child of a group's body or of a flat page's panel.</summary>
        private static bool StandsInRowContainer(Control? parent)
            => parent is FlowLayoutPanel && (parent.Parent is CollapsibleGroup || parent.Parent is TabPage);

        private static IEnumerable<Control> Editors(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (IsValueEditor(child)) yield return child;
                if (IsEditorWithOwnInside(child)) continue;
                foreach (Control deeper in Editors(child)) yield return deeper;
            }
        }

        /// <summary>The value editors of <paramref name="root"/> whose row (the nearest ancestor holding a description) has none.</summary>
        internal static List<Control> WithoutHint(Control root)
        {
            var offenders = new List<Control>();
            foreach (Control editor in Editors(root))
            {
                Control? cursor = editor;
                bool found = false;
                while (cursor != null)
                {
                    if (HoldsMutedHint(cursor)) { found = true; break; }
                    if (StandsInRowContainer(cursor.Parent)) break;   // the row ends here: its container is not the row
                    cursor = cursor.Parent;
                }
                if (!found) offenders.Add(editor);
            }
            return offenders;
        }

        private static string FieldNameOf(SettingsForm form, Control control)
        {
            foreach (FieldInfo field in typeof(SettingsForm).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                if (ReferenceEquals(field.GetValue(form), control)) return field.Name;
            return "";
        }

        [Fact]
        public void EveryValueEditorOfTheBuiltWindowHasAMutedDescriptionInItsRow()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(SettingsRowAnatomyTests.Config("English"));
                int editors = 0;
                var problems = new List<string>();
                foreach (TabPage page in form.Tabs!.TabPages)
                {
                    editors += Editors(page).Count();
                    foreach (Control offender in WithoutHint(page))
                    {
                        string field = FieldNameOf(form, offender);
                        if (field.Length > 0 && Exempt.ContainsKey(field)) continue;
                        string caption = offender.Text.Length > 0 ? offender.Text : offender.AccessibleName;
                        problems.Add($"{page.Text}: {offender.GetType().Name} {(field.Length > 0 ? field : "'" + caption + "'")} has no description in its row");
                    }
                }
                Assert.True(editors >= 45, "only " + editors + " value editors were found - the gate is looking at the wrong thing");
                Assert.True(problems.Count == 0,
                    "A setting was built without a description (APP-SETTINGS rule 3) - pass it to Setting(...) or HotkeyRow(...) with one:\n"
                    + string.Join("\n", problems));
            });
        }

        [Fact]
        public void EveryExemptionNamesAnEditorThatExists()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(SettingsRowAnatomyTests.Config("English"));
                foreach (string field in Exempt.Keys)
                {
                    FieldInfo? info = typeof(SettingsForm).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.True(info != null, "the exemption '" + field + "' names no field - delete it");
                    var control = (Control)info!.GetValue(form)!;
                    // And it is still unhinted: an exemption that became unnecessary is deleted, not carried.
                    bool anyPage = form.Tabs!.TabPages.Cast<TabPage>().Any(page => WithoutHint(page).Contains(control));
                    Assert.True(anyPage, "'" + field + "' now has a description - remove it from the exemptions");
                }
            });
        }

        /// <summary>The guard fact: the scan finds a drop-down with no description, and is satisfied by one that has it.</summary>
        [Fact]
        public void TheGateSeesAnEditorWithoutADescription()
        {
            var group = new CollapsibleGroup("test/group", "Group");
            var bare = new FlowLayoutPanel { AutoSize = true };
            bare.Controls.Add(new Label { Text = "Language:" });
            bare.Controls.Add(new ComboBox());
            var described = new FlowLayoutPanel { AutoSize = true };
            described.Controls.Add(new CheckBox { Text = "Switch" });
            described.Controls.Add(new Label { Text = "What it does.", ForeColor = ThemePalette.Light.TextMuted });
            group.Body.Controls.Add(bare);
            group.Body.Controls.Add(described);
            var page = new TabPage();
            var panel = new FlowLayoutPanel();
            panel.Controls.Add(group);
            page.Controls.Add(panel);

            List<Control> offenders = WithoutHint(page);
            Assert.Single(offenders);
            Assert.IsType<ComboBox>(offenders[0]);

            // A switch in a table row is a row of its own table: exempt by type.
            bare.Controls.Add(new RowCheckBox());
            Assert.Single(WithoutHint(page));
            // A dependency note is not a description.
            var note = new Label { Text = "Unavailable", ForeColor = ThemePalette.Light.TextMuted, Tag = SettingsForm.DependencyNoteTag };
            bare.Controls.Add(note);
            Assert.Single(WithoutHint(page));
        }
    }
}
