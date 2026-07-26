using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Edits one source-layout → target-layout conversion and its global shortcut.
    ///
    /// Laid out by a <see cref="TableLayoutPanel"/> inside an auto-sizing form rather than by pixel
    /// coordinates: the captions are translated into 13 languages and the whole thing is drawn at
    /// whatever the display scaling makes of the UI font, so any fixed geometry ends up cutting a
    /// button caption in half. Everything here sizes to its content.
    /// </summary>
    internal sealed class LayoutConversionDialog : Form
    {
        private readonly ComboBox _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _hotkey = new Label
        {
            AutoSize = true, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 5, 6, 5),
        };
        private readonly ComboBox _target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly string _uiLanguage;

        public LayoutConversionProfile? Profile { get; private set; }

        public LayoutConversionDialog(List<InputLayouts.Installed> layouts, LayoutConversionProfile? existing, string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            Text = T(existing == null ? "Новая конвертация раскладок" : "Изменить конвертацию");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ApplyScript(uiLanguage);

            foreach (InputLayouts.Installed layout in layouts)
            {
                string label = layout.LanguageName + (layout.DisplayName.Length > 0 ? " — " + layout.DisplayName : "") + "  (" + layout.Klid + ")";
                _source.Items.Add(new LayoutItem(layout.Klid, label)); _target.Items.Add(new LayoutItem(layout.Klid, label));
            }
            _source.Width = _target.Width = ComboWidth();
            SelectKlid(_source, existing?.SourceKlid); SelectKlid(_target, existing?.TargetKlid);
            _hotkey.Text = existing?.Hotkey ?? T("Не назначено");
            _hotkey.MinimumSize = new Size(TextWidth("Ctrl+Shift+Backspace"), 0); // the longest chord we can produce

            var set = new Button { Text = T("Задать..."), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(8, 3, 3, 3) };
            set.Click += (_, _) => SetHotkey();

            var ok = new Button { Text = "OK", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.OK };
            ok.Click += (_, _) => BuildProfile(existing);
            var cancel = new Button { Text = T("Отмена"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.Cancel };

            // RightToLeft flow, so the first control added sits rightmost - and WinForms mirrors the whole
            // thing for Arabic and Urdu, which puts OK on the left, as those UIs expect.
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            grid.Controls.Add(Caption(T("Из раскладки:")), 0, 0);
            grid.Controls.Add(_source, 1, 0); grid.SetColumnSpan(_source, 2);
            grid.Controls.Add(Caption(T("В раскладку:")), 0, 1);
            grid.Controls.Add(_target, 1, 1); grid.SetColumnSpan(_target, 2);
            grid.Controls.Add(Caption(T("Комбинация:")), 0, 2);
            grid.Controls.Add(_hotkey, 1, 2);
            grid.Controls.Add(set, 2, 2);
            grid.Controls.Add(buttons, 0, 3); grid.SetColumnSpan(buttons, 3);
            Controls.Add(grid);

            AcceptButton = ok; CancelButton = cancel;
        }

        private string T(string ru) => Localization.Translate(_uiLanguage, ru);

        /// <summary>Mirrors for Arabic/Urdu and picks a font that can draw the script - as the settings window does.</summary>
        private void ApplyScript(string uiLanguage)
        {
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family == null) return;
            try { Font = new Font(family, Font.SizeInPoints); }
            catch { /* the font is missing on this machine - keep the default */ }
        }

        private static Label Caption(string text)
            => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        /// <summary>
        /// Wide enough for the longest layout name in the list, within reason: these names come from
        /// Windows and can be long ("Английский (Соединённые Штаты) — США-международная"), but a dialog
        /// wider than the screen helps nobody, so past the cap the combo scrolls instead.
        /// </summary>
        private int ComboWidth()
        {
            int width = TextWidth("English (United States) — US  (00000409)");
            foreach (object item in _source.Items)
                width = Math.Max(width, TextWidth(item.ToString() ?? "") + SystemInformation.VerticalScrollBarWidth);
            return Math.Min(width, TextWidth(new string('W', 46)));
        }

        private static void SelectKlid(ComboBox combo, string? klid)
        {
            for (int i = 0; i < combo.Items.Count; i++)
                if (((LayoutItem)combo.Items[i]).Klid.Equals(klid, StringComparison.OrdinalIgnoreCase)) { combo.SelectedIndex = i; return; }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void SetHotkey()
        {
            using var dialog = new HotkeyDialog(_hotkey.Text, T("Сочетание конвертации"), _uiLanguage);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.CapturedHotkey != null) _hotkey.Text = dialog.CapturedHotkey;
        }

        private void BuildProfile(LayoutConversionProfile? existing)
        {
            if (!(_source.SelectedItem is LayoutItem source) || !(_target.SelectedItem is LayoutItem target)
                || !Hotkey.TryParse(_hotkey.Text, out _)) { DialogResult = DialogResult.None; return; }

            // Converting a layout into itself would spend a clipboard round trip to change nothing.
            if (string.Equals(source.Klid, target.Klid, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, T("Исходная и целевая раскладки должны отличаться."),
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            Profile = new LayoutConversionProfile { Id = existing?.Id ?? Guid.NewGuid().ToString("N"), SourceKlid = source.Klid, TargetKlid = target.Klid, Hotkey = _hotkey.Text, Enabled = existing?.Enabled ?? true };
        }

        private sealed class LayoutItem { public readonly string Klid, Label; public LayoutItem(string klid, string label) { Klid = klid; Label = label; } public override string ToString() => Label; }
    }
}
