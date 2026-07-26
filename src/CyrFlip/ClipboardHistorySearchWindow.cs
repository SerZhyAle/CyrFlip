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
        // This window is built fresh on every search, and a Form disposes neither the font nor the icon
        // it was handed - only the small copy it derives from the icon itself. Both are ours to free.
        private readonly Font? _ownFont;
        private readonly System.Drawing.Icon? _ownIcon;

        public ClipboardHistorySearchWindow(ClipboardHistoryService service, string language)
        {
            _service = service;
            _language = language;
            Text = Localize("Поиск по истории");
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(760, 500);
            MinimumSize = new Size(500, 320);
            if (Localization.IsRightToLeft(language)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(language);
            if (family != null) { try { Font = _ownFont = new Font(family, SystemFonts.MessageBoxFont.SizeInPoints); } catch { } }
            // The manager strip is always topmost. Keep its search dialog above that strip so
            // Windows does not keep alternating the two topmost surfaces while it activates it.
            TopMost = true;
            try { Icon = _ownIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12, 12, 12, 4) };
            top.Controls.Add(new Label { Text = Localize("Введите не менее 3 символов:"), AutoSize = true }, 0, 0);
            top.Controls.Add(_query, 0, 1);
            top.Controls.Add(_hint, 0, 2);
            Controls.Add(_results);
            Controls.Add(top);

            _results.Columns.Add(Localize("Текст"), 420);
            _results.Columns.Add(Localize("Дата"), 135);
            _results.Columns.Add(Localize("Источник"), 120);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var close = new Button { Text = Localize("Закрыть"), AutoSize = true };
            _restore.Text = Localize("Вернуть в буфер");
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); // the control tree first: it is still drawing with our font
            if (disposing)
            {
                _ownFont?.Dispose();
                _ownIcon?.Dispose();
            }
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
                _hint.Text = Localize("Введите минимум 3 символа для поиска по части текста.");
            }
            else
            {
                var matches = _service.Entries.Where(item => ClipboardHistorySearch.Matches(item, query)).ToList();
                _hint.Text = matches.Count == 0
                    ? Localize("Совпадений не найдено.")
                    : string.Format(Localize("Найдено: {0}"), matches.Count);
                foreach (ClipboardHistoryEntry entry in matches)
                {
                    string preview = entry.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                    var row = new ListViewItem(preview) { Tag = entry, ToolTipText = entry.SourceTitle.Length > 0 ? entry.SourceTitle : entry.Text };
                    row.SubItems.Add(entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    row.SubItems.Add(entry.SourceApp);
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
            if (_results.Columns.Count > 2)
                _results.Columns[0].Width = Math.Max(100, _results.ClientSize.Width - _results.Columns[1].Width - _results.Columns[2].Width - 6);
        }

        private string Localize(string ru) => Localization.Translate(_language, ru);
    }
}
