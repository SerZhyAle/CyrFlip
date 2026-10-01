using System;

namespace CyrFlip
{
    /// <summary>
    /// How the screen region capture's save-to-folder went - the failure kinds only, never a path and
    /// never a pixel: <c>CAPTURE-OUTPUT</c> rule 11 already tells the user in the moment, and the log
    /// is what outlives the balloon (ticket S0026). A one-line wrapper over
    /// <see cref="DiagnosticLog"/>, like <see cref="ClipboardFlipLog"/>.
    /// </summary>
    internal static class ScreenshotLog
    {
        public const string FileName = "screenshot.log";

        public static void Log(string message)
        {
            try { DiagnosticLog.Append(DiagnosticLog.Path(FileName), DateTime.Now.ToString("HH:mm:ss.fff") + " - " + message); }
            catch { /* diagnostics must never affect a capture */ }
        }
    }
}
