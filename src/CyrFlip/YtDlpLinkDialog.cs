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
    /// </summary>
    internal sealed class YtDlpLinkDialog : Form
    {
        private readonly TextBox _link = new TextBox();
        private readonly Button _ok;

        /// <summary>The entered link when confirmed, else null.</summary>
        public string? Link { get; private set; }

        public YtDlpLinkDialog(string uiLanguage)
        {
            Text = Localization.Translate(uiLanguage, "Загрузка yt-dlp");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = false;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }

            var caption = new Label
            {
                Text = Localization.Translate(uiLanguage, "Ссылка для скачивания:"),
                AutoSize = true, Margin = new Padding(3, 3, 3, 6),
            };
            _link.Width = TextWidth(new string('W', 42));
            _link.Margin = new Padding(3, 0, 3, 3);
            _link.TextChanged += (_, _) => _ok!.Enabled = _link.Text.Trim().Length > 0;

            _ok = Command(Localization.Translate(uiLanguage, "Начать"), DialogResult.OK);
            _ok.Enabled = false;
            _ok.Click += (_, _) => Link = _link.Text.Trim();
            Button cancel = Command(Localization.Translate(uiLanguage, "Отмена"), DialogResult.Cancel);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_ok);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            grid.Controls.Add(caption, 0, 0);
            grid.Controls.Add(_link, 0, 1);
            grid.Controls.Add(buttons, 0, 2);
            Controls.Add(grid);

            AcceptButton = _ok; CancelButton = cancel;
            Shown += (_, _) => _link.Focus();
        }

        private Button Command(string text, DialogResult result) => new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = result,
        };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;
    }
}
