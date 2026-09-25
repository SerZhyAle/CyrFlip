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
            }
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}
