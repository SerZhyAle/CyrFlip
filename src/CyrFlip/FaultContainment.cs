using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// The fault-containment policy D3 of S0061, decided 2026-10-11: <b>(b)+(c)</b> - a fault in one
    /// UI-thread handler costs that handler, not the process. The tray lives, the indicator lives,
    /// and the hooks are re-armed by their own watchdog on its next tick; the user gets one
    /// balloon. What keeps this from hiding a fault-loop is the (c) half: <see cref="MaxFaults"/>
    /// contained faults inside <see cref="Window"/> still end the process through
    /// <see cref="Program.Fatal"/>, because a handler that faults on every keystroke is a dead app
    /// that only looks alive.
    ///
    /// <para>Only <c>Application.ThreadException</c> routes here. An unhandled exception on a
    /// worker thread (<c>AppDomain.UnhandledException</c>) is terminal by the runtime's own rules
    /// and stays on the Fatal path - containment cannot apply there.</para>
    ///
    /// <para>State is a static queue because there is exactly one UI thread; the lock exists for
    /// the tests, which drive the core from several threads at once.</para>
    /// </summary>
    internal static class FaultContainment
    {
        /// <summary>Contained faults in <see cref="Window"/> that still end the process.</summary>
        public const int MaxFaults = 5;

        /// <summary>The sliding window <see cref="MaxFaults"/> is counted in.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

        private static readonly Queue<DateTime> _faults = new Queue<DateTime>();

        /// <summary>
        /// How a contained fault reaches the user - one balloon. <see cref="CyrFlipContext"/>
        /// registers this once its tray exists; until then (startup) there is nothing to show and
        /// the fault is only logged.
        /// </summary>
        private static Action<Exception>? _report;

        public static void RegisterReporter(Action<Exception> report) => _report = report;

        /// <summary>Contain one UI-thread fault: log it, tell the user once, keep the app alive.</summary>
        public static void Contain(Exception? ex)
            => Contain(ex, _report, Program.Fatal, () => DateTime.UtcNow);

        /// <summary>
        /// The testable core: <paramref name="report"/> is the balloon, <paramref name="fatal"/>
        /// is the exit, <paramref name="utcNow"/> is the clock the window is counted in.
        /// </summary>
        internal static void Contain(Exception? ex, Action<Exception>? report, Action<Exception?>? fatal, Func<DateTime> utcNow)
        {
            bool faultLoop = false;
            lock (_faults)
            {
                DateTime now = utcNow();
                while (_faults.Count > 0 && now - _faults.Peek() > Window)
                    _faults.Dequeue();
                _faults.Enqueue(now);
                faultLoop = _faults.Count >= MaxFaults;
            }

            if (faultLoop)
            {
                fatal?.Invoke(ex);
                return;
            }

            // The same line Fatal writes, marked as contained: the crash history has to hold the
            // survived faults too, and crash.log is what the log bundle carries (S0065 E-11).
            try
            {
                DiagnosticLog.Append(DiagnosticLog.Path("crash.log"),
                    Program.Stamp(DateTime.Now) + " - contained: " + (ex?.ToString() ?? "null"));
            }
            catch { /* diagnostics must never affect the app */ }
            try { report?.Invoke(ex ?? new Exception()); }
            catch { /* the balloon is a courtesy; a fault inside it is contained too */ }
        }

        /// <summary>Test seam: the window starts empty again.</summary>
        internal static void Reset() { lock (_faults) _faults.Clear(); }
    }
}
