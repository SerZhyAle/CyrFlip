using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Modeless full-history search window. It stays open while new items arrive.
    ///
    /// <para>Nothing here may cost O(history) on the UI thread, which is also the thread both
    /// low-level hooks run on (spec S0005 CH-4): keystrokes and history changes are coalesced by one
    /// 150 ms debounce, the match runs over a snapshot on a background task that the next keystroke
    /// cancels, and the list is a <see cref="ListView.VirtualMode"/> view over the result array - rows
    /// are built only for what is on screen, from the entry's short <see cref="ClipboardHistoryEntry.Preview"/>.
    /// Matching itself still reads the full text.</para>
    /// </summary>
    internal sealed class ClipboardHistorySearchWindow : ThemedForm
    {
        private const int DebounceMs = 150;

        private readonly ClipboardHistoryService _service;
        private readonly TextBox _query = new TextBox { Dock = DockStyle.Fill };
        private readonly Label _hint = new Label { AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Padding = new Padding(0, 6, 0, 0) };
        private readonly ListView _results = new ListView { Dock = DockStyle.Fill, FullRowSelect = true, HideSelection = false, MultiSelect = false, View = View.Details, VirtualMode = true };
        private readonly Button _restore = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
        private readonly System.Windows.Forms.Timer _debounce = new System.Windows.Forms.Timer { Interval = DebounceMs };
        private readonly string _language;
        // This window is built fresh on every search, and a Form disposes neither the font nor the icon
        // it was handed - only the small copy it derives from the icon itself. Both are ours to free.
        private readonly Font? _ownFont;
        private readonly System.Drawing.Icon? _ownIcon;
        private ClipboardHistoryEntry[] _matches = new ClipboardHistoryEntry[0];
        private CancellationTokenSource? _search;

        /// <param name="exchange">Export / import of the exchange file (S0023); null leaves the two buttons out.</param>
        public ClipboardHistorySearchWindow(ClipboardHistoryService service, string language, Action<IWin32Window, bool>? exchange = null)
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
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 48), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var close = new Button { Text = Localize("Закрыть"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
            _restore.Text = Localize("Вернуть в буфер");
            _restore.Enabled = false;
            bottom.Controls.Add(close);
            bottom.Controls.Add(_restore);
            if (exchange != null)
            {
                // Left of the restore button: they act on the whole history, not on the selected row.
                var export = new Button { Text = Localize("Экспортировать..."), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
                var import = new Button { Text = Localize("Импортировать..."), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4) };
                export.Click += (_, _) => exchange(this, false);
                import.Click += (_, _) => exchange(this, true);
                bottom.Controls.Add(import);
                bottom.Controls.Add(export);
            }
            Controls.Add(bottom);

            close.Click += (_, _) => Close();
            // INPUT-PARITY rule 1: Esc closes, Enter restores the selected row, Delete removes it, and
            // Down/Up in the query box walk into the results - every pointer action has a key.
            CancelButton = close;
            _query.KeyDown += OnQueryKeyDown;
            _results.KeyDown += OnResultsKeyDown;
            _query.TextChanged += (_, _) => ScheduleRefresh();
            _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshResults(); };
            _results.RetrieveVirtualItem += OnRetrieveVirtualItem;
            _results.SelectedIndexChanged += (_, _) => _restore.Enabled = _results.SelectedIndices.Count == 1;
            _results.DoubleClick += (_, _) => RestoreSelected();
            _restore.Click += (_, _) => RestoreSelected();
            _service.Changed += OnHistoryChanged;
            FormClosed += (_, _) => { _service.Changed -= OnHistoryChanged; _debounce.Stop(); _search?.Cancel(); };
            Shown += (_, _) => { _query.Focus(); RefreshResults(); };
            RefreshResults();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); // the control tree first: it is still drawing with our font
            if (disposing)
            {
                _search?.Cancel();
                _debounce.Dispose();
                _ownFont?.Dispose();
                _ownIcon?.Dispose();
            }
        }

        /// <summary>A burst of copies or keystrokes becomes one search, <see cref="DebounceMs"/> after the last.</summary>
        private void OnHistoryChanged(object? sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { if (IsHandleCreated) BeginInvoke((Action)ScheduleRefresh); }
            else ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (IsDisposed) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private void RefreshResults()
        {
            _search?.Cancel();
            _search = null;
            string query = _query.Text;
            if (!ClipboardHistorySearch.IsReady(query))
            {
                ShowMatches(new ClipboardHistoryEntry[0], Localize("Введите минимум 3 символа для поиска по части текста."));
                return;
            }

            // A snapshot of references: the live list belongs to the UI thread.
            ClipboardHistoryEntry[] snapshot = _service.Entries.ToArray();
            var search = new CancellationTokenSource();
            _search = search;
            CancellationToken token = search.Token;
            Task.Run(() => Filter(snapshot, query, token), token).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion || token.IsCancellationRequested) return;
                ClipboardHistoryEntry[] matches = task.Result;
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed || !ReferenceEquals(_search, search)) return;
                        ShowMatches(matches, matches.Length == 0
                            ? Localize("Совпадений не найдено.")
                            : Localization.Format(Localize, "Найдено: {0}", matches.Length));
                    }));
                }
                catch (InvalidOperationException) { /* the window went away in between */ }
            }, TaskScheduler.Default);
        }

        internal static ClipboardHistoryEntry[] Filter(IReadOnlyList<ClipboardHistoryEntry> entries, string query, CancellationToken token)
        {
            var matches = new List<ClipboardHistoryEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                if (ClipboardHistorySearch.Matches(entries[i], query)) matches.Add(entries[i]);
            }
            return matches.ToArray();
        }

        private void ShowMatches(ClipboardHistoryEntry[] matches, string hint)
        {
            _results.SelectedIndices.Clear();
            _matches = matches;
            _results.VirtualListSize = matches.Length;
            _results.Invalidate();
            _hint.Text = hint;
            _restore.Enabled = false;
        }

        private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _matches.Length)
            {
                e.Item = new ListViewItem(new[] { "", "", "" });
                return;
            }
            ClipboardHistoryEntry entry = _matches[e.ItemIndex];
            var row = new ListViewItem(entry.Preview) { Tag = entry, ToolTipText = entry.SourceTitle.Length > 0 ? entry.SourceTitle : entry.Preview };
            row.SubItems.Add(entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            row.SubItems.Add(entry.SourceApp);
            e.Item = row;
        }

        private void OnQueryKeyDown(object? sender, KeyEventArgs e)
        {
            if ((e.KeyCode != Keys.Down && e.KeyCode != Keys.Up) || e.Modifiers != Keys.None || _matches.Length == 0) return;
            int index = _results.SelectedIndices.Count == 1 ? _results.SelectedIndices[0] : 0;
            SelectRow(index);
            _results.Focus();
            e.Handled = e.SuppressKeyPress = true;
        }

        private void OnResultsKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Enter) { RestoreSelected(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = e.SuppressKeyPress = true; }
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= _matches.Length) return;
            _results.SelectedIndices.Clear();
            _results.SelectedIndices.Add(index);
            _results.FocusedItem = _results.Items[index];
            _results.EnsureVisible(index);
        }

        /// <summary>The strip's "×", from the keyboard; the list refreshes through <see cref="OnHistoryChanged"/>.</summary>
        private void DeleteSelected()
        {
            if (_results.SelectedIndices.Count != 1) return;
            int index = _results.SelectedIndices[0];
            if (index < 0 || index >= _matches.Length) return;
            _service.Delete(_matches[index]);
        }

        private void RestoreSelected()
        {
            if (_results.SelectedIndices.Count != 1) return;
            int index = _results.SelectedIndices[0];
            if (index < 0 || index >= _matches.Length) return;
            if (_service.Restore(_matches[index])) Close();
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
