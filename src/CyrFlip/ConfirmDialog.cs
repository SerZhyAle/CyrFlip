using System;
using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// CyrFlip's own message and confirmation box, in place of <see cref="MessageBox"/> (S0020 v0.2 item
    /// 6, S0019 decision D3). The system box is drawn by Windows and is always light - in the dark theme
    /// every one of them was a white flash - and its Yes/No variant has no Escape.
    ///
    /// <para>Three rules. <b>The safe answer is the cancel role</b>: Escape and the close box give No
    /// (or Cancel, or OK where OK is the only answer), never Yes (<c>APP-BEHAVIOUR</c> rule 1).
    /// <b>A destructive question defaults to the safe answer</b> and paints its Yes in the <c>danger</c>
    /// role (<c>APP-STYLE</c> section 4) - the one place a destructive button no longer looks exactly like
    /// the button beside it. <b>It is laid out by its content</b>, never by pixel geometry: the captions
    /// exist in 13 languages, and <c>DialogLayoutTests</c> builds it in each.</para>
    ///
    /// <para>Parity with the system box where it matters: the icon and its sound, Ctrl+C copies the
    /// message, and a box with no owner still comes to the front and has a taskbar button - most of these
    /// are raised from the tray, where there is no window to own them.</para>
    /// </summary>
    internal sealed class ConfirmDialog : ThemedForm
    {
        private readonly string _message;
        private readonly MessageBoxIcon _icon;
        private readonly bool _owned;
        private readonly Font? _ownFont;
        private readonly Bitmap? _iconImage;

        /// <summary>The answer buttons in reading order, for the layout test.</summary>
        internal Button[] Buttons { get; }

        internal ConfirmDialog(string language, string message, MessageBoxButtons buttons, MessageBoxIcon icon, bool danger, bool owned)
        {
            _message = message;
            _icon = icon;
            _owned = owned;
            string T(string ru) => Localization.Translate(language, ru);

            Text = "CyrFlip";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            KeyPreview = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = owned ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            ShowInTaskbar = !owned;
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
            // The system box's own font (SystemFonts builds a new Font on every read - hence the using).
            using (Font system = SystemFonts.MessageBoxFont)
            {
                try { Font = _ownFont = new Font(family ?? system.FontFamily.Name, system.SizeInPoints); }
                catch (ArgumentException) { /* the font is missing on this machine - keep the default */ }
            }

            var layout = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(14, 14, 14, 10),
            };
            Icon? systemIcon = SystemIconFor(icon);
            if (systemIcon != null)
            {
                _iconImage = systemIcon.ToBitmap();
                layout.Controls.Add(new PictureBox
                {
                    Image = _iconImage, SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 2, 12, 0),
                }, 0, 0);
            }
            var text = new Label
            {
                Text = message, AutoSize = true, UseMnemonic = false,
                // Wrap rather than widen without limit - about the width the system box uses.
                MaximumSize = new Size(TextRenderer.MeasureText(new string('x', 70), Font).Width, 0),
                Margin = new Padding(0, 4, 0, 14),
            };
            layout.Controls.Add(text, systemIcon != null ? 1 : 0, 0);
            if (systemIcon == null) layout.SetColumnSpan(text, 2);

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false,
            };
            layout.Controls.Add(row, 0, 1);
            layout.SetColumnSpan(row, 2);

            Button Answer(string caption, DialogResult result)
            {
                var button = new Button
                {
                    Text = caption, DialogResult = result, AutoSize = true, MinimumSize = new Size(88, 0),
                    Margin = new Padding(6, 0, 0, 0),
                };
                return button;
            }

            switch (buttons)
            {
                case MessageBoxButtons.YesNo:
                {
                    Button yes = Answer(T("Да"), DialogResult.Yes), no = Answer(T("Нет"), DialogResult.No);
                    if (danger) MarkDanger(yes);
                    Buttons = new[] { yes, no };
                    AcceptButton = danger ? no : yes;
                    CancelButton = no;
                    break;
                }
                case MessageBoxButtons.OKCancel:
                {
                    Button ok = Answer("OK", DialogResult.OK), cancel = Answer(T("Отмена"), DialogResult.Cancel);
                    if (danger) MarkDanger(ok);
                    Buttons = new[] { ok, cancel };
                    AcceptButton = danger ? cancel : ok;
                    CancelButton = cancel;
                    break;
                }
                default:
                {
                    Button ok = Answer("OK", DialogResult.OK);
                    Buttons = new[] { ok };
                    AcceptButton = ok;
                    CancelButton = ok;
                    break;
                }
            }
            // A right-to-left flow puts the first control added on the far right: the last answer goes
            // in first so the row reads in the order of Buttons.
            for (int i = Buttons.Length - 1; i >= 0; i--) row.Controls.Add(Buttons[i]);
            Controls.Add(layout);
        }

        /// <summary>
        /// The destructive answer takes the <c>danger</c> role. Built in the light palette like every
        /// control; <see cref="ThemeApply"/> keeps the role in dark and high contrast.
        /// </summary>
        private static void MarkDanger(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = ThemePalette.Light.Danger;
            button.ForeColor = ThemePalette.Light.AccentInk;
            button.FlatAppearance.BorderColor = ThemePalette.Light.Danger;
            button.UseVisualStyleBackColor = false;
        }

        /// <summary>
        /// Show a message and wait for the answer - the <see cref="MessageBox.Show(IWin32Window, string,
        /// string, MessageBoxButtons, MessageBoxIcon)"/> of the app. <paramref name="danger"/> marks an
        /// irreversible Yes; the caption is always "CyrFlip".
        /// </summary>
        public static DialogResult Show(IWin32Window? owner, string language, string message,
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, bool danger = false)
        {
            using var dialog = new ConfirmDialog(language, message, buttons, icon, danger, owner != null);
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }

        /// <summary>The same, with no window to own it (the tray, the one-shot launcher).</summary>
        public static DialogResult Show(string language, string message,
            MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None, bool danger = false)
            => Show(null, language, message, buttons, icon, danger);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Focus the default answer - the safe one for a destructive question.
            (AcceptButton as Control)?.Focus();
            // Raised from the tray, the process may not hold the foreground; the system box would come
            // up behind the user's editor exactly the same way.
            if (!_owned) ForegroundActivator.Activate(this);
            SoundFor(_icon)?.Play();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // The system box copies its text on Ctrl+C; people paste error messages into reports.
            if (keyData == (Keys.Control | Keys.C))
            {
                try { Clipboard.SetText("CyrFlip" + Environment.NewLine + Environment.NewLine + _message); }
                catch (System.Runtime.InteropServices.ExternalException) { /* the clipboard is busy - nothing lost */ }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // the control tree first: it still draws with both
            if (disposing)
            {
                _ownFont?.Dispose();
                _iconImage?.Dispose();
            }
        }

        private static Icon? SystemIconFor(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Information: return SystemIcons.Information;
                case MessageBoxIcon.Warning: return SystemIcons.Warning;
                case MessageBoxIcon.Error: return SystemIcons.Error;
                case MessageBoxIcon.Question: return SystemIcons.Question;
                default: return null;
            }
        }

        private static SystemSound? SoundFor(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Information: return SystemSounds.Asterisk;
                case MessageBoxIcon.Warning: return SystemSounds.Exclamation;
                case MessageBoxIcon.Error: return SystemSounds.Hand;
                case MessageBoxIcon.Question: return SystemSounds.Question;
                default: return null;
            }
        }
    }
}
