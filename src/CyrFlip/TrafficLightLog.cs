using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// The traffic light's diagnostic log (<c>traffic-light.log</c>, ticket S0069): where a best-effort step
    /// failed and with which kind of exception - <b>never an address, a label or a message</b>, since those name
    /// the user's own infrastructure. Each site writes at most one line a minute, so a fault that repeats every
    /// second (a tick, a probe) cannot flood the file. Every swallowed exception in the feature goes through
    /// here: a fault that is not worth the user's attention is still worth a line (the error-boundary gate).
    /// </summary>
    internal static class TrafficLightLog
    {
        public const string FileName = "traffic-light.log";
        private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(60);
        private static readonly Dictionary<string, DateTime> Last = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        public static void Fault(string site, Exception? ex = null)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                lock (Last)
                {
                    if (Last.TryGetValue(site, out DateTime last) && now - last < MinGap) return;
                    Last[site] = now;
                }
                DiagnosticLog.Append(DiagnosticLog.Path(FileName),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " - " + site + (ex != null ? " " + ex.GetType().Name : ""));
            }
            catch { /* a diagnostic must never throw into the code it describes */ }
        }
    }
}
