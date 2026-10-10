using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Owner-drawn grid of symbols (ticket S0056 section 5.2, revision 2026-10-08: twelve columns, vertical scroll).
    /// Answers WM_MOUSEACTIVATE with MA_NOACTIVATE so clicking never steals focus from the editor.
    /// Uses ThemePalette roles exclusively with zero Color literals.
    /// </summary>
    internal sealed class SymbolGrid : Control, IThemeAware
    {
        /// <summary>The cells are always this many across; the scroll bar sits beside them, not among them.</summary>
        public const int Columns = 12;

        private readonly VScrollBar _scrollBar;
        private readonly List<SymbolEntry> _entries = new List<SymbolEntry>();
        private int _hoverIndex = -1;
        private int _selectedIndex = -1;
        private int _flashIndex = -1;
        private readonly Timer _flashTimer = new Timer { Interval = 200 };

        private ThemePalette _palette = ThemePalette.Light;
        private string _uiLanguage = "ru";
        private Font? _symbolFont;
        private Font? _labelFont;

        public event Action<SymbolEntry>? SymbolHovered;
        public event Action<SymbolEntry>? SymbolActivated;

        public IReadOnlyList<SymbolEntry> Entries => _entries;

        public SymbolEntry? SelectedEntry =>
            _selectedIndex >= 0 && _selectedIndex < _entries.Count ? _entries[_selectedIndex] : null;

        public SymbolGrid()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            DoubleBuffered = true;
            TabStop = true;

            _scrollBar = new VScrollBar
            {
                Dock = DockStyle.Right,
                TabStop = false,
                SmallChange = 36,
                LargeChange = 144,
            };
            _scrollBar.ValueChanged += (_, _) => Invalidate();
            Controls.Add(_scrollBar);

            _flashTimer.Tick += (_, _) =>
            {
                _flashTimer.Stop();
                _flashIndex = -1;
                Invalidate();
            };

            UpdateFonts();
            ApplyTheme(ThemePalette.Light);
        }

        public void SetLanguage(string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            AccessibleName = Localization.Translate(_uiLanguage, "Сетка символов");
        }

        public void SetEntries(IEnumerable<SymbolEntry> entries)
        {
            _entries.Clear();
            if (entries != null)
            {
                _entries.AddRange(entries);
            }

            _hoverIndex = -1;
            _selectedIndex = _entries.Count > 0 ? 0 : -1;
            UpdateScroll();
            Invalidate();

            if (_selectedIndex >= 0)
            {
                SymbolHovered?.Invoke(_entries[_selectedIndex]);
            }
            else
            {
                SymbolHovered?.Invoke(null!);
            }
        }

        public void FlashSymbol(SymbolEntry entry)
        {
            int index = _entries.IndexOf(entry);
            if (index >= 0)
            {
                _flashIndex = index;
                _flashTimer.Stop();
                _flashTimer.Start();
                Invalidate();
            }
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.SurfaceWindow;
            ForeColor = palette.TextPrimary;
            Invalidate();
        }

        private void UpdateFonts()
        {
            float baseSize = Font.SizeInPoints > 0 ? Font.SizeInPoints : 9f;
            _symbolFont?.Dispose();
            _labelFont?.Dispose();

            try
            {
                _symbolFont = new Font("Segoe UI Symbol", baseSize + 5f, FontStyle.Regular);
            }
            catch
            {
                _symbolFont = new Font(Font.FontFamily, baseSize + 5f, FontStyle.Regular);
            }

            try
            {
                _labelFont = new Font(Font.FontFamily, Math.Max(7f, baseSize - 1.5f), FontStyle.Bold);
            }
            catch
            {
                _labelFont = new Font(Font.FontFamily, 7.5f, FontStyle.Regular);
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateFonts();
            UpdateScroll();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
        }

        private int CellSize
        {
            get
            {
                int h = Font.Height;
                return Math.Max(34, h * 2);
            }
        }

        /// <summary>The edge of one cell; the window sizes itself to <see cref="Columns"/> of them.</summary>
        public int CellPixelSize => CellSize;

        private int CellsWidth => Columns * CellSize;

        private int ColumnsCount => Columns;

        private int RowsCount => (_entries.Count + ColumnsCount - 1) / ColumnsCount;

        private void UpdateScroll()
        {
            int totalHeight = RowsCount * CellSize;
            int viewHeight = ClientSize.Height;

            if (totalHeight > viewHeight)
            {
                _scrollBar.Visible = true;
                _scrollBar.Maximum = totalHeight;
                _scrollBar.LargeChange = viewHeight;
                _scrollBar.SmallChange = CellSize;
            }
            else
            {
                _scrollBar.Visible = false;
                _scrollBar.Value = 0;
            }
        }

        private Rectangle CellRectangle(int index)
        {
            if (index < 0 || index >= _entries.Count) return Rectangle.Empty;

            int cols = ColumnsCount;
            int col = index % cols;
            int row = index / cols;
            int size = CellSize;
            int scrollY = _scrollBar.Visible ? _scrollBar.Value : 0;

            return new Rectangle(col * size, row * size - scrollY, size, size);
        }

        private int HitTest(Point pt)
        {
            if (_entries.Count == 0 || pt.X >= CellsWidth) return -1;

            int size = CellSize;
            int scrollY = _scrollBar.Visible ? _scrollBar.Value : 0;
            int col = pt.X / size;
            int row = (pt.Y + scrollY) / size;

            int cols = ColumnsCount;
            if (col < 0 || col >= cols || row < 0) return -1;

            int index = row * cols + col;
            return index < _entries.Count ? index : -1;
        }

        private void EnsureVisible(int index)
        {
            if (!_scrollBar.Visible || index < 0 || index >= _entries.Count) return;

            Rectangle rect = CellRectangle(index);
            int scrollY = _scrollBar.Value;
            int size = CellSize;

            int cellTop = (index / ColumnsCount) * size;
            int cellBottom = cellTop + size;

            if (cellTop < scrollY)
            {
                _scrollBar.Value = Math.Max(0, cellTop);
            }
            else if (cellBottom > scrollY + ClientSize.Height)
            {
                int maxVal = Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
                _scrollBar.Value = Math.Min(maxVal, cellBottom - ClientSize.Height);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            if (hit != _hoverIndex)
            {
                _hoverIndex = hit;
                Invalidate();
                if (_hoverIndex >= 0 && _hoverIndex < _entries.Count)
                {
                    SymbolHovered?.Invoke(_entries[_hoverIndex]);
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Invalidate();
                if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
                {
                    SymbolHovered?.Invoke(_entries[_selectedIndex]);
                }
                else
                {
                    SymbolHovered?.Invoke(null!);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = HitTest(e.Location);
            if (hit >= 0 && hit < _entries.Count)
            {
                _selectedIndex = hit;
                Invalidate();
                SymbolActivated?.Invoke(_entries[hit]);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_scrollBar.Visible) return;

            int delta = -Math.Sign(e.Delta) * _scrollBar.SmallChange * 3;
            int next = Math.Max(0, Math.Min(_scrollBar.Maximum - _scrollBar.LargeChange + 1, _scrollBar.Value + delta));
            if (next != _scrollBar.Value)
            {
                _scrollBar.Value = next;
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Enter:
                case Keys.Space:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_entries.Count == 0) return;

            int cols = ColumnsCount;
            int prev = _selectedIndex;

            switch (e.KeyCode)
            {
                case Keys.Left:
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    e.Handled = true;
                    break;
                case Keys.Right:
                    _selectedIndex = Math.Min(_entries.Count - 1, _selectedIndex + 1);
                    e.Handled = true;
                    break;
                case Keys.Up:
                    _selectedIndex = Math.Max(0, _selectedIndex - cols);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    _selectedIndex = Math.Min(_entries.Count - 1, _selectedIndex + cols);
                    e.Handled = true;
                    break;
                case Keys.Home:
                    _selectedIndex = 0;
                    e.Handled = true;
                    break;
                case Keys.End:
                    _selectedIndex = _entries.Count - 1;
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    _selectedIndex = Math.Max(0, _selectedIndex - cols * 4);
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    _selectedIndex = Math.Min(_entries.Count - 1, _selectedIndex + cols * 4);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                case Keys.Space:
                    if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
                    {
                        SymbolActivated?.Invoke(_entries[_selectedIndex]);
                    }
                    e.Handled = true;
                    break;
            }

            if (_selectedIndex != prev)
            {
                EnsureVisible(_selectedIndex);
                Invalidate();
                if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
                {
                    SymbolHovered?.Invoke(_entries[_selectedIndex]);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            int viewHeight = ClientSize.Height;
            int size = CellSize;
            int scrollY = _scrollBar.Visible ? _scrollBar.Value : 0;
            int cols = ColumnsCount;

            int startRow = Math.Max(0, scrollY / size);
            int endRow = Math.Min(RowsCount, (scrollY + viewHeight) / size + 1);

            int startIndex = startRow * cols;
            int endIndex = Math.Min(_entries.Count, endRow * cols);

            using var hoverBrush = new SolidBrush(_palette.ControlHover);
            using var selectedBrush = new SolidBrush(_palette.SurfaceSelected);
            using var flashBrush = new SolidBrush(_palette.Accent);
            using var borderPen = new Pen(_palette.Border);
            using var focusPen = new Pen(_palette.Accent, 2);

            for (int i = startIndex; i < endIndex; i++)
            {
                Rectangle rect = CellRectangle(i);
                if (rect.Bottom < 0 || rect.Top > viewHeight) continue;

                SymbolEntry entry = _entries[i];
                bool isFlash = (i == _flashIndex);
                bool isSelected = (i == _selectedIndex);
                bool isHover = (i == _hoverIndex);

                // Cell background
                if (isFlash)
                {
                    g.FillRectangle(flashBrush, rect);
                }
                else if (isSelected)
                {
                    g.FillRectangle(selectedBrush, rect);
                }
                else if (isHover)
                {
                    g.FillRectangle(hoverBrush, rect);
                }

                // Cell border
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

                // Focused cell outline
                if (isSelected && Focused)
                {
                    g.DrawRectangle(focusPen, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
                }

                // Drawing character
                Color textColor = isFlash ? _palette.AccentInk : _palette.TextPrimary;

                if (!string.IsNullOrEmpty(entry.Label))
                {
                    // Invisible character: draw label
                    Color labelColor = isFlash ? _palette.AccentInk : _palette.TextMuted;
                    TextRenderer.DrawText(
                        g,
                        entry.Label,
                        _labelFont ?? Font,
                        rect,
                        labelColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else if (entry.IsCombining)
                {
                    // Combining mark: draw dotted circle + mark
                    string display = "\u25CC" + entry.Text;
                    TextRenderer.DrawText(
                        g,
                        display,
                        _symbolFont ?? Font,
                        rect,
                        textColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else
                {
                    TextRenderer.DrawText(
                        g,
                        entry.Text,
                        _symbolFont ?? Font,
                        rect,
                        textColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
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

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new SymbolGridAccessibleObject(this);
        }

        private sealed class SymbolGridAccessibleObject : ControlAccessibleObject
        {
            private readonly SymbolGrid _owner;

            public SymbolGridAccessibleObject(SymbolGrid owner) : base(owner)
            {
                _owner = owner;
            }

            public override AccessibleRole Role => AccessibleRole.Table;

            public override int GetChildCount() => _owner._entries.Count;

            public override AccessibleObject? GetChild(int index)
            {
                if (index >= 0 && index < _owner._entries.Count)
                {
                    return new SymbolCellAccessibleObject(_owner, index);
                }
                return null;
            }
        }

        private sealed class SymbolCellAccessibleObject : AccessibleObject
        {
            private readonly SymbolGrid _owner;
            private readonly int _index;

            public SymbolCellAccessibleObject(SymbolGrid owner, int index)
            {
                _owner = owner;
                _index = index;
            }

            public override AccessibleRole Role => AccessibleRole.Cell;

            public override string Name
            {
                get
                {
                    if (_index >= 0 && _index < _owner._entries.Count)
                    {
                        return _owner._entries[_index].LocalizedName(_owner._uiLanguage);
                    }
                    return "";
                }
            }

            public override Rectangle Bounds
            {
                get
                {
                    Rectangle r = _owner.CellRectangle(_index);
                    return _owner.RectangleToScreen(r);
                }
            }

            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates states = AccessibleStates.Selectable;
                    if (_index == _owner._selectedIndex)
                    {
                        states |= AccessibleStates.Selected;
                        if (_owner.Focused) states |= AccessibleStates.Focused;
                    }
                    return states;
                }
            }

            public override AccessibleObject Parent => _owner.AccessibilityObject;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flashTimer.Dispose();
                _symbolFont?.Dispose();
                _labelFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
