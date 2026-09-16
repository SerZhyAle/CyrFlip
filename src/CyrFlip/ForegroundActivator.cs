using System;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Bring one of CyrFlip's own windows genuinely to the front when it is opened from a surface
    /// that never held the focus - the text context menu (<see cref="TextContextMenu"/>), which is
    /// deliberately a no-activate drop-down so the user's selection survives.
    ///
    /// <b>The failure this fixes.</b> Windows grants <c>SetForegroundWindow</c> only to a process that
    /// is already in the foreground or received the last input event, and it refuses <b>silently</b>:
    /// <see cref="Form.Activate"/> returns, the window really is visible - and it sits behind whatever
    /// the user was working in. From the user's side the menu item simply did nothing.
    ///
    /// The way around it is to share the foreground thread's input queue for the duration of the call
    /// (<c>AttachThreadInput</c>), which makes the two threads count as one for that rule. The attach
    /// is skipped when the foreground window is unknown, hung, or already ours, and is always undone.
    /// </summary>
    internal static class ForegroundActivator
    {
        public static void Activate(Form? form)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            Activate(form.Handle, form);
        }

        /// <summary>
        /// The same, for a raw window that is not ours: putting the user's own window back in the
        /// foreground before synthesizing input into it. Keyboard input always follows the foreground
        /// window, so a chord sent while something else holds it lands in the wrong application.
        /// </summary>
        public static void Activate(IntPtr window) => Activate(window, null);

        private static void Activate(IntPtr target, Form? form)
        {
            if (target == IntPtr.Zero) return;

            IntPtr foreground = GetForegroundWindow();
            uint ours = GetCurrentThreadId();
            uint theirs = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);

            // Guard against hung target processes: only attach thread input if the foreground window
            // responds to WM_NULL within 50 ms. If it is hung or unresponsive, skip AttachThreadInput
            // to prevent CyrFlip's UI thread from deadlocking.
            bool responsive = theirs != 0 && theirs != ours
                && SendMessageTimeout(foreground, 0, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 50, out _) != IntPtr.Zero;

            bool attached = responsive && AttachThreadInput(theirs, ours, true);
            try
            {
                BringWindowToTop(target);
                SetForegroundWindow(target);
                form?.Activate();
            }
            finally
            {
                if (attached) AttachThreadInput(theirs, ours, false);
            }
        }
    }
}
