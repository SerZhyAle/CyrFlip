using System;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The sign-out / shutdown / restart path. An <see cref="ApplicationContext"/> with no main form
    /// never has its message loop ended at logoff: Windows sends <c>WM_QUERYENDSESSION</c> and
    /// <c>WM_ENDSESSION</c> and then terminates the process, so neither <c>Dispose</c> nor
    /// <c>Application.ApplicationExit</c> ever runs - the layout files survived every sign-out and the
    /// last note edit was the debounce's to lose (spec S0003 LC-2).
    ///
    /// <para>A hidden <b>top-level</b> window, because those two messages go only to top-level windows
    /// - a message-only window (<c>HWND_MESSAGE</c>) never sees them. <c>WS_EX_TOOLWINDOW</c> keeps it
    /// off Alt+Tab. <c>SystemEvents.SessionEnding</c> is deliberately not used: it is raised on the
    /// SystemEvents thread and would need marshalling back to the UI thread, which is exactly what is
    /// not guaranteed while the session is going away.</para>
    /// </summary>
    internal sealed class SessionEndWatcher : NativeWindow, IDisposable
    {
        internal const int WM_QUERYENDSESSION = 0x0011;
        internal const int WM_ENDSESSION = 0x0016;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private bool _ended;

        /// <summary>
        /// <c>WM_QUERYENDSESSION</c>: the session may end. The answer is always "yes" - CyrFlip never
        /// vetoes a sign-out - and this is the moment to start the cheap part of the flush.
        /// </summary>
        public event EventHandler? QueryEnding;

        /// <summary>
        /// <c>WM_ENDSESSION</c> with <c>wParam == TRUE</c>: the session is ending and the process is
        /// about to be terminated. Raised at most once; everything that must reach the disk has to be
        /// done synchronously inside the handler.
        /// </summary>
        public event EventHandler? Ending;

        internal const int WM_SETTINGCHANGE = 0x001A;
        internal const int WM_DISPLAYCHANGE = 0x007E;
        internal const int WM_DPICHANGED = 0x02E0;

        /// <summary>
        /// <c>WM_SETTINGCHANGE</c>, with the <c>SPI_*</c> action in the argument - the same broadcast
        /// reaches every top-level window, and this one is already here (ticket S0011 LI-6: a system
        /// cursor reload removes the branded I-beam).
        /// </summary>
        public event Action<uint>? SettingChanged;

        /// <summary><c>WM_DISPLAYCHANGE</c> or <c>WM_DPICHANGED</c>: a monitor or its scaling changed.</summary>
        public event EventHandler? DisplayChanged;

        internal const int WM_SYSCOLORCHANGE = 0x0015;
        internal const int WM_THEMECHANGED = 0x031A;
        internal const uint SPI_SETHIGHCONTRAST = 0x0043;

        /// <summary>
        /// Something the app's theme may depend on changed (ticket S0020): Windows' light/dark switch
        /// (<c>WM_SETTINGCHANGE</c> carrying <c>"ImmersiveColorSet"</c>), high contrast, the system colours
        /// or the visual style. Windows sends these inconsistently, so all of them are raised and
        /// <see cref="ThemeManager.OnSystemSignal"/> ignores the ones that change nothing.
        /// </summary>
        public event EventHandler? ThemeSignal;

        public SessionEndWatcher()
        {
            CreateHandle(new CreateParams
            {
                Caption = "CyrFlip Session End Watcher",
                ExStyle = WS_EX_TOOLWINDOW,
            });
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_QUERYENDSESSION:
                    try { QueryEnding?.Invoke(this, EventArgs.Empty); } catch { /* never veto */ }
                    m.Result = (IntPtr)1;
                    return;
                case WM_ENDSESSION:
                    if (m.WParam != IntPtr.Zero && !_ended)
                    {
                        _ended = true;
                        try { Ending?.Invoke(this, EventArgs.Empty); } catch { /* the session ends anyway */ }
                    }
                    m.Result = IntPtr.Zero;
                    return;
                case WM_SETTINGCHANGE:
                    uint action = unchecked((uint)m.WParam.ToInt64());
                    try { SettingChanged?.Invoke(action); } catch { /* a broadcast */ }
                    if (action == SPI_SETHIGHCONTRAST || (action == 0 && IsImmersiveColorSet(m.LParam)))
                        try { ThemeSignal?.Invoke(this, EventArgs.Empty); } catch { /* a broadcast */ }
                    break;
                case WM_SYSCOLORCHANGE:
                case WM_THEMECHANGED:
                    try { ThemeSignal?.Invoke(this, EventArgs.Empty); } catch { /* a broadcast */ }
                    break;
                case WM_DISPLAYCHANGE:
                case WM_DPICHANGED:
                    try { DisplayChanged?.Invoke(this, EventArgs.Empty); } catch { /* a broadcast */ }
                    break;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// The broadcast Windows sends when the apps' light/dark preference flips. Read only with a zero
        /// action, where the argument is the name of the changed area (WinForms' own SystemEvents reads
        /// the same argument the same way).
        /// </summary>
        private static bool IsImmersiveColorSet(IntPtr lParam)
        {
            if (lParam == IntPtr.Zero) return false;
            try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet"; }
            catch (AccessViolationException) { return false; }
        }

        public void Dispose() => DestroyHandle();
    }

    /// <summary>
    /// What CyrFlip does in the seconds Windows gives it at sign-out, <b>in the order that matters</b>
    /// (ticket S0003 LC-2, S0035 QN2-6): what carries data first, the optional optimisation last. The
    /// quick-notes compaction used to come second - so a sign-out during a background compaction
    /// waited for it on the journal lock, compacted a second time, and reached the queued
    /// clipboard-history records with no budget left, and they were lost. Now the compaction runs only
    /// when none is already running and the budget is still fresh, and every wait is bounded by what
    /// is left. The steps are delegates so the order is tested without a session to end.
    /// </summary>
    internal sealed class SessionEndSequence
    {
        /// <summary>The whole budget: Windows kills a process that takes much longer after WM_ENDSESSION.</summary>
        public static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

        /// <summary>The compaction is only started while at most this much of the budget is spent.</summary>
        public static readonly TimeSpan CompactionCutoff = TimeSpan.FromSeconds(1);

        /// <summary>The cap on waiting for queued clipboard-history writes.</summary>
        public static readonly TimeSpan HistoryDrain = TimeSpan.FromSeconds(1);

        /// <summary>The cap on waiting for queued diagnostic-log lines (S0030 HT-4).</summary>
        public static readonly TimeSpan LogDrain = TimeSpan.FromMilliseconds(500);

        /// <summary>1. The layout files - their absence means "CyrFlip is not running" (LAYOUT-SIGNAL rule 6).</summary>
        public Action Retract { get; set; } = () => { };
        /// <summary>2. The last note edit, appended - never a compaction.</summary>
        public Action FlushNotes { get; set; } = () => { };
        /// <summary>3. Clipboard-history records still queued, waited for at most the given time.</summary>
        public Action<TimeSpan> DrainHistory { get; set; } = _ => { };
        /// <summary>4. The system cursor.</summary>
        public Action RestoreCursor { get; set; } = () => { };
        /// <summary>True while a background compaction runs - then there is no second one.</summary>
        public Func<bool> CompactionRunning { get; set; } = () => false;
        /// <summary>5. The compaction a clean exit would have made; skipped, a later exit makes it.</summary>
        public Action Compact { get; set; } = () => { };
        /// <summary>6. Diagnostic lines still queued, waited for at most the given time.</summary>
        public Action<TimeSpan> FlushLogs { get; set; } = _ => { };

        /// <param name="elapsed">Time spent since WM_ENDSESSION arrived.</param>
        public void Run(Func<TimeSpan> elapsed)
        {
            Try(Retract);
            Try(FlushNotes);

            TimeSpan left = Budget - elapsed();
            if (left > TimeSpan.Zero)
                Try(() => DrainHistory(left < HistoryDrain ? left : HistoryDrain));

            Try(RestoreCursor);

            if (elapsed() < CompactionCutoff && !Safe(CompactionRunning))
                Try(Compact);

            left = Budget - elapsed();
            if (left > TimeSpan.Zero)
                Try(() => FlushLogs(left < LogDrain ? left : LogDrain));
        }

        private static void Try(Action step)
        {
            try { step(); } catch { /* one failed step must not cost the next */ }
        }

        private static bool Safe(Func<bool> question)
        {
            try { return question(); } catch { return true; } // cannot tell: do not start a second one
        }
    }
}
