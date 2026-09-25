using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The one rule every "close means hide" window follows: only a close the <b>user</b> asked for is
    /// turned into a hide. A hidden top-level window still receives <c>WM_QUERYENDSESSION</c>, which
    /// WinForms raises as <c>FormClosing</c> with <see cref="CloseReason.WindowsShutDown"/>; cancelling
    /// that is what put "CyrFlip is preventing shutdown" on the sign-out screen. And
    /// <c>Application.Exit()</c> - the fatal-error path - is aborted by a single cancelled
    /// <see cref="CloseReason.ApplicationExitCall"/>, which left the app running after telling the user
    /// it would close.
    /// </summary>
    internal static class CloseToHide
    {
        /// <summary>
        /// Cancels <paramref name="e"/> and answers true when it is a user close (the caller then hides
        /// the window); leaves every other close alone and answers false.
        /// </summary>
        public static bool Intercept(FormClosingEventArgs e)
        {
            if (e.CloseReason != CloseReason.UserClosing) return false;
            e.Cancel = true;
            return true;
        }
    }
}
