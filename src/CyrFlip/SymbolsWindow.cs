using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The Symbols window palette (ticket S0056, revision 2026-10-08): a text field that collects the characters
    /// picked and mirrors the clipboard, one list (all symbols), a search box, a grid twelve cells wide, and a
    /// switch that makes a pick close the window. The grid never takes the keyboard focus from the user's window.
    /// </summary>
    internal sealed class SymbolsWindow : ThemedForm
    {
        /// <summary>WM_CLIPBOARDUPDATE: the clipboard changed; sent to a window registered as a format listener.</summary>
        private const int WmClipboardUpdate = 0x031D;
        /// <summary>The most clipboard text the field mirrors, in bytes of UTF-16. A larger clipboard leaves the field empty.</summary>
        internal const int MirrorMaxBytes = 256 * 1024;
        /// <summary>Reads retried when the copy guard was taken, 150 ms apart.</summary>
        private const int BusyRetryLimit = 20;
        private const int BusyRetryMs = 150;
        /// <summary>The default window shows twelve rows of the grid; fewer than three is never allowed.</summary>
        private const int DefaultRows = 12;
        private const int MinimumRows = 3;

        private readonly AppConfig _config;
        private readonly SymbolTypability _typability;
        private readonly Action<string, Action<bool, uint>> _copyAction;
        private readonly Action<Action<SymbolsClipboardSnapshot>>? _readAction;
        private readonly SymbolsClipboardMirror _mirror = new SymbolsClipboardMirror();

        private readonly TableLayoutPanel _layout = new TableLayoutPanel();
        private readonly TableLayoutPanel _headerRow = new TableLayoutPanel();
        private readonly Label _fieldCaption = new Label();
        private readonly TextBox _textField = new TextBox();
        private readonly TableLayoutPanel _filterRow = new TableLayoutPanel();
        private readonly ComboBox _setBox = new ComboBox();
        private readonly TextBox _searchBox = new TextBox();
        private readonly SymbolGrid _grid = new SymbolGrid();
        private readonly Label _statusLabel = new Label();
        private readonly NoActivateCheckBox _closeOnPick = new NoActivateCheckBox();
        private readonly Timer _retryTimer = new Timer { Interval = BusyRetryMs };
        private readonly ToolTip _toolTips = new ToolTip { ShowAlways = true };
        private Font? _clipboardFont;
        // The script font of the current language (hi/bn/zh), handed to the form - ours to release.
        private Font? _scriptFont;

        private string _uiLanguage = "ru";
        private IntPtr _lastActiveTarget = IntPtr.Zero;
        private bool _boundsRestored;
        private bool _clipboardListening;
        private bool _copyPending;
        private int _busyRetriesLeft = BusyRetryLimit;
        private bool _retryForce;
        private int _fixedWidth;
        private int _minimumHeight;
        private int _defaultHeight;
        // The group the list shows; SymbolsData.AllCategories is every symbol. Remembered in the config.
        private string _category = SymbolsData.AllCategories;
        private bool _settingCategory;

        protected override bool ShowWithoutActivation => true;

        public SymbolGrid Grid => _grid;
        public TextBox SearchBox => _searchBox;
        /// <summary>The characters picked so far, mirroring the clipboard.</summary>
        public TextBox TextField => _textField;

        public SymbolsWindow(
            AppConfig config,
            string uiLanguage,
            Action<string, Action<bool, uint>> copyAction,
            Action<Action<SymbolsClipboardSnapshot>>? readAction = null,
            SymbolTypability? typability = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _uiLanguage = uiLanguage ?? "ru";
            _copyAction = copyAction ?? throw new ArgumentNullException(nameof(copyAction));
            _readAction = readAction;
            _typability = typability ?? new SymbolTypability();
            _category = SymbolsData.NormalizeCategory(_config.SymbolsCategory);

            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;

            InitializeComponents();
            ApplyLanguage(_uiLanguage);
            ApplyThemePalette(ThemePalette.Light);
        }

        private void InitializeComponents()
        {
            _layout.Dock = DockStyle.Fill;
            _layout.ColumnCount = 1;
            _layout.RowCount = 6;
            _layout.Padding = new Padding(6);
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // header row: field caption and optional help button
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // text field, sized in ApplyWindowGeometry
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // symbol set and search
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // grid
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // status
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // close on pick

            // Header row with the caption of the text field and the context help button
            _headerRow.Dock = DockStyle.Fill;
            _headerRow.ColumnCount = 2;
            _headerRow.RowCount = 1;
            _headerRow.Margin = new Padding(0);
            _headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _headerRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Caption of the text field
            _fieldCaption.AutoSize = true;
            _fieldCaption.Dock = DockStyle.Fill;
            _fieldCaption.TextAlign = ContentAlignment.MiddleLeft;
            _fieldCaption.Margin = new Padding(2, 0, 2, 2);
            _headerRow.Controls.Add(_fieldCaption, 0, 0);

            // Text field: the picked characters, mirroring the clipboard
            _textField.Multiline = false;
            _textField.AcceptsReturn = false;
            _textField.ScrollBars = ScrollBars.None;
            _textField.MaxLength = int.MaxValue;
            _textField.Dock = DockStyle.Fill;
            _textField.Margin = new Padding(2, 0, 2, 4);
            _textField.KeyDown += OnTextFieldKeyDown;

            // Symbol set (a list with the one entry the palette needs, "all") and the search box
            _filterRow.ColumnCount = 2;
            _filterRow.RowCount = 1;
            _filterRow.Dock = DockStyle.Fill;   // fills its row, which ApplyWindowGeometry fixes to the content height
            _filterRow.Margin = new Padding(0);
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _setBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _setBox.Margin = new Padding(2, 2, 6, 2);
            _setBox.SelectedIndexChanged += (_, _) => OnCategoryChanged();
            _searchBox.Dock = DockStyle.Fill;
            _searchBox.Margin = new Padding(2, 2, 2, 2);
            _searchBox.TextChanged += (_, _) => RefreshSymbols();
            _searchBox.KeyDown += OnSearchKeyDown;
            _filterRow.Controls.Add(_setBox, 0, 0);
            _filterRow.Controls.Add(_searchBox, 1, 0);

            // Grid
            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(2);
            _grid.SymbolHovered += OnSymbolHovered;
            _grid.SymbolActivated += OnSymbolActivated;

            // Status line: the code of the hovered character, then the copy feedback
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoSize = false;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Margin = new Padding(4, 2, 4, 2);
            _statusLabel.TextChanged += (_, _) => _toolTips.SetToolTip(_statusLabel, _statusLabel.Text);

            // Close on pick, remembered
            _closeOnPick.AutoSize = true;
            _closeOnPick.Margin = new Padding(2, 2, 2, 2);
            _closeOnPick.Checked = _config.SymbolsCloseOnPick;
            _closeOnPick.CheckedChanged += (_, _) => OnCloseOnPickChanged();

            _layout.Controls.Add(_headerRow, 0, 0);
            _layout.Controls.Add(_textField, 0, 1);
            _layout.Controls.Add(_filterRow, 0, 2);
            _layout.Controls.Add(_grid, 0, 3);
            _layout.Controls.Add(_statusLabel, 0, 4);
            _layout.Controls.Add(_closeOnPick, 0, 5);

            Controls.Add(_layout);

            _retryTimer.Tick += OnRetryTick;
        }

        internal void ApplyImportedPreferences()
        {
            _category = SymbolsData.NormalizeCategory(_config.SymbolsCategory);
            _closeOnPick.Checked = _config.SymbolsCloseOnPick;
            ApplyLanguage(_uiLanguage);
        }

        internal void AttachHelpButton(Button button)
        {
            if (_headerRow.Controls.Contains(button)) return;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Margin = new Padding(2, 0, 0, 2);
            _headerRow.Controls.Add(button, 1, 0);
            ApplyWindowGeometry();
        }

        public void ApplyLanguage(string uiLanguage)
        {
            HelpLanguage = uiLanguage;
            _uiLanguage = uiLanguage;
            // Direction and script font follow the language here too, live (APP-BEHAVIOUR rule 8): the
            // palette is kept for the whole session, so a change made after it was built reaches it.
            WindowScript.Apply(this, _uiLanguage, ref _scriptFont);
            Text = Localization.Translate(_uiLanguage, "Symbols window");

            _fieldCaption.Text = Localization.Translate(_uiLanguage, "Current clipboard");
            _textField.AccessibleName = _fieldCaption.Text;
            _toolTips.SetToolTip(_fieldCaption, _fieldCaption.Text);
            _toolTips.SetToolTip(_textField, _fieldCaption.Text);

            // The list: "all", then the twelve groups in table order. Rebuilt in place, and the selection is set
            // without saving it - only a choice made by the user is remembered.
            _settingCategory = true;
            try
            {
                _setBox.Items.Clear();
                _setBox.Items.Add(Localization.Translate(_uiLanguage, "All"));
                foreach (SymbolCategory cat in SymbolsData.Categories)
                {
                    _setBox.Items.Add(Localization.Translate(_uiLanguage, SymbolsData.CategoryTitle(cat.Key)));
                }
                _setBox.SelectedIndex = CategoryIndex(_category);
            }
            finally
            {
                _settingCategory = false;
            }
            _setBox.AccessibleName = Localization.Translate(_uiLanguage, "Symbol set");
            _toolTips.SetToolTip(_setBox, _setBox.AccessibleName);

            _searchBox.AccessibleName = Localization.Translate(_uiLanguage, "Search symbols");
            _searchBox.PlaceholderTextSafe(_searchBox.AccessibleName + "…");
            _toolTips.SetToolTip(_searchBox, Localization.Translate(_uiLanguage, "Search by name, keywords or Unicode code"));
            _closeOnPick.Text = Localization.Translate(_uiLanguage, "Close on pick");
            _toolTips.SetToolTip(_closeOnPick, _closeOnPick.Text);
            _grid.SetLanguage(_uiLanguage);

            ApplyWindowGeometry();
            RefreshSymbols();
        }

        /// <summary>
        /// The window is twelve cells wide, always, and its height is measured from the rows around the grid, so a
        /// font, a language or a display scale re-measures it. The default shows twelve rows; beyond that the grid
        /// scrolls, and the window may be made taller or shorter by the user, never narrower or wider.
        /// </summary>
        private void ApplyWindowGeometry()
        {
            int cell = _grid.CellPixelSize;
            // Control.Font ignores equal replacements, so retain the instance it actually uses.
            // Disposing an equal previous font would leave the text box holding a disposed font.
            if (_clipboardFont == null || _clipboardFont.FontFamily.Name != Font.FontFamily.Name
                || _clipboardFont.SizeInPoints != Font.SizeInPoints + 5f || _clipboardFont.Style != Font.Style)
            {
                Font? previousFont = _clipboardFont;
                _clipboardFont = new Font(Font.FontFamily, Font.SizeInPoints + 5f, Font.Style);
                _textField.Font = _clipboardFont;
                previousFont?.Dispose();
            }
            int fieldHeight = _textField.PreferredHeight;
            _layout.RowStyles[1] = new RowStyle(SizeType.Absolute, fieldHeight + _textField.Margin.Vertical);
            _statusLabel.Height = Font.Height + 8;

            // The list is as wide as its longest group in this language, so no name is clipped
            int listWidth = 0;
            foreach (object item in _setBox.Items)
            {
                listWidth = Math.Max(listWidth, TextRenderer.MeasureText(_setBox.GetItemText(item), _setBox.Font).Width);
            }
            _setBox.Width = listWidth + SystemInformation.VerticalScrollBarWidth + 8;
            // A fixed row: an automatic one let the table hand the grid's share to the list and the search box
            _layout.RowStyles[2] = new RowStyle(SizeType.Absolute, _filterRow.PreferredSize.Height + _filterRow.Margin.Vertical);

            int headerHeight = _headerRow.PreferredSize.Height;
            _layout.RowStyles[0] = new RowStyle(SizeType.Absolute, headerHeight + _headerRow.Margin.Vertical);

            int chrome = _layout.Padding.Vertical
                + headerHeight + _headerRow.Margin.Vertical
                + fieldHeight + _textField.Margin.Vertical
                + _filterRow.PreferredSize.Height + _filterRow.Margin.Vertical
                + _statusLabel.Height + _statusLabel.Margin.Vertical
                + _closeOnPick.PreferredSize.Height + _closeOnPick.Margin.Vertical
                + _grid.Margin.Vertical;

            int clientWidth = _layout.Padding.Horizontal + _grid.Margin.Horizontal
                + SymbolGrid.Columns * cell + SystemInformation.VerticalScrollBarWidth;
            int frameWidth = Width - ClientSize.Width;
            int frameHeight = Height - ClientSize.Height;

            _fixedWidth = clientWidth + frameWidth;
            _minimumHeight = chrome + MinimumRows * cell + frameHeight;
            _defaultHeight = chrome + DefaultRows * cell + frameHeight;

            // The height is free up to the whole desktop: a zero maximum would also pull the minimum down to zero
            MinimumSize = new Size(_fixedWidth, _minimumHeight);
            MaximumSize = new Size(_fixedWidth, Math.Max(SystemInformation.VirtualScreen.Height, _minimumHeight));
            Size = new Size(_fixedWidth, Math.Max(Height, _minimumHeight));
        }

        /// <summary>The list position of a group key: 0 is "all", then the groups in table order.</summary>
        private static int CategoryIndex(string key)
        {
            for (int i = 0; i < SymbolsData.Categories.Length; i++)
            {
                if (string.Equals(SymbolsData.Categories[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i + 1;
            }
            return 0;
        }

        /// <summary>The user picked a group: it filters the grid, and it is what the next opening starts on.</summary>
        private void OnCategoryChanged()
        {
            if (_settingCategory) return;

            int index = _setBox.SelectedIndex;
            string key = index <= 0 ? SymbolsData.AllCategories : SymbolsData.Categories[index - 1].Key;
            if (string.Equals(key, _category, StringComparison.Ordinal)) return;

            _category = key;
            _config.SymbolsCategory = key;
            _config.SaveSymbolsCategory();
            RefreshSymbols();
        }

        private void RefreshSymbols()
        {
            string query = _searchBox.Text.Trim();
            Func<SymbolEntry, bool>? filter = _config.SymbolsHideTypable
                ? (Func<SymbolEntry, bool>)(e => !_typability.IsTypable(e))
                : null;

            // The selected group narrows the search and the grid; "all" searches the whole table in standard order
            string? category = _category == SymbolsData.AllCategories ? null : _category;
            _grid.SetEntries(SymbolSearch.Search(SymbolsData.Table, query, _uiLanguage, category, filter));
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!string.IsNullOrEmpty(_searchBox.Text))
                {
                    _searchBox.Clear();
                }
                else
                {
                    HidePalette();
                }
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SymbolEntry? selected = _grid.SelectedEntry;
                if (selected != null)
                {
                    OnSymbolActivated(selected);
                }
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp)
            {
                _grid.Focus();
            }
        }

        private void OnTextFieldKeyDown(object? sender, KeyEventArgs e)
        {
            // Escape closes the window, as it does in the search box.
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HidePalette();
            }
        }

        private void OnCloseOnPickChanged()
        {
            if (_config.SymbolsCloseOnPick == _closeOnPick.Checked) return;
            _config.SymbolsCloseOnPick = _closeOnPick.Checked;
            _config.SaveSymbolsCloseOnPick();
        }

        /// <summary>The code of the hovered character, and the hint how the user can type it (section 7).</summary>
        private void OnSymbolHovered(SymbolEntry? entry)
        {
            if (entry == null)
            {
                _statusLabel.Text = "";
                _toolTips.SetToolTip(_grid, Localization.Translate(_uiLanguage, "Symbols grid"));
                return;
            }

            string code = "U+" + entry.CodePoint.ToString("X4");
            string hint = _typability.Query(entry).GetHint(_uiLanguage, entry.NameEn);
            _statusLabel.Text = string.IsNullOrEmpty(hint) ? code : code + "   " + hint;
            _toolTips.SetToolTip(_grid, code + "   " + entry.NameEn);
        }

        private void OnSymbolActivated(SymbolEntry entry)
        {
            if (_copyPending)
            {
                _statusLabel.Text = Localization.Translate(_uiLanguage, "Clipboard is busy, please retry");
                return;
            }

            bool closeOnPick = _config.SymbolsCloseOnPick;
            bool hadFocus = Form.ActiveForm == this;
            // Close on pick: the character alone goes to the clipboard. Otherwise it is appended to the field,
            // and the whole field is what the clipboard receives, so that the field keeps reflecting it.
            string text = closeOnPick ? entry.Text : _textField.Text + entry.Text;

            _copyPending = true;
            _copyAction(text, (success, sequence) => OnCopyFinished(entry, text, success, sequence, closeOnPick, hadFocus));
        }

        private void OnCopyFinished(SymbolEntry entry, string text, bool success, uint sequence, bool closeOnPick, bool hadFocus)
        {
            _copyPending = false;
            if (IsDisposed) return;

            if (!success)
            {
                _statusLabel.Text = Localization.Translate(_uiLanguage, "Clipboard is busy, please retry");
                return;
            }

            _mirror.Own(sequence);
            if (closeOnPick)
            {
                HidePalette();
                return;
            }

            _textField.Text = text;
            _textField.SelectionStart = text.Length;
            _grid.FlashSymbol(entry);
            _statusLabel.Text = Localization.Format(_uiLanguage, "Copied: {0}", entry.Text);

            if (hadFocus)
            {
                HandBackFocus();
            }
        }

        /// <summary>
        /// While the window is shown the clipboard is watched, so the field follows a copy made anywhere. The
        /// listener is registered only while visible: a hidden palette reads nothing.
        /// </summary>
        private void StartClipboardMirror()
        {
            if (_readAction == null || !IsHandleCreated) return;

            _busyRetriesLeft = BusyRetryLimit;
            if (!_clipboardListening)
            {
                _clipboardListening = WindowInterop.AddClipboardFormatListener(Handle);
            }
            RequestClipboard(force: true);
        }

        private void StopClipboardMirror()
        {
            _retryTimer.Stop();
            _retryForce = false;
            if (!_clipboardListening) return;

            _clipboardListening = false;
            if (IsHandleCreated) WindowInterop.RemoveClipboardFormatListener(Handle);
        }

        private void RequestClipboard(bool force)
        {
            if (_readAction == null) return;
            _readAction(snapshot => OnClipboardRead(snapshot, force));
        }

        private void OnClipboardRead(SymbolsClipboardSnapshot snapshot, bool force)
        {
            if (IsDisposed || !Visible) return;

            if (snapshot.Busy)
            {
                // A flip or a copy holds the clipboard: read again shortly, so the field catches up with the change
                if (_busyRetriesLeft > 0)
                {
                    _busyRetriesLeft--;
                    _retryForce |= force;
                    _retryTimer.Start();
                }
                return;
            }

            // A pick in flight writes the field itself when it lands
            if (!snapshot.Read || _copyPending || !_mirror.Accept(snapshot.Sequence, force)) return;

            _busyRetriesLeft = BusyRetryLimit;
            string text = snapshot.Text ?? string.Empty;
            _textField.Text = text;
            _textField.SelectionStart = text.Length;
        }

        private void OnRetryTick(object? sender, EventArgs e)
        {
            _retryTimer.Stop();
            bool force = _retryForce;
            _retryForce = false;
            RequestClipboard(force);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmClipboardUpdate && _clipboardListening)
            {
                RequestClipboard(force: false);
            }
            base.WndProc(ref m);
        }

        public void ShowPalette(IntPtr targetWindow, bool active)
        {
            _lastActiveTarget = targetWindow;

            RestoreSavedBounds();
            if (!Visible) Show();

            WindowState = FormWindowState.Normal;
            StartClipboardMirror();
            if (active)
            {
                ForegroundActivator.Activate(this);
                _searchBox.Focus();
                _searchBox.SelectAll();
            }
        }

        public void Toggle(IntPtr targetWindow)
        {
            if (Visible)
            {
                HidePalette();
            }
            else
            {
                ShowPalette(targetWindow, active: true);
            }
        }

        public void HidePalette()
        {
            StopClipboardMirror();
            SaveGeometry();
            Hide();
            HandBackFocus();
        }

        private void HandBackFocus()
        {
            IntPtr target = _lastActiveTarget;
            if (target != IntPtr.Zero && WindowInterop.IsWindow(target) && !WindowInterop.IsHungAppWindow(target))
            {
                ForegroundActivator.Activate(target);
            }
        }

        private void RestoreSavedBounds()
        {
            if (_boundsRestored) return;
            _boundsRestored = true;

            ApplyWindowGeometry();
            int height = _config.SymbolsHeight > 0 ? Math.Max(_config.SymbolsHeight, _minimumHeight) : _defaultHeight;

            if (_config.SymbolsX != int.MinValue && _config.SymbolsY != int.MinValue)
            {
                var saved = new Rectangle(_config.SymbolsX, _config.SymbolsY, _fixedWidth, height);
                Bounds = ScreenPlacement.Clamp(saved, Screen.AllScreens.Select(s => s.WorkingArea));
            }
            else
            {
                Point mouse = Cursor.Position;
                Screen screen = Screen.FromPoint(mouse) ?? Screen.PrimaryScreen;
                Rectangle area = screen.WorkingArea;

                int posX = Math.Max(area.Left, Math.Min(mouse.X + 10, area.Right - _fixedWidth));
                int posY = Math.Max(area.Top, Math.Min(mouse.Y + 10, area.Bottom - height));
                Bounds = ScreenPlacement.Clamp(new Rectangle(posX, posY, _fixedWidth, height), new[] { area });
            }
        }

        private void SaveGeometry()
        {
            if (WindowState == FormWindowState.Normal)
            {
                _config.SymbolsX = Bounds.X;
                _config.SymbolsY = Bounds.Y;
                _config.SymbolsWidth = Bounds.Width;
                _config.SymbolsHeight = Bounds.Height;
                _config.SaveSymbolsWindow();
            }
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            SaveGeometry();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (Visible && WindowState == FormWindowState.Normal)
            {
                SaveGeometry();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePalette();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnThemeApplied(ThemePalette palette)
        {
            base.OnThemeApplied(palette);
            ApplyThemePalette(palette);
        }

        private void ApplyThemePalette(ThemePalette palette)
        {
            BackColor = palette.SurfaceWindow;
            ForeColor = palette.TextPrimary;

            _headerRow.BackColor = palette.SurfaceWindow;
            _fieldCaption.BackColor = palette.SurfaceWindow;
            _fieldCaption.ForeColor = palette.TextMuted;
            _textField.BackColor = palette.SurfaceRaised;
            _textField.ForeColor = palette.TextPrimary;
            _setBox.BackColor = palette.SurfaceRaised;
            _setBox.ForeColor = palette.TextPrimary;
            _searchBox.BackColor = palette.SurfaceRaised;
            _searchBox.ForeColor = palette.TextPrimary;

            _statusLabel.BackColor = palette.SurfaceWindow;
            _statusLabel.ForeColor = palette.TextMuted;

            _closeOnPick.BackColor = palette.SurfaceWindow;
            _closeOnPick.ForeColor = palette.TextPrimary;

            _grid.ApplyTheme(palette);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopClipboardMirror();
                _retryTimer.Dispose();
                _toolTips.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                _clipboardFont?.Dispose();
                _scriptFont?.Dispose();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A language change that flips the direction recreates the window; the clipboard listener
            // belonged to the old handle and has to be registered again, or the field stops following
            // the clipboard until the palette is hidden and shown.
            if (_clipboardListening) _clipboardListening = WindowInterop.AddClipboardFormatListener(Handle);
        }

        /// <summary>
        /// A control that answers WM_MOUSEACTIVATE with MA_NOACTIVATE: a click on it leaves the user's window
        /// active, so the editor keeps the keyboard focus.
        /// </summary>
        private sealed class NoActivateCheckBox : CheckBox
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WindowInterop.WM_MOUSEACTIVATE)
                {
                    m.Result = (IntPtr)WindowInterop.MA_NOACTIVATE;
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
