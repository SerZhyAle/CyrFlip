using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// SETTINGS-NAVIGATION section 2 rule 6: where the page menu lives. Pure, so the breakpoint is a
    /// documented decision (PLAN/S0076_unified-ui-contracts/unified-ui.md) that a test can hold.
    /// </summary>
    internal enum SettingsRailMode
    {
        /// <summary>The rail stays visible beside the content pane while pages change.</summary>
        Docked,
        /// <summary>The same rail, opened from a button over the content pane and closed on a choice.</summary>
        Drawer,
    }

    internal static class SettingsRailLayout
    {
        /// <summary>The narrowest content pane worth showing beside a docked rail, in interface-scaled logical px.</summary>
        public const int MinContentLogical = 480;

        /// <summary>
        /// The breakpoint: the rail docks while the content pane keeps at least <paramref name="minContent"/>
        /// px beside it, otherwise the same menu moves into a drawer. Neither pane is squeezed and no name is clipped.
        /// </summary>
        public static SettingsRailMode Decide(int clientWidth, int railWidth, int minContent)
            => clientWidth - railWidth >= minContent ? SettingsRailMode.Docked : SettingsRailMode.Drawer;

        /// <summary>
        /// The rail's width: wide enough for its longest caption on one line, never more than
        /// <paramref name="maxWidth"/> (a caption beyond that wraps and the row grows) and never less than <paramref name="minWidth"/>.
        /// </summary>
        public static int RailWidth(int widestCaptionRow, int minWidth, int maxWidth)
            => Math.Max(minWidth, Math.Min(widestCaptionRow, Math.Max(minWidth, maxWidth)));
    }

    /// <summary>
    /// The settings window's page menu (SETTINGS-NAVIGATION section 2): a vertical list at the reading start,
    /// one row per destination with its vocabulary glyph and a short caption that wraps whole (never an
    /// ellipsis), selected by an accent bar and a bold caption (never colour alone). It replaces the native
    /// vertical tab strip, which cannot scroll - once the rows no longer fit it wrapped into a second column -
    /// and clipped captions with an ellipsis.
    ///
    /// <para><b>One list, one selection.</b> The rail does not own pages; <see cref="SettingsForm"/> keeps the
    /// <c>TabControl</c> as the page host and mirrors its selection here, so every existing page, search,
    /// viewport and key lookup is unchanged. A destination is chosen by a click released on the row that
    /// took the press (a scroll gesture therefore never activates one) or by the keyboard.</para>
    ///
    /// <para><b>Sizes come from <see cref="UiMetrics"/></b>: row target, glyph, gaps and padding are the
    /// profile's tokens at the control's DPI, so the rail grows with the interface scale and the text
    /// enlargement together and the cached row heights are released on a change
    /// (<see cref="InvalidateMetrics"/>, UI-SIZING 1.1). The row height is measured with the bold caption at
    /// the final width, so selecting a row never moves another.</para>
    ///
    /// <para><b>Scrolling is native</b> (<see cref="ScrollableControl.AutoScroll"/>): every destination stays
    /// reachable at any scale, and the selected row is scrolled into view by every selection.</para>
    /// </summary>
    internal sealed class SettingsRail : ScrollableControl, IThemeAware
    {
        internal sealed class Entry
        {
            public string Caption = "";
            public string? GlyphId;
            public int Kind;
            public object? Tag;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int[] _tops = Array.Empty<int>();
        private int[] _heights = Array.Empty<int>();
        private int _total;
        private int _layoutWidth = -1;
        private bool _dirty = true;
        private int _selected = -1;
        private int _hot = -1;
        private int _pressed = -1;
        private ThemePalette _palette = ThemePalette.Light;
        private Font? _bold;

        public SettingsRail()
        {
            AutoScroll = true;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Name = "settingsRail";
        }

        /// <summary>The selected destination changed - by the user or by <see cref="SelectedIndex"/>.</summary>
        public event EventHandler? SelectedIndexChanged;

        /// <summary>The user chose a destination (a click or Enter/Space), even the one already selected: the drawer closes on it.</summary>
        public event EventHandler? DestinationChosen;

        public int Count => _entries.Count;

        /// <summary>The <see cref="System.Diagnostics.Stopwatch"/> stamp of the latest input that chose a destination - the start of the page-change timing.</summary>
        internal long InputStamp { get; private set; }

        public int SelectedIndex
        {
            get => _selected;
            set => SelectIndex(value, false);
        }

        public string CaptionAt(int index) => index >= 0 && index < _entries.Count ? _entries[index].Caption : "";

        /// <summary>Replace the destinations (language or glyph change); the selection stays on its index.</summary>
        public void SetEntries(IReadOnlyList<Entry> entries)
        {
            _entries.Clear();
            _entries.AddRange(entries);
            if (_selected >= _entries.Count) _selected = _entries.Count - 1;
            _hot = -1;
            InvalidateMetrics();
        }

        /// <summary>The profile, the DPI, the font or the width changed: the cached row heights are released and measured again.</summary>
        public void InvalidateMetrics()
        {
            _dirty = true;
            if (IsHandleCreated) { Relayout(); Invalidate(); }
        }

        /// <summary>
        /// The width the rail wants for its longest caption on a single line (bold, as drawn when selected), including
        /// the start padding, the glyph, its gap and the end padding. The window clamps it with
        /// <see cref="SettingsRailLayout.RailWidth"/>.
        /// </summary>
        public int WidestRow()
        {
            int widest = 0;
            Font bold = BoldFont;
            foreach (Entry entry in _entries)
                widest = Math.Max(widest, TextRenderer.MeasureText(entry.Caption, bold, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width);
            return widest + TextInset(ForMeasure.Of(this)) + EndPad(ForMeasure.Of(this));
        }

        /// <summary>The tokens a row is built from, resolved once per pass so a measure and a paint cannot disagree.</summary>
        private readonly struct ForMeasure
        {
            public readonly int Target, Glyph, Gap, StartPad, EndPad, Bar, VerticalPad;
            private ForMeasure(SettingsRail rail)
            {
                UiMetrics m = UiMetrics.For(rail);
                Target = m.Device(rail, m.Target);
                Glyph = m.Device(rail, m.Glyph);
                Gap = m.Device(rail, m.Gap);
                StartPad = m.Device(rail, m.Logical(12));
                EndPad = m.Device(rail, m.Logical(8));
                Bar = m.Device(rail, m.Logical(4));
                VerticalPad = m.Device(rail, m.Logical(6));
            }
            public static ForMeasure Of(SettingsRail rail) => new ForMeasure(rail);
        }

        private static int TextInset(ForMeasure t) => t.StartPad + t.Glyph + t.Gap;
        private static int EndPad(ForMeasure t) => t.EndPad;

        private Font BoldFont => _bold ??= new Font(Font, FontStyle.Bold);

        protected override void OnFontChanged(EventArgs e)
        {
            Font? old = _bold;
            _bold = null;
            old?.Dispose();
            base.OnFontChanged(e);
            InvalidateMetrics();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            InvalidateMetrics();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            if (ClientSize.Width != _layoutWidth) { _dirty = true; if (IsHandleCreated) Relayout(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Relayout();
        }

        /// <summary>Row tops and heights at the current width; sets the scroll extent. One pass per change, never per input event.</summary>
        private void Relayout()
        {
            int width = ClientSize.Width;
            if (width <= 0) return;
            if (!_dirty && width == _layoutWidth) return;
            Measure(width);
            // The vertical bar takes room from the text: when the rows only overflow at this width, measure again narrower.
            AutoScrollMinSize = new Size(0, _total);
            if (ClientSize.Width != width && ClientSize.Width > 0) Measure(ClientSize.Width);
            _dirty = false;
            _layoutWidth = ClientSize.Width;
        }

        private void Measure(int width)
        {
            ForMeasure t = ForMeasure.Of(this);
            var tops = new int[_entries.Count];
            var heights = new int[_entries.Count];
            int textWidth = Math.Max(1, width - TextInset(t) - t.EndPad);
            Font bold = BoldFont;
            int y = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Size text = TextRenderer.MeasureText(_entries[i].Caption, bold, new Size(textWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                int height = Math.Max(t.Target, text.Height + 2 * t.VerticalPad);
                tops[i] = y;
                heights[i] = height;
                y += height;
            }
            _tops = tops;
            _heights = heights;
            _total = y;
        }

        /// <summary>The row rectangle in control (client) coordinates, scrolled.</summary>
        internal Rectangle RowBounds(int index)
        {
            Relayout();
            if (index < 0 || index >= _entries.Count) return Rectangle.Empty;
            return new Rectangle(0, _tops[index] + AutoScrollPosition.Y, ClientSize.Width, _heights[index]);
        }

        /// <summary>The row under a client point, or -1.</summary>
        internal int IndexAt(Point location)
        {
            Relayout();
            for (int i = 0; i < _entries.Count; i++)
                if (RowBounds(i).Contains(location)) return i;
            return -1;
        }

        private void SelectIndex(int index, bool user)
        {
            if (index < 0 || index >= _entries.Count) return;
            bool changed = index != _selected;
            if (changed)
            {
                int old = _selected;
                _selected = index;
                InvalidateRow(old);
                InvalidateRow(index);
            }
            EnsureVisible(index);
            if (changed)
            {
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                if (IsHandleCreated)
                {
                    AccessibilityNotifyClients(AccessibleEvents.SelectionAdd, index);
                    AccessibilityNotifyClients(AccessibleEvents.Focus, index);
                }
            }
            if (user) DestinationChosen?.Invoke(this, EventArgs.Empty);
        }

        private void InvalidateRow(int index)
        {
            if (index >= 0 && index < _entries.Count && IsHandleCreated) Invalidate(RowBounds(index));
        }

        /// <summary>Scroll so the whole row is in view; never scrolls a row that already is.</summary>
        public void EnsureVisible(int index)
        {
            Relayout();
            if (index < 0 || index >= _entries.Count || ClientSize.Height <= 0) return;
            int offset = -AutoScrollPosition.Y;
            int top = _tops[index], bottom = top + _heights[index];
            int wanted = offset;
            if (top < offset) wanted = top;
            else if (bottom > offset + ClientSize.Height) wanted = bottom - ClientSize.Height;
            if (wanted != offset) AutoScrollPosition = new Point(0, Math.Max(0, wanted));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index == _hot) return;
            int old = _hot;
            _hot = index;
            InvalidateRow(old);
            InvalidateRow(index);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot < 0) return;
            int old = _hot;
            _hot = -1;
            InvalidateRow(old);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = e.Button == MouseButtons.Left ? IndexAt(e.Location) : -1;
            if (!Focused) Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int pressed = _pressed;
            _pressed = -1;
            // Press and release on the same row: a drag that left it (a scroll gesture) chooses nothing.
            if (e.Button == MouseButtons.Left && pressed >= 0 && IndexAt(e.Location) == pressed)
            {
                InputStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                SelectIndex(pressed, true);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _pressed = -1;
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            InputStamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int target = _selected < 0 ? 0 : _selected;
            switch (e.KeyCode)
            {
                case Keys.Up: target = Math.Max(0, target - 1); break;
                case Keys.Down: target = Math.Min(_entries.Count - 1, target + 1); break;
                case Keys.Home: target = 0; break;
                case Keys.End: target = _entries.Count - 1; break;
                case Keys.PageUp: target = Math.Max(0, target - VisibleRows()); break;
                case Keys.PageDown: target = Math.Min(_entries.Count - 1, target + VisibleRows()); break;
                case Keys.Enter: case Keys.Space:
                    e.Handled = e.SuppressKeyPress = true;
                    DestinationChosen?.Invoke(this, EventArgs.Empty);
                    return;
                default:
                    base.OnKeyDown(e);
                    return;
            }
            e.Handled = e.SuppressKeyPress = true;
            SelectIndex(target, false);
        }

        private int VisibleRows()
        {
            Relayout();
            int rows = 0, offset = -AutoScrollPosition.Y;
            for (int i = 0; i < _entries.Count; i++)
                if (_tops[i] >= offset && _tops[i] + _heights[i] <= offset + ClientSize.Height) rows++;
            return Math.Max(1, rows - 1);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); InvalidateRow(_selected); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); InvalidateRow(_selected); }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Relayout();
            ThemePalette palette = ThemeApply.PaletteOf(this) ?? _palette;
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(palette.SurfaceWindow)) g.FillRectangle(back, e.ClipRectangle);
            ForMeasure t = ForMeasure.Of(this);
            bool rtl = RightToLeft == RightToLeft.Yes;
            for (int i = 0; i < _entries.Count; i++)
            {
                Rectangle row = RowBounds(i);
                if (row.Bottom < e.ClipRectangle.Top) continue;
                if (row.Top > e.ClipRectangle.Bottom) break;
                DrawRow(g, i, row, t, palette, rtl);
            }
        }

        /// <summary>How far a selected and a hovered row lean from the window surface toward the accent.</summary>
        internal const float SelectedFillAmount = 0.18f, HotFillAmount = 0.14f;

        /// <summary>A row's fill - the surface its caption and glyph are measured on (<c>ThemePaletteTests</c>,
        /// <c>SettingsRailTests</c>). High contrast never tints: Highlight for the selection, else the window.</summary>
        internal static Color RowFill(ThemePalette palette, bool selected, bool hot)
            => palette.Kind == ThemeKind.HighContrast ? (selected ? palette.SurfaceSelected : palette.SurfaceWindow)
                : selected ? ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, SelectedFillAmount)
                : hot ? ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, HotFillAmount)
                : palette.SurfaceWindow;

        private void DrawRow(Graphics g, int index, Rectangle row, ForMeasure t, ThemePalette palette, bool rtl)
        {
            Entry entry = _entries[index];
            bool selected = index == _selected, hot = index == _hot;
            // The fills are mixes of two roles, re-mixed on every paint from the palette in force (APP-STYLE section 3).
            // High contrast shows the selection in the user's own system pair, Highlight under HighlightText, never a
            // tint (WINDOWS-UI section 6 rule 5, S0060 T-M2 d); hover there keeps the window fill and its underline.
            bool contrast = palette.Kind == ThemeKind.HighContrast;
            Color back = RowFill(palette, selected, hot);
            Color ink = contrast && selected ? palette.AccentInk : palette.TextPrimary;
            using (var fill = new SolidBrush(back)) g.FillRectangle(fill, row);
            // Selection is shown by the bar and the bold caption as well as the fill - never by colour alone.
            if (selected)
                using (var accent = new SolidBrush(contrast ? palette.AccentInk : palette.Accent))
                    g.FillRectangle(accent, rtl ? row.Right - t.Bar : row.Left, row.Top, t.Bar, row.Height);

            int glyphX = rtl ? row.Right - t.StartPad - t.Glyph : row.Left + t.StartPad;
            if (entry.GlyphId != null)
            {
                var box = new Rectangle(glyphX, row.Top + (row.Height - t.Glyph) / 2, t.Glyph, t.Glyph);
                GlyphRenderer.Draw(g, entry.GlyphId, box, contrast ? ink : ThemePalette.NavInk.For(entry.Kind, palette), rtl);
            }
            int textLeft = rtl ? row.Left + t.EndPad : row.Left + TextInset(t);
            int textRight = rtl ? row.Right - TextInset(t) : row.Right - t.EndPad;
            var textBox = Rectangle.FromLTRB(textLeft, row.Top, Math.Max(textLeft + 1, textRight), row.Bottom);
            TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
                | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding
                | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left);
            // Bold always: the row was measured bold, so choosing it changes no other row.
            TextRenderer.DrawText(g, entry.Caption, selected ? BoldFont : Font, textBox, ink, flags);
            if (hot && !selected)
                using (var underline = new Pen(palette.TextPrimary))
                {
                    Size line = TextRenderer.MeasureText(g, entry.Caption, Font, textBox.Size, flags);
                    int y = Math.Min(row.Bottom - 2, row.Top + (row.Height + line.Height) / 2);
                    g.DrawLine(underline, textBox.Left, y, Math.Min(textBox.Right, textBox.Left + line.Width), y);
                }
            // The keyboard focus frame (INPUT-PARITY rule 2): the selected row while the list holds the focus.
            if (selected && Focused && ShowFocusCues)
                using (var focus = new Pen(ink) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(focus, row.Left + t.Bar + 1, row.Top + 1, row.Width - t.Bar - 3, row.Height - 3);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new RailAccessibleObject(this);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _bold?.Dispose(); _bold = null; }
            base.Dispose(disposing);
        }

        /// <summary>
        /// The destinations as a <c>List</c> whose children are <c>ListItem</c>s named by their caption, each
        /// reporting whether it is the selected one (SETTINGS-NAVIGATION section 2 rule 7). Selecting an item
        /// through accessibility chooses a destination only - it never edits a value.
        /// </summary>
        internal sealed class RailAccessibleObject : Control.ControlAccessibleObject
        {
            public RailAccessibleObject(SettingsRail owner) : base(owner) { }
            private SettingsRail? Rail => Owner as SettingsRail;
            public override AccessibleRole Role => AccessibleRole.List;
            public override int GetChildCount() => Rail?._entries.Count ?? 0;
            public override AccessibleObject? GetChild(int index)
                => Rail != null && index >= 0 && index < Rail._entries.Count ? new RailItemAccessibleObject(Rail, index) : null;
            public override AccessibleObject? GetSelected() => Rail == null || Rail._selected < 0 ? null : GetChild(Rail._selected);
            public override AccessibleObject? GetFocused() => Rail != null && Rail.Focused ? GetSelected() : null;
            public override AccessibleObject? HitTest(int x, int y)
            {
                SettingsRail? rail = Rail;
                if (rail == null) return null;
                int index = rail.IndexAt(rail.PointToClient(new Point(x, y)));
                return index < 0 ? null : GetChild(index);
            }
        }

        internal sealed class RailItemAccessibleObject : AccessibleObject
        {
            private readonly SettingsRail _rail;
            private readonly int _index;

            public RailItemAccessibleObject(SettingsRail rail, int index) { _rail = rail; _index = index; }

            public override string? Name => _rail.CaptionAt(_index);
            public override AccessibleRole Role => AccessibleRole.ListItem;
            public override AccessibleObject? Parent => _rail.AccessibilityObject;
            public override string? DefaultAction => "Select";
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    Rectangle row = _rail.RowBounds(_index);
                    if (row.Bottom <= 0 || row.Top >= _rail.ClientSize.Height) state |= AccessibleStates.Offscreen;
                    if (_rail._selected == _index) state |= AccessibleStates.Selected | (_rail.Focused ? AccessibleStates.Focused : 0);
                    return state;
                }
            }
            public override Rectangle Bounds => _rail.RectangleToScreen(Rectangle.Intersect(_rail.RowBounds(_index), _rail.ClientRectangle));
            public override void DoDefaultAction()
            {
                _rail.InputStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _rail.SelectIndex(_index, true);
            }
            public override void Select(AccessibleSelection flags)
            {
                if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.TakeFocus)) == 0) return;
                _rail.InputStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _rail.SelectIndex(_index, false);
            }
        }
    }
}
