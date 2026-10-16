using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>What the board asks of whoever owns the feature - callbacks, so the window knows no scheduler.</summary>
    internal sealed class TrafficLightBoardActions
    {
        public Func<List<TrafficLightTile>> Snapshot = () => new List<TrafficLightTile>();
        public Func<TrafficLightSummary> Summary = () => new TrafficLightSummary();
        public Action<string> CheckNow = _ => { };
        public Action CheckAll = () => { };
        public Action AddTarget = () => { };
        public Action EditTargets = () => { };
        /// <summary>The user toggled "pin on top" from the board's menu.</summary>
        public Action<bool> PinnedChanged = _ => { };
        /// <summary>The window went away by the user's hand (the close box, the menu's Hide, the chord).</summary>
        public Action Hidden = () => { };
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
    }

    /// <summary>
    /// The owner-drawn tile board (ticket S0069 section 6): one small rounded rectangle per target - a mark,
    /// the label, a second line of detail - grouped under muted captions. Colours are <see cref="ThemePalette"/>
    /// roles only; the <b>state is never colour alone</b> - each tile carries its mark and, for a screen
    /// reader, its state word. Hover (and, from the keyboard, focus) shows the full information as a native
    /// tooltip. Like the symbols grid it answers <c>WM_MOUSEACTIVATE</c> with <c>MA_NOACTIVATE</c>: a click on
    /// the board never takes the focus from the user's window.
    /// </summary>
    internal sealed class TrafficLightBoard : ScrollableControl, IThemeAware
    {
        /// <summary>
        /// Tile edge in units of the font height, per size step (small, medium, large). The small step is a
        /// square: it carries the state's glyph and nothing else, the name is on hover.
        /// </summary>
        private static readonly float[] HeightUnits = { 2.1f, 2.5f, 3.3f };
        private static readonly float[] WidthUnits = { 2.1f, 9f, 12f };

        private ThemePalette _palette = ThemePalette.Light;
        private List<TrafficLightTile> _tiles = new List<TrafficLightTile>();
        private List<BoardItem> _items = new List<BoardItem>();
        private int _contentHeight;
        private int _layoutWidth = -1;
        private int _hover = -1;
        private int _focus = -1;
        private int _sizeStep = 1;
        private int _columns = AppConfig.DefaultTrafficLightColumns;
        private Func<string, string> _t = s => s;
        private Func<DateTime> _utcNow = () => DateTime.UtcNow;
        private Font? _markFont;
        private Font? _detailFont;
        private readonly ToolTip _tip = new ToolTip { ShowAlways = true, InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int _tipTile = -1;
        private Point? _dragOrigin;

        public TrafficLightBoard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            DoubleBuffered = true;
            TabStop = true;
            AutoScroll = true;
            ApplyTheme(ThemePalette.Light);
        }

        /// <summary>A tile was activated (double click, Enter): check that target now.</summary>
        public event Action<int>? TileActivated;
        /// <summary>The context menu was asked for - tile index or -1 - at a client point.</summary>
        public event Action<int, Point>? ContextMenuRequested;
        public event Action? CheckAllRequested;
        /// <summary>Esc: return the focus without hiding.</summary>
        public event Action? ReturnFocusRequested;
        /// <summary>A left press on empty space: the window may be dragged by it.</summary>
        public event Action? DragRequested;
        public event Action? SettingsRequested;

        public IReadOnlyList<TrafficLightTile> Tiles => _tiles;
        public int FocusedTile => _focus;

        // ---- state in ----

        public void SetTranslator(Func<string, string> translate, Func<DateTime> utcNow)
        {
            _t = translate;
            _utcNow = utcNow;
            AccessibleName = _t("Availability traffic light");
            Invalidate();
        }

        public void SetSizeStep(int step)
        {
            _sizeStep = Math.Max(0, Math.Min(AppConfig.MicroscopicTrafficLightTileSize, step));
            _dragOrigin = null;
            Relayout(force: true);
        }

        /// <summary>The small step: a bare square, no label and no detail (the tooltip says which is which).</summary>
        public bool IsBareSquare => _sizeStep == 0 || IsMicroscopic;
        public bool IsMicroscopic => _sizeStep == AppConfig.MicroscopicTrafficLightTileSize;

        /// <summary>Tiles in a row; the rows follow from the number of targets.</summary>
        public void SetColumns(int columns)
        {
            _columns = Math.Max(AppConfig.MinTrafficLightColumns, Math.Min(AppConfig.MaxTrafficLightColumns, columns));
            Relayout(force: true);
        }

        /// <summary>The columns in use: the chosen number, never more than the widest group has tiles.</summary>
        public int EffectiveColumns => TrafficLightBoardLayout.EffectiveColumns(_tiles, _columns);

        public void SetTiles(List<TrafficLightTile> tiles)
        {
            string? focusedId = _focus >= 0 && _focus < _tiles.Count ? _tiles[_focus].Target.Id : null;
            _tiles = tiles;
            Relayout(force: true);
            _focus = focusedId == null ? -1 : _tiles.FindIndex(t => t.Target.Id == focusedId);
            if (_focus < 0 && _tiles.Count > 0) _focus = 0;
            if (_hover >= _tiles.Count) _hover = -1;
            Invalidate();
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.SurfaceWindow;
            ForeColor = palette.TextPrimary;
            Invalidate();
        }

        /// <summary>
        /// The tiles' cells in the board's client coordinates, scrolled as they are painted - what the
        /// window keeps at full alpha while the plate around them fades (window transparency).
        /// </summary>
        internal List<Rectangle> TileBounds()
        {
            var bounds = new List<Rectangle>(_items.Count);
            Point offset = AutoScrollPosition;
            foreach (BoardItem item in _items)
                if (item.IsTile)
                {
                    Rectangle r = item.Bounds;
                    r.Offset(offset);
                    bounds.Add(r);
                }
            return bounds;
        }

        // ---- layout ----

        /// <summary>The tile's size at the current font and step: measured, never a pixel constant.</summary>
        public Size TileSize
        {
            get
            {
                int unit = Font.Height;
                if (IsMicroscopic)
                {
                    int side = Math.Max(1, (int)Math.Round(unit * WidthUnits[0]) / 2);
                    return new Size(side, side);
                }
                return new Size((int)Math.Round(unit * WidthUnits[_sizeStep]), (int)Math.Round(unit * HeightUnits[_sizeStep]));
            }
        }

        public int Gap => IsMicroscopic ? Math.Max(2, Font.Height / 6) : Math.Max(4, Font.Height / 3);
        private int CaptionHeight => IsMicroscopic ? 0 : Font.Height + 4;

        /// <summary>The narrowest the board may be: one tile and its gaps.</summary>
        public int MinimumContentWidth => TileSize.Width + 2 * Gap;

        /// <summary>The width of the grid as it is now: the columns in use, each a tile wide, and the gaps.</summary>
        public int GridWidth => TrafficLightBoardLayout.GridWidth(EffectiveColumns, TileSize.Width, Gap);

        /// <summary>The height the grid needs at a width, for the window's fit to its content.</summary>
        public int ContentHeightFor(int width)
        {
            TrafficLightBoardLayout.Compute(_tiles, width, EffectiveColumns, TileSize, Gap, CaptionHeight,
                RightToLeft == RightToLeft.Yes, out int height);
            return _tiles.Count == 0 ? Math.Max(height, EmptyMessageHeight + 2 * Gap) : height;
        }

        private int EmptyMessageHeight => Font.Height * 3;

        private void Relayout(bool force)
        {
            int width = Math.Max(1, ClientSize.Width);
            if (!force && width == _layoutWidth) return;
            _layoutWidth = width;
            UpdateFonts();
            _items = TrafficLightBoardLayout.Compute(_tiles, width, EffectiveColumns, TileSize, Gap, CaptionHeight,
                RightToLeft == RightToLeft.Yes, out _contentHeight);
            AutoScrollMinSize = new Size(0, _contentHeight);
            Invalidate();
        }

        private void UpdateFonts()
        {
            float baseSize = Font.SizeInPoints > 0 ? Font.SizeInPoints : 9f;
            if (_markFont == null || Math.Abs(_markFont.SizeInPoints - (baseSize + 3f)) > 0.01f)
            {
                _markFont?.Dispose();
                try { _markFont = new Font("Segoe UI Symbol", baseSize + 3f, FontStyle.Bold); }
                catch { _markFont = new Font(Font.FontFamily, baseSize + 3f, FontStyle.Bold); }
            }
            if (_detailFont == null || Math.Abs(_detailFont.SizeInPoints - Math.Max(7f, baseSize - 1f)) > 0.01f)
            {
                _detailFont?.Dispose();
                _detailFont = new Font(Font.FontFamily, Math.Max(7f, baseSize - 1f), FontStyle.Regular);
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Relayout(force: true);
        }

        protected override void OnRightToLeftChanged(EventArgs e)
        {
            base.OnRightToLeftChanged(e);
            Relayout(force: true);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            Relayout(force: false);
        }

        // ---- painting ----

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_palette.SurfaceWindow);
            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, _t("Add targets on the \"Availability traffic light\" settings page."), Font,
                    new Rectangle(Gap, Gap, Math.Max(1, ClientSize.Width - 2 * Gap), Font.Height * 3), _palette.TextMuted,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : 0));
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Point offset = AutoScrollPosition;
            foreach (BoardItem item in _items)
            {
                Rectangle r = item.Bounds;
                r.Offset(offset);
                if (r.Bottom < 0 || r.Top > ClientSize.Height) continue;
                if (!item.IsTile)
                {
                    TextRenderer.DrawText(g, item.Caption, Font, r, _palette.TextMuted,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
                        | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
                    continue;
                }

                DrawTile(g, r, _tiles[item.TileIndex], item.TileIndex == _focus && Focused, item.TileIndex == _hover);
            }
        }

        private void DrawTile(Graphics g, Rectangle r, TrafficLightTile tile, bool focused, bool hovered)
        {
            bool highContrast = _palette.Kind == ThemeKind.HighContrast;
            bool muted = tile.Disabled || tile.InvalidAddress;

            Color fill = muted ? _palette.SurfaceSunken : FillOf(tile);
            Color ink = muted ? _palette.TextDisabled : _palette.StatusInk;
            if (highContrast && !muted && tile.State == TrafficState.Unknown) ink = _palette.TextDisabled;
            // Stale: the colour and the mark stay, the tile steps back (D17).
            if (tile.Stale && !muted && !highContrast)
            {
                fill = ThemePalette.Mix(fill, _palette.SurfaceWindow, 0.55f);
                ink = ThemePalette.Mix(ink, _palette.SurfaceWindow, 0.25f);
            }

            // The bare square of the small step shows no mark for "available", so a switched-off target there
            // is told from it by its edge - dashed - and not by its plate's hue alone.
            bool dashedEdge = IsBareSquare && tile.Disabled;
            int radius = Math.Max(4, r.Height / 6);
            using (GraphicsPath path = RoundedRect(r, radius))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                Color edge = highContrast ? _palette.StatusInk : dashedEdge ? ink : _palette.Border;
                using (var pen = new Pen(edge, 1f))
                {
                    if (dashedEdge) pen.DashPattern = new[] { 2f, 2f };
                    g.DrawPath(pen, path);
                }
                if (hovered && !focused)
                    using (var pen = new Pen(_palette.Accent, 1f)) g.DrawPath(pen, path);
            }
            if (focused)
            {
                Rectangle ring = Rectangle.Inflate(r, -2, -2);
                using (GraphicsPath path = RoundedRect(ring, Math.Max(2, radius - 2)))
                using (var pen = new Pen(highContrast ? _palette.StatusInk : _palette.Accent, 2f)) g.DrawPath(pen, path);
            }

            bool rtl = RightToLeft == RightToLeft.Yes;
            int pad = PadOf(r);
            Rectangle mark = MarkRect(r, rtl, centered: IsBareSquare);
            string? glyph = GlyphOf(tile);
            if (!MarkShown(tile, IsBareSquare))
            {
                // "Available" on the bare square: green says it, and a tick on green says nothing more.
            }
            else if (glyph != null && GlyphRenderer.Draw(g, glyph, mark, ink, rtl))
            {
                // The vocabulary glyph carries the state; nothing else to draw.
            }
            else
            {
                // A glyph that cannot be drawn leaves the typographic mark, so the state never rests on hue alone.
                string markText = MarkOf(tile);
                if (markText.Length > 0)
                    TextRenderer.DrawText(g, markText, _markFont ?? Font, mark, ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            // The small step is a bare square: the glyph above is all it carries, the name is on hover.
            if (IsBareSquare) return;

            int textLeft = rtl ? r.Left + pad : mark.Right + pad;
            int textRight = rtl ? mark.Left - pad : r.Right - pad;
            var text = new Rectangle(textLeft, r.Top + pad / 2, Math.Max(1, textRight - textLeft), r.Height - pad);
            TextFormatFlags align = rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
            string detail = DetailOf(tile);
            if (detail.Length == 0)
            {
                TextRenderer.DrawText(g, TrafficLightText.Caption(tile.Target), Font, text, ink,
                    align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            else
            {
                int half = text.Height / 2;
                TextRenderer.DrawText(g, TrafficLightText.Caption(tile.Target), Font,
                    new Rectangle(text.Left, text.Top, text.Width, half + 1), ink,
                    align | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, detail, _detailFont ?? Font,
                    new Rectangle(text.Left, text.Top + half, text.Width, text.Height - half), ink,
                    align | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private static int PadOf(Rectangle tile) => Math.Max(4, tile.Height / 8);

        /// <summary>
        /// The square the state's mark is drawn into: the tile's height less a pad on every side, at its
        /// leading edge - or, for the bare square of the small step, the middle of the tile.
        /// </summary>
        internal static Rectangle MarkRect(Rectangle tile, bool rightToLeft, bool centered = false)
        {
            int pad = PadOf(tile);
            int side = Math.Max(1, Math.Min(tile.Height, tile.Width) - 2 * pad);
            if (centered)
                return new Rectangle(tile.Left + (tile.Width - side) / 2, tile.Top + (tile.Height - side) / 2, side, side);
            return rightToLeft
                ? new Rectangle(tile.Right - pad - side, tile.Top + pad, side, side)
                : new Rectangle(tile.Left + pad, tile.Top + pad, side, side);
        }

        /// <summary>
        /// Whether a tile draws its state's mark at all. Everywhere but the bare square it always does. On the bare
        /// square (the small step) an available target is green and nothing else - a tick on green repeats the hue
        /// and tells a person who cannot see it nothing new - while every other state keeps its glyph, so "no mark"
        /// is itself the reading and the state still never rests on colour alone. A switched-off target is told
        /// apart by its dashed edge (<see cref="DrawTile"/>), an invalid address by its glyph.
        /// </summary>
        internal static bool MarkShown(TrafficLightTile tile, bool bareSquare)
            => !(bareSquare && !tile.Disabled && !tile.InvalidAddress && tile.State == TrafficState.Ok);

        private Color FillOf(TrafficLightTile tile)
        {
            switch (tile.State)
            {
                case TrafficState.Ok: return _palette.StatusOkFill;
                case TrafficState.Degraded: return _palette.StatusWarningFill;
                case TrafficState.Down: return _palette.StatusErrorFill;
                default: return _palette.StatusUnknownFill;
            }
        }

        /// <summary>
        /// The vocabulary glyph of a tile's state (ICON-SET): a check in a circle, a triangle, an exclamation in
        /// a circle and an empty circle are four different shapes, so the state does not rest on hue. A switched-off
        /// target has none (a muted plate and the word "off"); an address that failed validation is not a verdict on
        /// reachability but needs attention, so it takes <c>status.error</c> on the muted plate - the mark it always had.
        /// </summary>
        internal static string? GlyphOf(TrafficLightTile tile)
        {
            if (tile.Disabled) return null;
            if (tile.InvalidAddress) return AppGlyphs.StatusError;
            switch (tile.State)
            {
                case TrafficState.Ok: return AppGlyphs.StatusOk;
                case TrafficState.Degraded: return AppGlyphs.StatusWarning;
                case TrafficState.Down: return AppGlyphs.StatusError;
                default: return AppGlyphs.StatusUnknown;
            }
        }

        /// <summary>
        /// The same four marks as text - what a list row (a label cannot draw a path) and a tile whose glyph
        /// could not be drawn show beside the state word. A switched-off target has none; an invalid address
        /// shows "!".
        /// </summary>
        internal static string MarkOf(TrafficLightTile tile)
        {
            if (tile.Disabled) return "";
            if (tile.InvalidAddress) return "!";
            switch (tile.State)
            {
                case TrafficState.Ok: return "✓";
                case TrafficState.Degraded: return "▲";
                case TrafficState.Down: return "✕";
                default: return "○";
            }
        }

        private string DetailOf(TrafficLightTile tile)
        {
            if (tile.Disabled) return _t("off");
            if (tile.InvalidAddress) return _t("invalid address");
            if (tile.PausedOffline) return _t("this computer is offline");
            switch (tile.State)
            {
                case TrafficState.Ok:
                    return tile.LatencyMs.HasValue ? Localization.Format(_t, "{0} ms", tile.LatencyMs.Value) : _t("Available");
                case TrafficState.Degraded:
                case TrafficState.Down:
                    return TrafficLightText.ReasonWord(tile.Reason, _t);
                default:
                    return tile.Checking ? _t("Checking..") : _t("Not checked yet");
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---- pointer ----

        private int TileAt(Point client)
        {
            Point offset = AutoScrollPosition;
            return TrafficLightBoardLayout.HitTest(_items, new Point(client.X - offset.X, client.Y - offset.Y));
        }

        private Rectangle ClientRectOf(int tileIndex)
        {
            BoardItem? item = TrafficLightBoardLayout.Find(_items, tileIndex);
            if (item == null) return Rectangle.Empty;
            Rectangle r = item.Bounds;
            r.Offset(AutoScrollPosition);
            return r;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragOrigin is Point origin && e.Button == MouseButtons.Left)
            {
                Size threshold = SystemInformation.DragSize;
                var deadZone = new Rectangle(origin.X - threshold.Width / 2, origin.Y - threshold.Height / 2,
                    threshold.Width, threshold.Height);
                if (!deadZone.Contains(e.Location))
                {
                    _dragOrigin = null;
                    Capture = false;
                    _hover = -1;
                    Invalidate();
                    SetHoverTip(-1);
                    DragRequested?.Invoke();
                    return;
                }
            }
            int hit = TileAt(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Invalidate();
            SetHoverTip(hit);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
            HideTip();
            SetHoverTip(-1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = TileAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (hit >= 0) _focus = hit;
                Invalidate();
                ContextMenuRequested?.Invoke(hit, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (IsMicroscopic)
            {
                _dragOrigin = e.Location;
                Capture = true;
            }
            if (hit >= 0) { _focus = hit; Invalidate(); }
            else if (!IsMicroscopic) DragRequested?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragOrigin = null;
                Capture = false;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) _dragOrigin = null;
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int hit = TileAt(e.Location);
            if (e.Button != MouseButtons.Left) return;
            if (IsMicroscopic) SettingsRequested?.Invoke();
            else if (hit >= 0) TileActivated?.Invoke(hit);
        }

        // ---- the tooltip: the same text on hover and beside the focused tile (D16) ----

        internal string TooltipFor(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= _tiles.Count) return "";
            return TrafficLightText.Tooltip(_tiles[tileIndex], _t, _utcNow(), TimeZoneInfo.Local);
        }

        /// <summary>
        /// The hover tip: the control's own tool, its text following the tile under the pointer. Not
        /// <see cref="ToolTip.Show(string, IWin32Window, int, int, int)"/> - that one shows only while the window
        /// is the active one, and this board is never activated by the mouse (<c>MA_NOACTIVATE</c>), so it showed
        /// nothing until the chord had put the focus on it. <c>ShowAlways</c> keeps this tool on an inactive window.
        /// </summary>
        private void SetHoverTip(int tileIndex)
        {
            try
            {
                HideTip();
                _tip.Active = false;        // pops a tip still showing the previous tile
                _tip.SetToolTip(this, tileIndex >= 0 ? TooltipFor(tileIndex) : "");
                _tip.Active = true;
            }
            catch (Exception ex) { TrafficLightLog.Fault("tooltip-hover", ex); }
        }

        /// <summary>What the hover tip currently holds - the text of the tile under the pointer, empty off the tiles.</summary>
        internal string HoverTipText => _tip.GetToolTip(this);

        private void ShowTip(int tileIndex, bool below)
        {
            Rectangle r = ClientRectOf(tileIndex);
            if (r.IsEmpty) return;
            _tipTile = tileIndex;
            try { _tip.Show(TooltipFor(tileIndex), this, r.Left, r.Bottom + 2, 15000); }
            catch (Exception ex) { TrafficLightLog.Fault("tooltip-show", ex); }
        }

        private void HideTip()
        {
            if (_tipTile < 0) return;
            _tipTile = -1;
            try { _tip.Hide(this); } catch (Exception ex) { TrafficLightLog.Fault("tooltip-hide", ex); }
        }

        // ---- keyboard ----

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.Enter: case Keys.Escape: case Keys.F5:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                {
                    // In a right-to-left board the horizontal arrows follow the mirrored layout.
                    Keys key = e.KeyCode;
                    if (RightToLeft == RightToLeft.Yes && (key == Keys.Left || key == Keys.Right))
                        key = key == Keys.Left ? Keys.Right : Keys.Left;
                    MoveFocus(TrafficLightBoardLayout.Navigate(_items, _focus, key));
                    e.Handled = true;
                    break;
                }
                case Keys.Enter:
                    if (_focus >= 0) TileActivated?.Invoke(_focus);
                    e.Handled = true;
                    break;
                case Keys.F5:
                    CheckAllRequested?.Invoke();
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    ReturnFocusRequested?.Invoke();
                    e.Handled = true;
                    break;
                case Keys.Apps:
                    OpenMenuFromKeyboard();
                    e.Handled = true;
                    break;
                case Keys.F10 when e.Shift:
                    OpenMenuFromKeyboard();
                    e.Handled = true;
                    break;
            }
        }

        private void OpenMenuFromKeyboard()
        {
            Rectangle r = _focus >= 0 ? ClientRectOf(_focus) : Rectangle.Empty;
            Point at = r.IsEmpty ? new Point(Gap, Gap) : new Point(r.Left + r.Width / 2, r.Bottom);
            ContextMenuRequested?.Invoke(_focus, at);
        }

        private void MoveFocus(int index)
        {
            if (index < 0 || index == _focus) return;
            _focus = index;
            Rectangle r = ClientRectOf(index);
            if (!r.IsEmpty)
            {
                // Scroll the tile into view.
                if (r.Top < 0 || r.Bottom > ClientSize.Height)
                {
                    BoardItem? item = TrafficLightBoardLayout.Find(_items, index);
                    if (item != null)
                    {
                        int wanted = r.Top < 0 ? item.Bounds.Top - Gap : item.Bounds.Bottom - ClientSize.Height + Gap;
                        AutoScrollPosition = new Point(0, Math.Max(0, wanted));
                    }
                }
            }
            Invalidate();
            ShowTip(index, below: true);
            AccessibilityNotifyClients(AccessibleEvents.Focus, index);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (_focus < 0 && _tiles.Count > 0) _focus = 0;
            Invalidate();
            if (_focus >= 0) ShowTip(_focus, below: true);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            HideTip();
            Invalidate();
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

        // ---- accessibility: one child per tile ----

        protected override AccessibleObject CreateAccessibilityInstance() => new BoardAccessibleObject(this);

        private sealed class BoardAccessibleObject : ControlAccessibleObject
        {
            private readonly TrafficLightBoard _owner;
            public BoardAccessibleObject(TrafficLightBoard owner) : base(owner) { _owner = owner; }
            public override AccessibleRole Role => AccessibleRole.List;
            public override int GetChildCount() => _owner._tiles.Count;
            public override AccessibleObject? GetChild(int index)
                => index >= 0 && index < _owner._tiles.Count ? new TileAccessibleObject(_owner, index) : null;
        }

        private sealed class TileAccessibleObject : AccessibleObject
        {
            private readonly TrafficLightBoard _owner;
            private readonly int _index;
            public TileAccessibleObject(TrafficLightBoard owner, int index) { _owner = owner; _index = index; }

            private TrafficLightTile? Tile => _index < _owner._tiles.Count ? _owner._tiles[_index] : null;
            public override AccessibleRole Role => AccessibleRole.ListItem;
            public override string Name => Tile == null ? "" : TrafficLightText.Caption(Tile.Target);
            public override string Value => Tile == null ? "" : TrafficLightText.AccessibleValue(Tile, _owner._t);
            public override string Description => _owner.TooltipFor(_index).Replace("\r\n", "; ");
            public override Rectangle Bounds => _owner.RectangleToScreen(_owner.ClientRectOf(_index));
            public override AccessibleObject Parent => _owner.AccessibilityObject;
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates states = AccessibleStates.Selectable | AccessibleStates.Focusable;
                    if (_index == _owner._focus)
                    {
                        states |= AccessibleStates.Selected;
                        if (_owner.Focused) states |= AccessibleStates.Focused;
                    }
                    if (Tile != null && Tile.Disabled) states |= AccessibleStates.Unavailable;
                    return states;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
                _markFont?.Dispose();
                _detailFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The board window (ticket S0069 section 6): one non-activating, borderless tool window that sits on top
    /// of the others in a corner (a switch - <c>TrafficLightPinned</c> - not a second window). It has its own
    /// title area - the title, then add / edit / pin / close as icon buttons - which, like any empty part of
    /// the board, drags the window. Its size is the grid's: the chosen number of columns, as many rows as the
    /// targets need, a status line under the title (not on the bare-square step), so it refits whenever the
    /// targets, the columns or the tile size change and only its position is remembered. Kept out of every
    /// capture and screen share (D9). The mouse never gives it the focus; the chord and the tray item do, on
    /// purpose (D16).
    /// </summary>
    internal sealed class TrafficLightWindow : ThemedForm
    {
        private const int WsExToolWindow = 0x00000080;
        /// <summary>The one-pixel frame the form paints in the border role, around the docked children.</summary>
        private const int FrameWidth = 1;

        private readonly AppConfig _config;
        private readonly TrafficLightBoardActions _actions;
        private readonly TrafficLightBoard _board = new TrafficLightBoard();
        private readonly TrafficLightTitleBar _titleBar = new TrafficLightTitleBar();
        private readonly Label _status = new Label();
        private readonly System.Windows.Forms.Timer _refresh = new System.Windows.Forms.Timer { Interval = 5000 };
        private ThemePalette _palette = ThemePalette.Light;
        private string _uiLanguage;
        // The script font of the current language (hi/bn/zh), handed to the form - ours to release.
        private Font? _scriptFont;
        private IntPtr _returnTo = IntPtr.Zero;
        private bool _placed;
        private bool _applyingBounds;
        // Control.Visible answers false for a child of a window that is not shown, so the line's own state is kept.
        private bool _statusShown = true;
        // The plate's transparency (0..100): while it is above zero the window is layered and its whole
        // picture is composed per pixel - the plate fades, the buttons and the tiles stay solid.
        private int _transparency;
        private bool _composeQueued;
        private bool _composing;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // A tool window: no Alt+Tab entry, however the shell decides about the taskbar.
                cp.ExStyle |= WsExToolWindow;
                // Born transparent: the style is decided before the handle exists; later changes go
                // through SetLayered, and the composed surface replaces this normal painting entirely.
                // The field, not the config: the property runs again inside the base constructor,
                // before the config is handed to this one.
                if (_transparency > 0) cp.ExStyle |= WindowInterop.WS_EX_LAYERED;
                return cp;
            }
        }

        public TrafficLightBoard Board => _board;

        public TrafficLightTitleBar TitleBar => _titleBar;

        /// <summary>False on the bare-square step: there is no status line there.</summary>
        internal bool StatusLineVisible => _statusShown;

        public TrafficLightWindow(AppConfig config, TrafficLightBoardActions actions, string uiLanguage)
        {
            HelpLanguage = uiLanguage;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _uiLanguage = uiLanguage;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = config.TrafficLightPinned;
            KeyPreview = false;
            Padding = new Padding(FrameWidth);

            _titleBar.Dock = DockStyle.Top;
            _titleBar.CommandInvoked += OnTitleCommand;
            _titleBar.DragRequested += BeginDrag;
            _titleBar.Pinned = config.TrafficLightPinned;

            _status.Dock = DockStyle.Top;
            _status.AutoSize = false;
            _status.AutoEllipsis = true;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(6, 0, 6, 0);

            _board.Dock = DockStyle.Fill;
            _board.TileActivated += index => CheckTile(index);
            _board.CheckAllRequested += () => _actions.CheckAll();
            _board.ReturnFocusRequested += HandBackFocus;
            _board.ContextMenuRequested += ShowMenu;
            _board.DragRequested += BeginDrag;
            // While the plate is transparent the window's whole picture comes from the composed layered
            // copy - and a layered window's children are never sent WM_PAINT, so their own Invalidated
            // events (a hover, a focus ring, a scroll, a state change) are what recompose it. DrawToBitmap
            // renders them fresh whatever their paint state; the _composing guard keeps the compose pass
            // itself (WM_PRINT does raise OnPaint) from recomposing in a circle.
            _board.Invalidated += (_, _) => ScheduleCompose();
            _titleBar.Invalidated += (_, _) => ScheduleCompose();
            // While the plate is transparent the window's whole picture comes from the composed layered
            // copy, so every repaint below - a hover, a focus ring, a state change - must recompose it.
            _board.SettingsRequested += () => _actions.EditTargets();

            // Docking runs from the last control added to the first: title, then status, then the board.
            Controls.Add(_board);
            Controls.Add(_status);
            Controls.Add(_titleBar);
            _refresh.Tick += (_, _) => { if (Visible) RefreshBoard(); };

            ApplyMetrics();
            ApplyLanguage(uiLanguage);
            ApplyThemePalette(ThemePalette.Light);
            ApplyTransparency();
        }

        public void ApplyLanguage(string uiLanguage)
        {
            HelpLanguage = uiLanguage;
            _uiLanguage = uiLanguage;
            // The board already paints for a right-to-left window (its layout, the title bar's marks, the
            // arrow keys); what was missing is anyone telling the window it is one, and the script font
            // (APP-BEHAVIOUR rule 8). The bar and the status line are measured from the font, so after it.
            if (WindowScript.Apply(this, uiLanguage, ref _scriptFont)) ApplyMetrics();
            Func<string, string> t = T;
            Text = t("Availability traffic light");
            _titleBar.SetTranslator(t);
            _titleBar.Title = Text;
            _board.SetTranslator(t, _actions.UtcNow);
            RefreshBoard();
        }

        private string T(string en) => Localization.Translate(_uiLanguage, en);

        /// <summary>The bar's and the status line's heights follow the font, so a scaling change re-derives them.</summary>
        private void ApplyMetrics()
        {
            _titleBar.Height = _titleBar.Side;
            _status.Height = Font.Height + 8;
        }

        // ---- content ----

        /// <summary>Re-reads the targets' last known state and repaints; cheap, and a hidden board does nothing.</summary>
        public void RefreshBoard()
        {
            if (IsDisposed) return;
            if (!Visible && _placed) return;
            LoadContent();
            if (_placed) FitToContent();
        }

        /// <summary>Everything the board shows, taken from the configuration and the engine - no sizing.</summary>
        private void LoadContent()
        {
            _board.SetSizeStep(_config.TrafficLightTileSize);
            bool bare = _board.IsBareSquare;
            _board.SetColumns(_config.TrafficLightColumns);
            _board.SetTiles(_actions.Snapshot());
            // The bare-square step is a compact board: no status line, no title text - the buttons and the squares.
            _statusShown = !bare;
            _status.Visible = _statusShown;
            _titleBar.ShowTitle = !bare;
            _titleBar.Visible = !_board.IsMicroscopic;
            _titleBar.Pinned = _config.TrafficLightPinned;

            if (!bare)
            {
                string? summary = TrafficLightText.SummaryLine(_actions.Summary(), T);
                _status.Text = summary ?? T("No targets are switched on");
            }
            // The status line is a plain label: it repaints without any event of its own, so the composed
            // copy is asked for here rather than waited for.
            ScheduleCompose();
        }

        /// <summary>The tile size, the columns, the pin or the transparency changed on the settings page.</summary>
        public void ApplyBoardOptions()
        {
            ApplyPinned();
            ApplyTransparency();
            if (!_placed) return;
            LoadContent();
            FitToContent();
        }

        public void ApplyPinned()
        {
            TopMost = _config.TrafficLightPinned;
            _titleBar.Pinned = _config.TrafficLightPinned;
        }

        // ---- showing, focus and hiding (D16) ----

        /// <summary>
        /// Shows the board. <paramref name="takeFocus"/> is the chord's and the tray item's route: it brings the
        /// board to the foreground and onto its first tile. Without it - a board restored at start, or shown by
        /// any other way - nothing is activated.
        /// </summary>
        public void ShowBoard(bool takeFocus, IntPtr returnTo)
        {
            if (returnTo != IntPtr.Zero) _returnTo = returnTo;
            LoadContent();
            PlaceWindow();
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            _refresh.Start();
            if (takeFocus) FocusBoard();
            ScheduleCompose();
        }

        public void FocusBoard()
        {
            ForegroundActivator.Activate(this);
            _board.Focus();
        }

        /// <summary>
        /// The chord (D16): hidden - show and focus; visible but not focused - focus; focused - hide and hand
        /// the focus back to the window the user came from.
        /// </summary>
        public void ToggleFromChord(IntPtr returnTo)
        {
            if (!Visible) ShowBoard(takeFocus: true, returnTo);
            else if (!_board.ContainsFocus && !ContainsFocus) { if (returnTo != IntPtr.Zero) _returnTo = returnTo; FocusBoard(); }
            else HideBoard(handBack: true);
        }

        public bool BoardHasFocus => ContainsFocus || _board.ContainsFocus;

        public void HideBoard(bool handBack)
        {
            _refresh.Stop();
            SaveGeometry();
            if (Visible) Hide();
            _actions.Hidden();
            if (handBack) HandBackFocus();
        }

        private void HandBackFocus()
        {
            IntPtr target = _returnTo;
            if (target != IntPtr.Zero && WindowInterop.IsWindow(target) && !WindowInterop.IsHungAppWindow(target))
                ForegroundActivator.Activate(target);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideBoard(handBack: true);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A map of the user's own network: out of every capture and every screen share (D9). The
            // balloon cannot be excluded - Windows draws it - which is why it names labels, never addresses.
            ScreenCapture.ExcludeFromCapture(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            // The window itself is never activated by a click either: a click on the title bar or a tile must
            // not steal the editor's focus.
            if (m.Msg == WindowInterop.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)WindowInterop.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        // ---- geometry: the grid's size, the user's position ----

        /// <summary>
        /// The client size the grid needs: the columns in use, as many rows as the targets make, under the title
        /// bar and - except on the bare-square step - the status line; capped at most of the work area, beyond
        /// which the board scrolls and the width grows by the scroll bar so no column is lost to it.
        /// </summary>
        internal Size FitSize()
        {
            Rectangle work = Screen.FromControl(this).WorkingArea;
            bool bare = _board.IsBareSquare;
            int title = _board.IsMicroscopic ? 0 : _titleBar.Side;
            int status = bare ? 0 : _status.Height;
            int frame = 2 * FrameWidth;
            int inner = _board.IsMicroscopic ? _board.GridWidth
                : Math.Max(_board.GridWidth, Math.Max(_titleBar.MinimumContentWidth, bare ? 0 : Font.Height * 9));
            if (_board.Tiles.Count == 0) inner = Math.Max(inner, Font.Height * 12);
            int body = _board.ContentHeightFor(inner);
            int room = Math.Max(_board.TileSize.Height + 2 * _board.Gap, work.Height * 8 / 10 - title - status - frame);
            bool scrolls = body > room;
            int width = inner + frame + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0);
            return new Size(width, title + status + Math.Min(body, room) + frame);
        }

        private void PlaceWindow()
        {
            if (_placed)
            {
                FitToContent();
                return;
            }
            _placed = true;
            _applyingBounds = true;
            try
            {
                ApplyMetrics();
                ClientSize = FitSize();
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                if (_config.TrafficLightX != int.MinValue && _config.TrafficLightY != int.MinValue)
                {
                    Bounds = ScreenPlacement.Clamp(new Rectangle(_config.TrafficLightX, _config.TrafficLightY, Width, Height),
                        Screen.AllScreens.Select(s => s.WorkingArea));
                }
                else
                {
                    // The corner the user's eye already knows: the bottom right of the main screen.
                    int margin = _board.Gap * 3;
                    Bounds = ScreenPlacement.Clamp(
                        new Rectangle(work.Right - Width - margin, work.Bottom - Height - margin, Width, Height), new[] { work });
                }
            }
            finally { _applyingBounds = false; }
        }

        /// <summary>The window follows its grid: a target added, a column more, a bigger tile - and stays on the screen.</summary>
        private void FitToContent()
        {
            if (!_placed) return;
            Size want = FitSize();
            if (want == ClientSize) return;
            _applyingBounds = true;
            try
            {
                ClientSize = want;
                Rectangle clamped = ScreenPlacement.Clamp(Bounds, Screen.AllScreens.Select(s => s.WorkingArea));
                if (clamped != Bounds) Bounds = clamped;
            }
            finally { _applyingBounds = false; }
            // The composed surface carries the window's own size; a refit means a recompose.
            ScheduleCompose();
        }

        private void SaveGeometry()
        {
            if (_applyingBounds || !_placed || WindowState != FormWindowState.Normal) return;
            _config.TrafficLightX = Bounds.X;
            _config.TrafficLightY = Bounds.Y;
            _config.SaveTrafficLightWindow();
        }

        // One write per gesture (WM_EXITSIZEMOVE), not one per pixel of a drag.
        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (Visible) SaveGeometry();
        }

        protected override void OnDpiChanged(int oldDpi, int newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ApplyMetrics();
            LoadContent();
            FitToContent();
            ScheduleCompose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // The frame: the children are docked inside the form's padding, so this is all that shows of it.
            using (var pen = new Pen(_palette.Kind == ThemeKind.HighContrast ? _palette.StatusInk : _palette.Border, 1f))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        // ---- window transparency: the plate fades, the buttons and the tiles stay solid ----

        /// <summary>
        /// The board window's plate (its background, the title bar's strip, the status line, the frame)
        /// fades with the setting while the title bar's buttons and the tiles keep full alpha: 0 is the
        /// usual opaque window, 100 leaves only the buttons and the tiles - the plate is see-through
        /// there and lets clicks pass to whatever is under it, except the bar's strip, which stays the
        /// drag handle. Called at construction and whenever the setting changes on the settings page.
        /// </summary>
        public void ApplyTransparency()
        {
            int transparency = Math.Max(0, Math.Min(100, _config.TrafficLightWindowTransparency));
            if (transparency == _transparency) return;
            _transparency = transparency;
            if (!IsHandleCreated) return;   // CreateParams carries the style into the birth
            SetLayered(transparency > 0);
        }

        /// <summary>Turns the per-pixel layered mode on or off; off returns the window to normal painting.</summary>
        private void SetLayered(bool layered)
        {
            int style = WindowInterop.GetWindowLong(Handle, WindowInterop.GWL_EXSTYLE);
            bool isLayered = (style & WindowInterop.WS_EX_LAYERED) != 0;
            if (isLayered == layered)
            {
                if (layered) ScheduleCompose();
                return;
            }
            WindowInterop.SetWindowLong(Handle, WindowInterop.GWL_EXSTYLE,
                layered ? style | WindowInterop.WS_EX_LAYERED : style & ~WindowInterop.WS_EX_LAYERED);
            if (layered) ComposeLayered();   // a layered window without a surface shows nothing
            else Invalidate(true);
        }

        /// <summary>
        /// One recompose per message-pump cycle, however many repaints asked for it - a sweep of the mouse
        /// across the board is one composition, not one per tile.
        /// </summary>
        private void ScheduleCompose()
        {
            if (_transparency <= 0 || !IsHandleCreated || !Visible || IsDisposed || Disposing || _composing) return;
            if (_composeQueued) return;
            _composeQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _composeQueued = false;
                    try { ComposeLayered(); }
                    catch (Exception ex) { TrafficLightLog.Fault("board-transparency", ex); }
                }));
            }
            catch (Exception ex) { _composeQueued = false; TrafficLightLog.Fault("board-transparency-schedule", ex); }
        }

        /// <summary>
        /// Renders the window and its children into a 32-bit bitmap, fades the plate's pixels in it and
        /// hands the bitmap to <c>UpdateLayeredWindow</c>: the window's every pixel then comes from the
        /// bitmap, alpha per pixel. The child controls stay the real children - input, focus, tooltips and
        /// the screen-reader tree are untouched - only the picture on the glass is a copy.
        /// </summary>
        private void ComposeLayered()
        {
            if (_transparency <= 0 || !IsHandleCreated || !Visible || _composing) return;
            _composing = true;
            try
            {
                int width = Math.Max(1, Width), height = Math.Max(1, Height);
                using var surface = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                DrawToBitmap(surface, new Rectangle(0, 0, width, height));
                ApplyPlateAlpha(surface);
                IntPtr screen = WindowInterop.GetDC(IntPtr.Zero);
                IntPtr memory = WindowInterop.CreateCompatibleDC(screen);
                IntPtr drawn = IntPtr.Zero, previous = IntPtr.Zero;
                try
                {
                    drawn = surface.GetHbitmap();
                    previous = WindowInterop.SelectObject(memory, drawn);
                    var size = new Size(width, height);
                    Point source = Point.Empty;
                    var blend = new WindowInterop.BLENDFUNCTION
                    {
                        BlendOp = WindowInterop.AC_SRC_OVER,
                        SourceConstantAlpha = 255,
                        AlphaFormat = WindowInterop.AC_SRC_ALPHA,
                    };
                    WindowInterop.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, memory,
                        ref source, 0, ref blend, WindowInterop.ULW_ALPHA);
                }
                finally
                {
                    if (previous != IntPtr.Zero) WindowInterop.SelectObject(memory, previous);
                    if (drawn != IntPtr.Zero) WindowInterop.DeleteObject(drawn);
                    WindowInterop.DeleteDC(memory);
                    WindowInterop.ReleaseDC(IntPtr.Zero, screen);
                }
            }
            finally { _composing = false; }
        }

        private void ApplyPlateAlpha(Bitmap surface)
        {
            int plate = (int)Math.Round((100 - _transparency) * 255.0 / 100.0);
            if (plate >= 255) return;
            int bar = plate == 0 ? 1 : plate;
            int width = surface.Width, height = surface.Height;
            var solid = new bool[width * height];
            void Keep(Rectangle r)
            {
                int left = Math.Max(0, r.Left), top = Math.Max(0, r.Top);
                int right = Math.Min(width, r.Right), bottom = Math.Min(height, r.Bottom);
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                        solid[y * width + x] = true;
            }
            Point barAt = _titleBar.Location, boardAt = _board.Location;
            // The microscopic step hides the whole bar: then there are no buttons to keep and no strip to
            // drag by, and only the tiles stay above the plate.
            bool barVisible = _titleBar.Visible;
            if (barVisible)
                for (int i = 0; i < 4; i++)
                {
                    Rectangle zone = _titleBar.ZoneOf(i);
                    zone.Offset(barAt);
                    Keep(zone);
                }
            foreach (Rectangle cell in _board.TileBounds())
            {
                Rectangle tile = cell;
                tile.Offset(boardAt);
                Keep(tile);
            }
            int barTop = barAt.Y, barBottom = barAt.Y + _titleBar.Height;

            var bounds = new Rectangle(0, 0, width, height);
            BitmapData data = surface.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                for (int y = 0; y < height; y++)
                {
                    int row = y * data.Stride;
                    bool inBar = barVisible && y >= barTop && y < barBottom;
                    for (int x = 0; x < width; x++)
                    {
                        int at = row + x * 4;
                        if (solid[y * width + x]) continue;
                        int alpha = inBar ? bar : plate;
                        pixels[at] = (byte)(pixels[at] * alpha / 255);
                        pixels[at + 1] = (byte)(pixels[at + 1] * alpha / 255);
                        pixels[at + 2] = (byte)(pixels[at + 2] * alpha / 255);
                        pixels[at + 3] = (byte)alpha;
                    }
                }
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally { surface.UnlockBits(data); }
        }

        // ---- the title bar's commands, the menu and the drag ----

        private void OnTitleCommand(TitleCommand command)
        {
            switch (command)
            {
                case TitleCommand.Help:
                    ContextHelp.Open(this);
                    break;
                case TitleCommand.Add:
                    _actions.AddTarget();
                    break;
                case TitleCommand.Edit:
                    _actions.EditTargets();
                    break;
                case TitleCommand.Pin:
                    TogglePinned();
                    break;
                default:
                    // The mouse never gave the board the focus, so there is nothing to hand back unless the keyboard did.
                    HideBoard(handBack: BoardHasFocus);
                    break;
            }
        }

        private void TogglePinned()
        {
            _config.TrafficLightPinned = !_config.TrafficLightPinned;
            ApplyPinned();
            _actions.PinnedChanged(_config.TrafficLightPinned);
        }

        private void BeginDrag()
        {
            try
            {
                WindowInterop.ReleaseCapture();
                WindowInterop.SendMessage(Handle, WindowInterop.WM_NCLBUTTONDOWN, (IntPtr)WindowInterop.HTCAPTION, IntPtr.Zero);
            }
            catch (Exception ex) { TrafficLightLog.Fault("board-drag", ex); }
        }

        private void CheckTile(int index)
        {
            if (index < 0 || index >= _board.Tiles.Count) return;
            TrafficLightTile tile = _board.Tiles[index];
            if (tile.Disabled || tile.InvalidAddress) return;
            _actions.CheckNow(tile.Target.Id);
        }

        private void ShowMenu(int tileIndex, Point at)
        {
            var menu = new ContextMenuStrip();
            menu.RightToLeft = Localization.IsRightToLeft(_uiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            bool onTile = tileIndex >= 0 && tileIndex < _board.Tiles.Count;
            if (onTile)
            {
                TrafficLightTile tile = _board.Tiles[tileIndex];
                var check = new ToolStripMenuItem(T("Check now"), null, (_, _) => CheckTile(tileIndex))
                {
                    Enabled = !tile.Disabled && !tile.InvalidAddress && !tile.Checking,
                };
                menu.Items.Add(check);
            }
            menu.Items.Add(new ToolStripMenuItem(T("Check everything now"), null, (_, _) => _actions.CheckAll()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(T("Add a target.."), null, (_, _) => _actions.AddTarget()));
            menu.Items.Add(new ToolStripMenuItem(T("Edit targets.."), null, (_, _) => _actions.EditTargets()));
            menu.Items.Add(new ToolStripSeparator());
            var pin = new ToolStripMenuItem(T("Always on top"), null, (_, _) => TogglePinned())
            { Checked = _config.TrafficLightPinned };
            menu.Items.Add(pin);
            menu.Items.Add(new ToolStripMenuItem(T("Hide"), null, (_, _) => HideBoard(handBack: true)));
            menu.Closed += (_, _) => BeginInvoke(new Action(() => menu.Dispose()));
            menu.Show(_board, at);
        }

        // ---- theme ----

        protected override void OnThemeApplied(ThemePalette palette)
        {
            base.OnThemeApplied(palette);
            ApplyThemePalette(palette);
        }

        private void ApplyThemePalette(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.SurfaceWindow;
            ForeColor = palette.TextPrimary;
            _status.BackColor = palette.SurfaceSunken;
            _status.ForeColor = palette.TextMuted;
            _titleBar.ApplyTheme(palette);
            _board.ApplyTheme(palette);
            Invalidate();
            // The children's own repaints recompose too, but the copy must not wait for them.
            ScheduleCompose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refresh.Dispose();
            base.Dispose(disposing);
            if (disposing) _scriptFont?.Dispose();
        }
    }
}
