using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Index of logical line start character positions for the quick-notes editor (ticket S0078).
    /// Logical lines are separated by CRLF, bare LF or lone CR. An empty string has 1 line starting at 0.
    /// A trailing line break creates a final empty line. The total matches <see cref="QuickNote.LineCount"/>.
    /// </summary>
    internal sealed class QuickNotesLineIndex
    {
        private static readonly int[] OneLine = { 0 };

        private int[] _lineStarts = OneLine;

        public int LineCount => _lineStarts.Length;

        public IReadOnlyList<int> LineStarts => _lineStarts;

        public void Update(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _lineStarts = OneLine;
                return;
            }

            // Two passes over the text, one exact allocation: no list to grow and copy for a 512 KB note.
            int lines = 1;
            for (int i = 0; i < text!.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    lines++;
                }
                else if (c == '\n') lines++;
            }

            var starts = new int[lines];
            int n = 1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    starts[n++] = i + 1;
                }
                else if (c == '\n') starts[n++] = i + 1;
            }
            _lineStarts = starts;
        }

        /// <summary>
        /// Finds the 1-based logical line number for <paramref name="charIndex"/> using binary search.
        /// <paramref name="isLineStart"/> is true if <paramref name="charIndex"/> matches the line's start index.
        /// An index past the end belongs to the last line, one before the start to the first.
        /// </summary>
        public int GetLineNumber(int charIndex, out bool isLineStart)
        {
            if (charIndex <= 0)
            {
                isLineStart = charIndex == 0;
                return 1;
            }

            int idx = Array.BinarySearch(_lineStarts, charIndex);
            if (idx >= 0)
            {
                isLineStart = true;
                return idx + 1;
            }

            int lineIdx = ~idx - 1;
            if (lineIdx < 0) lineIdx = 0;
            isLineStart = false;
            return lineIdx + 1;
        }
    }

    /// <summary>One number the gutter draws: where (gutter coordinates), which line, and whether it is the "↳" repeat.</summary>
    internal readonly struct GutterLabel
    {
        public GutterLabel(int y, int line, bool continuation)
        {
            Y = y;
            Line = line;
            Continuation = continuation;
        }

        /// <summary>Top of the row the number stands opposite, in the gutter's own client coordinates.</summary>
        public int Y { get; }

        public int Line { get; }

        /// <summary>True when the line began above the visible area and this is its first visible row.</summary>
        public bool Continuation { get; }

        public string Text => Continuation
            ? Line.ToString(CultureInfo.InvariantCulture) + " " + QuickNotesTextEditor.ContinuationMarker
            : Line.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The one rule of the gutter (S0078 items 3 and 4), pure so it is testable without a window:
    /// a visual row that begins a logical line carries its number; a continuation row carries nothing -
    /// except the first visible row of the view when the line began above it, which carries the number
    /// once with the "↳" marker, so the line is identifiable in the middle of a multi-screen paragraph.
    /// </summary>
    internal static class QuickNotesGutterRule
    {
        /// <param name="rowStartChar">Index of the first character of the visual row.</param>
        /// <param name="lastLine">The line a number was last drawn for in this view (-1 at the top of the view).</param>
        /// <returns>False when the row gets no number.</returns>
        public static bool TryLabel(QuickNotesLineIndex index, int rowStartChar, ref int lastLine, out int line, out bool continuation)
        {
            line = index.GetLineNumber(rowStartChar, out bool isLineStart);
            continuation = false;
            if (isLineStart)
            {
                lastLine = line;
                return true;
            }
            if (line != lastLine)
            {
                lastLine = line;
                continuation = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Native TextBox for quick notes that tells its host when its rows may have moved - after the
    /// edit control itself has handled scrolling, keys, wheel, size, font and paint - without a polling
    /// timer (ticket S0078). The host compares a cheap signature of the view and repaints the gutter
    /// only when it changed, so a mouse move over the text costs nothing.
    /// </summary>
    internal sealed class QuickNotesTextBox : TextBox
    {
        private const int WM_SIZE = 0x0005;
        private const int WM_SETFONT = 0x0030;
        private const int WM_PAINT = 0x000F;
        private const int WM_CHAR = 0x0102;
        private const int WM_TIMER = 0x0113;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int EM_SCROLL = 0x00B5;
        private const int EM_SCROLLCARET = 0x00B7;

        /// <summary>Raised after the edit control handled a message that can move or re-wrap its rows.</summary>
        public event EventHandler? ViewportMoved;

        public QuickNotesTextBox()
        {
            Multiline = true;
            AcceptsTab = true;
            AcceptsReturn = true;
            ScrollBars = ScrollBars.Both;
            MaxLength = 0;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            switch (m.Msg)
            {
                case WM_VSCROLL:       // scroll bar: thumb, arrows, page
                case WM_MOUSEWHEEL:
                case WindowInterop.WM_KEYDOWN:   // arrows, Page Up/Down, Ctrl+Home/End
                case WM_CHAR:
                case WM_SIZE:          // client area changed, also by a scroll bar appearing
                case WM_SETFONT:
                case WM_TIMER:         // drag-selection autoscroll
                case (int)WindowInterop.EM_LINESCROLL:
                case EM_SCROLL:
                case EM_SCROLLCARET:
                case WM_PAINT:         // the catch-all: whatever moved the rows ends in a repaint
                    ViewportMoved?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
    }

    /// <summary>
    /// The drawn line numbers gutter for <see cref="QuickNotesTextEditor"/> (ticket S0078).
    /// Not editable, no Tab focus, forwards the mouse wheel to the editor, double buffered.
    /// </summary>
    internal sealed class QuickNotesLineGutter : Control
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        private readonly QuickNotesTextEditor _owner;
        private readonly ToolTip _tip = new ToolTip { ShowAlways = true };

        public QuickNotesLineGutter(QuickNotesTextEditor owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Default;
            AccessibleRole = AccessibleRole.RowHeader;
        }

        public void SetAccessibleAndTooltip(string accessibleName, string tooltipText)
        {
            AccessibleName = accessibleName;
            _tip.SetToolTip(this, tooltipText);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                // The strip has no scroll bar of its own: the wheel over it scrolls the text.
                TextBox tb = _owner.TextBox;
                if (tb.IsHandleCreated)
                    WindowInterop.SendMessage(tb.Handle, (uint)m.Msg, m.WParam, m.LParam);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _owner.PaintGutter(e.Graphics, ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Composite text editor hosting a child <see cref="QuickNotesTextBox"/> and a <see cref="QuickNotesLineGutter"/>
    /// (ticket S0078). Keeps the line numbers physically on the left, computes the gutter width from the
    /// document, and preserves the caret, the selection and the top visible position on toggling options.
    ///
    /// <para>Decisions that are not obvious from the code. <b>The numbers are read from the edit control,
    /// never computed.</b> Where a row is comes from <c>EM_POSFROMCHAR</c> in the edit's own client
    /// coordinates, moved into the gutter's through the screen - the edit has a 3-D border, so the same
    /// number is not the same pixel in the two controls - and which rows are visible comes from the edit's
    /// formatting rectangle, which it trims to whole rows (the last, partly visible client row has no text
    /// and so no number). <b>A row's height is the edit's, not the font's:</b> <c>Font.Height</c> says 16
    /// for Consolas 10 pt where the edit lays out 15. <b>Wrapping is not a line:</b> in an edit control
    /// "line" means visual row, so a row is mapped to a logical line through an index of line starts that
    /// is rebuilt only when the text changes and only while the numbers are shown; scrolling and resizing
    /// never scan the document. <b>Nothing polls.</b> The edit tells the host after it has handled a
    /// message that can move its rows; the host compares a signature of the view and, when it changed,
    /// repaints the gutter synchronously so the numbers never trail the text by a frame.</para>
    /// </summary>
    internal sealed class QuickNotesTextEditor : Control
    {
        public const string ContinuationMarker = "↳";

        private readonly QuickNotesTextBox _textBox = new QuickNotesTextBox();
        private readonly QuickNotesLineGutter _gutter;
        private readonly QuickNotesLineIndex _lineIndex = new QuickNotesLineIndex();
        private readonly List<GutterLabel> _labels = new List<GutterLabel>();

        // The index is rebuilt on demand: the text changed (dirty) or its length no longer matches.
        private bool _indexDirty = true;
        private int _indexedLength = -1;

        private bool _showLineNumbers;
        private int _cachedGutterWidth;
        private string _language = "English";

        // What the gutter was last painted for: first visible row, row count and the formatting rectangle.
        private (int First, int Rows, int Left, int Top, int Right, int Bottom)? _painted;

        public QuickNotesTextBox TextBox => _textBox;

        /// <summary>The logical-line index, current for the text now in the editor.</summary>
        public QuickNotesLineIndex LineIndex
        {
            get
            {
                EnsureIndex();
                return _lineIndex;
            }
        }

        public bool ShowLineNumbers => _showLineNumbers;

        public QuickNotesLineGutter Gutter => _gutter;

        /// <summary>The numbers of the last paint, with their Y in the gutter's client coordinates (tests read it).</summary>
        internal IReadOnlyList<GutterLabel> LastLabels => _labels;

        /// <summary>First visible row of the last paint, or -1 if the gutter was never painted.</summary>
        internal int PaintedFirstRow => _painted?.First ?? -1;

        public QuickNotesTextEditor()
        {
            _gutter = new QuickNotesLineGutter(this);
            _gutter.Visible = false;

            Controls.Add(_gutter);
            Controls.Add(_textBox);

            _textBox.TextChanged += (_, _) => OnTextBoxTextChanged();
            _textBox.ViewportMoved += (_, _) => OnTextBoxViewportMoved();
            _textBox.FontChanged += (_, _) => OnTextBoxReflowed();
            _textBox.HandleCreated += (_, _) => OnTextBoxReflowed();
            ApplyLanguage(_language);
        }

        /// <summary>
        /// The gutter and the edit sit side by side as children of this control and must never swap sides
        /// or mirror their own drawing because an ancestor is mirrored: the numbers are always physically
        /// on the left (S0078 item 10).
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOINHERITLAYOUT = 0x00100000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOINHERITLAYOUT;
                return cp;
            }
        }

        public void ApplyLanguage(string language)
        {
            _language = language;
            _gutter.SetAccessibleAndTooltip(
                Localization.Translate(_language, "Line numbers"),
                Localization.Translate(_language, "↳ Continuation of a wrapped line from above"));
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _gutter.Invalidate();
            Invalidate();
        }

        // ---- Options, applied live ----

        public void ApplyLineNumbers(bool show)
        {
            if (_showLineNumbers == show) return;

            // The caret and the selection are untouched by a resize; the top of the view is not - the
            // text re-wraps in a narrower or wider edit - so it is the one thing to put back.
            int topChar = CaptureTopChar();

            _showLineNumbers = show;
            _gutter.Visible = show;
            _painted = null;
            if (!show)
            {
                // Nothing keeps a copy of the line starts while nobody looks at them.
                _lineIndex.Update(null);
                _indexDirty = true;
                _indexedLength = -1;
            }
            PerformEditorLayout();
            RestoreTopChar(topChar);
            RefreshGutter();
        }

        public void ApplyWordWrap(bool wrap)
        {
            ScrollBars wanted = wrap ? ScrollBars.Vertical : ScrollBars.Both;
            if (_textBox.WordWrap == wrap && _textBox.ScrollBars == wanted) return;

            int selStart = _textBox.SelectionStart;
            int selLen = _textBox.SelectionLength;
            int topChar = CaptureTopChar();

            // Both setters recreate the native handle, which is also why the edit's own undo history
            // does not survive this one switch (it never did).
            _textBox.WordWrap = wrap;
            _textBox.ScrollBars = wanted;

            _painted = null;
            PerformEditorLayout();
            if (_textBox.IsHandleCreated)
            {
                _textBox.Select(selStart, selLen);
                RestoreTopChar(topChar);
            }
            RefreshGutter();
        }

        private int CaptureTopChar()
        {
            if (!_textBox.IsHandleCreated) return -1;
            IntPtr h = _textBox.Handle;
            int row = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero));
            return Math.Max(0, ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_LINEINDEX, (IntPtr)row, IntPtr.Zero)));
        }

        private void RestoreTopChar(int topChar)
        {
            if (topChar < 0 || !_textBox.IsHandleCreated) return;
            IntPtr h = _textBox.Handle;
            int target = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_LINEFROMCHAR, (IntPtr)topChar, IntPtr.Zero));
            int now = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero));
            if (target != now)
                WindowInterop.SendMessage(h, WindowInterop.EM_LINESCROLL, IntPtr.Zero, (IntPtr)(target - now));
        }

        // ---- What the edit tells us ----

        private void OnTextBoxTextChanged()
        {
            _indexDirty = true;
            if (!_showLineNumbers) return;

            // The widest number may have gained a digit; if so the edit narrows, which re-wraps it.
            int required = CalculateRequiredGutterWidth();
            if (required != _cachedGutterWidth) PerformEditorLayout();
            _painted = null;
            RefreshGutter();
        }

        private void OnTextBoxReflowed()
        {
            if (!_showLineNumbers) return;
            PerformEditorLayout();   // a new font changes the width the digits need
            _painted = null;
            RefreshGutter();
        }

        private void OnTextBoxViewportMoved()
        {
            if (!_showLineNumbers || _gutter.IsDisposed || !_textBox.IsHandleCreated) return;
            if (_painted.HasValue && ReadView(out _).Equals(_painted.Value)) return;
            RefreshGutter();
        }

        /// <summary>Repaint the strip now, in the same turn as the text moved, so the two never disagree on screen.</summary>
        private void RefreshGutter()
        {
            if (!_showLineNumbers || _gutter.IsDisposed || !_gutter.IsHandleCreated || !_gutter.Visible) return;
            _gutter.Invalidate();
            _gutter.Update();
        }

        private (int First, int Rows, int Left, int Top, int Right, int Bottom) ReadView(out WindowInterop.RECT rc)
        {
            IntPtr h = _textBox.Handle;
            int first = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero));
            int rows = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero));
            rc = new WindowInterop.RECT();
            WindowInterop.SendMessageRect(h, WindowInterop.EM_GETRECT, IntPtr.Zero, ref rc);
            return (first, rows, rc.Left, rc.Top, rc.Right, rc.Bottom);
        }

        // ---- The index ----

        private void EnsureIndex()
        {
            int length = _textBox.TextLength;
            if (!_indexDirty && length == _indexedLength) return;
            _lineIndex.Update(_textBox.Text);
            _indexedLength = length;
            _indexDirty = false;
            IndexBuilds++;
        }

        /// <summary>How many times the whole text was scanned for line starts (tests: scrolling and resizing must not).</summary>
        internal int IndexBuilds { get; private set; }

        // ---- Width ----

        /// <summary>Padding on each side of the numbers, scaled with the display (96 dpi = 6 px).</summary>
        internal static int ScaledPadding(int dpi) => Math.Max(4, (int)Math.Round(6.0 * (dpi > 0 ? dpi : 96) / 96.0));

        /// <summary>
        /// Width of a strip for documents of up to <paramref name="maxLine"/> lines: the widest label
        /// (every digit, then the marker with its space), two paddings and the divider.
        /// </summary>
        internal static int GutterWidthFor(int maxLine, Font font, int dpi)
        {
            int digits = Math.Max(1, Math.Max(1, maxLine).ToString(CultureInfo.InvariantCulture).Length);
            string widest = new string('8', digits) + " " + ContinuationMarker;
            Size text = TextRenderer.MeasureText(widest, font, new Size(int.MaxValue, int.MaxValue), LabelFlags);
            int pad = ScaledPadding(dpi);
            return pad + text.Width + pad + 1;
        }

        private const TextFormatFlags LabelFlags = TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;

        public int CalculateRequiredGutterWidth()
        {
            if (!_showLineNumbers) return 0;
            EnsureIndex();
            return GutterWidthFor(_lineIndex.LineCount, _textBox.Font ?? Font, DeviceDpi);
        }

        // ---- Layout ----

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            PerformEditorLayout();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            PerformEditorLayout();
            _painted = null;
            RefreshGutter();
        }

        private void PerformEditorLayout()
        {
            int gw = _showLineNumbers ? CalculateRequiredGutterWidth() : 0;
            _cachedGutterWidth = gw;
            _gutter.Visible = _showLineNumbers;

            int clientW = ClientSize.Width;
            int clientH = ClientSize.Height;
            int textW = Math.Max(0, clientW - gw);

            // In a mirrored window child coordinates run from the right, so "physically left" is the far end.
            if (IsControlMirrored(this))
            {
                _gutter.Bounds = new Rectangle(textW, 0, gw, clientH);
                _textBox.Bounds = new Rectangle(0, 0, textW, clientH);
            }
            else
            {
                _gutter.Bounds = new Rectangle(0, 0, gw, clientH);
                _textBox.Bounds = new Rectangle(gw, 0, textW, clientH);
            }
        }

        // ---- Painting ----

        internal void PaintGutter(Graphics g, Rectangle clientRect)
        {
            ThemePalette palette = ThemeApply.PaletteOf(this) ?? ThemePalette.Light;
            using (var backBrush = new SolidBrush(palette.SurfaceSunken))
                g.FillRectangle(backBrush, clientRect);

            // The strip is physically left of the text, so its divider is its right edge. Its own drawing is
            // never mirrored (the editor passes no layout to its children).
            using (var borderPen = new Pen(palette.Border))
                g.DrawLine(borderPen, clientRect.Right - 1, clientRect.Top, clientRect.Right - 1, clientRect.Bottom);

            _labels.Clear();
            if (!_showLineNumbers || !_textBox.IsHandleCreated || !_gutter.IsHandleCreated) return;

            EnsureIndex();
            IntPtr h = _textBox.Handle;
            var view = ReadView(out WindowInterop.RECT rc);
            _painted = view;

            // The edit's client origin in the gutter's coordinates: through the screen, so the edit's border
            // and whatever else sits between the two windows is accounted for by the system and not by us.
            Point origin = _gutter.PointToClient(_textBox.PointToScreen(Point.Empty));
            int textTop = rc.Top + origin.Y;
            int textBottom = rc.Bottom + origin.Y;

            Font font = _textBox.Font ?? Font;
            int pad = ScaledPadding(DeviceDpi);
            int textLeft = clientRect.Left + pad;
            int textWidth = Math.Max(1, clientRect.Right - pad - 1 - textLeft);
            Color ink = palette.Kind == ThemeKind.HighContrast ? palette.TextPrimary : palette.TextMuted;
            int textLength = _textBox.TextLength;

            GraphicsState state = g.Save();
            try
            {
                g.SetClip(Rectangle.Intersect(clientRect, new Rectangle(clientRect.Left, textTop, clientRect.Width, Math.Max(0, textBottom - textTop))));

                int lastLine = -1;
                for (int row = view.First; ; row++)
                {
                    int ci = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_LINEINDEX, (IntPtr)row, IntPtr.Zero));
                    if (ci < 0) break;

                    int y;
                    if (ci < textLength)
                    {
                        int pos = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_POSFROMCHAR, (IntPtr)ci, IntPtr.Zero));
                        if (pos == -1) break;
                        y = (short)((pos >> 16) & 0xFFFF);
                    }
                    else if (textLength == 0)
                    {
                        y = rc.Top;
                    }
                    else
                    {
                        // The empty row after a final line break: the edit has no position for the end of
                        // the text, so it is one row below the row of the last character.
                        int pos = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_POSFROMCHAR, (IntPtr)(textLength - 1), IntPtr.Zero));
                        if (pos == -1) break;
                        y = (short)((pos >> 16) & 0xFFFF) + RowHeightBefore(h, row, font);
                    }

                    if (y >= rc.Bottom) break;   // the edit shows whole rows only: this one has no text
                    if (!QuickNotesGutterRule.TryLabel(_lineIndex, ci, ref lastLine, out int line, out bool continuation)) continue;

                    var label = new GutterLabel(y + origin.Y, line, continuation);
                    _labels.Add(label);
                    TextRenderer.DrawText(g, label.Text, font, new Rectangle(textLeft, label.Y, textWidth, font.Height), ink, LabelFlags);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// Height of one row as the edit lays it out: the distance between the two rows above
        /// <paramref name="row"/> when there are two, the font's text metrics otherwise.
        /// </summary>
        private int RowHeightBefore(IntPtr h, int row, Font font)
        {
            if (row >= 2)
            {
                int a = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_LINEINDEX, (IntPtr)(row - 2), IntPtr.Zero));
                int b = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_LINEINDEX, (IntPtr)(row - 1), IntPtr.Zero));
                if (a >= 0 && b > a)
                {
                    int ya = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_POSFROMCHAR, (IntPtr)a, IntPtr.Zero));
                    int yb = ToInt(WindowInterop.SendMessage(h, WindowInterop.EM_POSFROMCHAR, (IntPtr)b, IntPtr.Zero));
                    if (ya != -1 && yb != -1)
                    {
                        int delta = (short)((yb >> 16) & 0xFFFF) - (short)((ya >> 16) & 0xFFFF);
                        if (delta > 0) return delta;
                    }
                }
            }
            return MetricRowHeight(font);
        }

        private int MetricRowHeight(Font font)
        {
            IntPtr hwnd = _textBox.Handle;
            IntPtr hdc = WindowInterop.GetDC(hwnd);
            if (hdc == IntPtr.Zero) return font.Height;
            IntPtr hFont = font.ToHfont();
            IntPtr previous = WindowInterop.SelectObject(hdc, hFont);
            try
            {
                return WindowInterop.GetTextMetrics(hdc, out WindowInterop.TEXTMETRIC tm) && tm.tmHeight > 0
                    ? tm.tmHeight + tm.tmExternalLeading
                    : font.Height;
            }
            finally
            {
                WindowInterop.SelectObject(hdc, previous);
                WindowInterop.DeleteObject(hFont);
                WindowInterop.ReleaseDC(hwnd, hdc);
            }
        }

        private static int ToInt(IntPtr value) => unchecked((int)value.ToInt64());

        private static bool IsControlMirrored(Control control)
        {
            if (!control.IsHandleCreated) return false;
            try
            {
                return (WindowInterop.GetWindowLong(control.Handle, WindowInterop.GWL_EXSTYLE) & 0x00400000 /* WS_EX_LAYOUTRTL */) != 0;
            }
            catch { return false; }
        }
    }
}
