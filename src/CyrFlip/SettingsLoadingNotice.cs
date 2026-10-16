using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// "Loading settings.." - what stands on screen while the settings window is built for the first time.
    ///
    /// <para>The build runs on the UI thread (controls cannot be made anywhere else, and that thread is also
    /// the hooks' thread), so for its whole length nothing of ours answers. A double click on the tray icon
    /// then looked like it had done nothing, and people clicked again and again: every click queued behind
    /// the build, and the single clicks among them switched the keyboard layout. This window is painted
    /// <i>before</i> the build starts and closed once the settings window itself has painted.</para>
    ///
    /// <para>It is a still picture on purpose. The only thread that could animate it is the one that is busy,
    /// and a spinner that stops turning is worse than none - the sentence says what is happening and does
    /// not pretend to measure it. It never takes the focus and has no taskbar entry.</para>
    /// </summary>
    internal sealed class SettingsLoadingNotice : ThemedForm
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        private readonly Label _text;
        private readonly Font? _ownFont;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // A tool window that is never activated: it must not take the focus from the editor the
                // user came from, and it is not an Alt+Tab entry.
                cp.ExStyle |= WsExToolWindow | WsExNoActivate;
                return cp;
            }
        }

        internal SettingsLoadingNotice(string language)
        {
            HelpLanguage = language;
            Text = "CyrFlip";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; ShowIcon = false; TopMost = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }
            _text = new Label
            {
                Text = Localization.Translate(language, "Loading settings.."),
                AutoSize = true, UseMnemonic = false,
                BorderStyle = BorderStyle.FixedSingle,
                // Sized from the font, so the frame holds its air at any display scaling.
                Padding = new Padding(Font.Height, Font.Height / 2, Font.Height, Font.Height / 2),
            };
            Controls.Add(_text);
        }

        /// <summary>The sentence on screen, for the tests.</summary>
        internal string MessageText => _text.Text;

        /// <summary>
        /// Painted at once, in the middle of <paramref name="workArea"/>. <c>Update</c> is what makes it a
        /// promise: the picture is on screen before this method returns, whatever the thread does next.
        /// </summary>
        internal void ShowCentered(Rectangle workArea)
        {
            PerformLayout();
            Size size = GetPreferredSize(Size.Empty);
            Location = new Point(workArea.Left + (workArea.Width - size.Width) / 2,
                workArea.Top + (workArea.Height - size.Height) / 2);
            Show();
            Update();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ownFont?.Dispose();
            base.Dispose(disposing);
        }
    }
}
