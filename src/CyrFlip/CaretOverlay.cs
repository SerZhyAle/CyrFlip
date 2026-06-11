using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Feature #3: a small marker (EN/RU/UK) pinned next to the blinking text caret — the
    /// place that actually shows where text will land (the mouse pointer is often an arrow
    /// while you type).
    ///
    /// Caret position comes from two sources, tried in order:
    ///   1. <c>GetGUIThreadInfo</c> — fast, for classic Win32 edit controls.
    ///   2. UI Automation <c>TextPattern</c> — for modern apps (WinUI Notepad, Chromium, etc.)
    ///      that draw their own caret and expose no system caret.
    /// Tracking runs on a background MTA thread so UIA's cross-process calls never block the UI;
    /// the overlay window itself is touched only via BeginInvoke on the UI thread.
    /// </summary>
    internal sealed class CaretOverlay : IDisposable
    {
        private readonly OverlayForm _form;
        private Thread? _thread;
        private volatile bool _running;
        private volatile string _code = "";

        public CaretOverlay(int size) => _form = new OverlayForm(size);

        public void Start()
        {
            _ = _form.Handle; // create the handle on the UI thread so BeginInvoke works
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "CyrFlip.CaretTracker" };
            _thread.SetApartmentState(ApartmentState.MTA); // UIA client prefers MTA
            _thread.Start();
        }

        /// <summary>Set the layout code shown by the overlay (called on layout change).</summary>
        public void SetLayout(string code) => _code = code ?? "";

        private void Loop()
        {
            while (_running)
            {
                try { Tick(); }
                catch { /* never let tracking kill the app */ }
                Thread.Sleep(90);
            }
        }

        private void Tick()
        {
            string code = _code;
            if (code.Length == 0)
            {
                Post(false, 0, 0, code);
                return;
            }

            if (TryGetCaret(out int x, out int y))
                Post(true, x, y, code);
            else
                Post(false, 0, 0, code);
        }

        private void Post(bool show, int x, int y, string code)
        {
            if (!_form.IsHandleCreated)
                return;
            try
            {
                _form.BeginInvoke((Action)(() =>
                {
                    if (show)
                    {
                        _form.SetCode(code);
                        _form.ShowAt(x, y);
                    }
                    else
                    {
                        _form.HideOverlay();
                    }
                }));
            }
            catch (InvalidOperationException) { /* form disposing */ }
        }

        private static bool TryGetCaret(out int x, out int y)
        {
            x = 0; y = 0;

            // 1) System caret (classic Win32 edit controls).
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint tid = GetWindowThreadProcessId(fg, out _);
                var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                if (GetGUIThreadInfo(tid, ref gti)
                    && gti.hwndCaret != IntPtr.Zero
                    && gti.rcCaret.Bottom - gti.rcCaret.Top > 0)
                {
                    var pt = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Top };
                    ClientToScreen(gti.hwndCaret, ref pt);
                    x = pt.X + 4;
                    y = pt.Y;
                    return true;
                }
            }

            // 2) UI Automation fallback (modern apps with no system caret).
            return TryUiaCaret(out x, out y);
        }

        private static bool TryUiaCaret(out int x, out int y)
        {
            x = 0; y = 0;
            try
            {
                AutomationElement? focused = AutomationElement.FocusedElement;
                if (focused == null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj))
                    return false;

                var textPattern = (TextPattern)patternObj;
                TextPatternRange[] selection = textPattern.GetSelection();
                if (selection == null || selection.Length == 0)
                    return false;

                // Give the (usually collapsed) caret range a character of width so it has a rect.
                TextPatternRange range = selection[0].Clone();
                range.ExpandToEnclosingUnit(TextUnit.Character);
                var rects = range.GetBoundingRectangles();
                if (rects.Length == 0)
                    return false;

                var r = rects[rects.Length - 1]; // end of the range ≈ caret
                x = (int)r.Right + 2;
                y = (int)r.Top;
                return true;
            }
            catch
            {
                return false; // UIA throws freely (ElementNotAvailable, NotSupported, …)
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _thread?.Join(300); }
            catch { /* ignore */ }
            _form.Dispose();
        }

        // ------------------------------------------------------------------ overlay window

        private sealed class OverlayForm : Form
        {
            private readonly int _h;
            private readonly Font _font;
            private string _code = "";

            public OverlayForm(int size)
            {
                _h = Math.Max(14, Math.Min(40, size == 0 ? 18 : size));
                _font = new Font("Segoe UI", _h * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = ColorTranslator.FromHtml("#11161f");
                DoubleBuffered = true;
                ResizeToContent();
            }

            // Never take focus from the window the user is typing in.
            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                    return cp;
                }
            }

            public void SetCode(string code)
            {
                if (code == _code)
                    return;
                _code = code;
                ResizeToContent();
                Invalidate();
            }

            public void ShowAt(int x, int y)
            {
                Location = new Point(x, y);
                if (!Visible)
                    Show(); // ShowWithoutActivation => doesn't steal focus from the text field
                else
                    SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            }

            public void HideOverlay()
            {
                if (Visible)
                    Hide();
            }

            private void ResizeToContent()
            {
                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                SizeF s = g.MeasureString(_code.Length == 0 ? "EN" : _code, _font);
                int padX = (int)Math.Round(_h * 0.20);
                int padY = (int)Math.Round(_h * 0.12);
                Size = new Size((int)Math.Ceiling(s.Width) + padX * 2, (int)Math.Ceiling(s.Height) + padY * 2);

                using var path = new GraphicsPath();
                int r = Math.Max(3, _h / 4);
                int d = r * 2;
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(Width - d, 0, d, d, 270, 90);
                path.AddArc(Width - d, Height - d, d, d, 0, 90);
                path.AddArc(0, Height - d, d, d, 90, 90);
                path.CloseFigure();
                Region oldRegion = Region;
                Region = new Region(path);
                oldRegion?.Dispose();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                LayoutStyle.DrawCode(g, _code, _font, new RectangleF(0, 0, Width, Height));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _font.Dispose();
                    Region?.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
