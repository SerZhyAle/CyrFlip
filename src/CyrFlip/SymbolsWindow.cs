using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The Symbols window palette (ticket S0056).
    /// Displays rare symbols and copies clicked symbol to clipboard without taking editor focus.
    /// </summary>
    internal sealed class SymbolsWindow : ThemedForm
    {
        private const int DefaultPaletteWidth = 420;
        private const int DefaultPaletteHeight = 360;

        private readonly AppConfig _config;
        private readonly SymbolRecent _recent;
        private readonly SymbolTypability _typability;
        private readonly Action<string, Action<bool>> _copyAction;

        private readonly TextBox _searchBox = new TextBox();
        private readonly FlowLayoutPanel _categoriesBar = new FlowLayoutPanel();
        private readonly SymbolGrid _grid = new SymbolGrid();
        private readonly Label _statusLabel = new Label();
        private readonly TableLayoutPanel _layout = new TableLayoutPanel();
        private readonly List<CategoryButton> _categoryButtons = new List<CategoryButton>();

        private string _uiLanguage = "ru";
        private string _selectedCategory = SymbolsData.CategoryRecent;
        private IntPtr _lastActiveTarget = IntPtr.Zero;
        private bool _openedActive;
        private bool _boundsRestored;
        private ThemePalette _currentPalette = ThemePalette.Light;

        protected override bool ShowWithoutActivation => true;

        public SymbolRecent Recent => _recent;
        public SymbolGrid Grid => _grid;
        public TextBox SearchBox => _searchBox;

        public SymbolsWindow(
            AppConfig config,
            string uiLanguage,
            Action<string, Action<bool>> copyAction,
            SymbolTypability? typability = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _uiLanguage = uiLanguage ?? "ru";
            _copyAction = copyAction ?? throw new ArgumentNullException(nameof(copyAction));
            _typability = typability ?? new SymbolTypability();

            _recent = SymbolRecent.FromJson(_config.SymbolsRecent, text => SymbolsData.FindByText(text) != null);
            if (!string.IsNullOrEmpty(_config.SymbolsCategory))
            {
                _selectedCategory = _config.SymbolsCategory;
            }

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
            _layout.RowCount = 4;
            _layout.Padding = new Padding(6);
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Search
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Categories
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Grid
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Status

            // Search box
            _searchBox.Dock = DockStyle.Fill;
            _searchBox.Margin = new Padding(2, 2, 2, 4);
            _searchBox.TextChanged += (_, _) => RefreshSymbols();
            _searchBox.KeyDown += OnSearchKeyDown;

            // Categories strip
            _categoriesBar.Dock = DockStyle.Fill;
            _categoriesBar.AutoSize = true;
            _categoriesBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _categoriesBar.WrapContents = true;
            _categoriesBar.Margin = new Padding(0, 0, 0, 4);
            BuildCategoryButtons();

            // Grid
            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(2);
            _grid.SymbolHovered += OnSymbolHovered;
            _grid.SymbolActivated += OnSymbolActivated;

            // Status label
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoSize = false;
            _statusLabel.Height = 22;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Margin = new Padding(4, 2, 4, 2);

            _layout.Controls.Add(_searchBox, 0, 0);
            _layout.Controls.Add(_categoriesBar, 0, 1);
            _layout.Controls.Add(_grid, 0, 2);
            _layout.Controls.Add(_statusLabel, 0, 3);

            Controls.Add(_layout);
        }

        private void BuildCategoryButtons()
        {
            _categoriesBar.Controls.Clear();
            _categoryButtons.Clear();

            foreach (SymbolCategory cat in SymbolsData.Categories)
            {
                var btn = new CategoryButton(this, cat.Key)
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(1, 1, 1, 1),
                    Padding = new Padding(4, 2, 4, 2),
                    TabStop = false,
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += (_, _) => SelectCategory(cat.Key);

                _categoryButtons.Add(btn);
                _categoriesBar.Controls.Add(btn);
            }
            UpdateCategoryButtonsAppearance();
        }

        private void UpdateCategoryButtonsAppearance()
        {
            foreach (CategoryButton btn in _categoryButtons)
            {
                bool isSelected = string.Equals(btn.CategoryKey, _selectedCategory, StringComparison.OrdinalIgnoreCase);
                btn.BackColor = isSelected ? _currentPalette.SurfaceSelected : _currentPalette.Control;
                btn.ForeColor = isSelected ? _currentPalette.TextPrimary : _currentPalette.TextPrimary;
                btn.FlatAppearance.BorderColor = isSelected ? _currentPalette.Accent : _currentPalette.Border;
            }
        }

        private void SelectCategory(string categoryKey)
        {
            _selectedCategory = categoryKey;
            _config.SymbolsCategory = categoryKey;
            _config.SaveSymbolsCategory();
            UpdateCategoryButtonsAppearance();
            RefreshSymbols();
        }

        public void ApplyLanguage(string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            Text = Localization.Translate(_uiLanguage, "Окно символов");
            _searchBox.AccessibleName = Localization.Translate(_uiLanguage, "Поиск символов");
            _grid.SetLanguage(_uiLanguage);

            foreach (CategoryButton btn in _categoryButtons)
            {
                string titleRu = SymbolsData.CategoryTitleRu(btn.CategoryKey);
                btn.Text = Localization.Translate(_uiLanguage, titleRu);
                btn.AccessibleName = btn.Text;
            }

            ApplyMinimumSize();
            RefreshSymbols();
        }

        private void ApplyMinimumSize()
        {
            int minW = Math.Max(320, _categoriesBar.PreferredSize.Width + _layout.Padding.Horizontal);
            int minH = Math.Max(260, _searchBox.PreferredSize.Height + _categoriesBar.PreferredSize.Height + _statusLabel.Height + 160);

            Rectangle area = (Screen.FromControl(this) ?? Screen.PrimaryScreen).WorkingArea;
            MinimumSize = new Size(Math.Min(minW, area.Width), Math.Min(minH, area.Height));
        }

        private void RefreshSymbols()
        {
            string query = _searchBox.Text.Trim();
            bool hideTypable = _config.SymbolsHideTypable;

            Func<SymbolEntry, bool>? filter = hideTypable ? (Func<SymbolEntry, bool>)(e => !_typability.IsTypable(e)) : null;

            if (string.IsNullOrEmpty(query) && string.Equals(_selectedCategory, SymbolsData.CategoryRecent, StringComparison.OrdinalIgnoreCase))
            {
                // Recent symbols
                var recentEntries = new List<SymbolEntry>();
                foreach (string text in _recent.Items)
                {
                    SymbolEntry? entry = SymbolsData.FindByText(text);
                    if (entry != null && (filter == null || filter(entry)))
                    {
                        recentEntries.Add(entry);
                    }
                }
                _grid.SetEntries(recentEntries);
            }
            else if (string.IsNullOrEmpty(query))
            {
                // Curated category
                IEnumerable<SymbolEntry> candidates = SymbolsData.Table.Where(e =>
                    string.Equals(e.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
                if (filter != null) candidates = candidates.Where(filter);
                _grid.SetEntries(candidates.ToList());
            }
            else
            {
                // Active search across all curated symbols
                List<SymbolEntry> results = SymbolSearch.Search(
                    SymbolsData.Table,
                    query,
                    _uiLanguage,
                    selectedCategory: null,
                    filter: filter);
                _grid.SetEntries(results);
            }
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

        private void OnSymbolHovered(SymbolEntry? entry)
        {
            if (entry == null)
            {
                _statusLabel.Text = "";
                return;
            }

            TypabilityInfo typable = _typability.Query(entry);
            _statusLabel.Text = typable.GetHint(_uiLanguage);
        }

        private void OnSymbolActivated(SymbolEntry entry)
        {
            _copyAction(entry.Text, success =>
            {
                if (success)
                {
                    _recent.Add(entry.Text);
                    _config.SymbolsRecent = _recent.ToJson();
                    _config.SaveSymbolsRecent();

                    if (string.Equals(_selectedCategory, SymbolsData.CategoryRecent, StringComparison.OrdinalIgnoreCase))
                    {
                        RefreshSymbols();
                    }

                    _grid.FlashSymbol(entry);
                    _statusLabel.Text = Localization.Format(_uiLanguage, "Скопировано: {0}", entry.Text);

                    if (_openedActive)
                    {
                        HandBackFocus();
                    }
                }
                else
                {
                    _statusLabel.Text = Localization.Translate(_uiLanguage, "Буфер обмена занят, повторите");
                }
            });
        }

        public void ClearRecent()
        {
            if (_recent.Count == 0)
            {
                ConfirmDialog.Show(
                    this,
                    _uiLanguage,
                    Localization.Translate(_uiLanguage, "Список недавних символов уже пуст."),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (ConfirmDialog.Show(
                this,
                _uiLanguage,
                Localization.Translate(_uiLanguage, "Очистить список недавних символов?"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                danger: true) == DialogResult.Yes)
            {
                _recent.Clear();
                _config.SymbolsRecent = _recent.ToJson();
                _config.SaveSymbolsRecent();
                RefreshSymbols();
            }
        }

        public void ShowPalette(IntPtr targetWindow, bool active)
        {
            _lastActiveTarget = targetWindow;
            _openedActive = active;

            RestoreSavedBounds();
            if (!Visible) Show();

            WindowState = FormWindowState.Normal;
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

            int x = _config.SymbolsX;
            int y = _config.SymbolsY;
            int w = _config.SymbolsWidth;
            int h = _config.SymbolsHeight;

            if (x != int.MinValue && y != int.MinValue && w > 0 && h > 0)
            {
                var saved = new Rectangle(x, y, w, h);
                Bounds = ScreenPlacement.Clamp(saved, Screen.AllScreens.Select(s => s.WorkingArea));
            }
            else
            {
                Point mouse = Cursor.Position;
                Screen screen = Screen.FromPoint(mouse) ?? Screen.PrimaryScreen;
                Rectangle area = screen.WorkingArea;

                int defaultW = Math.Min(DefaultPaletteWidth, area.Width - 40);
                int defaultH = Math.Min(DefaultPaletteHeight, area.Height - 40);
                int posX = Math.Max(area.Left, Math.Min(mouse.X + 10, area.Right - defaultW));
                int posY = Math.Max(area.Top, Math.Min(mouse.Y + 10, area.Bottom - defaultH));

                Bounds = ScreenPlacement.Clamp(new Rectangle(posX, posY, defaultW, defaultH), new[] { area });
            }
            ApplyMinimumSize();
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
            _currentPalette = palette;
            BackColor = palette.SurfaceWindow;
            ForeColor = palette.TextPrimary;

            _searchBox.BackColor = palette.SurfaceRaised;
            _searchBox.ForeColor = palette.TextPrimary;

            _statusLabel.BackColor = palette.SurfaceWindow;
            _statusLabel.ForeColor = palette.TextMuted;

            _grid.ApplyTheme(palette);
            UpdateCategoryButtonsAppearance();
        }

        private sealed class CategoryButton : Button, IThemeAware
        {
            private readonly SymbolsWindow _owner;
            public string CategoryKey { get; }

            public CategoryButton(SymbolsWindow owner, string categoryKey)
            {
                _owner = owner;
                CategoryKey = categoryKey;
            }

            public void ApplyTheme(ThemePalette palette)
            {
                bool isSelected = string.Equals(CategoryKey, _owner._selectedCategory, StringComparison.OrdinalIgnoreCase);
                BackColor = isSelected ? palette.SurfaceSelected : palette.Control;
                ForeColor = palette.TextPrimary;
                FlatAppearance.BorderColor = isSelected ? palette.Accent : palette.Border;
            }

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
