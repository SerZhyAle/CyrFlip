using System;

namespace CyrFlip
{
    /// <summary>
    /// The "don't let the computer sleep / don't let the screen turn off" feature. Two independent
    /// switches map onto the two <see cref="WindowInterop.EXECUTION_STATE"/> flags and are OR-ed
    /// into a single <c>SetThreadExecutionState</c> call:
    /// <list type="bullet">
    ///   <item><see cref="KeepSystemAwake"/> → <c>ES_SYSTEM_REQUIRED</c> (system won't sleep).</item>
    ///   <item><see cref="KeepScreenOn"/> → <c>ES_DISPLAY_REQUIRED</c> (display won't blank / lock on
    ///   idle - the same "as while watching video" behaviour any media player asks for).</item>
    /// </list>
    ///
    /// State lives only in memory and both start OFF on every launch by design - "keep awake" is a
    /// deliberate per-session action, so it is never persisted to the registry (see the spec at
    /// <c>PLAN/done/KeepAwake_Spec_Idea_v0.1.md</c>, §5).
    ///
    /// <c>ES_CONTINUOUS</c> makes the request sticky, so one call per change is enough. The request
    /// is bound to the thread that made it, so this must be driven from a long-lived thread (the
    /// tray UI thread); that binding also clears the request automatically if the process is killed.
    /// On a normal exit we still <see cref="Reset"/> explicitly rather than rely on that alone (§4).
    /// </summary>
    internal static class KeepAwake
    {
        public static bool KeepSystemAwake { get; private set; } // option 1: don't sleep
        public static bool KeepScreenOn { get; private set; }    // option 2: don't blank/lock the screen

        /// <summary>
        /// Seam for unit tests (§10): the real work is the P/Invoke; tests swap in a recorder to
        /// inspect the composed mask without touching the machine's power policy.
        /// </summary>
        internal static Func<WindowInterop.EXECUTION_STATE, WindowInterop.EXECUTION_STATE> SetExecutionState =
            WindowInterop.SetThreadExecutionState;

        /// <summary>Toggle "keep the system awake" and re-send the combined mask.</summary>
        public static void SetSystemAwake(bool on)
        {
            KeepSystemAwake = on;
            Apply();
        }

        /// <summary>Toggle "keep the screen on" and re-send the combined mask.</summary>
        public static void SetScreenOn(bool on)
        {
            KeepScreenOn = on;
            Apply();
        }

        /// <summary>
        /// Drop both requests back to bare <c>ES_CONTINUOUS</c>, restoring Windows' normal idle
        /// timeouts. Called when both switches go off and on application exit (§4).
        /// </summary>
        public static void Reset()
        {
            KeepSystemAwake = false;
            KeepScreenOn = false;
            Apply();
        }

        /// <summary>Builds the flag mask from the two switches (both off → bare <c>ES_CONTINUOUS</c>).</summary>
        internal static WindowInterop.EXECUTION_STATE BuildMask()
        {
            WindowInterop.EXECUTION_STATE flags = WindowInterop.EXECUTION_STATE.ES_CONTINUOUS;
            if (KeepSystemAwake) flags |= WindowInterop.EXECUTION_STATE.ES_SYSTEM_REQUIRED;
            if (KeepScreenOn) flags |= WindowInterop.EXECUTION_STATE.ES_DISPLAY_REQUIRED;
            return flags;
        }

        private static void Apply() => SetExecutionState(BuildMask());
    }
}
