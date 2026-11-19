using System;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>What a request for a layout came to (ticket S0109): the command line needs to tell "asked" from "was already there".</summary>
    internal enum SwitchOutcome
    {
        /// <summary>The request was posted (it is a request, not proof that the window took it).</summary>
        Requested,
        /// <summary>The window's thread already has that layout; nothing was posted.</summary>
        AlreadyActive,
        /// <summary>There is no window to ask.</summary>
        NoWindow,
    }

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
                Request(hwnd, target);
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

            IntPtr current = hwnd != IntPtr.Zero ? HklOfWindow(hwnd) : GetKeyboardLayout(0);
            Request(hwnd, NextAfter(installed, current));
            return true;
        }

        /// <summary>
        /// The layout the window's input thread has right now, for the command line (S0109): the window itself
        /// is resolved to its input window first, the way the marker does, and a request for that layout posts nothing.
        /// </summary>
        internal static SwitchOutcome SwitchToHkl(IntPtr hwnd, IntPtr hkl)
        {
            if (hwnd == IntPtr.Zero || hkl == IntPtr.Zero) return SwitchOutcome.NoWindow;
            if (HklOfWindow(hwnd) == hkl) return SwitchOutcome.AlreadyActive;
            Request(hwnd, hkl);
            return SwitchOutcome.Requested;
        }

        /// <summary>
        /// The layout after <paramref name="current"/> in <paramref name="installed"/>, wrapping at the end: the
        /// rotation of the tray click, moved here unchanged so a command and the click cannot disagree. An unlisted
        /// <paramref name="current"/> (shouldn't happen) still gives a sensible destination.
        /// </summary>
        internal static IntPtr NextAfter(IntPtr[] installed, IntPtr current)
        {
            int index = Array.IndexOf(installed, current);
            IntPtr next = index >= 0 ? installed[(index + 1) % installed.Length] : installed[0];
            if (next == current)
                next = installed[(index + 1 + installed.Length) % installed.Length == 0 ? 1 : 0];
            return next;
        }

        /// <summary>The layout of the thread that owns the window's input window.</summary>
        private static IntPtr HklOfWindow(IntPtr hwnd) => CursorIndicator.HklOf(hwnd);

        /// <summary>
        /// The one posting tail of both public switches: the window's input window (and the window itself when it
        /// is another), every top-level window by broadcast, and this thread. A zero window posts to nobody but the
        /// broadcast, as the tray click always did.
        /// </summary>
        internal static void Request(IntPtr hwnd, IntPtr hkl)
        {
            IntPtr inputWnd = hwnd != IntPtr.Zero ? CursorIndicator.InputWindow(hwnd) : IntPtr.Zero;
            if (inputWnd != IntPtr.Zero)
                PostMessage(inputWnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            if (hwnd != IntPtr.Zero && hwnd != inputWnd)
                PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            PostMessage(HWND_BROADCAST, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            ActivateKeyboardLayout(hkl, 0);
        }
    }
}
