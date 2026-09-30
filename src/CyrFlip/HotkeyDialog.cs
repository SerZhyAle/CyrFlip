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
    internal sealed class HotkeyDialog : ThemedForm
    {
        private readonly Label _hintLabel;
        private readonly Label _previewLabel;
        // Says why a chord was refused (HotkeyRules); empty and collapsed otherwise.
        private readonly Label _reasonLabel;
        private readonly TableLayoutPanel _layout;
        private readonly string _uiLanguage;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        // WinForms never disposes a font that was assigned to a control, so the one we build here is
        // ours to release - otherwise every trip through the dialog leaves a GDI font behind.
        private readonly Font _previewFont;
        // The script font for hi/bn/zh, assigned to the form - equally ours to release (ST-5).
        private readonly Font? _ownFont;

        /// <summary>The widest chord the capture can produce (three modifiers and the longest key name).</summary>
        internal const string LongestChord = "Ctrl+Shift+Alt+Backspace";

        private bool _ctrl, _shift, _alt;
        private string _keyName = "";
        private bool _captured;

        /// <summary>The hotkey string (e.g. "Ctrl+Shift+F12") if the user confirmed, else null.</summary>
        public string? CapturedHotkey { get; private set; }

        public HotkeyDialog(string currentHotkey, string title = "Set hotkey", string uiLanguage = "English")
        {
            Text = title;
            _uiLanguage = uiLanguage;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
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
                // Measured with the font it is drawn in (1.4x bold), and for the longest chord the
                // capture can produce - a regular-font measure clipped it (ticket S0007, DL-5).
                MinimumSize = new Size(TextRenderer.MeasureText(LongestChord, previewFont).Width + 16, 0),
                Font = previewFont,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ThemePalette.Light.SurfaceRaised,
            };

            _reasonLabel = new Label
            {
                Text = "",
                AutoSize = true,
                MaximumSize = new Size(TextWidth(new string('W', 34)), 0),
                ForeColor = ThemePalette.Light.Danger,
                Margin = new Padding(3, 6, 3, 0),
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

            var layout = _layout = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            layout.Controls.Add(_hintLabel, 0, 0);
            layout.Controls.Add(_previewLabel, 0, 1);
            layout.Controls.Add(buttons, 0, 3);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Controls.Add(layout);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); // frees the control tree, but not the font we handed the label
            if (disposing)
            {
                _previewFont.Dispose();
                _ownFont?.Dispose();
                if (_reasonLabel.Parent == null) _reasonLabel.Dispose(); // outside the tree while silent
            }
        }

        /// <summary>
        /// The refusal line joins the layout only while it has something to say - out of the
        /// control tree, not merely hidden, so the layout guard (DialogLayoutTests) measures only
        /// what is actually laid out.
        /// </summary>
        private void ShowReason(string reason)
        {
            _reasonLabel.Text = reason;
            bool shown = _reasonLabel.Parent != null;
            if (reason.Length > 0 && !shown) _layout.Controls.Add(_reasonLabel, 0, 2);
            else if (reason.Length == 0 && shown) _layout.Controls.Remove(_reasonLabel);
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

            // TryParse is the round trip every setter makes; a name it did not know once turned a
            // captured Ctrl+Shift+PageUp into Ctrl+Shift+F12 (ticket S0004, KC-2). The key-name
            // round-trip test keeps this branch unreachable.
            string display = BuildDisplay(e.Control, e.Shift, e.Alt, keyName);
            if (!Hotkey.TryParse(display, out Hotkey chord))
                return;
            _previewLabel.Text = display;

            // A chord that would eat typing is shown, explained and not accepted (KC-5).
            string reason = HotkeyRules.Describe(HotkeyRules.Check(chord), _uiLanguage);
            ShowReason(reason);
            if (reason.Length > 0)
            {
                _captured = false;
                _okButton.Enabled = false;
                return;
            }

            _ctrl = e.Control;
            _shift = e.Shift;
            _alt = e.Alt;
            _keyName = keyName;
            _captured = true;
            _okButton.Enabled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // Windows posts no WM_KEYDOWN for PrintScreen, only the key-up, so that one key is
            // captured on its release (S0026 - the region capture's default chord uses it).
            if (e.KeyCode == Keys.PrintScreen)
            {
                OnKeyDown(e);
                return;
            }
            e.Handled = true;
        }

        private static int _capturingDialogs;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (System.Threading.Interlocked.Increment(ref _capturingDialogs) > 0)
                KeyboardHook.SuspendChords = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (System.Threading.Interlocked.Decrement(ref _capturingDialogs) <= 0)
                KeyboardHook.SuspendChords = false;
            base.OnFormClosed(e);
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

        /// <summary>The key names the dialog emits; every one must parse back (HotkeyRoundTripTests).</summary>
        internal static bool TryGetKeyName(Keys key, out string name)
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
                case Keys.PrintScreen: name = "PrintScreen"; return true;
            }
            name = "";
            return false;
        }
    }
}
