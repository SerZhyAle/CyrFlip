using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The restore offer of the rolling backup (S0072 section 4): a backup was found in Documents and
    /// this profile is fresh, so the user is asked what to do with it - and the three answers are not
    /// a Yes/No pair. <b>Restore</b> runs the import transaction; <b>Not now</b> keeps the question
    /// open for the next start (the safe default: Escape and the close box give it); <b>No - start
    /// fresh</b> is final for this profile. Modeled on <see cref="ConfirmDialog"/>: themed, laid out
    /// by content, safe answer focused.
    /// </summary>
    internal sealed class BackupRestoreDialog : ThemedForm
    {
        private readonly Font? _ownFont;

        internal BackupRestoreDialog(string language, string backupDate)
        {
            HelpLanguage = language;
            string T(string en) => Localization.Translate(language, en);
            Text = "CyrFlip";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowIcon = false; ShowInTaskbar = false;
            KeyPreview = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
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
            layout.Controls.Add(new PictureBox
            { Image = SystemIcons.Information.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 2, 12, 0) }, 0, 0);
            string message = Localization.Format(T, "A settings backup from {0} was found. Restore it? Everything in it will be applied: shortcuts, the traffic-light targets, the quick-launch scenarios and all the other settings.", backupDate);
            layout.Controls.Add(new Label
            {
                Text = message, AutoSize = true, UseMnemonic = false,
                MaximumSize = new Size(TextRenderer.MeasureText(new string('x', 70), Font).Width, 0),
                Margin = new Padding(0, 4, 0, 14),
            }, 1, 0);

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false,
            };
            layout.Controls.Add(row, 0, 1);
            layout.SetColumnSpan(row, 2);

            Button Answer(string caption, DialogResult result, string glyph, ButtonLook look)
            {
                var button = new Button
                { Text = caption, DialogResult = result, AutoSize = true, MinimumSize = new Size(88, 0), Margin = new Padding(6, 0, 0, 0) };
                ButtonGlyphs.Attach(button, glyph, look);
                return button;
            }
            Button restore = Answer(T("Restore"), DialogResult.Yes, AppGlyphs.Confirm, ButtonLook.Affirmative);
            Button notNow = Answer(T("Not now"), DialogResult.Cancel, AppGlyphs.Cancel, ButtonLook.Neutral);
            Button fresh = Answer(T("No - start fresh"), DialogResult.No, AppGlyphs.Cancel, ButtonLook.Dismissing);
            Buttons = new[] { restore, notNow, fresh };
            AcceptButton = notNow; // the safe answer: nothing changes, the question re-asks next start
            CancelButton = notNow;
            for (int i = Buttons.Length - 1; i >= 0; i--) row.Controls.Add(Buttons[i]);
            Controls.Add(layout);
        }

        /// <summary>The answer buttons in reading order, for the layout test.</summary>
        internal Button[] Buttons { get; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            (AcceptButton as Control)?.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _ownFont?.Dispose();
        }
    }
}
