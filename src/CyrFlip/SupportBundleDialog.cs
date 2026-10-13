using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// What the user sees before any message exists: exactly which files went into the archive, how
    /// big it is, and where it lies. This <b>is</b> the consent mechanism of the feature - not a row
    /// of checkboxes, but the chance to read what is about to leave the machine. Hence "Open the
    /// archive folder" sitting next to "Create the message" rather than behind it.
    ///
    /// Laid out by an auto-sizing <see cref="TableLayoutPanel"/> like every dialog here: the captions
    /// exist in 13 languages and fixed geometry clips them (<c>DialogLayoutTests</c>).
    /// </summary>
    internal sealed class SupportBundleDialog : ThemedForm
    {
        private const int TextWidth = 560;   // wrap width for the prose labels, not a layout constant

        private readonly string _uiLanguage;
        private readonly SupportBundle.Result _result;
        /// <summary>The script font this dialog created for itself - WinForms never disposes a font
        /// it was handed, so the dialog owns it (LK-3b-03 / S0066 L-5), like every other dialog.
        /// Not readonly: <see cref="ApplyScript"/> runs from the constructor, after the field's
        /// initializer.</summary>
        private Font? _ownFont;

        public SupportBundleDialog(SupportBundle.Result result, string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            _result = result;

            Text = T("Logs for the author");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ApplyScript(uiLanguage);

            var files = new ListView
            {
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false, Size = new Size(TextWidth, 190), Margin = new Padding(3, 6, 3, 10),
                ShowItemToolTips = true,
            };
            files.Columns.Add(T("File"), 170);
            files.Columns.Add(T("Size"), 70, HorizontalAlignment.Right);
            files.Columns.Add(T("What it holds"), 300);
            // What each file holds is part of the consent (S0010 TD-1): the user is shown the kind of
            // data, one line per file, not only a name and a size. The whole line is the tooltip,
            // since a translation can outgrow the column.
            foreach (SupportBundle.Entry entry in result.Entries)
            {
                var item = new ListViewItem(entry.Name);
                item.SubItems.Add(SupportBundle.FormatSize(entry.Bytes));
                string contents = Describe(entry.Name);
                if (entry.Truncated)
                    contents = contents.Length == 0
                        ? T("truncated - only the tail was kept")
                        : contents + " (" + T("truncated - only the tail was kept") + ")";
                item.SubItems.Add(contents);
                item.ToolTipText = contents;
                files.Items.Add(item);
            }
            foreach (string dropped in result.Dropped)
            {
                var item = new ListViewItem(dropped);
                item.SubItems.Add("");
                item.SubItems.Add(T("left out - the total size limit was reached"));
                item.ForeColor = ThemePalette.Light.TextMuted;
                files.Items.Add(item);
            }

            var create = Button(T("Create the message"), DialogResult.OK, AppGlyphs.Email);
            var open = Button(T("Open the archive folder"), DialogResult.None, AppGlyphs.Folder);
            open.Click += (_, _) => Reveal();
            var close = Button(T("Close"), DialogResult.Cancel);

            // RightToLeft flow, so the first control added sits rightmost - and WinForms mirrors the
            // whole row for Arabic and Urdu, which is what those UIs expect.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(open);
            buttons.Controls.Add(create);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            grid.Controls.Add(Prose(T("The log archive is ready:")), 0, 0);
            grid.Controls.Add(Prose(result.ArchivePath + "  (" + SupportBundle.FormatSize(result.ArchiveBytes) + ")"), 0, 1);
            grid.Controls.Add(files, 0, 2);
            grid.Controls.Add(Prose(T("You send the message yourself - CyrFlip transmits nothing over the network. Clipboard history and quick notes are not part of the archive; scenario paths and arguments, window titles and selected text are never written to the logs. The logs do contain file paths - the usual ones no longer carry your Windows account name, but one in an unusual shape still can.")), 0, 3);
            grid.Controls.Add(buttons, 0, 4);
            Controls.Add(grid);

            AcceptButton = create;
            CancelButton = close;
        }

        private void Reveal()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _result.ArchivePath + "\"")
                { UseShellExecute = true })?.Dispose();
            }
            catch { /* the path is on screen either way */ }
        }

        private Button Button(string text, DialogResult result, string? verbGlyph = null) => ButtonGlyphs.Answer(new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = result, Margin = new Padding(6, 3, 3, 3),
        }, verbGlyph);

        /// <summary>
        /// A wrapped paragraph: <see cref="MaximumSize"/> caps the width, AutoSize grows the height to
        /// whatever the translation needs. Nothing here is positioned by pixel.
        /// </summary>
        private static Label Prose(string text) => new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(TextWidth, 0),
            Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 3, 3),
        };

        /// <summary>Mirrors for Arabic/Urdu and picks a font that can draw the script.</summary>
        private void ApplyScript(string uiLanguage)
        {
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family == null) return;
            try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
            catch { /* the font is missing on this machine - keep the default */ }
        }

        private string Describe(string fileName)
            => SupportBundle.Contents.TryGetValue(fileName, out string? en) ? T(en) : "";

        private string T(string en) => Localization.Translate(_uiLanguage, en);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // the control tree first: it still draws with the font
            if (disposing) _ownFont?.Dispose();
        }
    }
}
