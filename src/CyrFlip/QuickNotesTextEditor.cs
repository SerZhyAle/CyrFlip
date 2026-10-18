using System;
using System.Collections.Generic;
using System.Drawing;
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
        private int[] _lineStarts = new int[] { 0 };

        public int LineCount => _lineStarts.Length;

        public IReadOnlyList<int> LineStarts => _lineStarts;

        public void Update(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _lineStarts = new int[] { 0 };
                return;
            }

            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    starts.Add(i + 1);
                }
                else if (c == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            _lineStarts = starts.ToArray();
        }

        /// <summary>
        /// Finds the 1-based logical line number for <paramref name="charIndex"/> using binary search.
        /// <paramref name="isLineStart"/> is true if <paramref name="charIndex"/> matches the line's start index.
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

    /// <summary>
    /// Native TextBox for quick notes that notifies its host when the visible viewport, scrolling,
    /// text, font or size changes without polling timers (ticket S0078).
    /// </summary>
    internal sealed class QuickNotesTextBox : TextBox
    {
        public event EventHandler? ViewportChanged;

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
                case 0x0115: // WM_VSCROLL
                case 0x0114: // WM_HSCROLL
                case 0x020A: // WM_MOUSEWHEEL
                case WindowInterop.WM_KEYDOWN:
                case WindowInterop.WM_KEYUP:
                case 0x0102: // WM_CHAR
                case 0x0005: // WM_SIZE
                case 0x0200: // WM_MOUSEMOVE (drag-selection autoscroll)
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0202: // WM_LBUTTONUP
                case 0x0215: // WM_CAPTURECHANGED
                case (int)WindowInterop.EM_LINESCROLL:
                case 0x00B5: // EM_SCROLL
                case 0x00B7: // EM_SCROLLCARET
                    ViewportChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The drawn line numbers gutter for <see cref="QuickNotesTextEditor"/> (ticket S0078).
    /// Not editable, no Tab focus, forwards mouse wheel to the editor, double buffered.
    /// </summary>
    internal sealed class QuickNotesLineGutter : Control
    {
        private readonly QuickNotesTextEditor _owner;
        private readonly ToolTip _tip = new ToolTip();

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
            if (m.Msg == 0x020A /* WM_MOUSEWHEEL */)
            {
                TextBox tb = _owner.TextBox;
                if (tb.IsHandleCreated)
                {
                    WindowInterop.SendMessage(tb.Handle, (uint)m.Msg, m.WParam, m.LParam);
                }
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
    /// (ticket S0078). Keeps line numbers physically on the left, computes gutter width dynamically, and preserves
    /// the caret, selection and top visible logical line on toggling options or resizing.
    /// </summary>
    internal sealed class QuickNotesTextEditor : Control
    {
        public const string ContinuationMarker = "↳";

        private readonly QuickNotesTextBox _textBox = new QuickNotesTextBox();
        private readonly QuickNotesLineGutter _gutter;
        private readonly QuickNotesLineIndex _lineIndex = new QuickNotesLineIndex();

        private bool _showLineNumbers;
        private int _cachedGutterWidth;
        private string _language = "English";

        public QuickNotesTextBox TextBox => _textBox;

        public QuickNotesLineIndex LineIndex => _lineIndex;

        public bool ShowLineNumbers => _showLineNumbers;

        public QuickNotesLineGutter Gutter => _gutter;

        public QuickNotesTextEditor()
        {
            _gutter = new QuickNotesLineGutter(this);
            _gutter.Visible = false;

            Controls.Add(_gutter);
            Controls.Add(_textBox);

            _textBox.TextChanged += (_, _) => OnTextBoxTextChanged();
            _textBox.ViewportChanged += (_, _) => OnTextBoxViewportChanged();
            _textBox.FontChanged += (_, _) => OnTextBoxFontChanged();

            _lineIndex.Update(_textBox.Text);
            ApplyLanguage(_language);
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

        public void ApplyLineNumbers(bool show)
        {
            if (_showLineNumbers == show) return;

            int selStart = 0, selLen = 0, topChar = 0;
            bool hasHandle = _textBox.IsHandleCreated;
            if (hasHandle)
            {
                selStart = _textBox.SelectionStart;
                selLen = _textBox.SelectionLength;
                int topRow = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                topChar = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_LINEINDEX, (IntPtr)topRow, IntPtr.Zero);
                if (topChar < 0) topChar = 0;
            }

            _showLineNumbers = show;
            _gutter.Visible = show;
            PerformEditorLayout();

            if (hasHandle)
            {
                int newTopRow = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                int targetRow = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_LINEFROMCHAR, (IntPtr)topChar, IntPtr.Zero);
                int delta = targetRow - newTopRow;
                if (delta != 0)
                {
                    WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
                }
                _textBox.Select(selStart, selLen);
            }

            _gutter.Invalidate();
        }

        public void ApplyWordWrap(bool wrap)
        {
            int selStart = _textBox.SelectionStart;
            int selLen = _textBox.SelectionLength;

            _textBox.WordWrap = wrap;
            _textBox.ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both;

            PerformEditorLayout();
            if (_textBox.IsHandleCreated)
            {
                _textBox.Select(selStart, selLen);
            }
            _gutter.Invalidate();
        }

        private void OnTextBoxTextChanged()
        {
            _lineIndex.Update(_textBox.Text);
            if (_showLineNumbers)
            {
                int required = CalculateRequiredGutterWidth();
                if (required != _cachedGutterWidth)
                {
                    PerformEditorLayout();
                }
                _gutter.Invalidate();
            }
        }

        private void OnTextBoxViewportChanged()
        {
            if (_showLineNumbers)
            {
                _gutter.Invalidate();
            }
        }

        private void OnTextBoxFontChanged()
        {
            if (_showLineNumbers)
            {
                PerformEditorLayout();
                _gutter.Invalidate();
            }
        }

        public int CalculateRequiredGutterWidth()
        {
            if (!_showLineNumbers) return 0;
            int maxLine = Math.Max(1, _lineIndex.LineCount);
            string sample = maxLine.ToString() + " " + ContinuationMarker;
            Font font = _textBox.Font ?? Font;
            Size textSize = TextRenderer.MeasureText(sample, font);

            int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            int pad = Math.Max(4, (int)Math.Round(6.0 * dpi / 96.0));
            return pad + textSize.Width + pad + 1; // +1 for 1px dividing border line
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformEditorLayout();
            if (_showLineNumbers) _gutter.Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            PerformEditorLayout();
        }

        private void PerformEditorLayout()
        {
            int gw = _showLineNumbers ? CalculateRequiredGutterWidth() : 0;
            _cachedGutterWidth = gw;
            _gutter.Visible = _showLineNumbers;

            int clientW = ClientSize.Width;
            int clientH = ClientSize.Height;

            bool mirrored = IsControlMirrored(this);
            if (mirrored)
            {
                _gutter.Bounds = new Rectangle(Math.Max(0, clientW - gw), 0, gw, clientH);
                _textBox.Bounds = new Rectangle(0, 0, Math.Max(0, clientW - gw), clientH);
            }
            else
            {
                _gutter.Bounds = new Rectangle(0, 0, gw, clientH);
                _textBox.Bounds = new Rectangle(gw, 0, Math.Max(0, clientW - gw), clientH);
            }
        }

        internal void PaintGutter(Graphics g, Rectangle clientRect)
        {
            ThemePalette palette = ThemeApply.PaletteOf(this) ?? ThemePalette.Light;
            using (var backBrush = new SolidBrush(palette.SurfaceSunken))
            {
                g.FillRectangle(backBrush, clientRect);
            }

            bool mirrored = IsControlMirrored(this);
            int borderX = mirrored ? clientRect.Left : clientRect.Right - 1;
            using (var borderPen = new Pen(palette.Border))
            {
                g.DrawLine(borderPen, borderX, clientRect.Top, borderX, clientRect.Bottom);
            }

            if (!_textBox.IsHandleCreated || _lineIndex.LineCount == 0) return;

            Font font = _textBox.Font ?? Font;
            int fontHeight = font.Height;
            int clientHeight = _textBox.ClientSize.Height;
            int firstVisibleVisualRow = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

            var rc = new WindowInterop.RECT();
            WindowInterop.SendMessageRect(_textBox.Handle, WindowInterop.EM_GETRECT, IntPtr.Zero, ref rc);

            int dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            int pad = Math.Max(4, (int)Math.Round(6.0 * dpi / 96.0));
            int textLeft = clientRect.Left + pad;
            int textRight = clientRect.Right - pad - 1;
            int textWidth = Math.Max(1, textRight - textLeft);

            Color textColor = palette.TextMuted;
            TextFormatFlags flags = TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

            int lastDrawnLogicalLine = -1;
            int lastKnownY = -1;

            for (int vRow = firstVisibleVisualRow; ; vRow++)
            {
                int charIndex = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_LINEINDEX, (IntPtr)vRow, IntPtr.Zero);
                if (charIndex < 0) break;

                int y;
                if (_textBox.TextLength == 0)
                {
                    y = rc.Top;
                }
                else if (charIndex < _textBox.TextLength)
                {
                    int pos = (int)WindowInterop.SendMessage(_textBox.Handle, WindowInterop.EM_POSFROMCHAR, (IntPtr)charIndex, IntPtr.Zero);
                    y = (short)((pos >> 16) & 0xFFFF);
                }
                else
                {
                    y = lastKnownY >= 0 ? lastKnownY + fontHeight : rc.Top;
                }

                if (y >= clientHeight) break;
                lastKnownY = y;

                int logicalLine = _lineIndex.GetLineNumber(charIndex, out bool isLineStart);
                string? textToDraw = null;

                if (isLineStart)
                {
                    textToDraw = logicalLine.ToString();
                    lastDrawnLogicalLine = logicalLine;
                }
                else
                {
                    if (lastDrawnLogicalLine != logicalLine)
                    {
                        textToDraw = logicalLine + " " + ContinuationMarker;
                        lastDrawnLogicalLine = logicalLine;
                    }
                }

                if (textToDraw != null)
                {
                    var rowRect = new Rectangle(textLeft, y, textWidth, fontHeight);
                    TextRenderer.DrawText(g, textToDraw, font, rowRect, textColor, flags);
                }

                if (_textBox.TextLength == 0) break;
            }
        }

        private static bool IsControlMirrored(Control control)
        {
            if (!control.IsHandleCreated) return false;
            try
            {
                return (WindowInterop.GetWindowLong(control.Handle, WindowInterop.GWL_EXSTYLE) & 0x00400000 /* WS_EX_LAYOUTRTL */) != 0;
            }
            catch { return false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gutter.Dispose();
                _textBox.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
