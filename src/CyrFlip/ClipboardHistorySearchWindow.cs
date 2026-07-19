using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Modeless full-history search window. It stays open while new items arrive.</summary>
    internal sealed class ClipboardHistorySearchWindow : Form
    {
        private readonly ClipboardHistoryService _service;
        private readonly TextBox _query = new TextBox { Dock = DockStyle.Fill };
        private readonly Label _hint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0) };
        private readonly ListView _results = new ListView { Dock = DockStyle.Fill, FullRowSelect = true, HideSelection = false, MultiSelect = false, View = View.Details };
        private readonly Button _restore = new Button { AutoSize = true };
        private readonly string _language;

        public ClipboardHistorySearchWindow(ClipboardHistoryService service, string language)
        {
            _service = service;
            _language = language;
            Text = Localize("Поиск по истории", "Search history", "Пошук в історії");
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(760, 500);
            MinimumSize = new Size(500, 320);
            // The manager strip is always topmost. Keep its search dialog above that strip so
            // Windows does not keep alternating the two topmost surfaces while it activates it.
            TopMost = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12, 12, 12, 4) };
            top.Controls.Add(new Label { Text = Localize("Введите не менее 3 символов:", "Enter at least 3 characters:", "Введіть щонайменше 3 символи:"), AutoSize = true }, 0, 0);
            top.Controls.Add(_query, 0, 1);
            top.Controls.Add(_hint, 0, 2);
            Controls.Add(_results);
            Controls.Add(top);

            _results.Columns.Add(Localize("Текст", "Text", "Текст"), 540);
            _results.Columns.Add(Localize("Дата", "Date", "Дата"), 135);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var close = new Button { Text = Localize("Закрыть", "Close", "Закрити"), AutoSize = true };
            _restore.Text = Localize("Вернуть в буфер", "Restore to clipboard", "Повернути в буфер");
            _restore.Enabled = false;
            bottom.Controls.Add(close);
            bottom.Controls.Add(_restore);
            Controls.Add(bottom);

            close.Click += (_, _) => Close();
            _query.TextChanged += (_, _) => RefreshResults();
            _results.SelectedIndexChanged += (_, _) => _restore.Enabled = _results.SelectedItems.Count == 1;
            _results.DoubleClick += (_, _) => RestoreSelected();
            _restore.Click += (_, _) => RestoreSelected();
            _service.Changed += OnHistoryChanged;
            FormClosed += (_, _) => _service.Changed -= OnHistoryChanged;
            Shown += (_, _) => { _query.Focus(); RefreshResults(); };
            RefreshResults();
        }

        private void OnHistoryChanged(object? sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshResults);
        }

        private void RefreshResults()
        {
            string query = _query.Text;
            _results.BeginUpdate();
            _results.Items.Clear();
            if (!ClipboardHistorySearch.IsReady(query))
            {
                _hint.Text = Localize("Введите минимум 3 символа для поиска по части текста.", "Enter at least 3 characters to search by text fragment.", "Введіть щонайменше 3 символи для пошуку за фрагментом тексту.");
            }
            else
            {
                var matches = _service.Entries.Where(item => ClipboardHistorySearch.Matches(item, query)).ToList();
                _hint.Text = matches.Count == 0
                    ? Localize("Совпадений не найдено.", "No matches found.", "Збігів не знайдено.")
                    : string.Format(Localize("Найдено: {0}", "Found: {0}", "Знайдено: {0}"), matches.Count);
                foreach (ClipboardHistoryEntry entry in matches)
                {
                    string preview = entry.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                    var row = new ListViewItem(preview) { Tag = entry, ToolTipText = entry.Text };
                    row.SubItems.Add(entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    _results.Items.Add(row);
                }
            }
            _results.EndUpdate();
            _restore.Enabled = false;
        }

        private void RestoreSelected()
        {
            if (_results.SelectedItems.Count != 1 || !(_results.SelectedItems[0].Tag is ClipboardHistoryEntry entry)) return;
            if (_service.Restore(entry)) Close();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_results.Columns.Count > 1)
                _results.Columns[0].Width = Math.Max(100, _results.ClientSize.Width - _results.Columns[1].Width - 6);
        }

        private string Localize(string ru, string en, string uk) => _language == "Українська" ? uk : _language == "Русский" ? ru : en;
    }
}
