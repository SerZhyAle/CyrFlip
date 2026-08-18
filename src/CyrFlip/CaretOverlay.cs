using System;
using System.Diagnostics;
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
    /// Feature #1b: a small marker (the layout code, or a colored dot) pinned next to the blinking text
    /// caret - the place that actually shows where text will land (the mouse pointer is often an
    /// arrow while you type).
    ///
    /// Caret position comes from three sources, tried in order:
    ///   1. <c>GetGUIThreadInfo</c> - fast, for classic Win32 edit controls.
    ///   2. COM UIA <c>IUIAutomationTextPattern2.GetCaretRange</c> (<see cref="UiaCaretCom"/>) - the
    ///      live-caret API; tracks the caret in Chromium/Electron webview inputs (VS Code chat,
    ///      browsers) where GetSelection reports a stale/whole-line rect.
    ///   3. managed UIA <c>TextPattern.GetSelection</c> - fallback for apps without GetCaretRange.
    /// Tracking runs on a background MTA thread so UIA's cross-process calls never block the UI;
    /// the overlay window itself is touched only via BeginInvoke on the UI thread.
    /// </summary>
    internal sealed class CaretOverlay : IDisposable
    {
        private readonly OverlayForm _form;
        private Thread? _thread;
        private volatile bool _running;
        private volatile string _code = "";
        private volatile string _klid = "";
        private volatile bool _caps;

        // UIA caret lookups are cross-process and expensive, so throttle them and reuse the last
        // position between polls. The cheap system-caret path still runs every tick.
        private const int UiaThrottleMs = 120;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastUiaMs = -1000;
        private bool _haveUia;
        private int _uiaX, _uiaY;

        public CaretOverlay(int size, bool dotMode = false)
            => _form = new OverlayForm(size, dotMode);

        public void Start()
        {
            _ = _form.Handle; // create the handle on the UI thread so BeginInvoke works
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "CyrFlip.CaretTracker" };
            _thread.SetApartmentState(ApartmentState.MTA); // UIA client prefers MTA
            _thread.Start();
        }

        /// <summary>Set the layout code, its KLID (which picks the colour) and the CapsLock state
        /// shown by the overlay (called on change).</summary>
        public void SetLayout(string code, string? klid = null, bool capsOn = false)
        {
            _code = code ?? "";
            _klid = klid ?? "";
            _caps = capsOn;
        }

        /// <summary>Switch between text label (the layout code) and colored dot rendering.</summary>
        public void SetDotMode(bool dot) => _form.SetDotMode(dot);

        /// <summary>How opaque the badge window is - read by the test that pins the marker's
        /// translucency, since the window itself is private to this class.</summary>
        internal double WindowOpacity => _form.Opacity;

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
            string klid = _klid;
            bool caps = _caps;
            if (code.Length == 0)
            {
                Post(false, 0, 0, code, klid, caps);
                return;
            }

            if (TryGetCaret(out int x, out int y))
                Post(true, x, y, code, klid, caps);
            else
                Post(false, 0, 0, code, klid, caps);
        }

        // What was last handed to the UI thread. The tracker ticks eleven times a second for the
        // whole life of the app, and most of those ticks say exactly what the previous one said -
        // overwhelmingly "still hidden" (a console, the desktop, any window with no caret). Posting
        // that costs an allocation and a wake-up of the UI thread to change nothing at all. The
        // initial values are the form's real initial state: created, hidden, no code.
        private bool _postedShow;
        private int _postedX, _postedY;
        private string _postedCode = "";
        private string _postedKlid = "";
        private bool _postedCaps;

        private void Post(bool show, int x, int y, string code, string klid, bool caps)
        {
            if (!_form.IsHandleCreated)
                return;
            if (show == _postedShow && x == _postedX && y == _postedY
                && code == _postedCode && klid == _postedKlid && caps == _postedCaps)
                return;
            _postedShow = show;
            _postedX = x;
            _postedY = y;
            _postedCode = code;
            _postedKlid = klid;
            _postedCaps = caps;
            try
            {
                _form.BeginInvoke((Action)(() =>
                {
                    if (show)
                    {
                        _form.SetCode(code, klid, caps);
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

        private bool TryGetCaret(out int x, out int y)
        {
            // Our own windows have a real caret already, and a VS Code editor has the companion
            // extension's marker at Monaco's caret - drawing over either is how the user ends up
            // looking at two markers a few pixels apart.
            if (IsOwnForegroundWindow() || EditorCaretSignal.ShouldYield())
            {
                _haveUia = false;
                x = 0; y = 0;
                return false;
            }

            // 1) System caret (classic Win32 edit controls) - cheap, run every tick.
            if (TrySystemCaret(out x, out y))
            {
                _haveUia = false; // a real system caret supersedes any cached UIA position
                return true;
            }

            // 2) Cross-process caret APIs (modern apps) - expensive, so throttle. Try in order:
            //    UIA GetCaretRange (TextPattern2: WinUI/UWP/WPF), then IAccessible2 (Chromium/Electron
            //    webviews like the VS Code chat box - the only source that works there), then the
            //    managed GetSelection path as a last resort.
            long now = _clock.ElapsedMilliseconds;
            if (now - _lastUiaMs >= UiaThrottleMs)
            {
                _lastUiaMs = now;
                if (UiaCaretCom.TryGetCaretRange(out int ux, out int uy)
                    || Ia2Caret.TryGetCaret(out ux, out uy)
                    || TryUiaCaret(out ux, out uy))
                {
                    _haveUia = true; _uiaX = ux; _uiaY = uy;
                    x = ux; y = uy;
                    return true;
                }
                _haveUia = false;
                x = 0; y = 0;
                return false;
            }

            // Between UIA polls: reuse the last known position so the marker doesn't flicker.
            if (_haveUia)
            {
                x = _uiaX; y = _uiaY;
                return true;
            }
            x = 0; y = 0;
            return false;
        }

        /// <summary>
        /// Our own process id, resolved once. The tracker asks "is this window ours?" twice per tick,
        /// eleven times a second, for the whole life of the app - and every
        /// <c>Process.GetCurrentProcess()</c> is a finalizable object the GC then has to walk.
        /// </summary>
        private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;

        private static bool IsOwnForegroundWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            return foreground != IntPtr.Zero
                && GetWindowThreadProcessId(foreground, out uint processId) != 0
                && processId == CurrentProcessId;
        }

        private static bool TrySystemCaret(out int x, out int y)
        {
            x = 0; y = 0;
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return false;

            uint tid = GetWindowThreadProcessId(fg, out uint processId);
            // CyrFlip's own text boxes (notably history search) already have the normal caret.
            // Putting our click-through topmost marker over them causes needless repainting and
            // can make the dialog look as though it is losing focus.
            if (processId == CurrentProcessId)
                return false;
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            if (GetGUIThreadInfo(tid, ref gti)
                && gti.hwndCaret != IntPtr.Zero
                && gti.rcCaret.Bottom - gti.rcCaret.Top > 0)
            {
                // Place the marker diagonally below-right of the caret so it never covers the
                // text on the current line (e.g. when arrowing back through it).
                var pt = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
                ClientToScreen(gti.hwndCaret, ref pt);
                x = pt.X + 2;
                y = pt.Y + 1;
                return true;
            }
            return false;
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

                // Collapse to the caret (the selection's end, where typing happens), then give it
                // one character of width so it has a bounding rect.
                TextPatternRange range = selection[0].Clone();
                range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
                range.ExpandToEnclosingUnit(TextUnit.Character);
                var rects = range.GetBoundingRectangles();
                if (rects.Length == 0)
                    return false;

                var r = rects[0];
                double h = r.Height > 0 ? r.Height : 16;
                // A caret/char rect is narrow. A wide rect means we got a whole line or the text
                // area (some controls report that for a collapsed caret) - unreliable, so skip it
                // rather than draw the marker at the edge of the box.
                if (r.Width > 4 * h)
                    return false;

                x = (int)r.Right + 2;     // diagonally below-right of the caret (see TrySystemCaret)
                y = (int)r.Bottom + 1;
                return true;
            }
            catch
            {
                return false; // UIA throws freely (ElementNotAvailable, NotSupported, ..)
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
            private string _klid = "";
            private bool _dotMode;
            private bool _caps;

            public OverlayForm(int size, bool dotMode = false)
            {
                _h = Math.Max(14, Math.Min(40, size == 0 ? 18 : size));
                _font = new Font("Segoe UI", _h * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
                _dotMode = dotMode;

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = ColorTranslator.FromHtml("#11161f");
                DoubleBuffered = true;
                // The badge sits on top of the user's text for as long as they are typing, so it is
                // translucent rather than solid: the character it lands next to stays readable through
                // it. Form.Opacity is a layered window, which the click-through/no-activate styles
                // below are happy to live with.
                Opacity = LayoutStyle.MarkerOpacity;
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

            public void SetCode(string code, string klid, bool caps)
            {
                if (code == _code && klid == _klid && caps == _caps)
                    return;
                bool sizeAffectingChange = code != _code;
                _code = code;
                _klid = klid;
                _caps = caps;
                if (sizeAffectingChange)
                    ResizeToContent();
                Invalidate();
            }

            /// <summary>Switch dot/text mode; must be called on the UI thread.</summary>
            public void SetDotMode(bool dot)
            {
                if (dot == _dotMode) return;
                _dotMode = dot;
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
                if (_dotMode)
                {
                    int d = Math.Max(6, _h / 3);
                    Size = new Size(d, d);
                    using var path = new GraphicsPath();
                    path.AddEllipse(0, 0, d, d);
                    Region oldRegion = Region;
                    Region = new Region(path);
                    oldRegion?.Dispose();
                }
                else
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
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                if (_dotMode)
                {
                    // Fill the entire (clipped-to-ellipse) window with the layout color. Here the colour
                    // is the whole marker - there are no letters to fall back on - which is why every
                    // one of the 25 curated layouts has its own shade.
                    g.Clear(_code.Length > 0 ? LayoutStyle.ColorForLayout(_klid, _code) : LayoutStyle.ColorFor("EN"));

                    // CapsLock: a 1px contrasting ring just inside the dot.
                    if (_caps)
                        using (var pen = new Pen(Color.FromArgb(235, Color.Black), 1f))
                            g.DrawEllipse(pen, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                }
                else
                {
                    LayoutStyle.DrawCode(g, _code, _font, new RectangleF(0, 0, Width, Height), _klid);

                    // CapsLock: a 1px layout-colour frame around the badge.
                    if (_caps)
                        LayoutStyle.DrawCapsFrame(g, new RectangleF(0, 0, Width, Height), Math.Max(3, _h / 4), _code, _klid);
                }
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
