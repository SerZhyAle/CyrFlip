using System;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Switches the foreground window's input layout after a conversion (feature:
    /// "Change the layout after converting text").
    ///
    /// Uses <c>PostMessage(WM_INPUTLANGCHANGEREQUEST)</c> against the target window so the change
    /// applies to that app's input thread - <c>ActivateKeyboardLayout</c> only affects our own
    /// thread and wouldn't move the user's actual typing layout. The destination HKL is resolved
    /// from the installed layout list; if the requested layout isn't installed, this is a no-op.
    /// </summary>
    internal static class LayoutSwitcher
    {
        private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

        /// <summary>
        /// Switch the foreground input thread to the exact configured keyboard layout. Only a layout
        /// the user really has installed is used - see <see cref="KeyboardLayoutConverter.ResolveInstalled"/>,
        /// which keeps a stale profile from re-adding a removed layout to Windows behind the user's back.
        /// </summary>
        public static void SwitchTo(IntPtr hwnd, string targetKlid)
        {
            if (hwnd == IntPtr.Zero) return;
            IntPtr target = KeyboardLayoutConverter.ResolveInstalled(targetKlid);
            if (target != IntPtr.Zero)
            {
                IntPtr inputWnd = CursorIndicator.InputWindow(hwnd);
                PostMessage(inputWnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
                if (inputWnd != hwnd)
                    PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
                PostMessage(HWND_BROADCAST, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
                ActivateKeyboardLayout(target, 0);
            }
        }

        /// <summary>
        /// Advance the window's input layout to the next one in Windows' own rotation - the same
        /// order Alt+Shift walks, because it is the same list (<c>GetKeyboardLayoutList</c> returns
        /// the installed layouts in preload order) - wrapping around at the end. Drives the single
        /// left click on the tray icon. No-op with fewer than two layouts installed.
        ///
        /// The next HKL is computed here rather than posted as <c>HKL_NEXT</c> so the rotation is
        /// the user's full layout list even in apps that keep a per-window layout.
        /// </summary>
        /// <returns>True when a switch was requested.</returns>
        public static bool SwitchToNext(IntPtr hwnd)
        {
            IntPtr[] installed = KeyboardLayoutConverter.InstalledLayouts();
            if (installed.Length < 2) return false;

            IntPtr inputWnd = hwnd != IntPtr.Zero ? CursorIndicator.InputWindow(hwnd) : IntPtr.Zero;
            IntPtr target = inputWnd != IntPtr.Zero ? inputWnd : hwnd;

            IntPtr current = target != IntPtr.Zero
                ? GetKeyboardLayout(GetWindowThreadProcessId(target, out _))
                : GetKeyboardLayout(0);

            int index = Array.IndexOf(installed, current);
            // An unlisted current layout (shouldn't happen) still gives a sensible destination.
            IntPtr next = index >= 0 ? installed[(index + 1) % installed.Length] : installed[0];
            if (next == current)
                next = installed[(index + 1 + installed.Length) % installed.Length == 0 ? 1 : 0];

            if (target != IntPtr.Zero)
            {
                PostMessage(target, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, next);
                if (hwnd != IntPtr.Zero && hwnd != target)
                    PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, next);
            }
            PostMessage(HWND_BROADCAST, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, next);
            ActivateKeyboardLayout(next, 0);
            return true;
        }
    }
}
