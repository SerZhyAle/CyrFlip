using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The quick-notes window (spec §3.2): a narrow list on the left, the editor on the right, and
    /// a search box that is the real way back to an old fragment - most notes are saved with no
    /// name at all, so searching the body is not a convenience here, it is the index.
    ///
    /// <para>Three behaviours are decisions rather than implementation. <b>Nothing is ever modal.</b>
    /// There is no "save changes?" between the user and the code they were writing: Ctrl+S saves at
    /// once, and otherwise the note is written <see cref="QuickNotesService.DebounceMs"/> after the
    /// last keystroke and immediately when the note is switched or the window closes.
    /// <b>The editor never formats.</b> No Markdown, no syntax colouring, no smart quotes, no tab
    /// expansion - a monospace <see cref="TextBox"/> with <c>AcceptsTab</c>, because the whole point
    /// is that a fragment comes back out able to be pasted straight back in. <b>Closing hides.</b>
    /// CyrFlip is a tray app and this window is one of its surfaces, not its life.</para>
    ///
    /// <para><b>One honest limit of a Win32 edit control.</b> The body is stored byte for byte, and
    /// an untouched note is never rewritten - the editor's contents are compared against the text it
    /// was given, so a note that came in with bare LF line endings still has them after a restart.
    /// But an <b>edited</b> note comes back with whatever the edit control produced, which on
    /// Windows is CRLF. That is a property of the control, not a choice made here, and pretending
    /// otherwise by re-normalizing on save would only move the surprise somewhere less visible.</para>
    /// </summary>
    internal sealed class QuickNotesWindow : ThemedForm
    {
        private const int SearchDebounceMs = 150;
        private const int PreviewLength = 90;

        private readonly QuickNotesService _service;
        private readonly AppConfig _config;
        private readonly Action _showSettings;

        private readonly TextBox _search = new TextBox { Width = 200 };
        private readonly Label _count = new Label { AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Padding = new Padding(8, 6, 8, 0) };
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = false, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        private readonly TextBox _title = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _editor = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Both, MaxLength = 0,
        };
        private readonly ListView _items = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, LabelEdit = true,
            FullRowSelect = true, MultiSelect = false, HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        private readonly Panel _itemsHost = new Panel { Dock = DockStyle.Fill, Visible = false };
        private readonly RadioButton _kindText = new RadioButton { AutoSize = true, Checked = true, Margin = new Padding(3, 4, 12, 3) };
        private readonly RadioButton _kindList = new RadioButton { AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
        private readonly Label _dates = new Label { AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Padding = new Padding(3, 8, 12, 0) };
        private readonly Button _copy = new Button { AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        private readonly Button _keep = new Button { AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        private readonly Button _export = new Button { AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        private readonly Button _delete = new Button { AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
        private readonly Button _newText = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        private readonly Button _newList = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        private readonly Button _settings = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        // The exchange file (ticket S0023): one button, its two commands in a drop-down.
        private readonly Button _transfer = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        private readonly ContextMenuStrip _transferMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _transferExport = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _transferImport = new ToolStripMenuItem();
        private readonly Button _addItem = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        private readonly Button _removeItem = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
        private readonly Button _itemUp = new Button { AutoSize = true, Width = 34, Margin = new Padding(3, 3, 3, 3) };
        private readonly Button _itemDown = new Button { AutoSize = true, Width = 34, Margin = new Padding(3, 3, 3, 3) };
        private Bitmap? _upImage, _downImage;   // the move glyphs, redrawn when the theme changes
        // SplitterDistance is deliberately not set here. A SplitContainer that has not been laid out
        // yet is 150 px wide, and it silently clamps the value to what fits - so a distance set in
        // the initializer does not throw, it just quietly becomes something else. It is applied in
        // OnLoad instead, once the form has a real width.
        private readonly SplitContainer _split = new SplitContainer { Dock = DockStyle.Fill };
        private const int ListWidth = 300;
        // The four panels MinimumSize is measured from - see ApplyMinimumSize.
        private FlowLayoutPanel? _topRow;
        private FlowLayoutPanel? _kindRow;
        private FlowLayoutPanel? _bottomRow;
        private TableLayoutPanel? _rightPanel;
        /// <summary>Rows of text the body keeps at the smallest size - a one-line editor is not a note.</summary>
        private const int MinEditorRows = 6;
        private readonly System.Windows.Forms.Timer _searchTimer = new System.Windows.Forms.Timer { Interval = SearchDebounceMs };

        private string _language;
        // Both are ours to free: a Form disposes neither the font it was handed nor the icon (only
        // the small copy it derives), and the monospace editor font is a second one again.
        private Font? _ownFont;
        private Font? _editorFont;
        private System.Drawing.Icon? _ownIcon;

        private QuickNote? _current;
        /// <summary>The editor's text as it was handed over - see the class remarks on line endings.</summary>
        private string _editorOriginal = "";
        private bool _loading;
        private bool _boundsRestored;
        // Writes AppConfig.QuickNotesSelected to the registry. A seam so the tests never touch HKCU.
        private readonly Action _persistSelection;
        // Export / import of the exchange file (S0023), run by the context. Null hides the button.
        private readonly Action<IWin32Window, bool>? _exchange;
        // What was asked of the window while the service was still replaying the journal - done
        // once the notes are there (ticket S0006, QN-2).
        private bool _pendingStartNew;
        private string? _pendingText;

        public QuickNotesWindow(QuickNotesService service, AppConfig config, Action showSettings,
            Action? persistSelection = null, Action<IWin32Window, bool>? exchange = null)
        {
            _service = service;
            _config = config;
            _showSettings = showSettings;
            _persistSelection = persistSelection ?? config.SaveQuickNotesSelected;
            _exchange = exchange;
            _transfer.Visible = exchange != null;
            _language = config.UiLanguage;

            StartPosition = FormStartPosition.Manual;
            Size = new Size(config.QuickNotesWidth, config.QuickNotesHeight);
            ShowInTaskbar = true;
            KeyPreview = true;
            try { Icon = _ownIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildLayout();
            ApplyLanguage(_language);   // also sets MinimumSize, and grows Size to it
            ApplyWordWrap(config.QuickNotesWordWrap);

            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RefreshList(); };
            _search.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
            _newText.Click += (_, _) => NewNote(QuickNoteKind.Text);
            _newList.Click += (_, _) => NewNote(QuickNoteKind.Checklist);
            _settings.Click += (_, _) => _showSettings();
            _transferMenu.Items.Add(_transferExport);
            _transferMenu.Items.Add(_transferImport);
            _transfer.Click += (_, _) => _transferMenu.Show(_transfer, new Point(0, _transfer.Height));
            _transferExport.Click += (_, _) => BeginInvoke((Action)(() => _exchange?.Invoke(this, false)));
            _transferImport.Click += (_, _) => BeginInvoke((Action)(() => _exchange?.Invoke(this, true)));
            _list.SelectedIndexChanged += (_, _) => OnListSelectionChanged();
            _title.TextChanged += (_, _) => OnEdited(titleChanged: true);
            _editor.TextChanged += (_, _) => OnEdited(titleChanged: false);
            _kindText.CheckedChanged += (_, _) => OnKindChosen(QuickNoteKind.Text);
            _kindList.CheckedChanged += (_, _) => OnKindChosen(QuickNoteKind.Checklist);
            _items.ItemChecked += (_, _) => OnEdited(titleChanged: false);
            _items.AfterLabelEdit += OnItemRenamed;
            _addItem.Click += (_, _) => AddChecklistItem();
            _removeItem.Click += (_, _) => RemoveChecklistItem();
            _itemUp.Click += (_, _) => MoveChecklistItem(-1);
            _itemDown.Click += (_, _) => MoveChecklistItem(+1);
            _copy.Click += (_, _) => CopyCurrent();
            _keep.Click += (_, _) => CopyForKeep();
            _export.Click += (_, _) => ExportCurrent();
            _delete.Click += (_, _) => DeleteCurrent();
            _service.Changed += OnServiceChanged;
            _service.Cleared += OnServiceCleared;
            _service.Imported += OnServiceImported;
            _service.Loaded += OnServiceLoaded;
            _service.SaveFailed += OnSaveFailed;
            _service.SaveRecovered += OnSaveRecovered;

            KeyDown += OnKeyDown;
            ResizeEnd += (_, _) => SaveBounds();
            VisibleChanged += (_, _) => { if (!Visible) SaveBounds(); };
            FormClosing += (_, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                CommitCurrent();
                SaveBounds();
                Hide();
            };

            RefreshList();
            if (_service.IsLoaded && !SelectRemembered()) NewNote(QuickNoteKind.Text);
            else ShowLoadingState(true);
        }

        /// <summary>
        /// The journal is still being replayed: the window is up (the chord and the tray entry work
        /// at once) but offers nothing to click until there is something to show.
        /// </summary>
        private void ShowLoadingState(bool loading)
        {
            _split.Enabled = !loading;
            _newText.Enabled = _newList.Enabled = _search.Enabled = !loading;
            if (loading) _count.Text = T("Загрузка заметок..");
        }

        private void OnUi(Action action)
        {
            if (IsDisposed) return;
            if (!InvokeRequired) { action(); return; }
            if (IsHandleCreated) BeginInvoke(action);
        }

        private void OnServiceLoaded(object? sender, EventArgs e) => OnUi(() =>
        {
            ShowLoadingState(false);
            RefreshList();
            string? text = _pendingText;
            bool startNew = _pendingStartNew;
            _pendingText = null;
            _pendingStartNew = false;
            if (text != null) StartNoteWithText(text);
            else if (startNew) { NewNote(QuickNoteKind.Text); if (Visible) FocusBody(); }
            else if (_current == null && !SelectRemembered()) NewNote(QuickNoteKind.Text);
        });

        /// <summary>
        /// "Delete every quick note" ran. The note open here is one of the deleted ones, so it is
        /// dropped - not merely unlisted: a later commit of it would write it back (QN-1).
        /// </summary>
        private void OnServiceCleared(object? sender, EventArgs e) => OnUi(() =>
        {
            _pendingText = null;
            _pendingStartNew = false;
            ClearEditor();
            RememberSelection(null);
            RefreshList();
            RefreshDates();
            UpdateButtons();
        });

        /// <summary>
        /// An exchange file was imported (S0023). The import changes notes in place, so the one open
        /// here may now hold a newer text than its editor shows: it is loaded again from the object -
        /// the flow wrote the editor into it before the import, so nothing typed is lost.
        /// </summary>
        private void OnServiceImported(object? sender, EventArgs e) => OnUi(() =>
        {
            QuickNote? open = _current;
            if (open != null && !_service.IsDraft(open)) LoadNote(open);
            RefreshList();
            RefreshDates();
            UpdateButtons();
        });

        // Both only repaint: RefreshDates itself shows the failure for as long as the service still
        // holds an unsaved write, so no later repaint can hide it (S0035 QN2-1).
        private void OnSaveFailed(object? sender, EventArgs e) => OnUi(RefreshDates);

        private void OnSaveRecovered(object? sender, EventArgs e) => OnUi(RefreshDates);

        /// <summary>Let go of the open note and empty every editor.</summary>
        private void ClearEditor()
        {
            _current = null;
            _loading = true;
            _title.Text = ""; _editor.Text = ""; _editorOriginal = ""; _items.Items.Clear();
            _loading = false;
        }

        /// <summary>
        /// Persist which note is open whenever that changes (QN-9) - one registry value, and only
        /// when it actually differs. A draft is not remembered: it is nowhere to come back to.
        /// </summary>
        private void RememberSelection(QuickNote? note)
        {
            string id = note == null ? "" : note.Id.ToString("D");
            if (id == _config.QuickNotesSelected) return;
            _config.QuickNotesSelected = id;
            _persistSelection();
        }

        // ---- Layout ----

        private void BuildLayout()
        {
            var top = _topRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 8, 8, 4) };
            top.Controls.Add(_newText);
            top.Controls.Add(_newList);
            top.Controls.Add(_search);
            top.Controls.Add(_count);
            top.Controls.Add(_transfer);
            top.Controls.Add(_settings);

            _list.Columns.Add("", 240);
            _list.Columns.Add("", 110);
            _split.Panel1.Controls.Add(_list);

            var right = _rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8, 6, 8, 6) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            right.Controls.Add(_title, 0, 0);
            var kindRow = _kindRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 4) };
            kindRow.Controls.Add(_kindText);
            kindRow.Controls.Add(_kindList);
            right.Controls.Add(kindRow, 0, 1);

            // Both editors live in the same cell; only one is ever visible. The checklist keeps its
            // buttons inside its own host so hiding the host takes them with it.
            var body = new Panel { Dock = DockStyle.Fill };
            _items.Columns.Add("", 460);
            var itemButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            itemButtons.Controls.Add(_addItem);
            itemButtons.Controls.Add(_removeItem);
            itemButtons.Controls.Add(_itemUp);
            itemButtons.Controls.Add(_itemDown);
            _itemsHost.Controls.Add(_items);
            _itemsHost.Controls.Add(itemButtons);
            body.Controls.Add(_itemsHost);
            body.Controls.Add(_editor);
            right.Controls.Add(body, 0, 2);

            var bottom = _bottomRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0) };
            bottom.Controls.Add(_dates);
            bottom.Controls.Add(_copy);
            bottom.Controls.Add(_keep);
            bottom.Controls.Add(_export);
            bottom.Controls.Add(_delete);
            right.Controls.Add(bottom, 0, 3);

            _split.Panel2.Controls.Add(right);
            Controls.Add(_split);
            Controls.Add(top);
        }

        /// <summary>
        /// The smallest size at which every caption is still drawn in full - <b>measured, never a pair
        /// of constants</b>. The buttons and the two kind captions are translated into
        /// 13 languages and drawn at whatever the display's scaling makes of the UI font, so the
        /// hard-coded 640x420 that used to stand here clipped the bottom row in most of them. It is
        /// also the size the window opens at the first time, since a stored size is only ever grown to
        /// it. A minimum wider than the monitor would be a window the user cannot place, so the working
        /// area caps it.
        /// </summary>
        private void ApplyMinimumSize()
        {
            if (_topRow == null || _kindRow == null || _bottomRow == null || _rightPanel == null) return;

            int rightWidth = _bottomRow.PreferredSize.Width + _rightPanel.Padding.Horizontal;
            int clientWidth = Math.Max(
                _topRow.PreferredSize.Width,
                ListWidth + _split.SplitterWidth + rightWidth);

            int clientHeight = _topRow.PreferredSize.Height
                + _title.PreferredSize.Height + _kindRow.PreferredSize.Height
                + _bottomRow.PreferredSize.Height + _rightPanel.Padding.Vertical
                + Font.Height * MinEditorRows;

            Size frame = Size - ClientSize;   // border + caption, whatever the theme makes of them
            Rectangle area = (Screen.FromControl(this) ?? Screen.PrimaryScreen).WorkingArea;
            var min = new Size(
                Math.Min(clientWidth + frame.Width, area.Width),
                Math.Min(clientHeight + frame.Height, area.Height));

            MinimumSize = min;
            // A window the user has never sized opens at the minimum; one they have is only grown
            // to it, so a size they chose is never taken away from them.
            if (_config.QuickNotesWidth <= 0 || _config.QuickNotesHeight <= 0) Size = min;
            else if (Width < min.Width || Height < min.Height)
                Size = new Size(Math.Max(Width, min.Width), Math.Max(Height, min.Height));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // The frame is only exactly known once there is a handle, so the measurement is repeated.
            ApplyMinimumSize();
            // Now the container has a width, so the clamp below is against the real one.
            try
            {
                _split.SplitterDistance = Math.Max(_split.Panel1MinSize,
                    Math.Min(ListWidth, _split.Width - _split.Panel2MinSize - _split.SplitterWidth));
            }
            catch { /* a window too narrow for either minimum keeps whatever WinForms chose */ }
        }

        // ---- Language and appearance ----

        public void ApplyLanguage(string language)
        {
            _language = language;
            Text = T("Быстрые заметки") + " (" + _config.QuickNotesHotkey + ")";
            RightToLeft = Localization.IsRightToLeft(language) ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = RightToLeft == RightToLeft.Yes;
            _transferMenu.RightToLeft = RightToLeft;
            // Not "null means leave it alone": switching from Hindi back to English has to switch the
            // family back too, or Nirmala UI stayed for good (ticket S0006, QN-10) - the same rule as
            // TranslationResultWindow and SettingsForm.
            string family = Localization.FontFamily(language) ?? SystemFonts.MessageBoxFont.FontFamily.Name;
            if (Font.FontFamily.Name != family)
            {
                Font? previous = _ownFont;
                try { Font = _ownFont = new Font(family, SystemFonts.MessageBoxFont.SizeInPoints); previous?.Dispose(); } catch { }
            }
            // The editor keeps a monospace face whatever the UI font is: the body is as often code
            // as prose, and a proportional face silently misaligns everything that was aligned.
            if (_editorFont == null)
            {
                try { _editorFont = new Font("Consolas", SystemFonts.MessageBoxFont.SizeInPoints + 0.5f); }
                catch { try { _editorFont = new Font(FontFamily.GenericMonospace, SystemFonts.MessageBoxFont.SizeInPoints + 0.5f); } catch { } }
                if (_editorFont != null) { _editor.Font = _editorFont; _items.Font = _editorFont; }
            }

            _newText.Text = "+ " + T("Заметка");
            _newList.Text = "+ " + T("Список");
            _settings.Text = T("Настройки");
            _transfer.Text = T("Перенос...");
            _transferExport.Text = T("Экспортировать...");
            _transferImport.Text = T("Импортировать...");
            _title.PlaceholderTextSafe(T("Имя (необязательно)"));
            _kindText.Text = T("Текст");
            _kindList.Text = T("Список");
            _addItem.Text = T("Добавить пункт");
            _removeItem.Text = T("Удалить пункт");
            _itemUp.AccessibleName = T("Переместить вверх");
            _itemDown.AccessibleName = T("Переместить вниз");
            ApplyMoveGlyphs();
            _copy.Text = T("Копировать");
            _keep.Text = T("Копировать для Google Keep");
            _export.Text = T("Экспорт...");
            _delete.Text = T("Удалить");
            _list.Columns[0].Text = T("Заметка");
            _list.Columns[1].Text = T("Создана");
            _items.Columns[0].Text = T("Пункт");
            RefreshList();
            RefreshDates();
            // Every caption above just changed length, so the floor has to be measured again.
            ApplyMinimumSize();
        }

        public void ApplyWordWrap(bool wrap)
        {
            _editor.WordWrap = wrap;
            _editor.ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both;
        }

        private string T(string ru) => Localization.Translate(_language, ru);

        // ---- Showing ----

        /// <summary>
        /// Bring the window up. <paramref name="startNew"/> is the hotkey's path: a fresh note with
        /// the caret already in the body, so the chord is one gesture and not an invitation to click
        /// something first (spec §3.1).
        /// </summary>
        public void ShowNotes(bool startNew)
        {
            RestoreSavedBounds();
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            ForegroundActivator.Activate(this);
            if (!_service.IsLoaded)
            {
                // The chord's fresh note waits for the notes to arrive (OnServiceLoaded).
                _pendingStartNew |= startNew;
                return;
            }
            if (startNew) NewNote(QuickNoteKind.Text);
            else if (_current == null && !SelectRemembered()) NewNote(QuickNoteKind.Text);
            FocusBody();
        }

        /// <summary>The context menu's path: a new note that already holds the user's selection.</summary>
        public void ShowWithText(string text)
        {
            RestoreSavedBounds();
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            ForegroundActivator.Activate(this);
            if (!_service.IsLoaded)
            {
                _pendingText = text;
                return;
            }
            StartNoteWithText(text);
        }

        private void StartNoteWithText(string text)
        {
            NewNote(QuickNoteKind.Text);
            if (QuickNote.ExceedsLimit(text))
            {
                ConfirmDialog.Show(this, _language,
                    Localization.Format(T, "Фрагмент больше {0} КБ и в заметку не помещается.", QuickNote.MaxBytes / 1024),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _loading = true;
            _editor.Text = ForDisplay(text);
            _editorOriginal = _editor.Text;
            _loading = false;
            if (_current != null)
            {
                // The captured text, not the editor's - the display copy has had its line endings
                // normalized for the edit control, and the point of this path is the exact bytes.
                // Touched rather than routed through OnEdited: that one compares the editor against
                // what it was handed, sees no change, and would leave the draft unsaved.
                _current.RawText = text;
                _service.Touch(_current);
                UpdateButtons();
            }
            FocusBody();
        }

        private void FocusBody()
        {
            if (_current != null && _current.Kind == QuickNoteKind.Checklist) { _items.Focus(); return; }
            _editor.Focus();
            _editor.SelectionStart = _editor.TextLength;
        }

        private void RestoreSavedBounds()
        {
            if (_boundsRestored) return;
            _boundsRestored = true;
            var saved = new Rectangle(_config.QuickNotesX, _config.QuickNotesY, Width, Height);
            if (_config.QuickNotesX != int.MinValue && _config.QuickNotesY != int.MinValue)
            {
                Rectangle clamped = ScreenPlacement.Clamp(saved, Screen.AllScreens.Select(s => s.WorkingArea));
                Location = clamped.Location;
                Size = clamped.Size;
                return;
            }
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        private void SaveBounds()
        {
            if (WindowState != FormWindowState.Normal) return;
            _config.QuickNotesX = Left; _config.QuickNotesY = Top;
            _config.QuickNotesWidth = Width; _config.QuickNotesHeight = Height;
            // QuickNotesSelected is kept current by RememberSelection; it is written along with the
            // geometry below, but never set from here - a draft would have put its id there.
            // Only these five values, not the whole config: this fires on every drag, and a full
            // Save() there would serialize both profile tables forty times across one move.
            _config.SaveQuickNotesWindow();
        }

        // ---- The list ----

        private void OnServiceChanged(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)RefreshList);
        }

        private void RefreshList()
        {
            if (IsDisposed) return;
            List<QuickNote> found = _service.Search(_search.Text);
            bool loading = _loading;
            _loading = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (QuickNote note in found)
            {
                var row = new ListViewItem(RowText(note)) { Tag = note };
                row.SubItems.Add(note.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                if (ReferenceEquals(note, _current)) row.Selected = true;
                _list.Items.Add(row);
            }
            _list.EndUpdate();
            _loading = loading;
            _count.Text = !_service.IsLoaded ? T("Загрузка заметок..")
                : _search.Text.Trim().Length == 0
                ? Localization.Format(T, "Заметок: {0}", found.Count)
                : Localization.Format(T, "Найдено: {0}", found.Count);
        }

        /// <summary>
        /// The derived caption: the name, else the first non-empty line. Built here and never stored
        /// (spec §3.3) - which is the whole reason changing that first line later is harmless.
        /// </summary>
        private string RowText(QuickNote note)
        {
            string preview = note.Preview(PreviewLength);
            string mark = note.Kind == QuickNoteKind.Checklist ? "☑ " : "";
            return preview.Length > 0 ? mark + preview : mark + T("(пусто)");
        }

        private void OnListSelectionChanged()
        {
            if (_loading) return;
            if (_list.SelectedItems.Count != 1) return;
            if (_list.SelectedItems[0].Tag is not QuickNote note) return;
            if (ReferenceEquals(note, _current)) return;
            CommitCurrent();   // switching notes writes immediately, never asks
            LoadNote(note);
        }

        private bool SelectRemembered()
        {
            if (!Guid.TryParse(_config.QuickNotesSelected, out Guid id)) return false;
            QuickNote? note = _service.Find(id);
            if (note == null) return false;
            LoadNote(note);
            SelectRow(note);
            return true;
        }

        private void SelectRow(QuickNote note)
        {
            bool loading = _loading;
            _loading = true;
            foreach (ListViewItem row in _list.Items)
                row.Selected = ReferenceEquals(row.Tag, note);
            _loading = loading;
        }

        // ---- Editing ----

        private void NewNote(QuickNoteKind kind)
        {
            CommitCurrent();
            QuickNote note = _service.CreateDraft(kind);
            LoadNote(note);
            // A draft is in no list yet, so nothing is selected until it earns a row.
            bool loading = _loading;
            _loading = true;
            foreach (ListViewItem row in _list.Items) row.Selected = false;
            _loading = loading;
            if (kind == QuickNoteKind.Text) { _editor.Focus(); } else { _items.Focus(); }
        }

        private void LoadNote(QuickNote note)
        {
            _loading = true;
            _current = note;
            _title.Text = note.Title ?? "";
            _kindText.Checked = note.Kind == QuickNoteKind.Text;
            _kindList.Checked = note.Kind == QuickNoteKind.Checklist;
            _editor.Text = ForDisplay(note.RawText);
            _editorOriginal = _editor.Text;
            ReloadItems();
            ShowEditorFor(note.Kind);
            _loading = false;
            RefreshDates();
            UpdateButtons();
            if (!_service.IsDraft(note)) RememberSelection(note);
        }

        private void ShowEditorFor(QuickNoteKind kind)
        {
            bool checklist = kind == QuickNoteKind.Checklist;
            _itemsHost.Visible = checklist;
            _editor.Visible = !checklist;
        }

        private void ReloadItems()
        {
            _items.BeginUpdate();
            _items.Items.Clear();
            if (_current != null)
                foreach (QuickNoteItem item in _current.Items)
                    _items.Items.Add(new ListViewItem(item.Text) { Checked = item.IsChecked, Tag = item });
            _items.EndUpdate();
        }

        /// <summary>
        /// A Win32 edit control does not render a bare LF as a line break, so the body is shown with
        /// CRLF. Nothing is written back unless the user actually edits (see the class remarks), so
        /// this is a display step and not a normalization of the stored text.
        /// </summary>
        private static string ForDisplay(string raw)
            => raw.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

        private void OnEdited(bool titleChanged)
        {
            if (_loading || _current == null) return;
            if (titleChanged) _current.Title = QuickNote.NormalizeTitle(_title.Text);
            else if (_current.Kind == QuickNoteKind.Text)
            {
                if (_editor.Text == _editorOriginal) return;   // untouched: keep the exact bytes
                if (QuickNote.ExceedsLimit(_editor.Text))
                {
                    ConfirmDialog.Show(this, _language,
                        Localization.Format(T, "Фрагмент больше {0} КБ и в заметку не помещается.", QuickNote.MaxBytes / 1024),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _loading = true;
                    _editor.Text = _editorOriginal;
                    _loading = false;
                    return;
                }
                _current.RawText = _editor.Text;
                _editorOriginal = _editor.Text;
            }
            else
            {
                ReadItemsFromView();
            }

            _service.Touch(_current);
            UpdateRowText();
            UpdateButtons();
        }

        private void ReadItemsFromView()
        {
            if (_current == null) return;
            var items = new List<QuickNoteItem>(_items.Items.Count);
            foreach (ListViewItem row in _items.Items)
            {
                var item = row.Tag as QuickNoteItem ?? new QuickNoteItem();
                item.Text = row.Text;
                item.IsChecked = row.Checked;
                item.Position = items.Count;
                row.Tag = item;
                items.Add(item);
            }
            _current.Items = items;
        }

        private void OnItemRenamed(object? sender, LabelEditEventArgs e)
        {
            if (e.Label == null) return;
            // The size cap holds for a checklist too (QN-6): the items joined as lines are measured
            // exactly as a text body would be. Renaming an item is the only way a checklist grows -
            // an added item is empty, and a text converted into one was already under the cap.
            var lines = new List<string>(_items.Items.Count);
            foreach (ListViewItem row in _items.Items) lines.Add(row.Index == e.Item ? e.Label : row.Text);
            if (QuickNote.ExceedsLimit(string.Join("\n", lines)))
            {
                e.CancelEdit = true;
                ConfirmDialog.Show(this, _language,
                    Localization.Format(T, "Фрагмент больше {0} КБ и в заметку не помещается.", QuickNote.MaxBytes / 1024),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // The label is not on the item yet inside AfterLabelEdit, so the read has to wait a turn.
            BeginInvoke((Action)(() => OnEdited(titleChanged: false)));
        }

        private void UpdateRowText()
        {
            if (_current == null) return;
            foreach (ListViewItem row in _list.Items)
                if (ReferenceEquals(row.Tag, _current)) { row.Text = RowText(_current); return; }
        }

        private void OnKindChosen(QuickNoteKind kind)
        {
            if (_loading || _current == null) return;
            if (!(kind == QuickNoteKind.Text ? _kindText.Checked : _kindList.Checked)) return;
            if (_current.Kind == kind) return;

            if (_current.LosesDataConvertingTo(kind)
                && ConfirmDialog.Show(this, _language,
                    kind == QuickNoteKind.Text
                        ? T("Отметки выполнения при переходе к тексту не сохранятся. Продолжить?")
                        : T("Пункты списка не хранят переносы строк: текст вернётся с обычными переводами строк. Продолжить?"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
            {
                _loading = true;
                _kindText.Checked = _current.Kind == QuickNoteKind.Text;
                _kindList.Checked = _current.Kind == QuickNoteKind.Checklist;
                _loading = false;
                return;
            }

            if (_current.Kind == QuickNoteKind.Text) _current.RawText = _editor.Text;
            else ReadItemsFromView();
            _current.ConvertTo(kind);

            _loading = true;
            _editor.Text = ForDisplay(_current.RawText);
            _editorOriginal = _editor.Text;
            ReloadItems();
            ShowEditorFor(kind);
            _loading = false;

            _service.Touch(_current);
            UpdateRowText();
        }

        private void AddChecklistItem()
        {
            if (_current == null || _current.Kind != QuickNoteKind.Checklist) return;
            var row = new ListViewItem("") { Tag = new QuickNoteItem() };
            _items.Items.Add(row);
            OnEdited(titleChanged: false);
            row.BeginEdit();
        }

        private void RemoveChecklistItem()
        {
            if (_items.SelectedItems.Count != 1) return;
            _items.Items.Remove(_items.SelectedItems[0]);
            OnEdited(titleChanged: false);
        }

        private void MoveChecklistItem(int delta)
        {
            if (_items.SelectedItems.Count != 1) return;
            ListViewItem row = _items.SelectedItems[0];
            int target = row.Index + delta;
            if (target < 0 || target >= _items.Items.Count) return;
            _items.BeginUpdate();
            _items.Items.Remove(row);
            _items.Items.Insert(target, row);
            _items.EndUpdate();
            row.Selected = true;
            OnEdited(titleChanged: false);
        }

        private void RefreshDates()
        {
            if (_service.SaveFailing)
            {
                _dates.Text = T("Заметка не сохранена на диск - подробности в журнале диагностики.");
                return;
            }
            if (_current == null || _current.CreatedAtUtc == default)
            {
                _dates.Text = T("Новая заметка");
                return;
            }
            _dates.Text = Localization.Format(T, "Создана {0}, изменена {1} | Строк: {2}, символов: {3}",
                _current.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                _current.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                _current.LineCount,
                _current.CharacterCount);
        }

        private void UpdateButtons()
        {
            bool has = _current != null && !_current.IsEmpty;
            _copy.Enabled = _keep.Enabled = _export.Enabled = has;
            _delete.Enabled = _current != null;
        }

        /// <summary>
        /// Write the open note now - on close, on switching notes and on Ctrl+S - but only when it
        /// differs from what was last saved (ticket S0006, QN-3). Every edit already marks the note
        /// dirty on its way through <see cref="OnEdited"/>; what is compared here is the view against
        /// the model, so an edit that never raised an event still counts, and a note that was only
        /// looked at is neither appended again nor given a new "modified" date.
        /// </summary>
        public void CommitCurrent()
        {
            if (_current == null) return;
            bool changed = false;
            if (_current.Kind == QuickNoteKind.Text && _editor.Text != _editorOriginal)
            {
                _current.RawText = _editor.Text;
                _editorOriginal = _editor.Text;
                changed = true;
            }
            else if (_current.Kind == QuickNoteKind.Checklist)
            {
                string before = ItemsSignature(_current.Items);
                ReadItemsFromView();
                changed = before != ItemsSignature(_current.Items);
            }
            string? title = QuickNote.NormalizeTitle(_title.Text);
            if (title != _current.Title)
            {
                _current.Title = title;
                changed = true;
            }
            if (changed) _service.Touch(_current);

            bool wasDraft = _service.IsDraft(_current);
            // Counting a creation is the service's job (its Created event): the debounce usually
            // creates the draft before this runs, which is why counting here almost never fired.
            if (!_service.Commit(_current)) return;
            if (wasDraft)
            {
                RefreshList();
                SelectRow(_current);
                RememberSelection(_current);
            }
            RefreshDates();
        }

        private static string ItemsSignature(List<QuickNoteItem> items)
        {
            var text = new System.Text.StringBuilder();
            foreach (QuickNoteItem item in items)
                text.Append(item.IsChecked ? '1' : '0').Append(item.Text).Append('\u0001');
            return text.ToString();
        }

        // ---- Commands ----

        private void CopyCurrent()
        {
            if (_current == null) return;
            Win32Clipboard.TrySetText(_current.ToPlainText());
        }

        private void CopyForKeep()
        {
            if (_current == null) return;
            Win32Clipboard.TrySetText(_current.ToKeepText());
            string message = T("Текст скопирован. Создайте заметку в Google Keep и вставьте его.");
            // Said out loud rather than discovered in Keep: pasted lines become list items there,
            // and the tick marks have nowhere to travel.
            if (_current.Kind == QuickNoteKind.Checklist && _current.HasCheckedItems)
                message += "\n\n" + T("Отметки выполнения в Google Keep не переносятся.");
            message += "\n\n" + T("Открыть Google Keep в браузере?");
            if (ConfirmDialog.Show(this, _language, message, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://keep.google.com/") { UseShellExecute = true }); }
            catch { /* the text is on the clipboard either way */ }
        }

        private void ExportCurrent()
        {
            if (_current == null) return;
            using var dialog = new SaveFileDialog
            {
                Title = T("Экспорт заметки"),
                Filter = T("Текстовый файл") + " (*.txt)|*.txt|Markdown (*.md)|*.md",
                FileName = SafeFileName(_current),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                bool markdown = dialog.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                System.IO.File.WriteAllText(dialog.FileName,
                    markdown ? _current.ToMarkdown() : _current.ToPlainText(), System.Text.Encoding.UTF8);
                // The one thing worth saying about an export: what leaves here is no longer encrypted.
                ConfirmDialog.Show(this, _language,
                    Localization.Format(T, "Заметка сохранена: {0}", dialog.FileName) + "\n\n"
                    + T("Этот файл не защищён DPAPI — его прочитает любой, у кого есть доступ к папке."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ConfirmDialog.Show(this, _language, Localization.Format(T, "Не удалось сохранить файл: {0}", FailureCause.Describe(ex, _language)),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>A file name from the note's preview, with everything Windows refuses taken out.</summary>
        internal static string SafeFileName(QuickNote note)
        {
            string name = note.Preview(40);
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            name = name.Trim().Trim('.');
            return name.Length == 0 ? "note.txt" : name + ".txt";
        }

        private void DeleteCurrent()
        {
            if (_current == null) return;
            if (!_service.IsDraft(_current)
                && ConfirmDialog.Show(this, _language, T("Удалить эту заметку? Отменить удаление нельзя."),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            _service.Delete(_current);
            ClearEditor();
            RememberSelection(null);
            RefreshList();
            RefreshDates();
            UpdateButtons();
        }

        // ---- Keys ----

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                CommitCurrent();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode != Keys.Escape) return;

            // Escape steps out before it closes: the first press leaves the field you are in, the
            // second hides the window. Closing straight from inside the editor is how a stray
            // Escape loses the place you were typing in.
            if (_search.Focused || _editor.Focused || _title.Focused || _items.Focused)
            {
                _list.Focus();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            CommitCurrent();
            Hide();
            e.Handled = e.SuppressKeyPress = true;
        }

        /// <summary>
        /// The two glyph-only buttons carry the vocabulary's move-up and move-down (ticket S0022 A4) in the
        /// theme's text colour, at tier 20, with no caption - the accessible name set with the captions is
        /// what names them. A glyph that cannot be drawn leaves the arrow character it used to be.
        /// </summary>
        private void ApplyMoveGlyphs()
        {
            ThemePalette palette = ThemeApply.PaletteOf(this) ?? ThemePalette.Light;
            SetMoveGlyph(_itemUp, ref _upImage, AppGlyphs.MoveUp, "↑", palette);
            SetMoveGlyph(_itemDown, ref _downImage, AppGlyphs.MoveDown, "↓", palette);
        }

        private static void SetMoveGlyph(Button button, ref Bitmap? current, string id, string fallbackText, ThemePalette palette)
        {
            Bitmap? image = GlyphRenderer.Render(id, HistoryStripGlyphs.Size, palette.TextPrimary);
            button.Image = image;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.Text = image == null ? fallbackText : "";
            button.AutoSize = image == null;
            if (image != null) button.Size = new Size(34, 30);
            current?.Dispose();   // after the button let go of it
            current = image;
        }

        protected override void OnThemeApplied(ThemePalette palette) => ApplyMoveGlyphs();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _service.Changed -= OnServiceChanged;
                _service.Cleared -= OnServiceCleared;
                _service.Imported -= OnServiceImported;
                _transferMenu.Dispose();
                _service.Loaded -= OnServiceLoaded;
                _service.SaveFailed -= OnSaveFailed;
                _service.SaveRecovered -= OnSaveRecovered;
                _searchTimer.Stop();
                _searchTimer.Dispose();
            }
            base.Dispose(disposing); // the control tree first: it is still drawing with our fonts
            if (disposing)
            {
                _upImage?.Dispose();
                _downImage?.Dispose();
                _ownFont?.Dispose();
                _editorFont?.Dispose();
                _ownIcon?.Dispose();
            }
        }
    }

    /// <summary>
    /// <c>TextBox.PlaceholderText</c> arrived in .NET Core; on net48 the same effect is one message
    /// the Win32 edit control has understood since Vista. A failure here is cosmetic by definition,
    /// so it is swallowed.
    /// </summary>
    internal static class PlaceholderTextExtensions
    {
        private const int EM_SETCUEBANNER = 0x1501;

        public static void PlaceholderTextSafe(this TextBox box, string text)
        {
            try
            {
                if (!box.IsHandleCreated) box.HandleCreated += (_, _) => Send(box, text);
                else Send(box, text);
            }
            catch { }
        }

        private static void Send(TextBox box, string text)
        {
            try { WindowInterop.SendMessageString(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); } catch { }
        }
    }
}
