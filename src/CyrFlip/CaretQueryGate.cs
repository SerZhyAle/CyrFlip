using System;
using System.Threading;

namespace CyrFlip
{
    /// <summary>
    /// When the caret overlay may ask another process where its caret is (ticket S0011 LI-1).
    ///
    /// <para>The cross-process sources - UIA <c>GetCaretRange</c>, IAccessible2, managed UIA - used to
    /// run every ~120 ms whenever the foreground window had no Win32 caret, which is always the case in
    /// Chrome, Edge, VS Code, Slack and Teams. Chromium treats that traffic as an assistive technology:
    /// it switches its full accessibility tree on in every tab (CPU and memory), a query every 120 ms
    /// defeats its automatic shut-off, and VS Code with <c>accessibilitySupport: auto</c> can flip into
    /// screen-reader mode. All of that for a user reading a page or watching a video.</para>
    ///
    /// <para>The caret does not move without input, so the questions are asked only while there is
    /// input to follow: for <see cref="KeyWindowMs"/> after a physical key-down, and for
    /// <see cref="FocusWindowMs"/> after the foreground or the focus changed (a click into another
    /// field, Alt+Tab). Outside those windows the overlay keeps the last position and asks nothing.
    /// The in-process <c>GetGUIThreadInfo</c> path is free and is not gated.</para>
    /// </summary>
    internal static class CaretQueryGate
    {
        /// <summary>How long after a physical key-down the caret is followed.</summary>
        public const int KeyWindowMs = 3000;

        /// <summary>How long after a foreground or focus change the caret is looked for.</summary>
        public const int FocusWindowMs = 1500;

        // Environment.TickCount values. "Never" is far enough in the past that neither window is open
        // at start - the first foreground event or key opens them.
        private static int s_lastKeyTick = Environment.TickCount - 10 * KeyWindowMs;
        private static int s_lastFocusTick = Environment.TickCount;

        /// <summary>
        /// A physical key went down. Called from inside the <c>WH_KEYBOARD_LL</c> callback, so it is one
        /// field write and nothing else.
        /// </summary>
        public static void NoteKey() => Volatile.Write(ref s_lastKeyTick, Environment.TickCount);

        /// <summary>The foreground window or the focused element changed.</summary>
        public static void NoteFocusChange() => Volatile.Write(ref s_lastFocusTick, Environment.TickCount);

        /// <summary>The live answer, for the tracker thread.</summary>
        public static bool ShouldQueryNow()
            => ShouldQuery(Environment.TickCount, Volatile.Read(ref s_lastKeyTick), Volatile.Read(ref s_lastFocusTick));

        /// <summary>
        /// The rule itself. Tick arithmetic is unchecked so it survives <c>TickCount</c> wrapping after
        /// 24.9 days; a stamp "in the future" (never produced, but cheap to refuse) opens nothing.
        /// </summary>
        public static bool ShouldQuery(int nowTick, int lastKeyTick, int lastFocusTick)
            => Within(nowTick, lastKeyTick, KeyWindowMs) || Within(nowTick, lastFocusTick, FocusWindowMs);

        private static bool Within(int now, int then, int windowMs)
        {
            int elapsed = unchecked(now - then);
            return elapsed >= 0 && elapsed < windowMs;
        }
    }
}
