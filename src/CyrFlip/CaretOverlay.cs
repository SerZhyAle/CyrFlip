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
    /// Caret position comes from four sources, tried in order:
    ///   1. <c>GetGUIThreadInfo</c> - fast, for classic Win32 edit controls.
    ///   2. COM UIA <c>IUIAutomationTextPattern2.GetCaretRange</c> (<see cref="UiaCaretCom"/>) -
    ///      WinUI/UWP/WPF.
    ///   3. IAccessible2 (<see cref="Ia2Caret"/>) - Chromium/Electron (VS Code chat, browsers).
    ///   4. managed UIA <c>TextPattern.GetSelection</c> - fallback for apps without GetCaretRange.
    /// Tracking runs on a background MTA thread so UIA's cross-process calls never block the UI;
    /// the overlay window itself is touched only via BeginInvoke on the UI thread.
    ///
    /// <para>Sources 2-4 are asked only while there is input to follow (<see cref="CaretQueryGate"/>,
    /// ticket S0011 LI-1) - asking Chromium eight times a second forever kept its whole accessibility
    /// tree switched on. Where the marker goes for a caret is <see cref="CaretPlacement"/>'s decision
    /// (LI-7), its size follows the DPI of the caret's monitor (LI-3), and a badge whose tracker has
    /// gone quiet - blocked inside a hung app - is hidden rather than left topmost over whatever the
    /// user switched to (LI-11).</para>
    ///
    /// <para><b>Out of the app's theme on purpose</b> (<c>APP-STYLE</c> rule 5): the badge is drawn over
    /// the user's own text in whatever window that is, in <see cref="LayoutStyle"/>'s layout colours with
    /// their own outline - a themed colour here would mean nothing and be unreadable half the time. Its
    /// window is therefore a plain <see cref="Form"/>, not a <see cref="ThemedForm"/>
    /// (<c>ThemeCoverageTests</c> lists it with this reason).</para>
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
        private CaretRect _uiaCaret;
        private IntPtr _uiaForeground;

        /// <summary>A tracker silent for longer than this is presumed blocked, and its badge hidden.</summary>
        internal const int StaleAfterMs = 1000;

        // The tracker's heartbeat (clock ms at the end of its last tick) and the UI thread's hide
        // generation: every hide the UI thread decides on its own bumps it, so the tracker's
        // "I already posted exactly this" shortcut cannot keep a hidden badge hidden.
        private long _heartbeatMs;
        private int _generation;
        private System.Windows.Forms.Timer? _staleTimer;

        // Foreground and focus changes, out of context (no DLL injected anywhere). The delegate is a
        // field because the native side holds only a function pointer to it.
        private WinEventDelegate? _winEventProc;
        private IntPtr _foregroundHook;
        private IntPtr _focusHook;

        public CaretOverlay(int size, bool dotMode = false)
            => _form = new OverlayForm(size, dotMode);

        public void Start()
        {
            _ = _form.Handle; // create the handle on the UI thread so BeginInvoke works
            Volatile.Write(ref _heartbeatMs, _clock.ElapsedMilliseconds);
            InstallWinEventHooks();
            _staleTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _staleTimer.Tick += (_, _) => HideIfTrackerIsSilent();
            _staleTimer.Start();

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

        /// <summary>The marker size setting (base pixels at 100%); UI thread.</summary>
        public void SetBaseSize(int size) => _form.SetBaseSize(size);

        /// <summary>How opaque the badge window is - read by the test that pins the marker's
        /// translucency, since the window itself is private to this class.</summary>
        internal double WindowOpacity => _form.Opacity;

        /// <summary>The badge's current height in pixels - for the DPI tests.</summary>
        internal int BadgeHeight => _form.BadgeHeight;

        /// <summary>Test seam: size the badge as it would be on a monitor of <paramref name="dpi"/>.</summary>
        internal void ApplyDpiForTest(int dpi) => _form.ApplyDpi(dpi);

        /// <summary>
        /// A monitor or its scaling changed (S0036 UI-4): the next placement asks the monitor's DPI
        /// again. It used to be re-read only when the caret moved to a <i>different</i> monitor, and
        /// a monitor whose scaling changed keeps its handle - the badge stayed at the old size until
        /// the caret visited another screen. Posted, so nothing is done inside the broadcast.
        /// </summary>
        public void OnDisplayChanged()
        {
            if (_form.IsDisposed || !_form.IsHandleCreated) return;
            try { _form.BeginInvoke((Action)_form.ForgetMonitor); }
            catch (InvalidOperationException) { /* torn down meanwhile */ }
        }

        // ------------------------------------------------------------------ foreground / focus

        private void InstallWinEventHooks()
        {
            _winEventProc = OnWinEvent;
            const uint flags = WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS;
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, flags);
            _focusHook = SetWinEventHook(EVENT_OBJECT_FOCUS, EVENT_OBJECT_FOCUS, IntPtr.Zero, _winEventProc, 0, 0, flags);
        }

        /// <summary>
        /// Runs on the UI thread (the thread that installed the hooks). A focus change opens the
        /// caret query window; a foreground change also hides the badge until the tracker reports a
        /// position for the new window - a tracker blocked in the old one would otherwise leave the
        /// badge floating over the new one.
        /// </summary>
        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                CaretQueryGate.NoteFocusChange();
                if (eventType == EVENT_SYSTEM_FOREGROUND)
                    HideUntilNextReport();
            }
            catch { /* a WinEvent callback must never throw */ }
        }

        private void HideUntilNextReport()
        {
            Interlocked.Increment(ref _generation);
            _form.HideOverlay();
        }

        private void HideIfTrackerIsSilent()
        {
            if (!_form.Visible) return;
            if (_clock.ElapsedMilliseconds - Volatile.Read(ref _heartbeatMs) > StaleAfterMs)
                HideUntilNextReport();
        }

        // ------------------------------------------------------------------ tracker thread

        private void Loop()
        {
            while (_running)
            {
                try { Tick(); }
                catch { /* never let tracking kill the app */ }
                Volatile.Write(ref _heartbeatMs, _clock.ElapsedMilliseconds);
                Thread.Sleep(90);
            }
        }

        private void Tick()
        {
            string code = _code;
            string klid = _klid;
            bool caps = _caps;
            IntPtr fg = GetForegroundWindow();
            if (code.Length == 0)
            {
                Post(false, default, fg, code, klid, caps);
                return;
            }

            if (TryGetCaret(fg, out CaretRect caret))
            {
                Remember(fg, caret);
                Post(true, caret, fg, code, klid, caps);
            }
            else
                Post(false, default, fg, code, klid, caps);
        }

        // What was last handed to the UI thread. The tracker ticks eleven times a second for the
        // whole life of the app, and most of those ticks say exactly what the previous one said -
        // overwhelmingly "still hidden" (a console, the desktop, any window with no caret). Posting
        // that costs an allocation and a wake-up of the UI thread to change nothing at all. The
        // initial values are the form's real initial state: created, hidden, no code.
        private bool _postedShow;
        private CaretRect _postedCaret;
        private IntPtr _postedForeground;
        private string _postedCode = "";
        private string _postedKlid = "";
        private bool _postedCaps;
        private int _postedGeneration;

        private void Post(bool show, CaretRect caret, IntPtr fg, string code, string klid, bool caps)
        {
            if (!_form.IsHandleCreated)
                return;
            int generation = Volatile.Read(ref _generation);
            if (show == _postedShow && caret.Equals(_postedCaret) && fg == _postedForeground
                && code == _postedCode && klid == _postedKlid && caps == _postedCaps
                && generation == _postedGeneration)
                return;
            _postedShow = show;
            _postedCaret = caret;
            _postedForeground = fg;
            _postedCode = code;
            _postedKlid = klid;
            _postedCaps = caps;
            _postedGeneration = generation;
            try
            {
                _form.BeginInvoke((Action)(() =>
                {
                    // Measured for a window that is no longer in front, or overtaken by a hide the UI
                    // thread made since: showing it now would put the badge over the wrong window.
                    bool current = generation == Volatile.Read(ref _generation) && fg == GetForegroundWindow();
                    if (show && current)
                    {
                        _form.SetCode(code, klid, caps);
                        _form.ShowAt(caret);
                    }
                    else
                    {
                        _form.HideOverlay();
                    }
                }));
            }
            catch (InvalidOperationException) { /* form disposing */ }
        }

        // The last caret the tracker found, for the text menu's keyboard chord (S0045 K2). One object
        // swapped whole, so the UI thread never reads a window from one tick and a position from another.
        private sealed class Sighting
        {
            public IntPtr Window;
            public CaretRect Caret;
            public long AtMs;
        }

        private Sighting? _sighting;

        private void Remember(IntPtr fg, CaretRect caret)
        {
            Sighting? last = Volatile.Read(ref _sighting);
            long now = _clock.ElapsedMilliseconds;
            // Most ticks see the same caret: do not allocate for them, only refresh the stamp.
            if (last != null && last.Window == fg && last.Caret.Equals(caret)) { Volatile.Write(ref last.AtMs, now); return; }
            Volatile.Write(ref _sighting, new Sighting { Window = fg, Caret = caret, AtMs = now });
        }

        /// <summary>How old a caret sighting may be and still say where the caret is now.</summary>
        internal const int SightingFreshMs = 2000;

        /// <summary>
        /// Where the tracker last saw the caret of <paramref name="window"/>, if it saw it within
        /// <see cref="SightingFreshMs"/> - the only caret source that covers Chromium and WinUI without
        /// a cross-process call on the UI thread. False while the overlay is off (nothing tracks then).
        /// </summary>
        public bool TryGetRecentCaret(IntPtr window, out CaretRect caret)
        {
            Sighting? last = Volatile.Read(ref _sighting);
            caret = default;
            if (last == null || last.Window != window
                || _clock.ElapsedMilliseconds - Volatile.Read(ref last.AtMs) > SightingFreshMs)
                return false;
            caret = last.Caret;
            return true;
        }

        private bool TryGetCaret(IntPtr fg, out CaretRect caret)
        {
            // Our own windows have a real caret already, and a VS Code editor has the companion
            // extension's marker at Monaco's caret - drawing over either is how the user ends up
            // looking at two markers a few pixels apart.
            if (IsOwnWindow(fg) || EditorCaretSignal.ShouldYield())
            {
                _haveUia = false;
                caret = default;
                return false;
            }

            // 1) System caret (classic Win32 edit controls) - cheap, run every tick.
            if (TrySystemCaret(fg, out caret))
            {
                _haveUia = false; // a real system caret supersedes any cached UIA position
                return true;
            }

            // A cached position belongs to the window it was measured in.
            if (_haveUia && _uiaForeground != fg)
                _haveUia = false;

            // 2) Cross-process caret APIs (modern apps) - expensive, so throttled, and asked only while
            //    the user is typing or has just moved the focus (S0011 LI-1). Outside that the caret
            //    has not moved, so the last position stands and nobody is asked anything. In order:
            //    UIA GetCaretRange (TextPattern2: WinUI/UWP/WPF), then IAccessible2 (Chromium/Electron
            //    webviews like the VS Code chat box - the only source that works there), then the
            //    managed GetSelection path as a last resort.
            long now = _clock.ElapsedMilliseconds;
            if (now - _lastUiaMs >= UiaThrottleMs && CaretQueryGate.ShouldQueryNow())
            {
                _lastUiaMs = now;
                if (UiaCaretCom.TryGetCaretRange(out CaretRect found)
                    || Ia2Caret.TryGetCaret(out found)
                    || TryUiaCaret(out found))
                {
                    _haveUia = true;
                    _uiaCaret = found;
                    _uiaForeground = fg;
                    caret = found;
                    return true;
                }
                _haveUia = false;
                caret = default;
                return false;
            }

            // Between polls, and while the gate is closed: the last known position.
            if (_haveUia)
            {
                caret = _uiaCaret;
                return true;
            }
            caret = default;
            return false;
        }

        /// <summary>
        /// Our own process id, resolved once. The tracker asks "is this window ours?" every tick,
        /// eleven times a second, for the whole life of the app - and every
        /// <c>Process.GetCurrentProcess()</c> is a finalizable object the GC then has to walk.
        /// </summary>
        private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;

        private static bool IsOwnWindow(IntPtr hwnd)
            => hwnd != IntPtr.Zero
                && GetWindowThreadProcessId(hwnd, out uint processId) != 0
                && processId == CurrentProcessId;

        internal static bool TrySystemCaret(IntPtr fg, out CaretRect caret)
        {
            caret = default;
            if (fg == IntPtr.Zero)
                return false;

            // CyrFlip's own text boxes (notably history search) are excluded by the caller: they
            // already have the normal caret, and a click-through topmost marker over them causes
            // needless repainting.
            uint tid = GetWindowThreadProcessId(fg, out _);
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            if (!GetGUIThreadInfo(tid, ref gti)
                || gti.hwndCaret == IntPtr.Zero
                || gti.rcCaret.Bottom - gti.rcCaret.Top <= 0)
                return false;

            var top = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Top };
            var bottom = new POINT { X = gti.rcCaret.Right, Y = gti.rcCaret.Bottom };
            ClientToPhysicalScreen(gti.hwndCaret, ref top);
            ClientToPhysicalScreen(gti.hwndCaret, ref bottom);
            caret = new CaretRect(bottom.X, top.Y, bottom.Y);
            return true;
        }

        /// <summary>
        /// <c>ClientToScreen</c> for a window of any DPI awareness (ticket S0011 LI-2). The caret rect of
        /// a DPI-unaware or system-aware window is in that window's <b>logical</b> client coordinates;
        /// this process is per-monitor aware, so a plain <c>ClientToScreen</c> read them as physical and
        /// the marker landed up and left of the caret on any scaled monitor - further off the further
        /// the caret was from the client origin. The conversion is done in the window's own DPI
        /// context, then lifted to physical pixels. Per-monitor-aware windows take neither step.
        /// </summary>
        internal static void ClientToPhysicalScreen(IntPtr hwnd, ref POINT pt)
        {
            IntPtr context = IntPtr.Zero;
            int awareness = 2;
            try
            {
                context = GetWindowDpiAwarenessContext(hwnd);
                if (context != IntPtr.Zero)
                    awareness = GetAwarenessFromDpiAwarenessContext(context);
            }
            catch (EntryPointNotFoundException) { /* pre-1607: the old behaviour */ }

            if (awareness == 2 || context == IntPtr.Zero)
            {
                ClientToScreen(hwnd, ref pt);
                return;
            }

            IntPtr previous = SetThreadDpiAwarenessContext(context);
            try { ClientToScreen(hwnd, ref pt); }
            finally
            {
                if (previous != IntPtr.Zero)
                    SetThreadDpiAwarenessContext(previous);
            }
            LogicalToPhysicalPointForPerMonitorDPI(hwnd, ref pt);
        }

        internal static bool TryUiaCaret(out CaretRect caret)
        {
            caret = default;
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
                // A caret/char rect is narrow. A wide rect means we got a whole line or the text
                // area (some controls report that for a collapsed caret) - unreliable, so skip it
                // rather than draw the marker at the edge of the box.
                if (CaretGeometry.IsWholeLine(r.Width, r.Height))
                    return false;

                double h = r.Height > 0 ? r.Height : 16;
                caret = new CaretRect((int)r.Right, (int)r.Top, (int)(r.Top + h));
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
            _staleTimer?.Dispose();
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
            if (_focusHook != IntPtr.Zero) UnhookWinEvent(_focusHook);
            _foregroundHook = _focusHook = IntPtr.Zero;
            _form.Dispose();
        }

        // ------------------------------------------------------------------ overlay window

        private sealed class OverlayForm : Form
        {
            private const int WM_DPICHANGED = 0x02E0;

            private int _baseSize;
            private int _dpi = 96;
            private IntPtr _monitor;
            private int _h;
            private Font _font;
            private string _code = "";
            private string _klid = "";
            private bool _dotMode;
            private bool _caps;

            public OverlayForm(int size, bool dotMode = false)
            {
                _baseSize = size;
                _h = MarkerSize.OverlayHeight(_baseSize, _dpi);
                _font = NewFont(_h);
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

            public int BadgeHeight => _h;

            private static Font NewFont(int h) => new Font("Segoe UI", h * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);

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

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // The badge is ours, drawn over the user's text: never part of a screenshot (S0026 4.2).
                ScreenCapture.ExcludeFromCapture(Handle);
            }

            // The badge sizes itself for each monitor (ApplyDpi); WinForms' own rescale on a DPI
            // change would only fight it.
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_DPICHANGED)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref m);
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

            /// <summary>The marker size setting changed; UI thread.</summary>
            public void SetBaseSize(int size)
            {
                if (size == _baseSize) return;
                _baseSize = size;
                Rescale();
            }

            /// <summary>Forget the monitor, so the next <see cref="ShowAt"/> reads its DPI again.</summary>
            public void ForgetMonitor() => _monitor = IntPtr.Zero;

            /// <summary>Size the badge for a monitor of <paramref name="dpi"/>; rebuilt only on a change.</summary>
            public void ApplyDpi(int dpi)
            {
                if (dpi == _dpi) return;
                _dpi = dpi;
                Rescale();
            }

            private void Rescale()
            {
                int h = MarkerSize.OverlayHeight(_baseSize, _dpi);
                if (h == _h) return;
                _h = h;
                Font old = _font;
                _font = NewFont(_h);
                old.Dispose();
                ResizeToContent();
                Invalidate();
            }

            public void ShowAt(CaretRect caret)
            {
                Point probe = CaretPlacement.MonitorProbe(caret);
                IntPtr monitor = MarkerSize.MonitorAt(probe.X, probe.Y);
                if (monitor != _monitor)
                {
                    _monitor = monitor;
                    ApplyDpi(MarkerSize.MonitorDpi(monitor));
                }

                Point at = CaretPlacement.Place(caret, Size, Screen.FromPoint(probe).Bounds);
                Location = at;
                if (!Visible)
                    Show(); // ShowWithoutActivation => doesn't steal focus from the text field
                else
                    SetWindowPos(Handle, HWND_TOPMOST, at.X, at.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
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
