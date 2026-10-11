using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The one rule every "close means hide" window follows: only a close that means "the user wants
    /// this window gone" is turned into a hide - the user's own close, and a <b>bare
    /// <c>WM_CLOSE</c></b> another program sent (a window manager, an AutoHotkey <c>WinClose</c>,
    /// <c>taskkill</c> without <c>/F</c>, Windows' Restart Manager during an update), which Windows
    /// reports as <see cref="CloseReason.TaskManagerClosing"/> (S0065 E-13). That bare close used to
    /// dispose the window - and the next open from the tray or the pipe threw
    /// <c>ObjectDisposedException</c> into the fatal path, taking the whole app. These are tray
    /// windows: ending the app is the tray's Exit, so a close that is not a session end is a hide.
    ///
    /// <para>What is still not cancelled: a hidden top-level window receives <c>WM_QUERYENDSESSION</c>,
    /// which WinForms raises as <c>FormClosing</c> with <see cref="CloseReason.WindowsShutDown"/>
    /// (cancelling that put "CyrFlip is preventing shutdown" on the sign-out screen), and
    /// <c>Application.Exit()</c> - the fatal-error path - is aborted by a single cancelled
    /// <see cref="CloseReason.ApplicationExitCall"/>.</para>
    /// </summary>
    internal static class CloseToHide
    {
        /// <summary>
        /// Cancels <paramref name="e"/> and answers true when the close means "gone for now" (the caller
        /// then hides the window): the user's own close and a bare program-sent
        /// <c>WM_CLOSE</c> (<see cref="CloseReason.TaskManagerClosing"/>, S0065 E-13). A session end and
        /// the fatal path's <c>Application.Exit()</c> are left alone, answering false.
        /// </summary>
        public static bool Intercept(FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing
                && e.CloseReason != CloseReason.TaskManagerClosing) return false;
            e.Cancel = true;
            return true;
        }
    }
}
