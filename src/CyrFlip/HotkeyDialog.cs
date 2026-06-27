using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Modal dialog that captures a new hotkey combination from the user.
    /// Requires at least one modifier key (Ctrl / Shift / Alt) plus a trigger key.
    /// </summary>
    internal sealed class HotkeyDialog : Form
    {
        private readonly Label _hintLabel;
        private readonly Label _previewLabel;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        private bool _ctrl, _shift, _alt;
        private string _keyName = "";
        private bool _captured;

        /// <summary>The hotkey string (e.g. "Ctrl+Shift+F12") if the user confirmed, else null.</summary>
        public string? CapturedHotkey { get; private set; }

        public HotkeyDialog(string currentHotkey, string title = "Set hotkey")
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(320, 130);
            ShowInTaskbar = false;
            KeyPreview = true;

            _hintLabel = new Label
            {
                Text = "New combo (a modifier is non-negotiable):",
                AutoSize = false,
                Location = new Point(12, 16),
                Size = new Size(296, 18),
            };

            _previewLabel = new Label
            {
                Text = currentHotkey,
                Location = new Point(12, 42),
                Size = new Size(296, 30),
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
            };

            _okButton = new Button
            {
                Text = "OK",
                Location = new Point(148, 90),
                Size = new Size(72, 26),
                DialogResult = DialogResult.OK,
                Enabled = false,
            };

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(236, 90),
                Size = new Size(72, 26),
                DialogResult = DialogResult.Cancel,
            };

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Controls.AddRange(new Control[] { _hintLabel, _previewLabel, _okButton, _cancelButton });
        }

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
