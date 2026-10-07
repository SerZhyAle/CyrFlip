using System;

namespace CyrFlip
{
    /// <summary>
    /// Diagnostic log for tray actions and the hover popup (ticket S0059).
    /// </summary>
    internal static class TrayLog
    {
        public const string FileName = "tray.log";

        public static void Log(string message)
        {
            try { DiagnosticLog.Append(DiagnosticLog.Path(FileName), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message); }
            catch { /* diagnostics must never affect tray */ }
        }
    }
}
