using System;

namespace CyrFlip
{
    /// <summary>
    /// Companion surface kinds that hold WS_EX_TOPMOST and are subject to the yield policy (S0094).
    /// </summary>
    public enum CompanionSurfaceKind
    {
        ClipboardHistory,
        TranslationResult,
        Symbols,
        TrayHotkeys,
        TrafficLight,
    }

    /// <summary>
    /// Pure decision logic for companion surface topmost state (S0094).
    /// Decides whether a satellite surface should retain WS_EX_TOPMOST or yield it (HWND_NOTOPMOST)
    /// when a selection surface (dialog or native dropdown) is active in this process.
    /// </summary>
    public static class TopmostYieldPolicy
    {
        /// <summary>
        /// Pure evaluation of whether a companion surface should currently be topmost.
        /// </summary>
        /// <param name="kind">The companion surface kind.</param>
        /// <param name="isVisible">Whether the companion window is currently visible.</param>
        /// <param name="isTrafficLightPinned">Configured pinned state for the traffic light (ignored for other kinds).</param>
        /// <param name="hasActiveSelectionSurface">Whether any picker, dialog or native combo dropdown is active in our process.</param>
        /// <returns>True if the companion surface should be topmost; false otherwise.</returns>
        public static bool ShouldBeTopmost(
            CompanionSurfaceKind kind,
            bool isVisible,
            bool isTrafficLightPinned,
            bool hasActiveSelectionSurface)
        {
            // Q2: Hidden satellites don't yield - no action needed when invisible.
            if (!isVisible) return false;

            // Base configured topmost state:
            // TrafficLight follows user setting (TrafficLightPinned); all other companions are topmost by default.
            bool configuredTopmost = (kind == CompanionSurfaceKind.TrafficLight)
                ? isTrafficLightPinned
                : true;

            if (!configuredTopmost) return false;

            // Yield topmost while a selection surface / picker / native dropdown is active
            if (hasActiveSelectionSurface) return false;

            return true;
        }

        /// <summary>
        /// Helper computing selection surface state from dialog foreground and dropped dropdown state.
        /// </summary>
        public static bool IsSelectionSurfaceActive(bool isDialogActive, bool isNativeListDropped)
        {
            return isDialogActive || isNativeListDropped;
        }
    }
}
