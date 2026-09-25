using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>How much of the unpinned history goes into an export (S0023, spec 6.1).</summary>
    internal enum ExchangeHistoryScope
    {
        Last50,
        Last100,
        Last7Days,
        All,
    }

    /// <summary>
    /// "What goes into the exchange file" (ticket S0023, spec 6.1) - and the warning that it will be
    /// plain text (spec section 2), on the same page as the button that writes it, so cancelling is
    /// one click away from reading it.
    ///
    /// <para>The defaults are deliberately conservative: notes and the pinned history in, the rest of
    /// the history out - a year of work clipboard must never leave the machine because a box was
    /// ticked by default. The warning is shown whenever history is in the export; for a notes-only
    /// export the user may switch it off (<see cref="AppConfig.ExchangeNotesWarningOff"/>).</para>
    ///
    /// <para>Laid out by content, never by pixel geometry: <c>DialogLayoutTests</c> builds it in all
    /// 13 languages.</para>
    /// </summary>
    internal sealed class ExchangeExportDialog : ThemedForm
    {
        private readonly CheckBox _notes = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(3, 3, 3, 2) };
        private readonly CheckBox _pinned = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(3, 2, 3, 2) };
        private readonly CheckBox _rest = new CheckBox { AutoSize = true, Margin = new Padding(3, 2, 3, 2) };
        private readonly ComboBox _scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(24, 2, 3, 6), Enabled = false };
        private readonly Label _warning = new Label { AutoSize = true, ForeColor = ThemePalette.Light.Warning, Margin = new Padding(3, 10, 3, 4), UseMnemonic = false };
        private readonly CheckBox _noNotesWarning = new CheckBox { AutoSize = true, Margin = new Padding(3, 2, 3, 2) };
        private readonly Button _ok;
        private readonly Font? _ownFont;

        public bool IncludeNotes => _notes.Enabled && _notes.Checked;
        public bool IncludePinned => _pinned.Checked;
        public bool IncludeRest => _rest.Checked;
        public ExchangeHistoryScope Scope => (ExchangeHistoryScope)Math.Max(0, _scope.SelectedIndex);
        /// <summary>The "do not warn again for notes" box as the user left it.</summary>
        public bool NotesWarningOff => _noNotesWarning.Checked;

        /// <param name="notesAvailable">False while the quick notes are switched off: their journal is not read for an export.</param>
        public ExchangeExportDialog(string language, bool notesAvailable, bool notesWarningOff)
        {
            string T(string ru) => Localization.Translate(language, ru);
            Text = T("Экспорт в файл обмена");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ShowIcon = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }
            int wrap = TextRenderer.MeasureText(new string('x', 64), Font).Width;

            var intro = new Label
            {
                Text = T("Один открытый текстовый файл: его можно прочитать в любом редакторе или на телефоне и импортировать в CyrFlip на другом компьютере. CyrFlip никуда его не отправляет."),
                AutoSize = true, MaximumSize = new Size(wrap, 0), Margin = new Padding(3, 3, 3, 8),
            };
            _notes.Text = T("Быстрые заметки");
            _pinned.Text = T("Закреплённые элементы истории");
            _rest.Text = T("Остальная история буфера");
            _scope.Items.AddRange(new object[]
            {
                T("последние 50"), T("последние 100"), T("за последние 7 дней"), T("вся история"),
            });
            _scope.SelectedIndex = 0;
            _scope.Width = WidestItem() + SystemInformation.VerticalScrollBarWidth + 12;
            if (!notesAvailable)
            {
                _notes.Checked = false;
                _notes.Enabled = false;
            }
            _warning.Text = T("Файл будет обычным незашифрованным текстом. Не передавайте его через небезопасные каналы и удалите после переноса, если он больше не нужен.");
            _warning.MaximumSize = new Size(wrap, 0);
            _noNotesWarning.Text = T("Больше не предупреждать для заметок");
            _noNotesWarning.Checked = notesWarningOff;

            _ok = Command(T("Экспортировать..."), DialogResult.OK);
            Button cancel = Command(T("Отмена"), DialogResult.Cancel);
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
            grid.Controls.Add(intro, 0, 0);
            grid.Controls.Add(_notes, 0, 1);
            grid.Controls.Add(_pinned, 0, 2);
            grid.Controls.Add(_rest, 0, 3);
            grid.Controls.Add(_scope, 0, 4);
            grid.Controls.Add(_warning, 0, 5);
            grid.Controls.Add(_noNotesWarning, 0, 6);
            grid.Controls.Add(buttons, 0, 7);
            Controls.Add(grid);

            _notes.CheckedChanged += (_, _) => UpdateState();
            _pinned.CheckedChanged += (_, _) => UpdateState();
            _rest.CheckedChanged += (_, _) => UpdateState();
            _noNotesWarning.CheckedChanged += (_, _) => UpdateState();
            AcceptButton = _ok; CancelButton = cancel;
            UpdateState();
        }

        /// <summary>
        /// Keep the page honest: the scope only while the rest of the history is in, the warning
        /// whenever it applies, and no Export with nothing chosen.
        /// </summary>
        private void UpdateState()
        {
            bool history = _pinned.Checked || _rest.Checked;
            _scope.Enabled = _rest.Checked;
            _warning.Visible = history || (IncludeNotes && !_noNotesWarning.Checked);
            // The switch is about notes-only exports; it has nothing to say while notes are not chosen.
            _noNotesWarning.Visible = IncludeNotes && !history;
            _ok.Enabled = IncludeNotes || history;
        }

        private int WidestItem()
        {
            int widest = 0;
            foreach (object item in _scope.Items)
                widest = Math.Max(widest, TextRenderer.MeasureText(item.ToString(), Font).Width);
            return widest;
        }

        private Button Command(string text, DialogResult result) => new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(TextRenderer.MeasureText("Cancel", Font).Width + 16, 0), DialogResult = result,
        };

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // the control tree first: it still draws with the font
            if (disposing) _ownFont?.Dispose();
        }
    }
}
