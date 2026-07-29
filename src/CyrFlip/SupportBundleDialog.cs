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
    internal sealed class SupportBundleDialog : Form
    {
        private const int TextWidth = 560;   // wrap width for the prose labels, not a layout constant

        private readonly string _uiLanguage;
        private readonly SupportBundle.Result _result;

        public SupportBundleDialog(SupportBundle.Result result, string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            _result = result;

            Text = T("Логи для автора");
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
                MultiSelect = false, Size = new Size(TextWidth, 150), Margin = new Padding(3, 6, 3, 10),
            };
            files.Columns.Add(T("Файл"), 200);
            files.Columns.Add(T("Размер"), 90, HorizontalAlignment.Right);
            files.Columns.Add(T("Примечание"), 250);
            foreach (SupportBundle.Entry entry in result.Entries)
            {
                var item = new ListViewItem(entry.Name);
                item.SubItems.Add(SupportBundle.FormatSize(entry.Bytes));
                item.SubItems.Add(entry.Truncated
                    ? T("обрезан — сохранён только конец файла") : "");
                files.Items.Add(item);
            }
            foreach (string dropped in result.Dropped)
            {
                var item = new ListViewItem(dropped);
                item.SubItems.Add("");
                item.SubItems.Add(T("не вошёл — превышен общий размер"));
                item.ForeColor = SystemColors.GrayText;
                files.Items.Add(item);
            }

            var create = Button(T("Создать письмо"), DialogResult.OK);
            var open = Button(T("Открыть папку с архивом"), DialogResult.None);
            open.Click += (_, _) => Reveal();
            var close = Button(T("Закрыть"), DialogResult.Cancel);

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
            grid.Controls.Add(Prose(T("Архив с логами собран:")), 0, 0);
            grid.Controls.Add(Prose(result.ArchivePath + "  (" + SupportBundle.FormatSize(result.ArchiveBytes) + ")"), 0, 1);
            grid.Controls.Add(files, 0, 2);
            grid.Controls.Add(Prose(T("Письмо отправляете вы сами — CyrFlip ничего не передаёт в сеть. История буфера обмена в архив не включена. Внутри логов встречаются пути к файлам, а в них — имя вашей учётной записи Windows.")), 0, 3);
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
                { UseShellExecute = true });
            }
            catch { /* the path is on screen either way */ }
        }

        private Button Button(string text, DialogResult result) => new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = result, Margin = new Padding(6, 3, 3, 3),
        };

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
            try { Font = new Font(family, Font.SizeInPoints); }
            catch { /* the font is missing on this machine - keep the default */ }
        }

        private string T(string ru) => Localization.Translate(_uiLanguage, ru);
    }
}
