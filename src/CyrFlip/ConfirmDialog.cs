using System;
using System.Collections.Generic;
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

        /// <summary>The answer buttons in reading order, for the layout test.</summary>
        internal Button[] Buttons { get; }

        /// <summary>The kind-of-box glyph beside the message, or null for a box without one (for the tests).</summary>
        internal ConfirmStateGlyph? StateGlyph { get; }

        /// <summary>
        /// What the box offers: the system-box answer set, or - for a failure (APP-BEHAVIOUR rule 6) - the
        /// actions that continue the user's session: Retry (only when there is something to retry), the
        /// folder of the logs, and Close.
        /// </summary>
        private readonly struct Presentation
        {
            public Presentation(MessageBoxButtons buttons, bool danger) { Buttons = buttons; Danger = danger; Failure = false; CanRetry = false; }
            private Presentation(bool canRetry) { Buttons = MessageBoxButtons.OK; Danger = false; Failure = true; CanRetry = canRetry; }
            public static Presentation ForFailure(bool canRetry) => new Presentation(canRetry);
            public MessageBoxButtons Buttons { get; }
            public bool Danger { get; }
            public bool Failure { get; }
            public bool CanRetry { get; }
        }

        internal ConfirmDialog(string language, string message, MessageBoxButtons buttons, MessageBoxIcon icon, bool danger, bool owned)
            : this(language, message, icon, owned, new Presentation(buttons, danger))
        {
            HelpLanguage = language;
        }

        /// <summary>The failure presentation (see <see cref="ShowFailure"/>); internal so the layout test builds it in 13 languages.</summary>
        internal ConfirmDialog(string language, string message, bool canRetry, bool owned)
            : this(language, message, MessageBoxIcon.Warning, owned, Presentation.ForFailure(canRetry))
        {
            HelpLanguage = language;
        }

        private ConfirmDialog(string language, string message, MessageBoxIcon icon, bool owned, Presentation presentation)
        {
            MessageBoxButtons buttons = presentation.Buttons;
            bool danger = presentation.Danger;
            _message = message;
            _icon = icon;
            _owned = owned;
            string T(string en) => Localization.Translate(language, en);

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
            string? glyph = GlyphFor(icon);
            if (glyph != null)
            {
                // ICON-SET: the vocabulary's own picture of the box's kind, drawn in the theme on every paint -
                // the system bitmap was a light-theme picture that never followed dark or high contrast.
                layout.Controls.Add(StateGlyph = new ConfirmStateGlyph(glyph, (int)Math.Round(32 * DeviceDpi / 96f))
                {
                    Margin = new Padding(0, 2, 12, 0),
                }, 0, 0);
            }
            var text = new Label
            {
                Text = message, AutoSize = true, UseMnemonic = false,
                // Wrap rather than widen without limit - about the width the system box uses.
                MaximumSize = new Size(TextRenderer.MeasureText(new string('x', 70), Font).Width, 0),
                Margin = new Padding(0, 4, 0, 14),
            };
            layout.Controls.Add(text, glyph != null ? 1 : 0, 0);
            if (glyph == null) layout.SetColumnSpan(text, 2);

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = true,
            };
            layout.Controls.Add(row, 0, 1);
            layout.SetColumnSpan(row, 2);

            // WINDOWS-UI section 11 item 4: the affirmative answer carries the check (green), the dismissing
            // one the cross (red); in a confirmation of an irreversible act the acting answer alone takes
            // the danger face and its ink, and the dismissing one keeps its red glyph with no tint (item 7).
            Button Answer(string caption, DialogResult result, string glyph, ButtonLook look)
            {
                var button = new Button
                {
                    Text = caption, DialogResult = result, AutoSize = true,
                    Margin = new Padding(6, 0, 0, 0),
                };
                ButtonGlyphs.Attach(button, glyph, look);
                ButtonGlyphs.UseCommandMetric(button);
                return button;
            }

            if (presentation.Failure)
            {
                // A failure is a set of actions, not a verdict (APP-BEHAVIOUR rule 6): try again, look at the
                // logs, or leave. Escape and the close box are Close; Enter retries when there is something to
                // retry - nothing here is destructive - and closes otherwise.
                var failureButtons = new List<Button>();
                Button? retry = null;
                if (presentation.CanRetry)
                {
                    retry = Answer(T("Retry"), DialogResult.Retry, AppGlyphs.Refresh, ButtonLook.Neutral);
                    failureButtons.Add(retry);
                }
                Button logs = Answer(T("Open log folder"), DialogResult.None, AppGlyphs.Folder, ButtonLook.Neutral);
                // The folder opens beside the box: the user reads the logs and comes back to the same choice.
                logs.Click += (_, _) => OpenLogFolder();
                failureButtons.Add(logs);
                Button close = Answer(T("Close"), DialogResult.Cancel, AppGlyphs.Cancel, ButtonLook.Dismissing);
                failureButtons.Add(close);
                Buttons = failureButtons.ToArray();
                AcceptButton = retry ?? close;
                CancelButton = close;
            }
            else
            switch (buttons)
            {
                case MessageBoxButtons.YesNo:
                {
                    Button yes = Answer(T("Yes"), DialogResult.Yes, AppGlyphs.Confirm, danger ? ButtonLook.Acting : ButtonLook.Affirmative);
                    Button no = Answer(T("No"), DialogResult.No, AppGlyphs.Cancel, danger ? ButtonLook.DismissingSafe : ButtonLook.Dismissing);
                    if (danger) MarkDanger(yes);
                    Buttons = new[] { yes, no };
                    AcceptButton = danger ? no : yes;
                    CancelButton = no;
                    break;
                }
                case MessageBoxButtons.OKCancel:
                {
                    Button ok = Answer("OK", DialogResult.OK, AppGlyphs.Confirm, danger ? ButtonLook.Acting : ButtonLook.Affirmative);
                    Button cancel = Answer(T("Cancel"), DialogResult.Cancel, AppGlyphs.Cancel, danger ? ButtonLook.DismissingSafe : ButtonLook.Dismissing);
                    if (danger) MarkDanger(ok);
                    Buttons = new[] { ok, cancel };
                    AcceptButton = danger ? cancel : ok;
                    CancelButton = cancel;
                    break;
                }
                default:
                {
                    Button ok = Answer("OK", DialogResult.OK, AppGlyphs.Confirm, ButtonLook.Affirmative);
                    Buttons = new[] { ok };
                    AcceptButton = ok;
                    CancelButton = ok;
                    break;
                }
            }
            // BUTTONS section 4.4: dismissal first, final affirmative last in reading order.
            foreach (Button button in Buttons)
                if (ReferenceEquals(button, CancelButton)) row.Controls.Add(button);
            foreach (Button button in Buttons)
                if (!ReferenceEquals(button, CancelButton)) row.Controls.Add(button);
            CommandPanel.Fit(row, text.MaximumSize.Width);
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

        /// <summary>
        /// A failure of something the user asked for, as actions (<c>APP-BEHAVIOUR</c> rule 6, ticket S0060 P7):
        /// the one-line cause, then <b>Retry</b> - only when <paramref name="retry"/> says what retrying is -,
        /// <b>Open log folder</b> and <b>Close</b>. Escape is Close. The caller passes a message built from a
        /// <see cref="FailureCause"/>, never an exception's text; the details are in the logs the second button
        /// opens. <paramref name="retry"/> runs after the box is gone, on the caller's thread.
        /// </summary>
        public static DialogResult ShowFailure(IWin32Window? owner, string language, string message, Action? retry = null)
        {
            DialogResult answer;
            using (var dialog = new ConfirmDialog(language, message, retry != null, owned: owner != null))
                answer = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (answer == DialogResult.Retry) retry?.Invoke();
            return answer;
        }

        /// <summary>The folder the logs are in (<see cref="DataFolder.Current"/>) - the step a person takes before writing to the author.</summary>
        private static void OpenLogFolder()
        {
            try
            {
                string folder = DataFolder.Current;
                // A folder that was never created has nothing to show; the parent is the nearest honest place.
                if (!System.IO.Directory.Exists(folder))
                    folder = System.IO.Path.GetDirectoryName(folder.TrimEnd('\\', '/')) ?? folder;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"")
                { UseShellExecute = true })?.Dispose();
            }
            catch { /* the shell refused: the box stays, the user still has Close and Retry */ }
        }

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
            }
        }

        /// <summary>
        /// The vocabulary record for the box's kind (S0060 I-M8). A question has no record of its own - the
        /// question mark in a circle is <c>app.help</c>, "explain this option", which is not what a question
        /// box does - so it takes <c>app.info</c>, the meaning of a box that tells before it asks.
        /// </summary>
        internal static string? GlyphFor(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Information: return AppGlyphs.Info;
                case MessageBoxIcon.Question: return AppGlyphs.Info;
                case MessageBoxIcon.Warning: return AppGlyphs.StatusWarning;
                case MessageBoxIcon.Error: return AppGlyphs.StatusError;
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

    /// <summary>
    /// The glyph beside a <see cref="ConfirmDialog"/> message, in the colour look of ICON-SET section 10
    /// item C (a state indicator): a status record in its state's role, a content record in the accent.
    /// It is decoration of the message - the text carries the meaning - so it takes no focus and no name.
    /// </summary>
    internal sealed class ConfirmStateGlyph : Control, IThemeAware
    {
        private ThemePalette _palette = ThemePalette.Light;

        public ConfirmStateGlyph(string glyph, int size)
        {
            Glyph = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            Size = MinimumSize = MaximumSize = new Size(size, size);
        }

        internal string Glyph { get; }

        /// <summary>The ink the glyph is drawn in under <paramref name="palette"/>.</summary>
        internal Color InkFor(ThemePalette palette)
        {
            switch (Glyph)
            {
                case AppGlyphs.StatusError: return palette.Danger;
                case AppGlyphs.StatusWarning: return palette.Warning;
                default: return palette.Accent;
            }
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            GlyphRenderer.Draw(e.Graphics, Glyph, new RectangleF(0, 0, Width, Height), InkFor(_palette));
        }
    }
}
