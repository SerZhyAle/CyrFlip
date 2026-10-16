using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Captions, hints and marker bounds come from the same UI tree as the screenshot.</summary>
    internal static class HelpElements
    {
        internal sealed class Element
        {
            internal Control Control = null!;
            internal string Name = "";
            internal string Explanation = "";
            internal Rectangle Bounds;
            internal int Number;
            internal string Anchor => "element-" + Number;
        }

        internal static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control control in root.Controls)
            {
                yield return control;
                foreach (Control child in Walk(control)) yield return child;
            }
        }

        internal static List<Element> Describe(Form form)
        {
            string language = form is ThemedForm themed ? themed.HelpLanguage : ContextHelp.ApplicationLanguage();
            string T(string key) => Localization.Translate(language, key);
            var hints = new Dictionary<Control, string>();
            if (form is SettingsForm settings)
                foreach (var pair in settings.HintPairs) hints[pair.Key] = pair.Value.Text;
            ToolTip[] tips = form.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => typeof(ToolTip).IsAssignableFrom(f.FieldType)).Select(f => f.GetValue(form)).OfType<ToolTip>().ToArray();
            var result = new List<Element>();
            foreach (Control control in Walk(form))
            {
                if (!control.Visible || control.Name == "contextHelp" || control.Name.StartsWith("settings", StringComparison.Ordinal)) continue;
                bool significant = control is ButtonBase || control is TextBoxBase || control is ComboBox || control is NumericUpDown
                    || control is TrackBar || control is ListView || control is ListBox || control is DataGridView
                    || control is ToggleSwitch || control is SymbolGrid || control is TrafficLightTitleBar || control is TrafficLightBoard;
                if (!significant) continue;
                string name = control.AccessibleName ?? "";
                if (string.IsNullOrWhiteSpace(name) && control is ButtonBase) name = control.Text;
                if (string.IsNullOrWhiteSpace(name))
                {
                    for (Control? container = control.Parent; container != null && container != form; container = container.Parent)
                    {
                        name = container.Controls.OfType<Label>().FirstOrDefault(l => l.Visible && !string.IsNullOrWhiteSpace(l.Text))?.Text ?? "";
                        if (name.Length > 0) break;
                    }
                }
                if (string.IsNullOrWhiteSpace(name)) name = control is ListView || control is ListBox || control is DataGridView ? T("The elements") : T("Choose or enter the value for the adjacent label. Review it before continuing.");
                string explanation = control.AccessibleDescription ?? "";
                if (explanation.Length == 0) explanation = tips.Select(t => t.GetToolTip(control)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
                for (Control? anchor = control; explanation.Length == 0 && anchor != null; anchor = anchor.Parent)
                    if (hints.TryGetValue(anchor, out string? hint)) explanation = hint;
                if (explanation.Length == 0)
                    explanation = T(control is ButtonBase ? "Use this command when you need the action named above. Review any confirmation before accepting it."
                        : control is ListView || control is ListBox || control is DataGridView ? "This area shows the current contents or status. Select an entry to work with it."
                        : "Choose or enter the value for the adjacent label. Review it before continuing.");
                if (!control.Enabled) explanation += " " + T("This control is currently unavailable. Enable its feature or select an entry first.");
                if (control is ListView list) explanation += " " + string.Join(" / ", list.Columns.Cast<ColumnHeader>().Select(c => c.Text));
                result.Add(new Element { Control = control, Name = name.Replace("&", ""), Explanation = explanation,
                    Bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle)), Number = result.Count + 1 });
            }
            // Owner-drawn commands expose their own element tree; never infer positions from pixels.
            foreach (Control owner in new[] { (Control)form }.Concat(Walk(form)))
            {
                if (!(owner is ClipboardHistoryWindow) && !(owner is TrafficLightTitleBar) && !(owner is TrafficLightBoard)) continue;
                if (!owner.Visible) continue;
                AccessibleObject accessible = owner.AccessibilityObject;
                for (int index = 0; index < accessible.GetChildCount(); index++)
                {
                    AccessibleObject? child = accessible.GetChild(index);
                    if (child == null || string.IsNullOrWhiteSpace(child.Name)) continue;
                    result.Add(new Element { Control = owner, Name = child.Name!, Number = result.Count + 1,
                        Explanation = T("Use this command when you need the action named above. Review any confirmation before accepting it."),
                        Bounds = form.RectangleToClient(child.Bounds) });
                }
            }
            return result;
        }
    }
}
