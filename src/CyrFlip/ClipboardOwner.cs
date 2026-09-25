using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// One paste CyrFlip has promised the clipboard but not yet handed over (ticket S0009, FP-1B).
    /// The owner thread records what happens to it; the flip worker waits on it.
    /// </summary>
    internal sealed class PasteOffer : IPasteSignal
    {
        private readonly object _lock = new object();
        private bool _armed, _renderedEarly, _consumed, _lost;
        private long _consumedAt;

        public PasteOffer(string text, uint locale, TransientMarks marks)
        {
            Text = text;
            Locale = locale;
            Marks = marks;
        }

        public string Text { get; }
        public uint Locale { get; }

        /// <summary>Which "do not record" markers the offer goes up with (<see cref="TransientMarks"/>).</summary>
        public TransientMarks Marks { get; }

        /// <summary>The clipboard sequence number right after the offer went up - our own write.</summary>
        public uint OwnSequence { get; internal set; }

        public long ConsumedAtMs { get { lock (_lock) return _consumedAt; } }

        /// <summary>
        /// The process that first asked for the text - the target, or the clipboard monitor that beat
        /// it to it. A process name, never anything the text contains; for the diagnostics log.
        /// </summary>
        public string RenderedBy { get; internal set; } = "";

        /// <summary>
        /// Called just before the Ctrl+V goes out. A render before this moment was not the target -
        /// the target has not been asked to paste yet - but a clipboard monitor fetching the text.
        /// </summary>
        public void Arm() { lock (_lock) _armed = true; }

        internal void MarkRendered()
        {
            lock (_lock)
            {
                if (!_armed) _renderedEarly = true;
                else if (!_consumed) { _consumed = true; _consumedAt = PasteWait.NowMs(); }
                Monitor.PulseAll(_lock);
            }
        }

        internal void MarkLost()
        {
            lock (_lock)
            {
                _lost = true;
                Monitor.PulseAll(_lock);
            }
        }

        public PasteSignalState Wait(int timeoutMs)
        {
            long deadline = PasteWait.NowMs() + Math.Max(0, timeoutMs);
            lock (_lock)
            {
                while (true)
                {
                    // Once rendered the text stays rendered, so the target's later read raises nothing:
                    // an early render is final, whatever follows it.
                    if (_renderedEarly) return PasteSignalState.RenderedEarly;
                    if (_consumed) return PasteSignalState.Consumed;
                    if (_lost) return PasteSignalState.Lost;
                    long left = deadline - PasteWait.NowMs();
                    if (left <= 0) return PasteSignalState.Pending;
                    Monitor.Wait(_lock, (int)left);
                }
            }
        }
    }

    /// <summary>
    /// The window that owns the clipboard while a flip's text is on it, so the text can be
    /// <b>delay-rendered</b>: the clipboard announces CF_UNICODETEXT with no data, and the target's
    /// read arrives here as <c>WM_RENDERFORMAT</c>. That message is the one reliable proof that the
    /// target has taken the paste - which is what the restore has to wait for. A fixed 140 ms wait
    /// let a busy Word, a Java IDE or above all a remote-desktop session read the clipboard <i>after</i>
    /// the restore and paste the user's previous clipboard - a password, sometimes - over the selection.
    ///
    /// <para>It needs a thread of its own: the flip worker is MTA with no message pump, and
    /// <c>OpenClipboard(NULL)</c> cannot own delayed rendering at all. So this is an STA thread with a
    /// message-only window and a plain message loop, doing nothing but answer the clipboard - the
    /// target blocks inside <c>GetClipboardData</c> until it has.</para>
    ///
    /// <para>Everything here degrades to the old behaviour rather than failing: an owner that cannot
    /// start or cannot take the clipboard makes <see cref="Shared"/> or <see cref="Offer"/> answer null,
    /// and the caller writes the text directly and waits the fixed time.</para>
    /// </summary>
    internal sealed class ClipboardOwner : IDisposable
    {
        private static readonly IntPtr HwndMessage = new IntPtr(-3);
        private const int WM_RENDERFORMAT = 0x0305;
        private const int WM_RENDERALLFORMATS = 0x0306;
        private const int WM_DESTROYCLIPBOARD = 0x0307;
        private const int WM_APP_OFFER = 0x8000 + 0x51;
        private const int WM_APP_STOP = 0x8000 + 0x52;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int nExitCode);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();

        private static readonly object SharedLock = new object();
        private static ClipboardOwner? _shared;
        private static bool _unavailable;

        /// <summary>
        /// The process-wide owner, started on first use; null when it could not be started, in which
        /// case the caller falls back to the plain write. A failed start is not retried every flip.
        /// </summary>
        public static ClipboardOwner? Shared
        {
            get
            {
                lock (SharedLock)
                {
                    if (_shared != null || _unavailable) return _shared;
                    try { _shared = new ClipboardOwner(); }
                    catch (Exception ex)
                    {
                        _unavailable = true;
                        ClipboardFlipLog.Log("clipboard owner unavailable: " + ex.GetType().Name);
                    }
                    return _shared;
                }
            }
        }

        /// <summary>Stop the shared owner if one was ever started (application exit).</summary>
        public static void ShutdownShared()
        {
            ClipboardOwner? owner;
            lock (SharedLock)
            {
                owner = _shared;
                _shared = null;
                _unavailable = true;
            }
            owner?.Dispose();
        }

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly object _requestLock = new object();
        private OwnerWindow? _window;
        private IntPtr _hwnd;

        // Owner-thread state. _request is handed over by Offer under _requestLock; _current is only
        // ever touched on the owner thread, inside its window procedure.
        private PasteOffer? _request;
        private PasteOffer? _current;

        private ClipboardOwner()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "CyrFlip clipboard owner" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.Wait(3000) || _hwnd == IntPtr.Zero)
                throw new InvalidOperationException("The clipboard owner window was not created.");
        }

        /// <summary>
        /// Put <paramref name="text"/> on the clipboard as a delayed-rendered paste. Returns null when
        /// the clipboard could not be taken; the caller then writes the text directly.
        /// </summary>
        /// <param name="marks">
        /// How the text is marked "do not record" (<see cref="TransientMarks"/>), which keeps it out of
        /// Win+V and - the point here - keeps a well-behaved clipboard monitor from fetching it before
        /// the target does, which would hide the target's own read from us.
        /// </param>
        public PasteOffer? Offer(string text, uint locale, TransientMarks marks)
        {
            var offer = new PasteOffer(text, locale, marks);
            lock (_requestLock) _request = offer;
            try
            {
                IntPtr sent = SendMessageTimeout(_hwnd, WM_APP_OFFER, IntPtr.Zero, IntPtr.Zero,
                    SMTO_ABORTIFHUNG, 3000, out IntPtr result);
                return sent != IntPtr.Zero && result != IntPtr.Zero ? offer : null;
            }
            finally
            {
                lock (_requestLock) _request = null;
            }
        }

        /// <summary>
        /// Is this window still the clipboard owner - i.e. has nobody written since our offer? Any
        /// writer empties the clipboard first, and that takes the ownership away. Asked by the restore
        /// with the clipboard held open, so the answer cannot change under it.
        /// </summary>
        public bool OwnsClipboard() => _hwnd != IntPtr.Zero && GetClipboardOwner() == _hwnd;

        private void Pump()
        {
            // Held in a field as well: NativeWindow's own handle table references it only weakly.
            var window = _window = new OwnerWindow(this);
            try
            {
                window.CreateHandle(new CreateParams { Caption = "CyrFlip Clipboard Owner", Parent = HwndMessage });
                _hwnd = window.Handle;
            }
            catch { _hwnd = IntPtr.Zero; }
            finally { _ready.Set(); }
            if (_hwnd == IntPtr.Zero) return;

            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // Destroying the owner while it still promises text makes Windows send WM_RENDERALLFORMATS,
            // which renders it for good - so a paste still in flight at exit is not left empty.
            try { window.DestroyHandle(); } catch { }
        }

        /// <summary>The offer, on the owner thread: take the clipboard and announce the text without data.</summary>
        private bool TakeClipboard()
        {
            PasteOffer? offer;
            lock (_requestLock) offer = _request;
            if (offer == null) return false;

            bool opened = false;
            for (int i = 0; i < 12 && !opened; i++)
            {
                opened = OpenClipboard(_hwnd);
                if (!opened) Thread.Sleep(15);
            }
            if (!opened) return false;

            bool ok = false;
            try
            {
                // Emptying makes this window the owner. If it already was one (an earlier offer nobody
                // restored over), Windows sends WM_DESTROYCLIPBOARD to it first - synchronously, right
                // here - which retires that offer before the new one takes its place.
                if (!EmptyClipboard()) return false;
                SetClipboardData(CF_UNICODETEXT, IntPtr.Zero); // NULL data = "ask me when you need it"
                // SetClipboardData answers NULL for a delayed format on success too, so success is
                // judged by what the clipboard now announces.
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return false;
                Win32Clipboard.SetCompanions(offer.Locale, offer.Marks);
                _current = offer;
                ok = true;
            }
            finally { CloseClipboard(); }

            offer.OwnSequence = GetClipboardSequenceNumber();
            return ok;
        }

        /// <summary>
        /// Hand the promised text over. Inside <c>WM_RENDERFORMAT</c> the clipboard is already open on
        /// the requester's behalf and must not be opened again; for <c>WM_RENDERALLFORMATS</c> it is
        /// opened here, and only rendered while this window is still the owner.
        /// </summary>
        private void Render(bool openFirst)
        {
            PasteOffer? offer = _current;
            if (offer == null) return;

            if (openFirst)
            {
                if (!OpenClipboard(_hwnd)) return;
                try
                {
                    if (GetClipboardOwner() == _hwnd) SetText(offer);
                }
                finally { CloseClipboard(); }
                return;
            }
            // Inside WM_RENDERFORMAT the requester holds the clipboard open - usually with a window of
            // its own, which names the process that asked.
            if (offer.RenderedBy.Length == 0) offer.RenderedBy = ProcessOf(GetOpenClipboardWindow());
            SetText(offer);
        }

        private static string ProcessOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(no window)";
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
                    return process.ProcessName;
            }
            catch { return "(unknown)"; }
        }

        private static void SetText(PasteOffer offer)
        {
            IntPtr hMem = Win32Clipboard.AllocBytes(Win32Clipboard.UnicodeBytes(offer.Text));
            if (hMem == IntPtr.Zero) return;
            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
            {
                Win32Clipboard.FreeBytes(hMem); // ownership not transferred on failure
                return;
            }
            offer.MarkRendered();
        }

        private bool WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_APP_OFFER:
                    m.Result = TakeClipboard() ? (IntPtr)1 : IntPtr.Zero;
                    return true;
                case WM_RENDERFORMAT:
                    if ((uint)m.WParam.ToInt64() == CF_UNICODETEXT) Render(openFirst: false);
                    return true;
                case WM_RENDERALLFORMATS:
                    Render(openFirst: true);
                    return true;
                case WM_DESTROYCLIPBOARD:
                    // Somebody emptied the clipboard - our own restore, or another program's copy.
                    _current?.MarkLost();
                    _current = null;
                    return true;
                case WM_APP_STOP:
                    PostQuitMessage(0);
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_hwnd == IntPtr.Zero) return;
            SendMessageTimeout(_hwnd, WM_APP_STOP, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _);
            _thread.Join(1000);
            _hwnd = IntPtr.Zero;
        }

        private sealed class OwnerWindow : NativeWindow
        {
            private readonly ClipboardOwner _owner;

            public OwnerWindow(ClipboardOwner owner) { _owner = owner; }

            protected override void WndProc(ref Message m)
            {
                // A target is blocked inside GetClipboardData while this runs; nothing here may throw
                // out into NativeWindow's handler, which would put up a dialog on a hidden thread.
                try
                {
                    if (_owner.WndProc(ref m)) return;
                }
                catch { return; }
                base.WndProc(ref m);
            }
        }
    }
}
