using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Modal dialog that captures a new hotkey combination from the user.
    /// Requires at least one modifier key (Ctrl / Shift / Alt) plus a trigger key.
    ///
    /// Sizes itself to its content (see <see cref="LayoutConversionDialog"/> for why): the hint is
    /// translated into 13 languages and the whole dialog is drawn at whatever the display scaling makes
    /// of the UI font, so fixed geometry clips captions.
    /// </summary>
    internal sealed class HotkeyDialog : Form
    {
        private readonly Label _hintLabel;
        private readonly Label _previewLabel;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        // WinForms never disposes a font that was assigned to a control, so the one we build here is
        // ours to release - otherwise every trip through the dialog leaves a GDI font behind.
        private readonly Font _previewFont;

        private bool _ctrl, _shift, _alt;
        private string _keyName = "";
        private bool _captured;

        /// <summary>The hotkey string (e.g. "Ctrl+Shift+F12") if the user confirmed, else null.</summary>
        public string? CapturedHotkey { get; private set; }

        public HotkeyDialog(string currentHotkey, string title = "Set hotkey", string uiLanguage = "English")
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }

            _hintLabel = new Label
            {
                Text = Localization.Translate(uiLanguage, "Новая комбинация (модификатор обязателен):"),
                AutoSize = true,
                MaximumSize = new Size(TextWidth(new string('W', 34)), 0), // wrap rather than widen without limit
                Margin = new Padding(3, 3, 3, 8),
            };

            // Derived from the dialog's own font, not from a hard-coded family: the chord is shown large
            // and bold, but in the family the current language needs (Devanagari, Bengali, Chinese).
            Font previewFont = _previewFont = new Font(Font.FontFamily, Font.SizeInPoints * 1.4f, FontStyle.Bold);
            _previewLabel = new Label
            {
                Text = currentHotkey,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = previewFont.Height + 12,
                MinimumSize = new Size(TextWidth("Ctrl+Shift+Backspace"), 0),
                Font = previewFont,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
            };

            _okButton = Command("OK", DialogResult.OK);
            _okButton.Enabled = false;
            _cancelButton = Command(Localization.Translate(uiLanguage, "Отмена"), DialogResult.Cancel);

            // RightToLeft flow puts the first control added on the right; WinForms mirrors it for RTL UIs.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_okButton);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            layout.Controls.Add(_hintLabel, 0, 0);
            layout.Controls.Add(_previewLabel, 0, 1);
            layout.Controls.Add(buttons, 0, 2);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Controls.Add(layout);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); // frees the control tree, but not the font we handed the label
            if (disposing) _previewFont.Dispose();
        }

        private Button Command(string text, DialogResult result) => new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = result,
        };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Pure modifier press: update preview but don't confirm yet.
            if (IsModifierOnly(e.KeyCode))
            {
                string mods = FormatModifiers(e.Control, e.Shift, e.Alt);
                _previewLabel.Text = mods.Length > 0 ? mods + "+..." : "...";
                return;
            }

            // Esc with no modifiers = cancel.
            if (e.KeyCode == Keys.Escape && !e.Control && !e.Shift && !e.Alt)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            // Require at least one modifier.
            if (!e.Control && !e.Shift && !e.Alt)
                return;

            if (!TryGetKeyName(e.KeyCode, out string keyName))
                return;

            _ctrl = e.Control;
            _shift = e.Shift;
            _alt = e.Alt;
            _keyName = keyName;
            _captured = true;

            _previewLabel.Text = BuildDisplay(_ctrl, _shift, _alt, _keyName);
            _okButton.Enabled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK && _captured)
                CapturedHotkey = BuildDisplay(_ctrl, _shift, _alt, _keyName);
            base.OnFormClosing(e);
        }

        private static string BuildDisplay(bool ctrl, bool shift, bool alt, string key)
        {
            var parts = new List<string>(4);
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            if (key.Length > 0) parts.Add(key);
            return string.Join("+", parts);
        }

        private static string FormatModifiers(bool ctrl, bool shift, bool alt)
        {
            var parts = new List<string>(3);
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (alt) parts.Add("Alt");
            return string.Join("+", parts);
        }

        private static bool IsModifierOnly(Keys key)
            => key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
            || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
            || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;

        private static bool TryGetKeyName(Keys key, out string name)
        {
            if (key >= Keys.F1 && key <= Keys.F24)
            {
                name = "F" + ((int)key - (int)Keys.F1 + 1);
                return true;
            }
            if (key >= Keys.A && key <= Keys.Z)
            {
                name = key.ToString();
                return true;
            }
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                name = ((char)('0' + (key - Keys.D0))).ToString();
                return true;
            }
            switch (key)
            {
                case Keys.Space: name = "Space"; return true;
                case Keys.Return: name = "Enter"; return true;
                case Keys.Tab: name = "Tab"; return true;
                case Keys.Back: name = "Backspace"; return true;
                case Keys.Delete: name = "Delete"; return true;
                case Keys.Insert: name = "Insert"; return true;
                case Keys.Home: name = "Home"; return true;
                case Keys.End: name = "End"; return true;
                case Keys.PageUp: name = "PageUp"; return true;
                case Keys.PageDown: name = "PageDown"; return true;
            }
            name = "";
            return false;
        }
    }
}
