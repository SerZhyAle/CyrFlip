using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    internal sealed class SettingsTransferDialog : ThemedForm
    {
        private readonly Func<ISet<string>, SettingsImportPlan> _build;
        private readonly List<KeyValuePair<CheckBox, string[]>> _choices = new List<KeyValuePair<CheckBox, string[]>>();
        private readonly FlowLayoutPanel _changes;
        private readonly Label _status;
        private readonly Button _apply;
        private readonly string _language;
        private readonly Font? _ownFont;
        internal SettingsImportPlan Plan { get; private set; }
        internal ISet<string> SelectedKeys { get; private set; } = new HashSet<string>();
        internal SettingsTransferDialog(string language, SettingsFileDocument document, Func<ISet<string>, SettingsImportPlan> build)
        {
            _language = language; _build = build;
            string T(string text) => Localization.Translate(language, text);
            Plan = build(new HashSet<string>(document.Values.Keys));
            Text = T("Settings transfer"); ShowIcon = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
            if (family != null) Font = _ownFont = new Font(family, Font.SizeInPoints);
            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(12) };
            int width = Math.Min(Screen.PrimaryScreen.WorkingArea.Width * 3 / 4, TextRenderer.MeasureText(new string('x', 90), Font).Width);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(width, 0), UseMnemonic = false,
                Text = Localization.Format(T, "File version: {0}; date: {1}", document.App, document.ExportedAtUtc) });
            if (Version.TryParse(document.App, out Version? version) && version > typeof(AppConfig).Assembly.GetName().Version)
                layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(width, 0), Text = T("This file was created by a newer CyrFlip version.") });
            var choices = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            foreach (var group in Plan.Changes.Where(c => !c.OptIn).GroupBy(c => c.Entry.Group))
            {
                // The page and then the group (S0062): a group caption alone - "Запись", "С клавиатуры" - does not
                // say which feature it belongs to, and the page titles are all translated already.
                string pageCaption = PageCaption(group.Key), groupCaption = GroupCaption(group.Key);
                string shown = pageCaption == groupCaption ? T(pageCaption) : T(pageCaption) + " - " + T(groupCaption);
                var box = new CheckBox { Text = shown, Checked = true, AutoSize = true };
                _choices.Add(new KeyValuePair<CheckBox, string[]>(box, group.Select(c => c.Entry.Key).ToArray())); choices.Controls.Add(box);
            }
            foreach (var change in Plan.Changes.Where(c => c.OptIn))
            {
                string caption = change.Entry.Key == "TranslateEndpoint" ? "Send selections to another server"
                    : change.Entry.Key == "TrafficLightTargets" ? "Add the availability traffic light targets: CyrFlip will check the addresses listed in the file"
                    : change.Entry.Caption;
                var box = new CheckBox { Text = T(caption), Checked = false, AutoSize = true };
                _choices.Add(new KeyValuePair<CheckBox, string[]>(box, new[] { change.Entry.Key })); choices.Controls.Add(box);
            }
            layout.Controls.Add(choices);
            _changes = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                MaximumSize = new Size(width, 0) };
            var scroll = new Panel { AutoScroll = true, Width = width,
                Height = Math.Min(Screen.PrimaryScreen.WorkingArea.Height / 3, Font.Height * 16), Margin = new Padding(0, 8, 0, 8) };
            scroll.Controls.Add(_changes); layout.Controls.Add(scroll);
            _status = new Label { AutoSize = true, MaximumSize = new Size(width, 0), UseMnemonic = false }; layout.Controls.Add(_status);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var cancel = ButtonGlyphs.Answer(new Button { AutoSize = true, Text = T(Plan.Changes.Count == 0 ? "Close" : "Cancel"), DialogResult = DialogResult.Cancel });
            _apply = ButtonGlyphs.Answer(new Button { AutoSize = true, Text = T("Apply"), DialogResult = DialogResult.OK });
            buttons.Controls.Add(cancel); buttons.Controls.Add(_apply); layout.Controls.Add(buttons); Controls.Add(layout);
            CancelButton = cancel; AcceptButton = cancel;
            var offered = new HashSet<string>(_choices.SelectMany(c => c.Value));
            // Identical settings stay selected so a checked table can suppress reseeding even when empty.
            var unchanged = new HashSet<string>(document.Values.Keys.Where(k => !offered.Contains(k)));
            void Update()
            {
                SelectedKeys = new HashSet<string>(unchanged);
                foreach (var pair in _choices.Where(c => c.Key.Checked)) SelectedKeys.UnionWith(pair.Value);
                Plan = _build(SelectedKeys); Render(document.UnknownKeys, width);
            }
            foreach (var pair in _choices) pair.Key.CheckedChanged += (_, _) => Update();
            foreach (var pair in _choices) pair.Key.MinimumSize = new Size(TextRenderer.MeasureText(pair.Key.Text, Font).Width + Font.Height * 2, 0);
            foreach (Button button in buttons.Controls) button.MinimumSize = new Size(TextRenderer.MeasureText(button.Text, Font).Width + Font.Height, 0);
            Update();
        }
        private void Render(int unknown, int width)
        {
            string T(string text) => Localization.Translate(_language, text);
            _changes.SuspendLayout();
            foreach (Control control in _changes.Controls.Cast<Control>().ToArray()) control.Dispose();
            foreach (var change in Plan.Changes)
            {
                string Value(object value) => change.Entry.IsTable
                    ? Localization.Format(T, "Rows: {0}", new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<object[]>((string)value).Length)
                    : change.Entry.Property.PropertyType == typeof(bool) ? T((int)value != 0 ? "On" : "Off") : Convert.ToString(value)!;
                _changes.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(width - 24, 0), UseMnemonic = false,
                    Text = T(change.Entry.Caption) + ": " + Value(change.Before) + " → " + Value(change.After) });
                if (change.Entry.IsTable)
                {
                    var detail = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = width - 24 };
                    foreach (object value in new[] { change.Before, change.After })
                        detail.Controls.Add(new TextBox { ReadOnly = true, Multiline = true, WordWrap = false,
                            ScrollBars = ScrollBars.Both, Width = (width - 48) / 2, Height = Font.Height * 6,
                            Text = SettingsFileWriter.Json(new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject((string)value)).Replace("\n", "\r\n") });
                    _changes.Controls.Add(detail);
                }
            }
            foreach (var issue in Plan.Issues)
                _changes.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(width - 24, 0), UseMnemonic = false,
                    Text = issue.Key + ": " + T(Reason(issue.Reason)) });
            _status.Text = Localization.Format(T, "Unchanged: {0}; unknown keys: {1}", Plan.Unchanged, unknown);
            if (Plan.Changes.Count == 0) _status.Text += "\n" + T("Settings already match.");
            _apply.Enabled = Plan.Changes.Count > 0; _apply.Visible = _choices.Count > 0;
            _changes.ResumeLayout(true);
        }
        internal static string Reason(string code)
        {
            switch (code)
            {
                case "clamped": return "Number clamped to the allowed range.";
                case "chord": return "Shortcut invalid or in use; local value kept. Table rows are disabled without a shortcut.";
                case "windows": return "Windows owns this shortcut. Import it only with your consent.";
                case "layout": return "Keyboard layout is not installed.";
                default: return "Invalid value; skipped.";
            }
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _ownFont?.Dispose();
        }
        internal static string GroupCaption(string id)
        {
            switch (id)
            {
                case "translate/ollama": return "Ollama";
                case "translate/directions": return "Translation directions";
                case "translate/result": return "Result";
                case "notes/opening": return "How to open it";
                case "notes/editor": return "Editor";
                case "notes/storage": return "Export and storage";
                case "graphics/capture": return "Screen region capture";
                // S0062: the groups the regrouped pages are made of.
                case "general/language": return "Language and theme";
                case "general/power": return "Power and screen";
                case "hotkeys/switches": return "Main switches";
                case "conversions/table": return "Layout table";
                case "conversions/after": return "After conversion";
                case "conversions/symbols": return "Punctuation";
                case "conversions/case": return "Letter case";
                case "clipboard/recording": return "Recording";
                case "clipboard/window": return "History window";
                case "selection/mouse": return "Menu over a selection";
                case "selection/keyboard": return "From the keyboard";
                case "trafficlight/targets": return "Targets";
            }
            return PageCaption(id);
        }

        /// <summary>The page a group id belongs to (its first segment), by the page's source title.</summary>
        internal static string PageCaption(string id)
        {
            switch (id.Split('/')[0])
            {
                case "general": return "General"; case "indicators": return "Indicators";
                case "hotkeys": return "Hotkeys"; case "conversions": return "Text correction";
                case "clipboard": return "Clipboard"; case "selection": return "Selection menu";
                case "translate": return "Translation"; case "notes": return "Quick notes";
                case "trafficlight": return "Availability traffic light";
                default: return "Screenshots";
            }
        }
    }
}
