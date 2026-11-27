using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Modal prompt for the yt-dlp link, shown on the UI thread right before the download starts
    /// (spec §7). Empty input is a cancel, not an error; the character validation itself lives in
    /// <see cref="LauncherExecution.ValidateYtDlpLink"/> so every entry point applies it.
    ///
    /// Sized by content, not pixels - the captions exist in 13 languages (see the layout note in
    /// CLAUDE.md and <see cref="LayoutConversionDialog"/>).
    ///
    /// <para>It is also sized for the screen it opens on. The app is per-monitor aware but does not use
    /// WinForms' DPI scaling, so WinForms shrinks the default font to keep every window at 96-DPI pixel
    /// size while Windows scales the frame, the title and the button glyphs: on a 175 percent display the
    /// text, the link box and the gaps were left a third of the size of the title bar around them. The
    /// font is put back at its nominal size (the shrink is by the system DPI, so undoing it by the same
    /// number is exact) and every pixel constant follows the window's own DPI.</para>
    /// </summary>
    internal sealed class YtDlpLinkDialog : ThemedForm
    {
        private readonly TextBox _link = new TextBox();
        private readonly Label _caption;
        private readonly FlowLayoutPanel _buttons;
        private readonly TableLayoutPanel _grid;
        private readonly Button _ok;
        private readonly Button _cancel;
        /// <summary>The script font this dialog created for itself - WinForms never disposes a font
        /// it was handed, so the dialog owns it (LK-3b-03 / S0066 L-5), like every other dialog.</summary>
        private readonly Font? _ownFont;
        /// <summary>The font at its nominal size for this system's DPI, when the default one was shrunk.</summary>
        private readonly Font? _nominalFont;
        /// <summary>The DPI of the window the pixel constants are currently measured for.</summary>
        private int _dpi = 96;

        /// <summary>The entered link when confirmed, else null.</summary>
        public string? Link { get; private set; }

        public YtDlpLinkDialog(string uiLanguage)
        {
            HelpLanguage = uiLanguage;
            Text = Localization.Translate(uiLanguage, "yt-dlp download");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }

            // The font the shrink is undone on is the one the dialog is authored in - the default, or the
            // script's own family from above - and it becomes the base the size profile composes on.
            float nominal = Math.Max(96, MarkerSize.PrimaryDpi()) / 96f;
            if (nominal > 1.001f)
            {
                try
                {
                    Font restored = new Font(Font.FontFamily, Font.SizeInPoints * nominal, Font.Style, GraphicsUnit.Point);
                    _nominalFont = restored;
                    SetBaseFont(restored);
                }
                catch { /* keep the font as it is: a small dialog beats none */ }
            }

            _caption = new Label
            {
                Text = Localization.Translate(uiLanguage, "Link to download:"),
                AutoSize = true,
            };
            _link.TextChanged += (_, _) => _ok!.Enabled = _link.Text.Trim().Length > 0;

            _ok = Command(Localization.Translate(uiLanguage, "Start"), DialogResult.OK, AppGlyphs.Run);
            _ok.Enabled = false;
            _ok.Click += (_, _) => Link = _link.Text.Trim();
            _cancel = Command(Localization.Translate(uiLanguage, "Cancel"), DialogResult.Cancel);

            _buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            };
            _buttons.Controls.Add(_cancel);
            _buttons.Controls.Add(_ok);

            _grid = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
            };
            _grid.Controls.Add(_caption, 0, 0);
            _grid.Controls.Add(_link, 0, 1);
            _grid.Controls.Add(_buttons, 0, 2);
            Controls.Add(_grid);

            // Every pixel constant, for the screen the dialog is about to open on; the window's own DPI
            // replaces this guess when its handle exists (UseWindowDpi).
            _dpi = Math.Max(96, ButtonGlyphs.Dpi(this));
            Fit();

            AcceptButton = _ok; CancelButton = _cancel;
            // Opened from the Jump List/hotkey/IPC, this process often holds no foreground rights -
            // without this the dialog sits behind the user's window instead of appearing (same fix
            // as ShowSettings/ClipboardHistoryWindow via ForegroundActivator).
            Shown += (_, _) => { ForegroundActivator.Activate(this); _link.Focus(); };
        }

        private Button Command(string text, DialogResult result, string? verbGlyph = null) => ButtonGlyphs.Answer(new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = result,
        }, verbGlyph);

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + Px(16);

        /// <summary>A 96-DPI pixel value at the DPI of the window the dialog is on.</summary>
        private int Px(int logical) => (int)Math.Round(logical * _dpi / 96.0);

        /// <summary>
        /// Everything this dialog measures in pixels, for the current DPI and font: the gaps, the link box
        /// (42 wide letters of the dialog's own font) and the buttons' least width. Cheap and repeatable -
        /// it runs when the DPI or the size profile changes, never from a size the user could have set.
        /// </summary>
        private void Fit()
        {
            SuspendLayout();
            try
            {
                _grid.Padding = new Padding(Px(12));
                _caption.Margin = new Padding(Px(3), Px(3), Px(3), Px(6));
                _link.Margin = new Padding(Px(3), 0, Px(3), Px(3));
                _link.Width = TextWidth(new string('W', 42));
                _buttons.Margin = new Padding(0, Px(12), 0, 0);
                var least = new Size(TextWidth("Cancel"), 0);
                _ok.MinimumSize = least;
                _cancel.MinimumSize = least;
                _ok.Margin = new Padding(Px(3));
                _cancel.Margin = new Padding(Px(3));
                FitHelpButton();
            }
            finally { ResumeLayout(true); }
        }

        /// <summary>
        /// The help button <see cref="ThemedForm"/> adds when the window loads sizes itself from
        /// <c>DeviceDpi</c>, which stays 96 here until a window answers <c>WM_DPICHANGED</c>
        /// (<see cref="ControlDpi"/>) - 44 device pixels on a 175 percent screen, a quarter smaller than its
        /// glyph's own scale. It is 44 logical pixels (UI-SIZING's smallest target) at the window's DPI, and
        /// as tall as the caption and the link box together, so it does not hold the caption's row open.
        /// </summary>
        private void FitHelpButton()
        {
            foreach (Control control in _grid.Controls)
            {
                if (control.Name != "contextHelp") continue;
                int edge = Px(44);
                control.Size = new Size(edge, edge);
                if (_grid.ColumnStyles.Count > 1) _grid.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, edge);
                _grid.SetRowSpan(control, 2);
                break;
            }
        }

        /// <summary>
        /// The window's own DPI is known - Windows' answer (<see cref="ButtonGlyphs.Dpi"/>): the pixel
        /// constants are measured again when it is not the one they were built for.
        /// </summary>
        private void UseWindowDpi(int dpi)
        {
            dpi = Math.Max(96, dpi);
            if (dpi == _dpi) return;
            _dpi = dpi;
            Fit();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UseWindowDpi(ButtonGlyphs.Dpi(this));
        }

        /// <summary>The dialog moved to a screen of another DPI: the gaps and the link box follow.</summary>
        protected override void OnDpiChanged(int oldDpi, int newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            UseWindowDpi(newDpi);
        }

        /// <summary>The size profile changed the font: the link box is 42 letters of the new one.</summary>
        protected override void OnUiProfileApplied(UiMetrics metrics)
        {
            base.OnUiProfileApplied(metrics);
            Fit();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);   // the base attaches the help button, into this table's reserved last column
            SuspendLayout();
            try { FitHelpButton(); }
            finally { ResumeLayout(true); }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // the control tree first: it still draws with the font
            if (!disposing) return;
            _nominalFont?.Dispose();
            _ownFont?.Dispose();
        }
    }
}
